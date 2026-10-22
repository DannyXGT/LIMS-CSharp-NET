using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Lims.Application.Common;
using Lims.Application.ReferenceMaterials;
using Lims.Application.ReferencePreparations;
using Lims.Contracts.Errors;
using Lims.Contracts.ReferenceMaterials;
using Lims.Contracts.ReferencePreparations;
using Lims.Domain.ReferenceMaterials;
using Lims.Domain.ReferencePreparations;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Lims.Infrastructure.Persistence;

public sealed class CommercialPreparationService(LimsDbContext db, TimeProvider clock) : ICommercialPreparationService
{
    private ReferenceMaterialService Materials => new(new ReferenceMaterialRepository(db), clock);
    public async Task<CommercialPreparationOptions> OptionsAsync(string kind, string? search, CancellationToken ct)
    {
        if (kind is not ("Stock" or "Intermedia")) throw new ArgumentException("Modalidad inválida.");
        var query = PreparationSources.Commercial(db).AsNoTracking().Where(m => m.Status == ReferenceMaterialStatus.Active);
        if (kind == "Stock") query = query.Where(m => m.Presentation == MaterialPresentations.Solution);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = CommercialPreparationQuery.Pattern(search);
            query = query.Where(m => EF.Functions.ILike(m.Name, pattern, "\\") || EF.Functions.ILike(m.Lot, pattern, "\\") ||
                (m.CatalogNumber != null && EF.Functions.ILike(m.CatalogNumber, pattern, "\\")));
        }
        // This is a searchable selector, not an unbounded inventory download.
        var rows = await query.Include(m => m.Location).OrderBy(m => m.Name).ThenBy(m => m.Id).Take(100).ToArrayAsync(ct).ConfigureAwait(false);
        var today = DateOnly.FromDateTime(clock.GetUtcNow().ToOffset(TimeSpan.FromHours(-6)).DateTime);
        var materials = rows.Select(m => new ReferenceMaterialDetail(m.Id, m.Name, m.CasNumber, m.CatalogNumber,
            m.MethodId, m.Method.Name, m.PurityPercent, m.Lot, m.Brand, m.ReceivedDate, m.ExpirationDate,
            m.PresentationQuantity, m.UnitId, m.Unit.Symbol, m.PackageCount, m.TotalQuantity, m.AvailableQuantity,
            m.StorageTemperature, m.LocationId, m.Location.Name, m.EffectiveStatus(today).ToString(), m.CreatedByUserId,
            m.CreatedAt, m.UpdatedByUserId, m.UpdatedAt, m.ArchivedByUserId, m.ArchivedAt, m.ArchiveReason,
            m.ReplacedByMaterialId, m.Version, Presentation: m.Presentation, Commercial: CommercialMaterials.Certificate(m),
            EffectiveExpirationDate: m.EffectiveExpirationDate)).ToArray();
        return new(await Materials.GetMethodsAsync(ct).ConfigureAwait(false), await Materials.GetUnitsAsync(ct).ConfigureAwait(false),
            await Materials.GetLocationsAsync(ct).ConfigureAwait(false), materials,
            await new StockRepository(db).SolventsAsync(null, ct).ConfigureAwait(false), true);
    }

    public async Task<OperationResult<CommercialPreparationDetail>> GetAsync(string kind, Guid materialId, CancellationToken ct)
    {
        var role = await db.Set<ReferenceCommercialRole>().AsNoTracking().SingleOrDefaultAsync(r => r.MaterialId == materialId && r.Kind == kind, ct).ConfigureAwait(false);
        if (role is null) return Failure(ErrorCodes.NotFound, "El ingreso comercial solicitado no existe.");
        var material = await Materials.GetAsync(materialId, ct).ConfigureAwait(false);
        if (!material.IsSuccess) return OperationResult.Failure<CommercialPreparationDetail>(material.Error!);
        var solventRows = await db.StockSolvents.AsNoTracking().Where(s => s.MaterialId == materialId).OrderBy(s => s.Position)
            .Select(s => new StockSolventDetail(s.SolventId, s.Solvent.Name, s.Solvent.Abbreviation, s.Percent, s.Position)).ToArrayAsync(ct).ConfigureAwait(false);
        var detail = material.Value!;
        if (solventRows.Length > 0 && detail.Commercial is { } certificate)
            detail = detail with { Commercial = certificate with { Solvents = solventRows.Select(r => new StockSolventInput(r.SolventId, r.Percent)).ToArray(),
                SolventDetails = solventRows, CompositionBasis = StockSolventComposition.Basis, Matrix = StockSolventComposition.Summary(solventRows) } };
        var actor = await db.Users.Where(u => u.Id == role.RegisteredByUserId).Select(u => u.Name).SingleAsync(ct).ConfigureAwait(false);
        return OperationResult.Success(new CommercialPreparationDetail(kind, role.Code, detail, actor, role.RegisteredAt));
    }

    public Task<OperationResult<CommercialPreparationDetail>> RegisterAsync(string kind, RegisterCommercialPreparationRequest request, int actor, CancellationToken ct)
    {
        if (actor <= 0) return Task.FromResult(Failure(ErrorCodes.InvalidSession, "La sesión no es válida."));
        if (request.RequestId == Guid.Empty || kind is not ("Stock" or "Intermedia") ||
            (request.ExistingMaterialId.HasValue == (request.NewMaterial is not null)))
            return Task.FromResult(Failure(ErrorCodes.ValidationError, "Seleccione un frasco existente o registre uno nuevo."));
        var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { kind, request, actor })));
        return db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            try
            {
                var previous = await db.Set<ReferenceCommercialRole>().AsNoTracking().SingleOrDefaultAsync(r => r.RequestId == request.RequestId, ct).ConfigureAwait(false);
                if (previous is not null) return await Retry(previous, kind, fingerprint, ct).ConfigureAwait(false);
                Guid materialId;
                if (request.NewMaterial is { } input)
                {
                    if (!MaterialPresentations.IsCommercial(input.Presentation) || (kind == "Stock" && input.Presentation != MaterialPresentations.Solution))
                        return Failure(ErrorCodes.ValidationError, "Stock comercial requiere una solución individual; Intermedia admite soluciones y Mix.");
                    foreach (var component in input.Commercial?.Components ?? []) _ = StockUnits.MilligramsPerLiter(component.ConcentrationUnit);
                    var compositionError = StockSolventComposition.Validate(input.Commercial?.Solvents, input.Commercial?.CompositionBasis);
                    if (compositionError is not null) return Failure(ErrorCodes.ValidationError, compositionError);
                    var rows = input.Commercial!.Solvents!;
                    if (input.Commercial.MatrixSolventId is { } matrixId && (rows.Count != 1 || rows[0].SolventId != matrixId))
                        return Failure(ErrorCodes.ValidationError, "La matriz individual no coincide con la composición documentada.");
                    var catalog = await new StockRepository(db).SolventsAsync(null, ct).ConfigureAwait(false);
                    if (rows.Any(r => !catalog.Any(s => s.Id == r.SolventId))) return Failure(ErrorCodes.ValidationError, "Seleccione solventes activos del catálogo compartido.");
                    var details = rows.Select((r, i) => { var solvent = catalog.Single(s => s.Id == r.SolventId); return new StockSolventDetail(r.SolventId, solvent.Name, solvent.Abbreviation, r.Percent, i); }).ToArray();
                    // New operational registrations use the certified concentration. Historical certificates remain unchanged.
                    input = input with { Commercial = input.Commercial with {
                        Matrix = StockSolventComposition.Summary(details), MatrixSolventId = rows.Count == 1 ? rows[0].SolventId : null,
                        SolventDetails = details, UseNominalConcentration = false } };
                    var created = await Materials.CreateAsync(input, actor, ct).ConfigureAwait(false);
                    if (!created.IsSuccess) return OperationResult.Failure<CommercialPreparationDetail>(created.Error!);
                    materialId = created.Value!.Id;
                    db.StockSolvents.AddRange(rows.Select((row, position) => StockSolvent.ForMaterial(materialId, row.SolventId, row.Percent, position)));
                    await db.SaveChangesAsync(ct).ConfigureAwait(false);
                }
                else
                {
                    materialId = request.ExistingMaterialId!.Value;
                    // Lock the physical row until association commits; never reset its quantity or version.
                    var material = await db.ReferenceMaterials.FromSqlInterpolated($"SELECT * FROM reference_materials WHERE id = {materialId} FOR UPDATE").SingleOrDefaultAsync(ct).ConfigureAwait(false);
                    if (material is null || !MaterialPresentations.IsCommercial(material.Presentation) || material.Status != ReferenceMaterialStatus.Active ||
                        (kind == "Stock" && material.Presentation != MaterialPresentations.Solution))
                        return Failure(ErrorCodes.InvalidState, "Seleccione un producto comercial compatible y no archivado.");
                    if (material.Version != request.MaterialVersion) return Failure(ErrorCodes.Conflict, "El saldo o los datos del frasco cambiaron. Actualice la selección.");
                    var certificate = CommercialMaterials.Certificate(await PreparationSources.Commercial(db).SingleAsync(m => m.Id == materialId, ct).ConfigureAwait(false))!;
                    foreach (var component in certificate.Components) _ = StockUnits.MilligramsPerLiter(component.ConcentrationUnit);
                }
                var role = await db.Set<ReferenceCommercialRole>().SingleOrDefaultAsync(r => r.MaterialId == materialId && r.Kind == kind, ct).ConfigureAwait(false);
                if (role is not null) return await GetAsync(kind, materialId, ct).ConfigureAwait(false);
                var received = await db.ReferenceMaterials.Where(m => m.Id == materialId).Select(m => m.ReceivedDate).SingleAsync(ct).ConfigureAwait(false);
                var sequence = kind == "Stock"
                    ? await db.Database.SqlQuery<long>($"SELECT nextval('reference_stock_code_sequence') AS \"Value\"").SingleAsync(ct).ConfigureAwait(false)
                    : await db.Database.SqlQuery<long>($"SELECT nextval('reference_intermediate_code_sequence') AS \"Value\"").SingleAsync(ct).ConfigureAwait(false);
                var prefix = kind == "Stock" ? "STK" : "INT";
                var code = string.Create(CultureInfo.InvariantCulture, $"{prefix}-{received:yyyyMMdd}-{sequence:D3}");
                db.Add(new ReferenceCommercialRole(materialId, kind, code, request.RequestId, fingerprint, actor, clock.GetUtcNow()));
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                var result = await GetAsync(kind, materialId, ct).ConfigureAwait(false);
                await transaction.CommitAsync(ct).ConfigureAwait(false);
                return result;
            }
            catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                await transaction.RollbackAsync(ct).ConfigureAwait(false); db.ChangeTracker.Clear();
                var previous = await db.Set<ReferenceCommercialRole>().AsNoTracking().SingleOrDefaultAsync(r => r.RequestId == request.RequestId, ct).ConfigureAwait(false);
                return previous is null ? Failure(ErrorCodes.Conflict, "El producto ya fue asociado. Actualice la lista.") : await Retry(previous, kind, fingerprint, ct).ConfigureAwait(false);
            }
            catch (ArgumentException e) { await transaction.RollbackAsync(ct).ConfigureAwait(false); db.ChangeTracker.Clear(); return Failure(ErrorCodes.ValidationError, e.Message); }
            catch { await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false); db.ChangeTracker.Clear(); throw; }
        });
    }
    private Task<OperationResult<CommercialPreparationDetail>> Retry(ReferenceCommercialRole role, string kind, string fingerprint, CancellationToken ct) =>
        role.Kind == kind && role.RequestFingerprint == fingerprint ? GetAsync(kind, role.MaterialId, ct) : Task.FromResult(Failure(ErrorCodes.Conflict, "La solicitud ya se utilizó con otros datos."));
    private static OperationResult<CommercialPreparationDetail> Failure(string code, string message) => OperationResult.Failure<CommercialPreparationDetail>(new(code, message));
}

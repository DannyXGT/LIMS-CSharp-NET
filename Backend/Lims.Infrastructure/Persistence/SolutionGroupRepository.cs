using System.Text.Json;
using Lims.Application.Common;
using Lims.Application.ReferencePreparations;
using Lims.Contracts.Errors;
using Lims.Contracts.ReferencePreparations;
using Lims.Domain.ReferencePreparations;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Lims.Infrastructure.Persistence;

public sealed class SolutionGroupRepository(LimsDbContext db) : ISolutionGroupRepository
{
    public async Task<IntermediateOptions> OptionsAsync(DateOnly today, CancellationToken cancellationToken)
    {
        var options = await new IntermediateRepository(db).OptionsAsync(today, cancellationToken).ConfigureAwait(false);
        return options with { Sources = options.Sources.Where(s => s.Kind == "Intermedia" || Lims.Contracts.ReferenceMaterials.MaterialPresentations.IsCommercial(s.Kind)).ToArray() };
    }

    public Task<OperationResult<SolutionGroupDetail>> CreateAsync(string kind, CreateSolutionGroupRequest request, int actor,
        string fingerprint, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(now.ToOffset(TimeSpan.FromHours(-6)).DateTime);
        return db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var existing = await db.ReferencePreparations.AsNoTracking().SingleOrDefaultAsync(p => p.Id == request.RequestId, cancellationToken).ConfigureAwait(false);
                if (existing is not null) return await Existing(kind, existing, fingerprint, today, cancellationToken).ConfigureAwait(false);
                if (request.Calculation.Parts is null || request.Calculation.Parts.Count is < 1 or > 100 || request.Calculation.Parts.Any(p => p is null))
                    return Fail("Agregue entre 1 y 100 soluciones completas.", ErrorCodes.ValidationError);
                var method = await db.ReferenceMethods.AsNoTracking().SingleOrDefaultAsync(m => m.Id == request.Calculation.MethodId && m.IsActive, cancellationToken).ConfigureAwait(false);
                if (method is null) return Fail("Seleccione un método activo.", ErrorCodes.ValidationError);
                var ids = request.Calculation.Parts.Select(p => p.SourceId).Distinct().ToArray();
                var sources = await db.ReferencePreparations.Where(p => ids.Contains(p.Id) && p.Kind == "Intermedia").OrderBy(p => p.Id).ToArrayAsync(cancellationToken).ConfigureAwait(false);
                var materials = await PreparationSources.Commercial(db).Where(m => ids.Contains(m.Id)).OrderBy(m => m.Id).ToArrayAsync(cancellationToken).ConfigureAwait(false);
                var options = sources.Select(Source).Concat(materials.Select(PreparationSources.TrySource).OfType<IntermediateSource>()).ToArray();
                if (options.Length != ids.Length || materials.Any(m => !PreparationSources.Eligible(m, today)) || sources.Any(s => s.Kind != "Intermedia" || s.Status != "Active" || s.AvailableVolume <= 0 || s.ExpirationDate < today || s.PreparationDate > today))
                    return Fail("Un origen ya no está disponible.", ErrorCodes.InvalidState);
                if (request.Calculation.Parts.Any(p => options.Single(s => s.Id == p.SourceId).Version != p.SourceVersion))
                    return Fail("Un origen cambió. Actualice los orígenes y revise sus saldos.");
                if (options.Any(s => request.PreparationDate < s.PreparationDate || request.PreparationDate > s.ExpirationDate))
                    return Fail("La preparación debe estar dentro de la vigencia de todos los orígenes.", ErrorCodes.ValidationError);
                var calculation = SolutionGroupCalculator.Calculate(request.Calculation, options);
                if (!calculation.HasSufficientVolume) return Fail("El consumo total del grupo supera el saldo de un origen.", ErrorCodes.InvalidState);
                var preparation = ReferencePreparation.SolutionGroup(request.RequestId, kind, request.Code,
                    string.IsNullOrWhiteSpace(request.Name) ? request.Code : request.Name, method.Id, method.Name,
                    calculation.Parts.Sum(p => StockUnits.ConvertVolume(p.FinalVolume, p.FinalVolumeUnit, "mL")),
                    JsonSerializer.Serialize(calculation), request.PreparationDate, request.ExpirationDate, actor,
                    request.Notes, request.StorageTemperature, now, fingerprint);
                db.ReferencePreparations.Add(preparation);
                // Persist each distinct physical source once. Per-solution aforo, role, analytes and aliquots remain in ResultsJson.
                var position = 0;
                foreach (var debit in calculation.Debits.OrderBy(d => d.Source.Id))
                {
                    PreparationSources.Debit(db, preparation, debit.Source, position++, debit.Quantity,
                        debit.Source.VolumeUnit, sources, materials, actor, now, today);
                }
                // The same Version token used by IntermediateRepository protects all competing consumers.
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                var detail = (await GetAsync(kind, preparation.Id, today, cancellationToken).ConfigureAwait(false))!;
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return OperationResult.Success(detail);
            }
            catch (DbUpdateConcurrencyException)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false); db.ChangeTracker.Clear();
                return Fail("Otra operación consumió el origen. Actualice los saldos; no se guardó ninguna parte del grupo.");
            }
            catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false); db.ChangeTracker.Clear();
                var existing = await db.ReferencePreparations.AsNoTracking().SingleOrDefaultAsync(p => p.Id == request.RequestId, cancellationToken).ConfigureAwait(false);
                return existing is null ? Fail("El código ya existe. Utilice otro código de preparación.") : await Existing(kind, existing, fingerprint, today, cancellationToken).ConfigureAwait(false);
            }
            catch (ArgumentException e)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false); db.ChangeTracker.Clear();
                return Fail(e.Message, ErrorCodes.ValidationError);
            }
            catch (OverflowException)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false); db.ChangeTracker.Clear();
                return Fail("Las cantidades exceden el rango admitido.", ErrorCodes.ValidationError);
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false); db.ChangeTracker.Clear(); throw;
            }
        });
    }

    public async Task<SolutionGroupPage> ListAsync(string kind, string? search, int page, int pageSize, DateOnly today,
        CancellationToken cancellationToken, PreparationListFilter? filter = null)
    {
        var query = db.ReferencePreparations.AsNoTracking().Where(p => p.Kind == kind);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = "%" + Escape(search.Trim()) + "%";
            query = query.Where(p => EF.Functions.ILike(p.Code, pattern, "\\") || EF.Functions.ILike(p.Name, pattern, "\\") ||
                EF.Functions.ILike(p.SourceMethod, pattern, "\\") || db.ReferencePreparationComponents.Any(c => c.PreparationId == p.Id &&
                    db.ReferencePreparations.Any(s => s.Id == c.SourcePreparationId && (EF.Functions.ILike(s.Code, pattern, "\\") || EF.Functions.ILike(s.Name, pattern, "\\")))));
        }
        query = query.Filter(filter, today);
        var count = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await query.OrderByDescending(p => p.PreparationDate).ThenByDescending(p => p.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(cancellationToken).ConfigureAwait(false);
        var actors = await Actors(rows.Select(p => p.PreparedByUserId), cancellationToken).ConfigureAwait(false);
        return new(rows.Select(p => Summary(p, actors.GetValueOrDefault(p.PreparedByUserId) ?? "Usuario no informado", today)).ToArray(), page, pageSize, count);
    }
    public async Task<SolutionGroupDetail?> GetAsync(string kind, Guid id, DateOnly today, CancellationToken cancellationToken)
    {
        var p = await db.ReferencePreparations.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id && p.Kind == kind, cancellationToken).ConfigureAwait(false);
        if (p is null) return null;
        var calculation = Calculation(p);
        var movements = await db.ReferenceMaterialMovements.AsNoTracking().Where(m => m.PreparationId == id).OrderBy(m => m.Id).ToArrayAsync(cancellationToken).ConfigureAwait(false);
        var actors = await Actors(movements.Select(m => m.ActorUserId).Append(p.PreparedByUserId), cancellationToken).ConfigureAwait(false);
        return new(Summary(p, actors[p.PreparedByUserId], today), calculation,
            movements.Select(m => new IntermediateConsumption(m.Id, (m.SourcePreparationId ?? m.SourceMaterialId)!.Value,
                calculation.Debits.Single(d => d.Source.Id == (m.SourcePreparationId ?? m.SourceMaterialId)).Source.Code, m.Quantity, m.Unit,
                m.BalanceBefore, m.BalanceAfter, actors[m.ActorUserId], m.OccurredAt)).ToArray(), p.Notes, p.StorageTemperature, p.CreatedAt, p.Version);
    }
    private Task<Dictionary<int, string>> Actors(IEnumerable<int> ids, CancellationToken ct)
    {
        var keys = ids.Distinct().ToArray();
        return db.Users.AsNoTracking().Where(u => keys.Contains(u.Id)).Select(u => new { u.Id, u.Name }).ToDictionaryAsync(u => u.Id, u => u.Name, ct);
    }
    private async Task<OperationResult<SolutionGroupDetail>> Existing(string kind, ReferencePreparation p, string fingerprint, DateOnly today, CancellationToken ct) =>
        p.Kind == kind && p.RequestFingerprint == fingerprint ? OperationResult.Success((await GetAsync(kind, p.Id, today, ct).ConfigureAwait(false))!) : Fail("Esta solicitud ya se utilizó con otros datos.");
    private static IntermediateSource Source(ReferencePreparation p) => new(p.Id, p.Kind, p.Code, p.Name, p.MethodId!.Value, p.SourceMethod,
        p.AvailableVolume, p.FinalVolume, p.FinalVolumeUnit, p.PreparationDate, p.ExpirationDate, p.Version, JsonSerializer.Deserialize<IntermediateAnalyte[]>(p.ResultsJson)!);
    private static SolutionCalculation Calculation(ReferencePreparation p) => JsonSerializer.Deserialize<SolutionCalculation>(p.ResultsJson)!;
    private static SolutionGroupSummary Summary(ReferencePreparation p, string actor, DateOnly today)
    {
        var c = Calculation(p);
        return new(p.Id, p.Kind, p.Code, p.Name, p.SourceMethod, c.Parts.Count,
            string.Join(" · ", c.Debits.Select(d => d.Source.Code)), p.PreparationDate, p.ExpirationDate, actor,
            p.Status == "Archived" ? "Archived" : p.ExpirationDate < today ? "Expired" : p.Status);
    }
    private static string Escape(string text) => text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);
    private static OperationResult<SolutionGroupDetail> Fail(string message, string code = ErrorCodes.Conflict) => OperationResult.Failure<SolutionGroupDetail>(new(code, message));
}

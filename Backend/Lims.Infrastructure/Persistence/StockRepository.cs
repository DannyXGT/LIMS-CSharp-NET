using Lims.Application.Common;
using Lims.Application.ReferencePreparations;
using Lims.Contracts.Errors;
using Lims.Contracts.ReferencePreparations;
using Lims.Domain.ReferenceMaterials;
using Lims.Domain.ReferencePreparations;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Lims.Infrastructure.Persistence;

public sealed class StockRepository(LimsDbContext dbContext) : IStockRepository
{
    public async Task<IReadOnlyList<SolventOption>> SolventsAsync(string? search, CancellationToken cancellationToken)
    {
        var query = dbContext.ReferenceSolvents.AsNoTracking().Where(s => s.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = Pattern(search);
            query = query.Where(s => EF.Functions.ILike(s.Name, term, "\\") || EF.Functions.ILike(s.Abbreviation, term, "\\"));
        }
        return await query.OrderBy(s => s.DisplayOrder).ThenBy(s => s.Id)
            .Select(s => new SolventOption(s.Id, s.Name, s.Abbreviation, s.IsActive, s.DisplayOrder)).ToArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<StockSourcePage> SourcesAsync(string? search, DateOnly today, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = Sources().AsNoTracking().Where(source => source.Presentation == "Solid" && source.Status == ReferenceMaterialStatus.Active &&
            source.AvailableQuantity > 0 && (!HistoricalEntryPolicy.EnforceCurrentExpiration || source.ExpirationDate >= today) &&
            (source.Unit.Symbol == "g" || source.Unit.Symbol == "mg" || source.Unit.Symbol == "µg" || source.Unit.Symbol == "μg" || source.Unit.Symbol == "ug"));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = Pattern(search);
            query = query.Where(source => EF.Functions.ILike(source.Name, term, "\\") || EF.Functions.ILike(source.Lot, term, "\\") ||
                (source.CasNumber != null && EF.Functions.ILike(source.CasNumber, term, "\\")) ||
                (source.CatalogNumber != null && EF.Functions.ILike(source.CatalogNumber, term, "\\")));
        }
        var count = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var items = await query.OrderBy(source => source.Name).ThenBy(source => source.Lot).ThenBy(source => source.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(cancellationToken).ConfigureAwait(false);
        return new StockSourcePage(items.Select(ToSource).ToArray(), page, pageSize, count);
    }

    public Task<ReferenceMaterial?> FindSourceAsync(Guid id, CancellationToken cancellationToken) =>
        Sources().AsNoTracking().SingleOrDefaultAsync(source => source.Id == id, cancellationToken);

    public Task<OperationResult<StockDetail>> CreateAsync(Guid requestId, string fingerprint,
        Func<ReferenceMaterial, long, OperationResult<ReferencePreparation>> prepare, Guid sourceId,
        DateOnly today, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prepare);
        var strategy = dbContext.Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(async () =>
        {
            // Retries must reload original values, not reuse a balance changed during a rolled back attempt.
            dbContext.ChangeTracker.Clear();
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var existing = await dbContext.ReferencePreparations.AsNoTracking()
                    .SingleOrDefaultAsync(item => item.Id == requestId, cancellationToken).ConfigureAwait(false);
                if (existing is not null)
                    return existing.RequestFingerprint == fingerprint
                        ? OperationResult.Success((await GetAsync(requestId, cancellationToken).ConfigureAwait(false))!)
                        : Failure("Esta solicitud ya se utilizó con otros datos.");
                var source = await Sources().SingleOrDefaultAsync(item => item.Id == sourceId, cancellationToken).ConfigureAwait(false);
                if (source is null) return OperationResult.Failure<StockDetail>(new OperationError(ErrorCodes.NotFound, "El estándar solicitado no existe."));
                var sequence = await dbContext.Database.SqlQuery<long>($"SELECT nextval('reference_stock_code_sequence') AS \"Value\"")
                    .SingleAsync(cancellationToken).ConfigureAwait(false);
                var prepared = prepare(source, sequence);
                if (!prepared.IsSuccess) return OperationResult.Failure<StockDetail>(prepared.Error!);
                var preparation = prepared.Value!;
                var solventIds = preparation.Solvents.Select(s => s.SolventId).ToArray();
                // Lock catalog rows until commit so deactivation cannot race the validation.
                var solvents = await dbContext.ReferenceSolvents.FromSqlInterpolated($"SELECT * FROM reference_solvents WHERE id = ANY({solventIds}) FOR SHARE")
                    .AsNoTracking().ToArrayAsync(cancellationToken).ConfigureAwait(false);
                if (solvents.Length != solventIds.Length || solvents.Any(s => !s.IsActive))
                    return OperationResult.Failure<StockDetail>(new OperationError(ErrorCodes.ValidationError, "Seleccione solventes activos del catálogo actual."));
                var movement = new ReferenceMaterialMovement(preparation, source.AvailableQuantity, now);
                source.Consume(preparation.ActualWeight, preparation.PreparedByUserId, now, today,
                    enforceCurrentExpiration: HistoricalEntryPolicy.EnforceCurrentExpiration);
                dbContext.ReferencePreparations.Add(preparation);
                dbContext.ReferenceMaterialMovements.Add(movement);
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                // Materialize response before commit, so a failed display query cannot leave partial success.
                var detail = (await GetAsync(requestId, cancellationToken).ConfigureAwait(false))!;
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return OperationResult.Success(detail);
            }
            catch (DbUpdateConcurrencyException)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                dbContext.ChangeTracker.Clear();
                return Failure("El estándar cambió durante la preparación. Actualice su disponibilidad y vuelva a calcular.");
            }
            catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                dbContext.ChangeTracker.Clear();
                var existing = await dbContext.ReferencePreparations.AsNoTracking()
                    .SingleOrDefaultAsync(item => item.Id == requestId, cancellationToken).ConfigureAwait(false);
                return existing?.RequestFingerprint == fingerprint
                    ? OperationResult.Success((await GetAsync(requestId, cancellationToken).ConfigureAwait(false))!)
                    : Failure("La preparación coincide con una solicitud existente. Actualice los datos.");
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                dbContext.ChangeTracker.Clear();
                throw;
            }
        });
    }

    public async Task<StockPage> ListAsync(string? search, int page, int pageSize, CancellationToken cancellationToken, PreparationListFilter? filter = null)
    {
        var query = dbContext.ReferencePreparations.AsNoTracking().Where(item => item.Kind == "Stock");
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = Pattern(search);
            query = query.Where(item => EF.Functions.ILike(item.Code, term, "\\") || EF.Functions.ILike(item.Name, term, "\\") ||
                EF.Functions.ILike(item.SourceName, term, "\\") || EF.Functions.ILike(item.SourceLot, term, "\\"));
        }
        query = query.Filter(filter, DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-6)).DateTime));
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-6)).DateTime);
        var entries = await CommercialPreparationQuery.PageAsync(dbContext, query, "Stock", search, filter, today, page, pageSize, cancellationToken).ConfigureAwait(false);
        var ids = entries.Keys.Where(k => !k.Commercial).Select(k => k.Id).ToArray();
        var items = await dbContext.ReferencePreparations.AsNoTracking().Where(p => ids.Contains(p.Id)).ToArrayAsync(cancellationToken).ConfigureAwait(false);
        var actorIds = items.Select(item => item.PreparedByUserId).Concat(entries.Commercial.Select(r => r.RegisteredByUserId)).Distinct().ToArray();
        var actors = await dbContext.Users.AsNoTracking().Where(user => actorIds.Contains(user.Id))
            .Select(user => new { user.Id, user.Name }).ToDictionaryAsync(user => user.Id, user => user.Name, cancellationToken).ConfigureAwait(false);
        var rows = entries.Keys.Select(k => k.Commercial
            ? CommercialPreparationQuery.Stock(entries.Commercial.Single(r => r.MaterialId == k.Id), actors[entries.Commercial.Single(r => r.MaterialId == k.Id).RegisteredByUserId], today)
            : Summary(items.Single(p => p.Id == k.Id), actors.GetValueOrDefault(items.Single(p => p.Id == k.Id).PreparedByUserId) ?? "Usuario no informado")).ToArray();
        return new StockPage(rows, page, pageSize, entries.Total, true);
    }

    public async Task<StockDetail?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var item = await dbContext.ReferencePreparations.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id && item.Kind == "Stock", cancellationToken).ConfigureAwait(false);
        if (item is null) return null;
        var movement = await dbContext.ReferenceMaterialMovements.AsNoTracking().SingleAsync(item => item.PreparationId == id, cancellationToken).ConfigureAwait(false);
        var actor = await dbContext.Users.AsNoTracking().Where(user => user.Id == item.PreparedByUserId)
            .Select(user => user.Name).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false) ?? "Usuario no informado";
        var source = new StockSource(item.SourceMaterialId!.Value, item.SourceName, item.SourceCasNumber, item.SourceCatalogNumber,
            item.SourceLot, item.SourceBrand, item.SourceMethod, item.PurityPercentUsed, movement.BalanceBefore,
            item.SourceTotalQuantity, item.SourceUnit, item.SourceExpirationDate, item.SourceLocation, item.SourceVersion);
        var calculation = new StockCalculation(item.CalculatedWeight, item.ActualWeight, item.SourceUnit,
            movement.BalanceBefore, movement.BalanceAfter, 0, true, item.PurityPercentUsed, item.TargetConcentration,
            item.ActualConcentration, item.ConcentrationUnit, item.FinalVolume, item.FinalVolumeUnit, item.Formula);
        var solvents = await dbContext.StockSolvents.AsNoTracking().Where(s => s.PreparationId == id).OrderBy(s => s.Position)
            .Select(s => new StockSolventDetail(s.SolventId, s.Solvent.Name, s.Solvent.Abbreviation, s.Percent, s.Position))
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        return new StockDetail(Summary(item, actor), source, calculation,
            new StockMovement(movement.Id, movement.Quantity, movement.Unit, movement.BalanceBefore, movement.BalanceAfter, actor, movement.OccurredAt),
            item.Notes, item.CreatedAt, item.UpdatedAt, item.Version, item.StorageTemperature, await PreparationListQuery.UsageAsync(dbContext, id, cancellationToken).ConfigureAwait(false), item.Compound, solvents, item.CompositionBasis);
    }

    private IQueryable<ReferenceMaterial> Sources() => dbContext.ReferenceMaterials.Include(item => item.Unit).Include(item => item.Method).Include(item => item.Location);
    private static string Pattern(string value) => "%" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
    private static StockSource ToSource(ReferenceMaterial item) => new(item.Id, item.Name, item.CasNumber, item.CatalogNumber,
        item.Lot, item.Brand, item.Method.Name, item.PurityPercent ?? 0, item.AvailableQuantity, item.TotalQuantity, item.Unit.Symbol,
        item.ExpirationDate, item.Location.Name, item.Version, item.ReceivedDate);
    private static StockSummary Summary(ReferencePreparation item, string actor) => new(item.Id, item.Code, item.Name,
        item.SourceName, item.SourceLot, item.TargetConcentration, item.ActualConcentration, item.ConcentrationUnit,
        item.FinalVolume, item.FinalVolumeUnit, item.PreparationDate, actor, item.Status == "Archived" ? "Archived" : item.ExpirationDate < DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-6)).DateTime) ? "Expired" : item.AvailableVolume == 0 ? "Depleted" : item.Status, item.ActualWeight, item.ActualWeightUnit, item.ExpirationDate, item.AvailableVolume);
    private static OperationResult<StockDetail> Failure(string message) => OperationResult.Failure<StockDetail>(new OperationError(ErrorCodes.Conflict, message));
}

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
    public async Task<StockSourcePage> SourcesAsync(string? search, DateOnly today, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = Sources().AsNoTracking().Where(source => source.Status == ReferenceMaterialStatus.Active &&
            source.AvailableQuantity > 0 && source.ExpirationDate >= today &&
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
                var movement = new ReferenceMaterialMovement(preparation, source.AvailableQuantity, now);
                source.Consume(preparation.ActualWeight, preparation.PreparedByUserId, now, today);
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

    public async Task<StockPage> ListAsync(string? search, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = dbContext.ReferencePreparations.AsNoTracking().Where(item => item.Kind == "Stock");
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = Pattern(search);
            query = query.Where(item => EF.Functions.ILike(item.Code, term, "\\") || EF.Functions.ILike(item.Name, term, "\\") ||
                EF.Functions.ILike(item.SourceName, term, "\\") || EF.Functions.ILike(item.SourceLot, term, "\\"));
        }
        var count = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var items = await query.OrderByDescending(item => item.PreparationDate).ThenByDescending(item => item.Code)
            .Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(cancellationToken).ConfigureAwait(false);
        var actorIds = items.Select(item => item.PreparedByUserId).Distinct().ToArray();
        var actors = await dbContext.Users.AsNoTracking().Where(user => actorIds.Contains(user.Id))
            .Select(user => new { user.Id, user.Name }).ToDictionaryAsync(user => user.Id, user => user.Name, cancellationToken).ConfigureAwait(false);
        return new StockPage(items.Select(item => Summary(item, actors.GetValueOrDefault(item.PreparedByUserId) ?? "Usuario no informado")).ToArray(), page, pageSize, count);
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
        return new StockDetail(Summary(item, actor), source, calculation,
            new StockMovement(movement.Id, movement.Quantity, movement.Unit, movement.BalanceBefore, movement.BalanceAfter, actor, movement.OccurredAt),
            item.Notes, item.CreatedAt, item.UpdatedAt, item.Version, item.StorageTemperature);
    }

    private IQueryable<ReferenceMaterial> Sources() => dbContext.ReferenceMaterials.Include(item => item.Unit).Include(item => item.Method).Include(item => item.Location);
    private static string Pattern(string value) => "%" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
    private static StockSource ToSource(ReferenceMaterial item) => new(item.Id, item.Name, item.CasNumber, item.CatalogNumber,
        item.Lot, item.Brand, item.Method.Name, item.PurityPercent, item.AvailableQuantity, item.TotalQuantity, item.Unit.Symbol,
        item.ExpirationDate, item.Location.Name, item.Version, item.ReceivedDate);
    private static StockSummary Summary(ReferencePreparation item, string actor) => new(item.Id, item.Code, item.Name,
        item.SourceName, item.SourceLot, item.TargetConcentration, item.ActualConcentration, item.ConcentrationUnit,
        item.FinalVolume, item.FinalVolumeUnit, item.PreparationDate, actor, item.ExpirationDate < DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-6)).DateTime) ? "Expired" : item.AvailableVolume == 0 ? "Depleted" : item.Status, item.ActualWeight, item.ActualWeightUnit, item.ExpirationDate);
    private static OperationResult<StockDetail> Failure(string message) => OperationResult.Failure<StockDetail>(new OperationError(ErrorCodes.Conflict, message));
}

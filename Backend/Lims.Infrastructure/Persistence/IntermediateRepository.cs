using System.Globalization;
using System.Text.Json;
using Lims.Application.Common;
using Lims.Application.ReferencePreparations;
using Lims.Contracts.Errors;
using Lims.Contracts.ReferencePreparations;
using Lims.Domain.ReferenceMaterials;
using Lims.Domain.ReferencePreparations;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Lims.Infrastructure.Persistence;

public sealed class IntermediateRepository(LimsDbContext db) : IIntermediateRepository
{
    public async Task<IntermediateOptions> OptionsAsync(DateOnly today, CancellationToken cancellationToken)
    {
        var methods = await db.ReferenceMethods.AsNoTracking().Where(m => m.IsActive).OrderBy(m => m.Name)
            .Select(m => new IntermediateMethod(m.Id, m.Name)).ToArrayAsync(cancellationToken).ConfigureAwait(false);
        var ids = methods.Select(m => m.Id).ToArray();
        var sources = await db.ReferencePreparations.AsNoTracking().Where(p => p.Status == "Active" && p.AvailableVolume > 0 &&
            p.ExpirationDate >= today && p.PreparationDate <= today && p.MethodId != null && ids.Contains(p.MethodId.Value) &&
            (p.Kind == "Intermedia" || p.SourceMaterial!.Status == ReferenceMaterialStatus.Active))
            .OrderBy(p => p.Code).ToArrayAsync(cancellationToken).ConfigureAwait(false);
        return new(methods, sources.Select(Source).ToArray());
    }

    public Task<OperationResult<IntermediateDetail>> CreateAsync(CreateIntermediateRequest request, int actor, string fingerprint,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(now.ToOffset(TimeSpan.FromHours(-6)).DateTime);
        return db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var existing = await db.ReferencePreparations.AsNoTracking().SingleOrDefaultAsync(p => p.Id == request.RequestId, cancellationToken).ConfigureAwait(false);
                if (existing is not null) return await Existing(existing, fingerprint, today, cancellationToken).ConfigureAwait(false);
                if (request.Calculation.Components is null || request.Calculation.Components.Count is < 1 or > 100 || request.Calculation.Components.Any(c => c is null))
                    return Fail("Seleccione al menos un componente (máximo 100).", ErrorCodes.ValidationError);
                var method = await db.ReferenceMethods.AsNoTracking().SingleOrDefaultAsync(m => m.Id == request.Calculation.MethodId && m.IsActive, cancellationToken).ConfigureAwait(false);
                if (method is null) return Fail("Seleccione un método activo.", ErrorCodes.ValidationError);
                var sourceIds = request.Calculation.Components.Select(c => c.SourceId).Distinct().ToArray();
                // Stable order also prevents cycles in row-update locking. Optimistic Version protects concurrent debits.
                var sources = await db.ReferencePreparations.Include(p => p.SourceMaterial).Where(p => sourceIds.Contains(p.Id)).OrderBy(p => p.Id).ToArrayAsync(cancellationToken).ConfigureAwait(false);
                if (sources.Length != sourceIds.Length || sources.Any(s => s.Status != "Active" || s.AvailableVolume <= 0 || s.ExpirationDate < today ||
                    (s.Kind == "Stock" && s.SourceMaterial?.Status != ReferenceMaterialStatus.Active)))
                    return Fail("Un origen ya no está disponible.", ErrorCodes.InvalidState);
                if (request.Calculation.Components.Any(c => sources.Single(s => s.Id == c.SourceId).Version != c.SourceVersion))
                    return Fail("Un origen cambió. Actualice su disponibilidad y vuelva a revisar la preparación.");
                if (sources.Any(s => request.PreparationDate < s.PreparationDate || request.PreparationDate > s.ExpirationDate))
                    return Fail("La fecha de preparación debe estar dentro de la vigencia de todos los orígenes.", ErrorCodes.ValidationError);
                var calculation = IntermediateCalculator.Calculate(request.Calculation, sources.Select(Source).ToArray());
                if (calculation.ExceedsFinalVolume) return Fail("El volumen de los componentes supera el volumen de aforo.", ErrorCodes.ValidationError);
                if (!calculation.HasSufficientVolume) return Fail("No hay volumen disponible suficiente en uno de los orígenes.", ErrorCodes.InvalidState);
                var sequence = await db.Database.SqlQuery<long>($"SELECT nextval('reference_intermediate_code_sequence') AS \"Value\"").SingleAsync(cancellationToken).ConfigureAwait(false);
                var code = string.Create(CultureInfo.InvariantCulture, $"INT-{request.PreparationDate:yyyyMMdd}-{sequence:D3}");
                var preparation = ReferencePreparation.Intermediate(request.RequestId, code, request.Name, method.Id, method.Name,
                    request.Calculation.FinalVolume, request.Calculation.FinalVolumeUnit, JsonSerializer.Serialize(calculation.Results),
                    request.PreparationDate, request.ExpirationDate, actor, request.Notes, now, fingerprint, request.Calculation.DilutedPreparationId);
                db.ReferencePreparations.Add(preparation);
                for (var index = 0; index < calculation.Components.Count; index++)
                {
                    var contribution = calculation.Components[index];
                    var source = sources.Single(s => s.Id == contribution.Source.Id);
                    var component = new ReferencePreparationComponent(preparation.Id, source, index, contribution.VolumeTaken,
                        contribution.VolumeUnit, JsonSerializer.Serialize(contribution.Source), now);
                    var debit = StockUnits.ConvertVolume(contribution.VolumeTaken, contribution.VolumeUnit, source.FinalVolumeUnit);
                    var movement = new ReferenceMaterialMovement(preparation, source, component, debit, now);
                    source.ConsumeVolume(debit, actor, now, today);
                    db.ReferencePreparationComponents.Add(component);
                    db.ReferenceMaterialMovements.Add(movement);
                }
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                var detail = (await GetAsync(preparation.Id, today, cancellationToken).ConfigureAwait(false))!;
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return OperationResult.Success(detail);
            }
            catch (DbUpdateConcurrencyException)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false); db.ChangeTracker.Clear();
                return Fail("Un origen cambió durante el consumo. Actualice su disponibilidad; no se guardó ningún componente.");
            }
            catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false); db.ChangeTracker.Clear();
                var existing = await db.ReferencePreparations.AsNoTracking().SingleOrDefaultAsync(p => p.Id == request.RequestId, cancellationToken).ConfigureAwait(false);
                return existing is null ? Fail("La solicitud coincide con otra preparación. Actualice los datos.")
                    : await Existing(existing, fingerprint, today, cancellationToken).ConfigureAwait(false);
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

    public async Task<IntermediatePage> ListAsync(string? search, int page, int pageSize, DateOnly today, CancellationToken cancellationToken)
    {
        var query = db.ReferencePreparations.AsNoTracking().Where(p => p.Kind == "Intermedia");
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = "%" + search.Trim().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
            query = query.Where(p => EF.Functions.ILike(p.Code, pattern, "\\") || EF.Functions.ILike(p.Name, pattern, "\\") || EF.Functions.ILike(p.SourceMethod, pattern, "\\"));
        }
        var count = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var items = await query.OrderByDescending(p => p.PreparationDate).ThenByDescending(p => p.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(cancellationToken).ConfigureAwait(false);
        var ids = items.Select(p => p.Id).ToArray();
        var components = await db.ReferencePreparationComponents.AsNoTracking().Where(c => ids.Contains(c.PreparationId)).GroupBy(c => c.PreparationId)
            .Select(g => new { Id = g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Id, g => g.Count, cancellationToken).ConfigureAwait(false);
        var actors = await Actors(items.Select(p => p.PreparedByUserId), cancellationToken).ConfigureAwait(false);
        return new(items.Select(p => Summary(p, components.GetValueOrDefault(p.Id), actors.GetValueOrDefault(p.PreparedByUserId) ?? "Usuario no informado", today)).ToArray(), page, pageSize, count);
    }

    public async Task<IntermediateDetail?> GetAsync(Guid id, DateOnly today, CancellationToken cancellationToken)
    {
        var preparation = await db.ReferencePreparations.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id && p.Kind == "Intermedia", cancellationToken).ConfigureAwait(false);
        if (preparation is null) return null;
        var components = await db.ReferencePreparationComponents.AsNoTracking().Where(c => c.PreparationId == id).OrderBy(c => c.Position).ToArrayAsync(cancellationToken).ConfigureAwait(false);
        var request = new IntermediateCalculationRequest(preparation.MethodId!.Value, preparation.FinalVolume, preparation.FinalVolumeUnit,
            components.Select(c => new IntermediateComponentRequest(c.SourcePreparationId, c.SourceVersion, c.VolumeTaken, c.VolumeUnit)).ToArray(), preparation.DilutedPreparationId);
        var calculation = IntermediateCalculator.Calculate(request, components.Select(c => JsonSerializer.Deserialize<IntermediateSource>(c.SourceSnapshotJson)!).ToArray());
        // The stored final results are authoritative historical values.
        calculation = calculation with { Results = Results(preparation) };
        var movements = await db.ReferenceMaterialMovements.AsNoTracking().Where(m => m.PreparationId == id).OrderBy(m => m.Id).ToArrayAsync(cancellationToken).ConfigureAwait(false);
        var actors = await Actors(movements.Select(m => m.ActorUserId).Append(preparation.PreparedByUserId).Append(preparation.UpdatedByUserId ?? preparation.PreparedByUserId), cancellationToken).ConfigureAwait(false);
        var consumptions = movements.Select(m => new IntermediateConsumption(m.Id, m.SourcePreparationId!.Value,
            calculation.Components.Single(c => c.Source.Id == m.SourcePreparationId).Source.Code, m.Quantity, m.Unit,
            m.BalanceBefore, m.BalanceAfter, actors.GetValueOrDefault(m.ActorUserId) ?? "Usuario no informado", m.OccurredAt)).ToArray();
        return new(Summary(preparation, components.Length, actors[preparation.PreparedByUserId], today), calculation, consumptions,
            preparation.DilutedPreparationId, preparation.Notes, preparation.CreatedAt, preparation.UpdatedAt,
            actors.GetValueOrDefault(preparation.UpdatedByUserId ?? preparation.PreparedByUserId) ?? "Usuario no informado", preparation.Version);
    }

    private Task<Dictionary<int, string>> Actors(IEnumerable<int> ids, CancellationToken cancellationToken)
    {
        var keys = ids.Distinct().ToArray();
        return db.Users.AsNoTracking().Where(u => keys.Contains(u.Id)).Select(u => new { u.Id, u.Name }).ToDictionaryAsync(u => u.Id, u => u.Name, cancellationToken);
    }
    private async Task<OperationResult<IntermediateDetail>> Existing(ReferencePreparation existing, string fingerprint, DateOnly today, CancellationToken cancellationToken) =>
        existing.Kind == "Intermedia" && existing.RequestFingerprint == fingerprint
            ? OperationResult.Success((await GetAsync(existing.Id, today, cancellationToken).ConfigureAwait(false))!)
            : Fail("Esta solicitud ya se utilizó con otros datos.");
    private static IntermediateSource Source(ReferencePreparation p) => new(p.Id, p.Kind, p.Code, p.Name, p.MethodId!.Value, p.SourceMethod,
        p.AvailableVolume, p.FinalVolume, p.FinalVolumeUnit, p.PreparationDate, p.ExpirationDate, p.Version, Results(p));
    private static IntermediateAnalyte[] Results(ReferencePreparation p) => p.Kind == "Stock"
        ? [new(p.SourceMaterialId!.Value, p.SourceName, p.SourceCasNumber, p.SourceLot, p.ActualConcentration, p.ConcentrationUnit)]
        : JsonSerializer.Deserialize<IntermediateAnalyte[]>(p.ResultsJson)!;
    private static IntermediateSummary Summary(ReferencePreparation p, int count, string actor, DateOnly today) => new(p.Id, p.Code, p.Name,
        p.SourceMethod, count, p.FinalVolume, p.FinalVolumeUnit, p.AvailableVolume, Results(p), p.PreparationDate, p.ExpirationDate, actor,
        p.Status != "Active" ? p.Status : p.ExpirationDate < today ? "Expired" : p.AvailableVolume == 0 ? "Depleted" : "Active");
    private static OperationResult<IntermediateDetail> Fail(string message, string code = ErrorCodes.Conflict) => OperationResult.Failure<IntermediateDetail>(new(code, message));
}

using Lims.Contracts.ReferencePreparations;
using Lims.Domain.ReferencePreparations;
using Microsoft.EntityFrameworkCore;

namespace Lims.Infrastructure.Persistence;

internal static class PreparationListQuery
{
    public static async Task<IReadOnlyList<PreparationUsage>> UsageAsync(LimsDbContext db, Guid id, CancellationToken cancellationToken)
    {
        // Existing immutable debits only. The persisted solution balance remains authoritative.
        var rows = await (from movement in db.ReferenceMaterialMovements.AsNoTracking()
            where movement.SourcePreparationId == id && movement.Kind == "IntermediateConsumption"
            join actor in db.Users.AsNoTracking() on movement.ActorUserId equals actor.Id into actors
            from actor in actors.DefaultIfEmpty()
            orderby movement.OccurredAt descending, movement.Id
            select new { movement.Id, movement.PreparationId, movement.Preparation.Code, movement.Preparation.Name,
                movement.Quantity, movement.Unit, movement.BalanceAfter, Actor = actor == null ? "Usuario no informado" : actor.Name, movement.OccurredAt })
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(r => new PreparationUsage(r.Id, r.PreparationId, r.Code, r.Name, r.Quantity, r.Unit, r.BalanceAfter, r.Actor, r.OccurredAt)).ToArray();
    }

    public static IQueryable<ReferencePreparation> Filter(this IQueryable<ReferencePreparation> query, PreparationListFilter? filter, DateOnly today)
    {
        if (filter is null) return query;
        query = filter.Status switch
        {
            "Active" => query.Where(p => (p.Status == "Active" || p.Status == "Depleted") && p.ExpirationDate >= today),
            "Expired" => query.Where(p => (p.Status == "Active" || p.Status == "Depleted" || p.Status == "Expired") && p.ExpirationDate < today),
            "Archived" => query.Where(p => p.Status == "Archived"),
            _ => query
        };
        // Thresholds match the existing availability presentation (warning at <= 60%).
        query = filter.Availability switch
        {
            "Positive" => query.Where(p => p.AvailableVolume > 0),
            "Low" => query.Where(p => p.FinalVolume > 0 && p.AvailableVolume > 0 && p.AvailableVolume <= p.FinalVolume * 0.60m),
            "Depleted" => query.Where(p => p.AvailableVolume == 0),
            _ => query
        };
        if (!string.IsNullOrWhiteSpace(filter.Method))
        {
            var term = "%" + filter.Method.Trim().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
            query = query.Where(p => EF.Functions.ILike(p.SourceMethod, term, "\\"));
        }
        return query;
    }
}

using Lims.Application.ReferenceMaterials.Ports;
using Lims.Domain.ReferenceMaterials;
using Microsoft.EntityFrameworkCore;

namespace Lims.Infrastructure.Persistence;

public sealed class ReferenceMaterialRepository(LimsDbContext dbContext) : IReferenceMaterialRepository
{
    public async Task<(IReadOnlyList<ReferenceMaterial> Items, int TotalCount)> SearchAsync(
        string? search,
        ReferenceMaterialStatus? status,
        string? method,
        DateOnly asOfDate,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = dbContext.ReferenceMaterials.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{EscapeLikePattern(search)}%";
            query = query.Where(material =>
                EF.Functions.ILike(material.Name, pattern, "\\") ||
                (material.CasNumber != null && EF.Functions.ILike(material.CasNumber, pattern, "\\")) ||
                (material.CatalogNumber != null && EF.Functions.ILike(material.CatalogNumber, pattern, "\\")) ||
                EF.Functions.ILike(material.Lot, pattern, "\\") ||
                EF.Functions.ILike(material.Brand, pattern, "\\"));
        }

        if (status == ReferenceMaterialStatus.Active)
        {
            query = query.Where(material =>
                material.Status == ReferenceMaterialStatus.Active &&
                material.AvailableQuantity > 0 &&
                material.ExpirationDate >= asOfDate);
        }
        else if (status == ReferenceMaterialStatus.Expired)
        {
            query = query.Where(material =>
                material.Status == ReferenceMaterialStatus.Active &&
                material.AvailableQuantity > 0 &&
                material.ExpirationDate < asOfDate);
        }
        else if (status == ReferenceMaterialStatus.Depleted)
        {
            query = query.Where(material =>
                material.Status == ReferenceMaterialStatus.Active &&
                material.AvailableQuantity <= 0);
        }
        else if (status is not null)
        {
            query = query.Where(material => material.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(method))
        {
            var methodPattern = $"%{EscapeLikePattern(method)}%";
            query = query.Where(material => EF.Functions.ILike(material.Method, methodPattern, "\\"));
        }

        var totalCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var items = await query
            .OrderBy(material => material.Name)
            .ThenBy(material => material.Lot)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        return (items, totalCount);
    }

    public Task<ReferenceMaterial?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.ReferenceMaterials.SingleOrDefaultAsync(
            material => material.Id == id,
            cancellationToken);

    public void Add(ReferenceMaterial material) => dbContext.ReferenceMaterials.Add(material);

    public async Task<bool> SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }

    private static string EscapeLikePattern(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("%", "\\%", StringComparison.Ordinal)
        .Replace("_", "\\_", StringComparison.Ordinal);
}

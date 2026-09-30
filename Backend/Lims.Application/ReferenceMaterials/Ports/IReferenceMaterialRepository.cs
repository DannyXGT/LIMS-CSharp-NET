using Lims.Domain.ReferenceMaterials;

namespace Lims.Application.ReferenceMaterials.Ports;

public interface IReferenceMaterialRepository
{
    Task<(IReadOnlyList<ReferenceMaterial> Items, int TotalCount)> SearchAsync(
        string? search,
        ReferenceMaterialStatus? status,
        string? method,
        DateOnly asOfDate,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<ReferenceMaterial?> FindByIdAsync(Guid id, CancellationToken cancellationToken);

    void Add(ReferenceMaterial material);

    Task<bool> SaveChangesAsync(CancellationToken cancellationToken);
}

using Lims.Domain.ReferenceMaterials;

namespace Lims.Application.ReferenceMaterials.Ports;

public interface IReferenceMaterialRepository
{
    Task<ReferenceMaterialCatalogSelection> ResolveCatalogsAsync(
        int methodId,
        int unitId,
        int locationId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ReferenceMethod>> ListActiveMethodsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<ReferenceUnit>> ListActiveUnitsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<ReferenceLocation>> ListActiveLocationsAsync(CancellationToken cancellationToken);

    Task<(IReadOnlyList<ReferenceMaterial> Items, int TotalCount)> SearchAsync(
        string? search,
        ReferenceMaterialStatus? status,
        string? method,
        DateOnly asOfDate,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<ReferenceMaterial?> FindByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<ReferenceMaterialDisplayContext> ResolveDisplayContextAsync(
        ReferenceMaterial material, CancellationToken cancellationToken);

    void Add(ReferenceMaterial material);

    Task<bool> SaveChangesAsync(CancellationToken cancellationToken);
}

public sealed record ReferenceMaterialCatalogSelection(
    ReferenceMethod? Method,
    ReferenceUnit? Unit,
    ReferenceLocation? Location);

public sealed record ReferenceMaterialDisplayContext(
    string? CreatedByName,
    string? UpdatedByName,
    string? ArchivedByName,
    string? ReplacedByMaterialName);

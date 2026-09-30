using Lims.Contracts.ReferenceMaterials;

namespace Lims.Desktop.Http;

public interface IReferenceMaterialsApiClient
{
    Task<ApiCallResult<ReferenceMaterialPage>> ListAsync(
        string? search,
        string? status,
        string? method,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<ApiCallResult<ReferenceMaterialDetail>> GetAsync(
        Guid id,
        CancellationToken cancellationToken);

    Task<ApiCallResult<ReferenceMaterialDetail>> CreateAsync(
        CreateReferenceMaterialRequest request,
        CancellationToken cancellationToken);

    Task<ApiCallResult<ReferenceMaterialDetail>> UpdateAsync(
        Guid id,
        UpdateReferenceMaterialRequest request,
        CancellationToken cancellationToken);

    Task<ApiCallResult<ReferenceMaterialDetail>> ArchiveAsync(
        Guid id,
        ArchiveReferenceMaterialRequest request,
        CancellationToken cancellationToken);

    Task<ApiCallResult<ReferenceMaterialDetail>> ReplaceAsync(
        Guid id,
        ReplaceReferenceMaterialRequest request,
        CancellationToken cancellationToken);
}

using Lims.Application.Common;
using Lims.Contracts.ReferenceMaterials;

namespace Lims.Application.ReferenceMaterials;

public interface IReferenceMaterialService
{
    Task<OperationResult<ReferenceMaterialPage>> ListAsync(
        string? search,
        string? status,
        string? method,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<OperationResult<ReferenceMaterialDetail>> GetAsync(
        Guid id,
        CancellationToken cancellationToken);

    Task<OperationResult<ReferenceMaterialDetail>> CreateAsync(
        CreateReferenceMaterialRequest request,
        int actorUserId,
        CancellationToken cancellationToken);

    Task<OperationResult<ReferenceMaterialDetail>> UpdateAsync(
        Guid id,
        UpdateReferenceMaterialRequest request,
        int actorUserId,
        CancellationToken cancellationToken);

    Task<OperationResult<ReferenceMaterialDetail>> ArchiveAsync(
        Guid id,
        ArchiveReferenceMaterialRequest request,
        int actorUserId,
        CancellationToken cancellationToken);

    Task<OperationResult<ReferenceMaterialDetail>> ReplaceAsync(
        Guid id,
        ReplaceReferenceMaterialRequest request,
        int actorUserId,
        CancellationToken cancellationToken);
}

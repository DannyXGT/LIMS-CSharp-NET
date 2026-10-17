using Lims.Application.Common;
using Lims.Contracts.ReferencePreparations;

namespace Lims.Application.ReferencePreparations;

public interface ISolutionGroupRepository
{
    Task<IntermediateOptions> OptionsAsync(DateOnly today, CancellationToken cancellationToken);
    Task<OperationResult<SolutionGroupDetail>> CreateAsync(string kind, CreateSolutionGroupRequest request, int actor,
        string fingerprint, DateTimeOffset now, CancellationToken cancellationToken);
    Task<SolutionGroupPage> ListAsync(string kind, string? search, int page, int pageSize, DateOnly today,
        CancellationToken cancellationToken, PreparationListFilter? filter = null);
    Task<SolutionGroupDetail?> GetAsync(string kind, Guid id, DateOnly today, CancellationToken cancellationToken);
}

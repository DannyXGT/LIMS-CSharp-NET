using Lims.Contracts.ReferencePreparations;

namespace Lims.Desktop.Http;

public interface ISolutionGroupApiClient
{
    Task<ApiCallResult<IntermediateOptions>> OptionsAsync(string kind, CancellationToken cancellationToken);
    Task<ApiCallResult<SolutionCalculation>> PreviewAsync(string kind, SolutionCalculationRequest request, CancellationToken cancellationToken);
    Task<ApiCallResult<SolutionGroupDetail>> CreateAsync(string kind, CreateSolutionGroupRequest request, CancellationToken cancellationToken);
    Task<ApiCallResult<SolutionGroupPage>> ListAsync(string kind, string? search, int page, CancellationToken cancellationToken, PreparationListFilter? filter = null);
    Task<ApiCallResult<SolutionGroupDetail>> GetAsync(string kind, Guid id, CancellationToken cancellationToken);
}

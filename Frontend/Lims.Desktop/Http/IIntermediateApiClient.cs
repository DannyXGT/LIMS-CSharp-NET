using Lims.Contracts.ReferencePreparations;

namespace Lims.Desktop.Http;

public interface IIntermediateApiClient
{
    Task<ApiCallResult<IntermediateOptions>> OptionsAsync(CancellationToken cancellationToken);
    Task<ApiCallResult<IntermediateCalculation>> PreviewAsync(IntermediateCalculationRequest request, CancellationToken cancellationToken);
    Task<ApiCallResult<IntermediateDetail>> CreateAsync(CreateIntermediateRequest request, CancellationToken cancellationToken);
    Task<ApiCallResult<IntermediatePage>> ListAsync(string? search, int page, CancellationToken cancellationToken);
    Task<ApiCallResult<IntermediateDetail>> GetAsync(Guid id, CancellationToken cancellationToken);
}

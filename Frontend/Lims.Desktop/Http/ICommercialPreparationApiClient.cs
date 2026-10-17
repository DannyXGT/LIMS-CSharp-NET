using Lims.Contracts.ReferencePreparations;
namespace Lims.Desktop.Http;

public interface ICommercialPreparationApiClient
{
    Task<ApiCallResult<CommercialPreparationOptions>> OptionsAsync(string kind, string? search, CancellationToken ct);
    Task<ApiCallResult<CommercialPreparationDetail>> RegisterAsync(string kind, RegisterCommercialPreparationRequest request, CancellationToken ct);
    Task<ApiCallResult<CommercialPreparationDetail>> GetAsync(string kind, Guid materialId, CancellationToken ct);
}

using Lims.Contracts.ReferencePreparations;

namespace Lims.Desktop.Http;

public interface IStockApiClient
{
    Task<ApiCallResult<StockOptions>> OptionsAsync(CancellationToken cancellationToken);
    Task<ApiCallResult<StockSourcePage>> SourcesAsync(string? search, int page, CancellationToken cancellationToken);
    Task<ApiCallResult<StockCalculation>> PreviewAsync(StockCalculationRequest request, CancellationToken cancellationToken);
    Task<ApiCallResult<StockDetail>> CreateAsync(CreateStockRequest request, CancellationToken cancellationToken);
    Task<ApiCallResult<StockPage>> ListAsync(string? search, int page, CancellationToken cancellationToken);
    Task<ApiCallResult<StockDetail>> GetAsync(Guid id, CancellationToken cancellationToken);
}

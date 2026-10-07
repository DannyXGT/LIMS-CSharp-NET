using Lims.Application.Common;
using Lims.Contracts.ReferencePreparations;

namespace Lims.Application.ReferencePreparations;

public interface IStockService
{
    StockOptions GetOptions();
    Task<OperationResult<StockSourcePage>> SourcesAsync(string? search, int page, int pageSize, CancellationToken cancellationToken);
    Task<OperationResult<StockCalculation>> PreviewAsync(StockCalculationRequest request, CancellationToken cancellationToken);
    Task<OperationResult<StockDetail>> CreateAsync(CreateStockRequest request, int actorUserId, CancellationToken cancellationToken);
    Task<OperationResult<StockPage>> ListAsync(string? search, int page, int pageSize, CancellationToken cancellationToken);
    Task<OperationResult<StockDetail>> GetAsync(Guid id, CancellationToken cancellationToken);
}

using Lims.Application.Common;
using Lims.Contracts.ReferencePreparations;
using Lims.Domain.ReferenceMaterials;
using Lims.Domain.ReferencePreparations;

namespace Lims.Application.ReferencePreparations;

public interface IStockRepository
{
    Task<StockSourcePage> SourcesAsync(string? search, DateOnly today, int page, int pageSize, CancellationToken cancellationToken);
    Task<ReferenceMaterial?> FindSourceAsync(Guid id, CancellationToken cancellationToken);
    // Callback runs after loading a fresh source inside the transaction and execution strategy.
    Task<OperationResult<StockDetail>> CreateAsync(Guid requestId, string fingerprint,
        Func<ReferenceMaterial, long, OperationResult<ReferencePreparation>> prepare, Guid sourceId,
        DateOnly today, DateTimeOffset now, CancellationToken cancellationToken);
    Task<StockPage> ListAsync(string? search, int page, int pageSize, CancellationToken cancellationToken);
    Task<StockDetail?> GetAsync(Guid id, CancellationToken cancellationToken);
}

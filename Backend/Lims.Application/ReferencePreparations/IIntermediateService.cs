using Lims.Application.Common;
using Lims.Contracts.ReferencePreparations;

namespace Lims.Application.ReferencePreparations;

public interface IIntermediateService
{
    Task<OperationResult<IntermediateOptions>> OptionsAsync(CancellationToken cancellationToken);
    Task<OperationResult<IntermediateCalculation>> PreviewAsync(IntermediateCalculationRequest request, CancellationToken cancellationToken);
    Task<OperationResult<IntermediateDetail>> CreateAsync(CreateIntermediateRequest request, int actorUserId, CancellationToken cancellationToken);
    Task<OperationResult<IntermediatePage>> ListAsync(string? search, int page, int pageSize, CancellationToken cancellationToken);
    Task<OperationResult<IntermediateDetail>> GetAsync(Guid id, CancellationToken cancellationToken);
}
public interface IIntermediateRepository
{
    Task<IntermediateOptions> OptionsAsync(DateOnly today, CancellationToken cancellationToken);
    Task<OperationResult<IntermediateDetail>> CreateAsync(CreateIntermediateRequest request, int actor, string fingerprint, DateTimeOffset now, CancellationToken cancellationToken);
    Task<IntermediatePage> ListAsync(string? search, int page, int pageSize, DateOnly today, CancellationToken cancellationToken);
    Task<IntermediateDetail?> GetAsync(Guid id, DateOnly today, CancellationToken cancellationToken);
}

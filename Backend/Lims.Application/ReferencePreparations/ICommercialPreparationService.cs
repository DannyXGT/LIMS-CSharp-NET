using Lims.Application.Common;
using Lims.Contracts.ReferencePreparations;

namespace Lims.Application.ReferencePreparations;

public interface ICommercialPreparationService
{
    Task<CommercialPreparationOptions> OptionsAsync(string kind, string? search, CancellationToken ct);
    Task<OperationResult<CommercialPreparationDetail>> RegisterAsync(string kind, RegisterCommercialPreparationRequest request, int actor, CancellationToken ct);
    Task<OperationResult<CommercialPreparationDetail>> GetAsync(string kind, Guid materialId, CancellationToken ct);
}

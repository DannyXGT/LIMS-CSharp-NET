using Lims.Application.Common;
using Lims.Contracts.Permissions;

namespace Lims.Application.Permissions;

public sealed class PermissionOptions
{
    public bool EnforceIndividualPermissions { get; set; }
}
public interface IPermissionRepository
{
    Task<OperationResult<PermissionUsers>> UsersAsync(int actor, string? search, CancellationToken ct);
    Task<OperationResult<UserPermissionState>> GetAsync(int actor, int user, CancellationToken ct);
    Task<OperationResult<UserPermissionState>> SaveAsync(int actor, int user, SavePermissionsRequest request, CancellationToken ct);
    Task<OperationResult<PermissionHistoryPage>> HistoryAsync(int actor, int user, int page, CancellationToken ct);
    Task<EffectivePermissions?> EffectiveAsync(int user, CancellationToken ct);
}

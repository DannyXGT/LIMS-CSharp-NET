using Lims.Contracts.Permissions;

namespace Lims.Desktop.Http;

public interface IPermissionsApiClient
{
    Task<ApiCallResult<PermissionUsers>> UsersAsync(string? search, CancellationToken ct);
    Task<ApiCallResult<IReadOnlyList<PermissionDefinition>>> CatalogAsync(CancellationToken ct);
    Task<ApiCallResult<UserPermissionState>> GetAsync(int user, CancellationToken ct);
    Task<ApiCallResult<UserPermissionState>> SaveAsync(int user, SavePermissionsRequest request, CancellationToken ct);
    Task<ApiCallResult<PermissionHistoryPage>> HistoryAsync(int user, int page, CancellationToken ct);
    Task<ApiCallResult<EffectivePermissions>> EffectiveAsync(CancellationToken ct);
}

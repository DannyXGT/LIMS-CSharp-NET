using Lims.Contracts.Errors;
using Lims.Contracts.Permissions;
using Lims.Desktop.Http;

namespace Lims.Desktop.VisualHarness;

internal sealed class FixturePermissionsApi : IPermissionsApiClient
{
    private readonly PermissionUser[] _users = [new(84, "Ana López", "ana.lopez@example.invalid", "Analista", PermissionCatalog.DepartmentName), new(85, "Carlos Méndez", "carlos.mendez@example.invalid", "Supervisor", PermissionCatalog.DepartmentName), new(86, "Lucía Pérez", "lucia.perez@example.invalid", "Analista", PermissionCatalog.DepartmentName)];
    private UserPermissionState? _state;
    private readonly List<PermissionHistory> _history = [new(PermissionCatalog.StockView, "Ana López", "Administrador de prueba", "Otorgado", false, true, new DateTimeOffset(2026, 10, 12, 16, 30, 0, TimeSpan.Zero))];
    public bool FailSave { get; set; }
    public EffectivePermissions Current { get; set; } = new("Administrador", [], true);
    private static Task<ApiCallResult<T>> Ok<T>(T value) => Task.FromResult(new ApiCallResult<T>(true, value, null, 200));
    public Task<ApiCallResult<PermissionUsers>> UsersAsync(string? search, CancellationToken ct) => Ok(new PermissionUsers(_users.Where(u => string.IsNullOrWhiteSpace(search) || u.Name.Contains(search, StringComparison.OrdinalIgnoreCase) || u.Email.Contains(search, StringComparison.OrdinalIgnoreCase)).ToArray(), true));
    public Task<ApiCallResult<IReadOnlyList<PermissionDefinition>>> CatalogAsync(CancellationToken ct) => Ok(PermissionCatalog.All);
    public Task<ApiCallResult<UserPermissionState>> GetAsync(int user, CancellationToken ct)
    {
        if (_state?.User.Id != user) _state = new(_users.Single(u => u.Id == user), ["reference_materials.view", PermissionCatalog.StockView, PermissionCatalog.IntermediateView], Guid.NewGuid(), true);
        return Ok(_state);
    }
    public Task<ApiCallResult<UserPermissionState>> SaveAsync(int user, SavePermissionsRequest request, CancellationToken ct)
    {
        if (FailSave) return Task.FromResult(new ApiCallResult<UserPermissionState>(false, null, new(ErrorCodes.NetworkUnavailable, "No se pudo conectar con el servidor. Los cambios siguen pendientes.", "fixture-only"), 0));
        foreach (var code in _state!.Permissions.Concat(request.Permissions).Distinct(StringComparer.Ordinal))
        {
            var before = _state.Permissions.Contains(code); var after = request.Permissions.Contains(code);
            if (before != after) _history.Insert(0, new(code, _state.User.Name, "Administrador de prueba", after ? "Otorgado" : "Revocado", before, after, DateTimeOffset.UtcNow));
        }
        _state = _state with { Permissions = request.Permissions, Version = Guid.NewGuid() }; return Ok(_state);
    }
    public Task<ApiCallResult<PermissionHistoryPage>> HistoryAsync(int user, int page, CancellationToken ct) => Ok(new PermissionHistoryPage(_history.ToArray(), page, _history.Count));
    public Task<ApiCallResult<EffectivePermissions>> EffectiveAsync(CancellationToken ct) => Ok(Current);
}

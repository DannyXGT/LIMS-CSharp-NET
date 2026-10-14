using Lims.Contracts.Errors;
using Lims.Contracts.Permissions;
using Lims.Desktop.Http;

namespace Lims.Desktop.VisualHarness;

internal sealed class FixturePermissionsApi : IPermissionsApiClient
{
    private readonly PermissionUser[] _users = [new(84, "Ana López", "ana.lopez@example.invalid", "Analista", PermissionCatalog.DepartmentName), new(85, "Carlos Méndez", "carlos.mendez@example.invalid", "Supervisor", PermissionCatalog.DepartmentName), new(86, "Lucía Pérez", "lucia.perez@example.invalid", "Analista", PermissionCatalog.DepartmentName), new(87, "Administrador de prueba", "admin@example.invalid", "Administrador", PermissionCatalog.DepartmentName),
        .. Enumerable.Range(1, 12).Select(i => new PermissionUser(100 + i, $"Analista de prueba {i:00}", $"analista{i:00}@example.invalid", "Analista", PermissionCatalog.DepartmentName))];
    private readonly Dictionary<int, UserPermissionState> _states = [];
    private readonly List<PermissionHistory> _history = Enumerable.Range(0, 36).Select(i => new PermissionHistory(PermissionCatalog.StockView, "Ana López", "Administrador de prueba", i % 2 == 0 ? "Otorgado" : "Revocado", i % 2 != 0, i % 2 == 0, new DateTimeOffset(2026, 10, 12, 16, 30, 0, TimeSpan.Zero).AddMinutes(-i))).ToList();
    public int SaveCalls { get; private set; }
    public int GetDelayMilliseconds { get; set; } = 250;
    public int SaveDelayMilliseconds { get; set; } = 650;
    public bool FailSave { get; set; }
    public EffectivePermissions Current { get; set; } = new("Administrador", [], true);
    private static Task<ApiCallResult<T>> Ok<T>(T value) => Task.FromResult(new ApiCallResult<T>(true, value, null, 200));
    public Task<ApiCallResult<PermissionUsers>> UsersAsync(string? search, CancellationToken ct) => Ok(new PermissionUsers(_users.Where(u => string.IsNullOrWhiteSpace(search) || u.Name.Contains(search, StringComparison.OrdinalIgnoreCase) || u.Email.Contains(search, StringComparison.OrdinalIgnoreCase)).ToArray(), true));
    public Task<ApiCallResult<IReadOnlyList<PermissionDefinition>>> CatalogAsync(CancellationToken ct) => Ok(PermissionCatalog.All);
    public async Task<ApiCallResult<UserPermissionState>> GetAsync(int user, CancellationToken ct)
    {
        await Task.Delay(GetDelayMilliseconds, ct);
        if (!_states.TryGetValue(user, out var state))
        {
            state = new(_users.Single(u => u.Id == user), user is 85 or 86 or 87 ? [] : ["reference_materials.view", PermissionCatalog.StockView, PermissionCatalog.IntermediateView], user is 85 or 87 ? Guid.Empty : Guid.NewGuid(), user is not (85 or 87));
            _states[user] = state;
        }
        return new(true, state, null, 200);
    }
    public async Task<ApiCallResult<UserPermissionState>> SaveAsync(int user, SavePermissionsRequest request, CancellationToken ct)
    {
        SaveCalls++; await Task.Delay(SaveDelayMilliseconds, ct);
        if (FailSave) return new(false, null, new(ErrorCodes.NetworkUnavailable, "No se pudo conectar con el servidor. Los cambios siguen pendientes.", "fixture-only"), 0);
        var state = _states[user];
        foreach (var code in state.Permissions.Concat(request.Permissions).Distinct(StringComparer.Ordinal))
        {
            var before = state.Permissions.Contains(code); var after = request.Permissions.Contains(code);
            if (before != after) _history.Insert(0, new(code, state.User.Name, "Administrador de prueba", after ? "Otorgado" : "Revocado", before, after, DateTimeOffset.UtcNow));
        }
        state = state with { Permissions = request.Permissions, Version = Guid.NewGuid(), IsConfigured = true }; _states[user] = state; return new(true, state, null, 200);
    }
    public Task<ApiCallResult<PermissionHistoryPage>> HistoryAsync(int user, int page, CancellationToken ct)
    {
        var history = _history.Where(h => h.User == _users.Single(u => u.Id == user).Name).ToArray();
        return Ok(new PermissionHistoryPage(history.Skip((page - 1) * 30).Take(30).ToArray(), page, history.Length));
    }
    public Task<ApiCallResult<EffectivePermissions>> EffectiveAsync(CancellationToken ct) => Ok(Current);
}

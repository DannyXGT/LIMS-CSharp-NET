using Lims.Application.Common;
using Lims.Application.Permissions;
using Lims.Contracts.Errors;
using Lims.Contracts.Permissions;

namespace Lims.Api.Tests;

public sealed class TestPermissionRepository : IPermissionRepository
{
    public EffectivePermissions? Current { get; set; } = new("Administrador", [], true);
    public int LastActor { get; private set; }
    public UserPermissionState State { get; private set; } = new(new(84, "Ana López", "ana@intertek.com", "Analista", PermissionCatalog.DepartmentName), [], Guid.Empty, false);
    private readonly List<PermissionHistory> _history = [];
    public Task<EffectivePermissions?> EffectiveAsync(int user, CancellationToken ct) => Task.FromResult(Current);
    public Task<OperationResult<PermissionUsers>> UsersAsync(int actor, string? search, CancellationToken ct) => Task.FromResult(OperationResult.Success(new PermissionUsers([State.User], true)));
    public Task<OperationResult<UserPermissionState>> GetAsync(int actor, int user, CancellationToken ct) => Task.FromResult(user == State.User.Id ? OperationResult.Success(State) : Denied());
    public Task<OperationResult<UserPermissionState>> SaveAsync(int actor, int user, SavePermissionsRequest request, CancellationToken ct)
    {
        LastActor = actor;
        if (user != State.User.Id) return Task.FromResult(Denied());
        foreach (var code in State.Permissions.Concat(request.Permissions).Distinct(StringComparer.Ordinal))
        {
            var before = State.Permissions.Contains(code); var after = request.Permissions.Contains(code);
            if (before != after) _history.Add(new(code, State.User.Name, "Administrador de prueba", after ? "Otorgado" : "Revocado", before, after, DateTimeOffset.UtcNow));
        }
        State = State with { Permissions = request.Permissions, Version = Guid.NewGuid(), IsConfigured = true };
        return Task.FromResult(OperationResult.Success(State));
    }
    public Task<OperationResult<PermissionHistoryPage>> HistoryAsync(int actor, int user, int page, CancellationToken ct) => Task.FromResult(OperationResult.Success(new PermissionHistoryPage(_history.ToArray(), page, _history.Count)));
    private static OperationResult<UserPermissionState> Denied() => OperationResult.Failure<UserPermissionState>(new(ErrorCodes.Forbidden, "Usuario fuera de alcance."));
}

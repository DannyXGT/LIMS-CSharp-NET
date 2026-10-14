using Lims.Contracts.Authentication;
using Lims.Contracts.Errors;
using Lims.Contracts.Permissions;
using Lims.Desktop.Http;
using Lims.Desktop.Services;
using Lims.Desktop.ViewModels;

namespace Lims.Desktop.Tests;

public sealed class PermissionsViewModelTests
{
    [Fact]
    public async Task ReadsOnlySavedAssignmentsShowsPendingChangesAndPreservesThemOnNetworkFailure()
    {
        var api = new FakeApi(); var vm = new PermissionsViewModel(api, new Session());
        await vm.LoadUsersAsync(); await vm.SelectAsync(api.State.User);
        Assert.False(vm.HasPendingChanges); Assert.Single(vm.Groups.SelectMany(g => g.Items), c => c.IsGranted);
        var create = vm.Groups.SelectMany(g => g.Items).Single(c => c.Definition.Code == PermissionCatalog.StockCreate); create.IsGranted = true;
        Assert.True(vm.HasPendingChanges); Assert.True(vm.CanSave); Assert.False(await vm.SelectAsync(api.State.User with { Id = 90 }));
        api.FailSave = true; Assert.False(await vm.SaveAsync()); Assert.True(vm.HasPendingChanges); Assert.True(create.IsGranted); Assert.Contains("pendientes", vm.Message, StringComparison.Ordinal);
        api.FailSave = false; Assert.True(await vm.SaveAsync()); Assert.False(vm.HasPendingChanges);
        Assert.Contains(PermissionCatalog.StockCreate, api.LastRequest!.Permissions); Assert.Equal(api.InitialVersion, api.LastRequest.Version);
        Assert.Contains("confirmados", vm.Message, StringComparison.Ordinal); Assert.Single(vm.History);
    }
    [Fact]
    public async Task CommonUserCannotLoadProtectedInformationAndNavigationUsesIndividualPermissions()
    {
        var api = new FakeApi(); var session = new Session(); session.UpdateAuthorization("Analista", [PermissionCatalog.StockView]);
        var vm = new PermissionsViewModel(api, session); await vm.LoadUsersAsync();
        Assert.Equal(0, api.Calls); Assert.Empty(vm.Users); Assert.Contains("Acceso denegado", vm.Message, StringComparison.Ordinal);
        var shell = new ShellViewModel(session, null!, null!, api); shell.RefreshProfile();
        Assert.False(shell.IsAdministrator); Assert.False(shell.CanViewReferenceMaterials); Assert.True(shell.CanViewStock); Assert.True(shell.CanViewPreparations); Assert.False(shell.CanViewIntermediate);
        api.Effective = new("Analista", [], true); await shell.RefreshAccessAsync();
        Assert.False(shell.CanViewPreparations); Assert.False(shell.CanViewStock);
    }
    [Fact]
    public async Task ExplicitDiscardLoadsAnotherUserAndEmptyFirstConfigurationIsSaveableWithoutFakeChanges()
    {
        var api = new FakeApi(); var vm = new PermissionsViewModel(api, new Session());
        await vm.LoadUsersAsync(); await vm.SelectAsync(api.State.User);
        vm.Groups[0].Items[0].IsGranted = true; Assert.True(vm.HasPendingChanges);
        api.State = api.State with { User = api.State.User with { Id = 90 }, Permissions = [], Version = Guid.Empty, IsConfigured = false };
        Assert.True(await vm.SelectAsync(api.State.User, true)); Assert.False(vm.HasPendingChanges); Assert.True(vm.CanSave);
        Assert.DoesNotContain(vm.Groups.SelectMany(g => g.Items), p => p.IsGranted);
    }
    [Fact]
    public async Task AdministratorKeepsIdentityAndManagementNavigationWhileUnverifiedOperationalAccessFailsClosed()
    {
        var api = new FakeApi(); var session = new Session(); var shell = new ShellViewModel(session, null!, null!, api);
        shell.RefreshProfile(); Assert.True(shell.IsAdministrator); Assert.True(shell.CanViewReferenceMaterials); Assert.True(shell.CanViewStock); Assert.True(shell.CanViewIntermediate);
        api.FailEffective = true;
        Assert.False(await shell.RefreshAccessAsync());
        Assert.True(shell.IsAdministrator); Assert.Equal("Administrador", shell.Role); Assert.Equal("Administración", shell.Department);
        Assert.Equal("Administrador", session.Profile!.Role); Assert.True(shell.HasAccessWarning);
        Assert.False(shell.CanViewReferenceMaterials); Assert.False(shell.CanViewPreparations);
        api.FailEffective = false; Assert.True(await shell.RefreshAccessAsync());
        Assert.False(shell.HasAccessWarning); Assert.True(shell.CanViewReferenceMaterials); Assert.True(shell.CanViewPreparations);
        api.Effective = new("Analista", [], true); Assert.True(await shell.RefreshAccessAsync());
        Assert.False(shell.IsAdministrator); Assert.False(shell.CanViewPreparations);
    }
    private sealed class FakeApi : IPermissionsApiClient
    {
        public Guid InitialVersion { get; } = Guid.NewGuid();
        public UserPermissionState State { get; set; }
        public FakeApi() => State = new(new(84, "Ana López", "ana@intertek.com", "Analista", PermissionCatalog.DepartmentName), [PermissionCatalog.StockView], InitialVersion, true);
        public bool FailSave { get; set; }
        public bool FailEffective { get; set; }
        public int Calls { get; private set; }
        public SavePermissionsRequest? LastRequest { get; private set; }
        public EffectivePermissions Effective { get; set; } = new("Administrador", [], true);
        private static Task<ApiCallResult<T>> Ok<T>(T value) => Task.FromResult(new ApiCallResult<T>(true, value, null, 200));
        public Task<ApiCallResult<PermissionUsers>> UsersAsync(string? search, CancellationToken ct) { Calls++; return Ok(new PermissionUsers([State.User], true)); }
        public Task<ApiCallResult<IReadOnlyList<PermissionDefinition>>> CatalogAsync(CancellationToken ct) { Calls++; return Ok(PermissionCatalog.All); }
        public Task<ApiCallResult<UserPermissionState>> GetAsync(int user, CancellationToken ct) => Ok(State);
        public Task<ApiCallResult<UserPermissionState>> SaveAsync(int user, SavePermissionsRequest request, CancellationToken ct)
        {
            LastRequest = request;
            if (FailSave) return Task.FromResult(new ApiCallResult<UserPermissionState>(false, null, new(ErrorCodes.NetworkUnavailable, "Sin red. Cambios pendientes.", "test"), 0));
            State = State with { Permissions = request.Permissions, Version = Guid.NewGuid(), IsConfigured = true }; return Ok(State);
        }
        public Task<ApiCallResult<PermissionHistoryPage>> HistoryAsync(int user, int page, CancellationToken ct) => Ok(new PermissionHistoryPage([new(PermissionCatalog.StockView, "Ana López", "Administrador", "Otorgado", false, true, DateTimeOffset.UtcNow)], page, 1));
        public Task<ApiCallResult<EffectivePermissions>> EffectiveAsync(CancellationToken ct) => FailEffective ? Task.FromResult(new ApiCallResult<EffectivePermissions>(false, null, null, 503)) : Ok(Effective);
    }
    private sealed class Session : ISessionService
    {
        public string? AccessToken => "test";
        public UserProfile? Profile { get; private set; } = new(1, "Administrador", "admin@intertek.com", "Administrador", "Administración", []);
        public void UpdateAuthorization(string role, IReadOnlyList<string> permissions) => Profile = Profile! with { Role = role, Permissions = permissions };
        public Task SetAuthenticatedAsync(AuthenticationResponse response, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<string?> ReadRefreshTokenAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);
        public Task ClearAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

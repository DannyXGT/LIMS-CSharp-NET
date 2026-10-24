using Lims.Contracts.Alerts;
using Lims.Contracts.Authentication;
using Lims.Contracts.Permissions;
using Lims.Desktop.Http;
using Lims.Desktop.Services;
using Lims.Desktop.ViewModels;

namespace Lims.Desktop.Tests;

public sealed class AlertsViewModelTests
{
    [Fact]
    public async Task ManualEvaluationRequiresAdministratorSuppressesDoubleClickAndRefreshesBellAndHealthyZero()
    {
        var api = new Fake(); var session = new Session(); var model = new AlertsViewModel(api, session);
        await model.EvaluateAsync(); Assert.Equal(0, api.EvaluationCalls);
        session.Profile = session.Profile! with { Role = "Administrador" };
        var bellUpdates = 0; model.NotificationsChanged += (_, _) => bellUpdates++;
        var evaluation = model.EvaluateAsync(); await model.EvaluateAsync(); Assert.Equal(1, api.EvaluationCalls);
        Assert.True(model.IsActionBusy); Assert.Single(api.Loads);
        api.Loads[0].SetResult(new(true, Page with { Summary = Page.Summary with { Active = 0 } }, null, 200));
        await evaluation;
        Assert.Equal(AlertScreenState.Empty, model.State); Assert.True(model.Synchronized);
        Assert.Equal(1, bellUpdates); Assert.False(model.IsActionBusy); Assert.True(model.IsMessageInformational);
    }
    private static AlertPage Page => new([], 1, 30, 0, new(3, 1, 1, new(2026, 10, 28), 1, 2, 0, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
    [Fact]
    public async Task PendingAttentionIsIndependentOfUnreadCountAndServerFailureIsNotZero()
    {
        var api = new Fake(); var model = new AlertsViewModel(api, new Session()); var first = model.LoadAsync();
        api.Loads[0].SetResult(new(true, Page, null, 200)); await first;
        Assert.Equal(2, model.Data!.Summary.Pending); Assert.Equal(0, model.Data.Summary.Unread);
        var fail = model.LoadAsync(); api.Loads[1].SetResult(new(false, null, null, 503)); await fail;
        Assert.Null(model.Data); Assert.NotEmpty(model.Message); Assert.False(model.IsBusy);
    }
    [Fact]
    public async Task LogoutOrRevocationCannotRestoreAnOldResponse()
    {
        var api = new Fake(); var model = new AlertsViewModel(api, new Session()); var first = model.LoadAsync(); model.Clear();
        api.Loads[0].SetResult(new(true, Page, null, 200)); await first;
        Assert.Null(model.Data); Assert.Empty(model.Items); Assert.False(model.IsBusy);
    }
    [Fact]
    public async Task UserChangeDiscardsPreviousUsersData()
    {
        var api = new Fake(); var session = new Session(); var model = new AlertsViewModel(api, session); var first = model.LoadAsync();
        session.Profile = session.Profile! with { Id = 999 };
        api.Loads[0].SetResult(new(true, Page, null, 200)); await first; Assert.Null(model.Data);
    }
    [Fact]
    public async Task OpeningADetailDuringRefreshDoesNotLeaveSynchronizationRunning()
    {
        var api = new Fake(); var model = new AlertsViewModel(api, new Session()); var first = model.LoadAsync();
        await model.OpenAsync(Guid.NewGuid()); api.Loads[0].SetResult(new(true, Page, null, 200)); await first; Assert.False(model.IsBusy);
    }
    [Fact]
    public async Task IdenticalLoadsShareOneRequestAndNewFiltersCancelThePreviousOne()
    {
        var api = new Fake(); var model = new AlertsViewModel(api, new Session());
        var first = model.LoadAsync(); var duplicate = model.LoadAsync(); Assert.Same(first, duplicate); Assert.Single(api.Loads);
        model.Filter = new(Search: "nuevo"); var second = model.LoadAsync(); Assert.True(api.Tokens[0].IsCancellationRequested);
        api.Loads[1].SetResult(new(true, Page, null, 200)); await second;
        var latest = model.Data; api.Loads[0].SetResult(new(false, null, null, 404)); await first;
        Assert.Same(latest, model.Data); Assert.Empty(model.Message); Assert.False(model.IsBusy);
    }
    [Theory] [InlineData(false, false, AlertScreenState.Empty)] [InlineData(true, false, AlertScreenState.FilteredEmpty)] [InlineData(false, true, AlertScreenState.EnginePending)]
    public async Task EmptyAndEnginePendingRequireASuccessfulServerResponse(bool filtered, bool pending, AlertScreenState expected)
    {
        var api = new Fake(); var model = new AlertsViewModel(api, new Session()) { Filter = new(Search: filtered ? "sin coincidencia" : null) };
        var load = model.LoadAsync(); Assert.Equal(AlertScreenState.Loading, model.State);
        api.Loads[0].SetResult(new(true, Page with { Summary = Page.Summary with { Active = 0, LastEvaluation = pending ? null : DateTimeOffset.UtcNow } }, null, 200));
        await load; Assert.Equal(expected, model.State); Assert.Equal(!pending, model.Synchronized);
        var fail = model.LoadAsync(); api.Loads[1].SetResult(new(false, null, null, 404)); await fail;
        Assert.Equal(AlertScreenState.Unavailable, model.State); Assert.False(model.Synchronized); Assert.Equal(404, model.FailureStatus);
        Assert.DoesNotContain("Primera evaluación", model.UpdateDisplay, StringComparison.Ordinal);
    }
    private sealed class Session : ISessionService
    {
        public string? AccessToken => null;
        public UserProfile? Profile { get; set; } = new(42, "Usuario de prueba", "test@example.invalid", "Analista", "Laboratorio Analítico", [PermissionCatalog.AlertsView]);
        public void UpdateAuthorization(string role, IReadOnlyList<string> permissions) => Profile = Profile! with { Role = role, Permissions = permissions };
        public Task SetAuthenticatedAsync(AuthenticationResponse r, CancellationToken ct) => Task.CompletedTask;
        public Task<string?> ReadRefreshTokenAsync(CancellationToken ct) => Task.FromResult<string?>(null);
        public Task ClearAsync(CancellationToken ct) { Profile = null; return Task.CompletedTask; }
    }
    private sealed class Fake : IAlertsApiClient
    {
        public int EvaluationCalls { get; private set; }
        public Task<ApiCallResult<AlertJobResult>> EvaluateAsync(CancellationToken ct) { EvaluationCalls++; return Task.FromResult(new ApiCallResult<AlertJobResult>(true, new(true, 12, 0, 0), null, 200)); }
        public Task<ApiCallResult<IReadOnlyList<AlertEvaluationItem>>> EvaluationsAsync(CancellationToken ct) => Task.FromResult(new ApiCallResult<IReadOnlyList<AlertEvaluationItem>>(true, [], null, 200));
        public List<TaskCompletionSource<ApiCallResult<AlertPage>>> Loads { get; } = [];
        public List<CancellationToken> Tokens { get; } = [];
        public Task<ApiCallResult<AlertPage>> ListAsync(AlertFilter f, CancellationToken ct) { var completion = new TaskCompletionSource<ApiCallResult<AlertPage>>(); Loads.Add(completion); Tokens.Add(ct); return completion.Task; }
        public Task<ApiCallResult<AlertDetail>> DetailAsync(Guid id, CancellationToken ct) => Task.FromResult(new ApiCallResult<AlertDetail>(false, null, null, 403));
        public Task<ApiCallResult<AlertDetail>> AcknowledgeAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<ApiCallResult<NotificationPage>> NotificationsAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<ApiCallResult<bool>> ReadAsync(Guid? id, CancellationToken ct) => throw new NotSupportedException();
        public Task<ApiCallResult<bool>> ClaimWindowsAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<ApiCallResult<NotificationPreferences>> PreferencesAsync(NotificationPreferences p, CancellationToken ct) => throw new NotSupportedException();
        public Task<ApiCallResult<AlertAdministration>> AdministrationAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<ApiCallResult<AlertAdministration>> ConfigureAsync(AlertSettings s, CancellationToken ct) => throw new NotSupportedException();
        public Task<ApiCallResult<DeliveryPage>> DeliveriesAsync(int page, CancellationToken ct) => throw new NotSupportedException();
        public Task<ApiCallResult<DeliveryItem>> TestEmailAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
    }
}

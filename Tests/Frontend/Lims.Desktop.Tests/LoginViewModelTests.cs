using Lims.Contracts.Authentication;
using Lims.Desktop.Services;
using Lims.Desktop.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lims.Desktop.Tests;

public sealed class LoginViewModelTests
{
    [Fact]
    public async Task SignInCommandPreventsDoubleSubmit()
    {
        var gateway = new ControllableGateway();
        var navigation = new FakeNavigation();
        var viewModel = CreateViewModel(gateway, navigation);
        viewModel.Identifier = "test.user";
        viewModel.Password = "password";

        var first = viewModel.SignInCommand.ExecuteAsync(null);
        var second = viewModel.SignInCommand.ExecuteAsync(null);
        await gateway.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, gateway.CallCount);
        Assert.True(viewModel.IsBusy);
        gateway.Complete(new AuthenticationOutcome(AuthenticationOutcomeKind.InvalidCredentials));
        await Task.WhenAll(first, second);
    }

    [Fact]
    public async Task InvalidCredentialsProducesExplicitStateAndMessage()
    {
        var gateway = new ControllableGateway();
        var viewModel = CreateViewModel(gateway, new FakeNavigation());
        viewModel.Identifier = "test.user";
        viewModel.Password = "wrong";

        var execution = viewModel.SignInCommand.ExecuteAsync(null);
        await gateway.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        gateway.Complete(new AuthenticationOutcome(
            AuthenticationOutcomeKind.InvalidCredentials,
            CorrelationId: "support-123"));
        await execution;

        Assert.Equal(LoginState.InvalidCredentials, viewModel.State);
        Assert.Contains("no son válidos", viewModel.Message, StringComparison.Ordinal);
        Assert.Contains("support-123", viewModel.SupportId, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SuccessfulLoginClearsPasswordAndNavigatesOnce()
    {
        var gateway = new ControllableGateway();
        var navigation = new FakeNavigation();
        var viewModel = CreateViewModel(gateway, navigation);
        viewModel.Identifier = "test.user";
        viewModel.Password = "correct";

        var execution = viewModel.SignInCommand.ExecuteAsync(null);
        await gateway.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        gateway.Complete(new AuthenticationOutcome(
            AuthenticationOutcomeKind.Authenticated,
            CreateProfile()));
        await execution;

        Assert.Equal(LoginState.Authenticated, viewModel.State);
        Assert.Empty(viewModel.Password);
        Assert.Equal(1, navigation.ShellNavigations);
    }

    private static LoginViewModel CreateViewModel(
        IAuthenticationGateway gateway,
        FakeNavigation navigation)
    {
        var session = new FakeSession();
        var shell = new ShellViewModel(session, gateway, navigation);
        return new LoginViewModel(
            gateway,
            navigation,
            shell,
            new DesktopOptions
            {
                ApiBaseUrl = "https://localhost/",
                CorporateDomain = "intertek.com",
                EnvironmentName = "Testing",
            });
    }

    private static UserProfile CreateProfile() => new(
        42,
        "Test User",
        "test.user@intertek.com",
        "Usuario",
        "Laboratorio Analítico",
        []);

    private sealed class ControllableGateway : IAuthenticationGateway
    {
        private readonly TaskCompletionSource<AuthenticationOutcome> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int CallCount { get; private set; }

        public Task<AuthenticationOutcome> SignInAsync(
            string identifier,
            string password,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Started.TrySetResult();
            return _completion.Task.WaitAsync(cancellationToken);
        }

        public Task<LogoutOutcome> LogoutAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new LogoutOutcome(true));

        public void Complete(AuthenticationOutcome outcome) => _completion.TrySetResult(outcome);
    }

    private sealed class FakeNavigation : INavigationService
    {
        public int ShellNavigations { get; private set; }

        public void Initialize(ContentControl host, FrameworkElement loginView, FrameworkElement shellView)
        {
        }

        public void ShowLogin()
        {
        }

        public void ShowAuthenticatedShell() => ShellNavigations++;
    }

    private sealed class FakeSession : ISessionService
    {
        public string? AccessToken => null;

        public UserProfile? Profile => CreateProfile();

        public Task SetAuthenticatedAsync(AuthenticationResponse response, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<string?> ReadRefreshTokenAsync(CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);

        public Task ClearAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

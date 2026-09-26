using Lims.Contracts.Authentication;
using Lims.Desktop.Http;
using Lims.Desktop.Services;

namespace Lims.Desktop.Tests;

public sealed class SessionAndRefreshTests
{
    [Fact]
    public async Task SessionStoresOnlyRefreshTokenInSecureStorageAndClearsAllState()
    {
        var storage = new FakeSecureStorage();
        var session = new SessionService(storage);
        var response = CreateResponse("access-one", "refresh-one");

        await session.SetAuthenticatedAsync(response, CancellationToken.None);

        Assert.Equal("access-one", session.AccessToken);
        Assert.Equal(42, session.Profile?.Id);
        Assert.Equal("refresh-one", storage.RefreshToken);

        await session.ClearAsync(CancellationToken.None);
        Assert.Null(session.AccessToken);
        Assert.Null(session.Profile);
        Assert.Null(storage.RefreshToken);
    }

    [Fact]
    public async Task ConcurrentRefreshCallsShareOneApiOperation()
    {
        var storage = new FakeSecureStorage { RefreshToken = "stored-refresh" };
        var session = new SessionService(storage);
        var api = new ControllableAuthenticationApi();
        var coordinator = new TokenRefreshCoordinator(api, session);

        var first = coordinator.RefreshAsync(CancellationToken.None);
        var second = coordinator.RefreshAsync(CancellationToken.None);
        await api.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, api.RefreshCalls);
        api.Complete(CreateResponse("access-two", "refresh-two"));
        Assert.True(await first);
        Assert.True(await second);
        Assert.Equal("access-two", session.AccessToken);
        Assert.Equal("refresh-two", storage.RefreshToken);
    }

    private static AuthenticationResponse CreateResponse(string accessToken, string refreshToken) => new(
        accessToken,
        DateTimeOffset.UtcNow.AddMinutes(10),
        refreshToken,
        DateTimeOffset.UtcNow.AddHours(12),
        new UserProfile(
            42,
            "Test User",
            "test.user@intertek.com",
            "Usuario",
            "Laboratorio Analítico",
            []));

    private sealed class FakeSecureStorage : ISecureStorage
    {
        public string? RefreshToken { get; set; }

        public Task<string?> ReadRefreshTokenAsync(CancellationToken cancellationToken) =>
            Task.FromResult(RefreshToken);

        public Task WriteRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken)
        {
            RefreshToken = refreshToken;
            return Task.CompletedTask;
        }

        public Task ClearRefreshTokenAsync(CancellationToken cancellationToken)
        {
            RefreshToken = null;
            return Task.CompletedTask;
        }
    }

    private sealed class ControllableAuthenticationApi : IAuthenticationApiClient
    {
        private readonly TaskCompletionSource<ApiCallResult<AuthenticationResponse>> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int RefreshCalls { get; private set; }

        public Task<ApiCallResult<AuthenticationResponse>> LoginAsync(
            LoginRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ApiCallResult<AuthenticationResponse>> RefreshAsync(
            RefreshRequest request,
            CancellationToken cancellationToken)
        {
            RefreshCalls++;
            Started.TrySetResult();
            return _completion.Task;
        }

        public Task<bool> LogoutAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public void Complete(AuthenticationResponse response) =>
            _completion.TrySetResult(new ApiCallResult<AuthenticationResponse>(true, response, null, 200));
    }
}

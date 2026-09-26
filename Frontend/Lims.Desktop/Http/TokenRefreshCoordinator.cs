using Lims.Contracts.Authentication;
using Lims.Contracts.Errors;
using Lims.Desktop.Services;

namespace Lims.Desktop.Http;

internal sealed class TokenRefreshCoordinator(
    IAuthenticationApiClient authenticationApi,
    ISessionService session) : ITokenRefreshCoordinator
{
    private readonly object _gate = new();
    private Task<bool>? _activeRefresh;

    public Task<bool> RefreshAsync(CancellationToken cancellationToken)
    {
        Task<bool> refresh;
        lock (_gate)
        {
            if (_activeRefresh is null || _activeRefresh.IsCompleted)
            {
                _activeRefresh = RefreshCoreAsync();
            }

            refresh = _activeRefresh;
        }

        return refresh.WaitAsync(cancellationToken);
    }

    private async Task<bool> RefreshCoreAsync()
    {
        var refreshToken = await session.ReadRefreshTokenAsync(CancellationToken.None).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return false;
        }

        try
        {
            var result = await authenticationApi.RefreshAsync(
                    new RefreshRequest(refreshToken),
                    CancellationToken.None)
                .ConfigureAwait(false);
            if (result.IsSuccess && result.Value is not null)
            {
                await session.SetAuthenticatedAsync(result.Value, CancellationToken.None).ConfigureAwait(false);
                return true;
            }

            if (result.Error?.Code == ErrorCodes.InvalidSession)
            {
                await session.ClearAsync(CancellationToken.None).ConfigureAwait(false);
            }

            return false;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException)
        {
            return false;
        }
    }
}

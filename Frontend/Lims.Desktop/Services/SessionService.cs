using Lims.Contracts.Authentication;

namespace Lims.Desktop.Services;

public sealed class SessionService(ISecureStorage secureStorage) : ISessionService
{
    private readonly object _gate = new();
    private string? _accessToken;
    private UserProfile? _profile;

    public string? AccessToken
    {
        get
        {
            lock (_gate)
            {
                return _accessToken;
            }
        }
    }

    public UserProfile? Profile
    {
        get
        {
            lock (_gate)
            {
                return _profile;
            }
        }
    }

    public async Task SetAuthenticatedAsync(
        AuthenticationResponse response,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);
        await secureStorage.WriteRefreshTokenAsync(response.RefreshToken, cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            _accessToken = response.AccessToken;
            _profile = response.Profile;
        }
    }

    public Task<string?> ReadRefreshTokenAsync(CancellationToken cancellationToken) =>
        secureStorage.ReadRefreshTokenAsync(cancellationToken);

    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _accessToken = null;
            _profile = null;
        }

        await secureStorage.ClearRefreshTokenAsync(cancellationToken).ConfigureAwait(false);
    }
}

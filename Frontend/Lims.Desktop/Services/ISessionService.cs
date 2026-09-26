using Lims.Contracts.Authentication;

namespace Lims.Desktop.Services;

public interface ISessionService
{
    string? AccessToken { get; }

    UserProfile? Profile { get; }

    Task SetAuthenticatedAsync(AuthenticationResponse response, CancellationToken cancellationToken);

    Task<string?> ReadRefreshTokenAsync(CancellationToken cancellationToken);

    Task ClearAsync(CancellationToken cancellationToken);
}

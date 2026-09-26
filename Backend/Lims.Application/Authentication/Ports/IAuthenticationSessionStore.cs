using Lims.Application.Authentication.Security;
using Lims.Domain.Authentication;

namespace Lims.Application.Authentication.Ports;

public interface IAuthenticationSessionStore
{
    Task CreateAsync(
        AuthSession session,
        RefreshToken refreshToken,
        CancellationToken cancellationToken);

    Task<RefreshSessionSnapshot?> FindByRefreshTokenHashAsync(
        string tokenHash,
        CancellationToken cancellationToken);

    Task<RefreshRotationStatus> RotateAsync(
        Guid currentTokenId,
        RefreshToken replacement,
        DateTimeOffset instant,
        CancellationToken cancellationToken);

    Task<AuthSession?> FindActiveSessionAsync(
        Guid sessionId,
        DateTimeOffset instant,
        CancellationToken cancellationToken);

    Task<bool> RevokeSessionAsync(
        Guid sessionId,
        DateTimeOffset instant,
        string reason,
        CancellationToken cancellationToken);
}

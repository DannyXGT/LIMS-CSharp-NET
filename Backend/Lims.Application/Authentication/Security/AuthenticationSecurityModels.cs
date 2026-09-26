using Lims.Domain.Authentication;

namespace Lims.Application.Authentication.Security;

public sealed record IssuedAccessToken(string Value, DateTimeOffset ExpiresAt);

public sealed record GeneratedRefreshToken(string PlainText, string Hash);

public sealed record RefreshSessionSnapshot(AuthSession Session, RefreshToken RefreshToken);

public enum RefreshRotationStatus
{
    Rotated,
    NotFound,
    Expired,
    Revoked,
    AlreadyUsed,
}

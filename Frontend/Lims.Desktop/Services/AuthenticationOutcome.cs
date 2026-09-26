using Lims.Contracts.Authentication;

namespace Lims.Desktop.Services;

public enum AuthenticationOutcomeKind
{
    Authenticated,
    InvalidCredentials,
    NetworkUnavailable,
    Timeout,
    ServerUnavailable,
    UnexpectedError,
}

public sealed record AuthenticationOutcome(
    AuthenticationOutcomeKind Kind,
    UserProfile? Profile = null,
    string? CorrelationId = null);

public sealed record LogoutOutcome(bool RemoteRevoked);

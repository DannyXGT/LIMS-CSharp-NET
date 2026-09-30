namespace Lims.Contracts.Errors;

public static class ErrorCodes
{
    public const string InvalidCredentials = "auth.invalid_credentials";
    public const string InvalidSession = "auth.invalid_session";
    public const string Forbidden = "auth.forbidden";
    public const string ValidationError = "validation.error";
    public const string RateLimitExceeded = "auth.rate_limit_exceeded";
    public const string NetworkUnavailable = "client.network_unavailable";
    public const string Timeout = "client.timeout";
    public const string ServerUnavailable = "server.unavailable";
    public const string UnexpectedError = "server.unexpected_error";
    public const string NotFound = "resource.not_found";
    public const string Conflict = "resource.conflict";
    public const string InvalidState = "resource.invalid_state";
}

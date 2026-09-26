using Microsoft.Extensions.Logging;

namespace Lims.Desktop;

internal static partial class DesktopLog
{
    [LoggerMessage(2100, LogLevel.Information, "LoginAttempt")]
    public static partial void LoginAttempt(ILogger logger);

    [LoggerMessage(2101, LogLevel.Information, "LoginSucceeded for user {UserId}")]
    public static partial void LoginSucceeded(ILogger logger, int userId);

    [LoggerMessage(2102, LogLevel.Warning, "LoginFailed with code {ErrorCode} and correlation {CorrelationId}")]
    public static partial void LoginFailed(ILogger logger, string errorCode, string? correlationId);

    [LoggerMessage(2190, LogLevel.Warning, "ApiUnavailable")]
    public static partial void ApiUnavailable(ILogger logger);

    [LoggerMessage(2300, LogLevel.Information, "Logout completed; remote revocation confirmed: {RemoteRevoked}")]
    public static partial void Logout(ILogger logger, bool remoteRevoked);
}

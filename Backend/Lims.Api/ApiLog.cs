namespace Lims.Api;

internal static partial class ApiLog
{
    [LoggerMessage(1000, LogLevel.Information, "ApplicationStarted for {Environment} with authentication endpoints enabled")]
    public static partial void ApplicationStarted(ILogger logger, string environment);

    [LoggerMessage(1100, LogLevel.Information, "LoginAttempt {CorrelationId}")]
    public static partial void LoginAttempt(ILogger logger, string correlationId);

    [LoggerMessage(1101, LogLevel.Information, "LoginSucceeded for user {UserId} {CorrelationId}")]
    public static partial void LoginSucceeded(ILogger logger, int userId, string correlationId);

    [LoggerMessage(1102, LogLevel.Warning, "LoginFailed with code {ErrorCode} {CorrelationId}")]
    public static partial void LoginFailed(ILogger logger, string errorCode, string correlationId);

    [LoggerMessage(1103, LogLevel.Warning, "LoginFailed due to identifier cooldown {CorrelationId}")]
    public static partial void LoginCooldown(ILogger logger, string correlationId);

    [LoggerMessage(1200, LogLevel.Information, "RefreshAttempt {CorrelationId}")]
    public static partial void RefreshAttempt(ILogger logger, string correlationId);

    [LoggerMessage(1201, LogLevel.Information, "RefreshSucceeded for user {UserId} {CorrelationId}")]
    public static partial void RefreshSucceeded(ILogger logger, int userId, string correlationId);

    [LoggerMessage(1202, LogLevel.Warning, "RefreshFailed {CorrelationId}")]
    public static partial void RefreshFailed(ILogger logger, string correlationId);

    [LoggerMessage(1300, LogLevel.Information, "Logout for session {SessionId} {CorrelationId}")]
    public static partial void Logout(ILogger logger, Guid sessionId, string correlationId);

    [LoggerMessage(1900, LogLevel.Warning, "DatabaseUnavailable: PostgreSQL readiness check returned false")]
    public static partial void DatabaseUnavailable(ILogger logger);

    [LoggerMessage(1901, LogLevel.Warning, "DatabaseUnavailable: PostgreSQL readiness check failed")]
    public static partial void DatabaseCheckFailed(ILogger logger, Exception exception);

    [LoggerMessage(1999, LogLevel.Error, "Unexpected API failure {CorrelationId}")]
    public static partial void UnexpectedFailure(ILogger logger, string correlationId, Exception? exception);
}

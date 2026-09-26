using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using Lims.Application.Authentication;
using Lims.Application.Common;
using Lims.Contracts.Authentication;
using Lims.Contracts.Errors;
using Lims.Infrastructure.Security;

namespace Lims.Api.Authentication;

internal static class AuthenticationEndpoints
{
    public static IEndpointRouteBuilder MapAuthenticationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/auth").WithTags("Authentication");
        group.MapPost("/login", LoginAsync).AllowAnonymous().RequireRateLimiting("login");
        group.MapPost("/refresh", RefreshAsync).AllowAnonymous().RequireRateLimiting("refresh");
        group.MapPost("/logout", LogoutAsync).RequireAuthorization();
        group.MapGet("/me", MeAsync).RequireAuthorization();
        return endpoints;
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        IAuthenticationService authentication,
        LoginAttemptTracker attempts,
        ILoggerFactory loggerFactory,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("Authentication");
        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();
        var identifier = request.Identifier ?? string.Empty;
        ApiLog.LoginAttempt(logger, httpContext.TraceIdentifier);
        if (!attempts.IsAllowed(identifier, ipAddress))
        {
            ApiLog.LoginCooldown(logger, httpContext.TraceIdentifier);
            return Error(httpContext, StatusCodes.Status429TooManyRequests, new OperationError(
                ErrorCodes.RateLimitExceeded,
                "Se alcanzó temporalmente el límite de intentos. Intente nuevamente más tarde."));
        }

        var result = await authentication.LoginAsync(request, cancellationToken).ConfigureAwait(false);
        if (result.IsSuccess)
        {
            attempts.Reset(identifier, ipAddress);
            ApiLog.LoginSucceeded(logger, result.Value!.Profile.Id, httpContext.TraceIdentifier);
            return Results.Ok(result.Value);
        }

        if (result.Error!.Code == ErrorCodes.InvalidCredentials)
        {
            attempts.RecordFailure(identifier, ipAddress);
        }

        ApiLog.LoginFailed(logger, result.Error.Code, httpContext.TraceIdentifier);
        return MapError(httpContext, result.Error);
    }

    private static async Task<IResult> RefreshAsync(
        RefreshRequest request,
        IAuthenticationService authentication,
        ILoggerFactory loggerFactory,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("Authentication");
        ApiLog.RefreshAttempt(logger, httpContext.TraceIdentifier);
        var result = await authentication.RefreshAsync(request, cancellationToken).ConfigureAwait(false);
        if (result.IsSuccess)
        {
            ApiLog.RefreshSucceeded(logger, result.Value!.Profile.Id, httpContext.TraceIdentifier);
            return Results.Ok(result.Value);
        }

        ApiLog.RefreshFailed(logger, httpContext.TraceIdentifier);
        return MapError(httpContext, result.Error!);
    }

    private static async Task<IResult> LogoutAsync(
        IAuthenticationService authentication,
        ILoggerFactory loggerFactory,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!TryGetActor(httpContext, out var sessionId, out _))
        {
            return InvalidSession(httpContext);
        }

        var result = await authentication.LogoutAsync(sessionId, cancellationToken).ConfigureAwait(false);
        ApiLog.Logout(
            loggerFactory.CreateLogger("Authentication"),
            sessionId,
            httpContext.TraceIdentifier);
        return result.IsSuccess ? Results.Ok(result.Value) : MapError(httpContext, result.Error!);
    }

    private static async Task<IResult> MeAsync(
        IAuthenticationService authentication,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!TryGetActor(httpContext, out var sessionId, out var userId))
        {
            return InvalidSession(httpContext);
        }

        var result = await authentication.GetCurrentUserAsync(sessionId, userId, cancellationToken)
            .ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : MapError(httpContext, result.Error!);
    }

    private static bool TryGetActor(HttpContext context, out Guid sessionId, out int userId)
    {
        userId = 0;
        return Guid.TryParse(context.User.FindFirst(JwtAccessTokenService.SessionIdClaim)?.Value, out sessionId) &&
            int.TryParse(
            context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out userId);
    }

    private static IResult MapError(HttpContext context, OperationError error)
    {
        var status = error.Code switch
        {
            ErrorCodes.ValidationError => StatusCodes.Status400BadRequest,
            ErrorCodes.InvalidCredentials or ErrorCodes.InvalidSession => StatusCodes.Status401Unauthorized,
            ErrorCodes.Forbidden => StatusCodes.Status403Forbidden,
            ErrorCodes.RateLimitExceeded => StatusCodes.Status429TooManyRequests,
            _ => StatusCodes.Status500InternalServerError,
        };
        return Error(context, status, error);
    }

    private static IResult InvalidSession(HttpContext context) => Error(
        context,
        StatusCodes.Status401Unauthorized,
        new OperationError(ErrorCodes.InvalidSession, "La sesión no es válida o expiró."));

    private static IResult Error(HttpContext context, int status, OperationError error) => Results.Json(
        new ApiError(error.Code, error.Message, context.TraceIdentifier, error.ValidationErrors),
        statusCode: status);
}

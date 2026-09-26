using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using Lims.Api.Http;
using Lims.Application.Authentication.Ports;
using Lims.Contracts.Errors;
using Lims.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace Lims.Api.Authentication;

internal static class JwtSessionValidation
{
    public static JwtBearerEvents CreateEvents() => new()
    {
        OnTokenValidated = ValidateSessionAsync,
        OnChallenge = async context =>
        {
            context.HandleResponse();
            await ApiErrorWriter.WriteAsync(
                context.HttpContext,
                StatusCodes.Status401Unauthorized,
                ErrorCodes.InvalidSession,
                "La sesión no es válida o expiró.",
                context.HttpContext.RequestAborted).ConfigureAwait(false);
        },
        OnForbidden = context => ApiErrorWriter.WriteAsync(
            context.HttpContext,
            StatusCodes.Status403Forbidden,
            ErrorCodes.Forbidden,
            "No tiene autorización para realizar esta operación.",
            context.HttpContext.RequestAborted),
    };

    private static async Task ValidateSessionAsync(TokenValidatedContext context)
    {
        var sessionValue = context.Principal?.FindFirst(JwtAccessTokenService.SessionIdClaim)?.Value;
        var userValue = context.Principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        if (!Guid.TryParse(sessionValue, out var sessionId) ||
            !int.TryParse(userValue, NumberStyles.None, CultureInfo.InvariantCulture, out var userId))
        {
            context.Fail("Required session claims are invalid.");
            return;
        }

        var sessions = context.HttpContext.RequestServices.GetRequiredService<IAuthenticationSessionStore>();
        var timeProvider = context.HttpContext.RequestServices.GetRequiredService<TimeProvider>();
        var session = await sessions.FindActiveSessionAsync(
                sessionId,
                timeProvider.GetUtcNow(),
                context.HttpContext.RequestAborted)
            .ConfigureAwait(false);
        if (session is null || session.UserId != userId)
        {
            context.Fail("Server-side session is no longer active.");
        }
    }
}

using Lims.Contracts.Authentication;
using Microsoft.Extensions.Logging;
using Lims.Contracts.Errors;
using Lims.Desktop.Http;

namespace Lims.Desktop.Services;

internal sealed class AuthenticationGateway(
    IAuthenticationApiClient api,
    ISessionService session,
    ILogger<AuthenticationGateway> logger) : IAuthenticationGateway
{
    public async Task<AuthenticationOutcome> SignInAsync(
        string identifier,
        string password,
        CancellationToken cancellationToken)
    {
        DesktopLog.LoginAttempt(logger);
        try
        {
            var result = await api.LoginAsync(
                    new LoginRequest(identifier, password, "Lims.Desktop"),
                    cancellationToken)
                .ConfigureAwait(false);
            if (result.IsSuccess && result.Value is not null)
            {
                await session.SetAuthenticatedAsync(result.Value, cancellationToken).ConfigureAwait(false);
                DesktopLog.LoginSucceeded(logger, result.Value.Profile.Id);
                return new AuthenticationOutcome(AuthenticationOutcomeKind.Authenticated, result.Value.Profile);
            }

            if (result.Error?.Code == ErrorCodes.InvalidCredentials)
            {
                DesktopLog.LoginFailed(logger, ErrorCodes.InvalidCredentials, result.Error.CorrelationId);
                return new AuthenticationOutcome(
                    AuthenticationOutcomeKind.InvalidCredentials,
                    CorrelationId: result.Error.CorrelationId);
            }

            var outcome = result.StatusCode is 502 or 503 or 504
                ? new AuthenticationOutcome(AuthenticationOutcomeKind.ServerUnavailable, CorrelationId: result.Error?.CorrelationId)
                : new AuthenticationOutcome(AuthenticationOutcomeKind.UnexpectedError, CorrelationId: result.Error?.CorrelationId);
            DesktopLog.LoginFailed(logger, result.Error?.Code ?? ErrorCodes.UnexpectedError, result.Error?.CorrelationId);
            return outcome;
        }
        catch (HttpRequestException)
        {
            DesktopLog.ApiUnavailable(logger);
            return new AuthenticationOutcome(AuthenticationOutcomeKind.NetworkUnavailable);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new AuthenticationOutcome(AuthenticationOutcomeKind.Timeout);
        }
    }

    public async Task<LogoutOutcome> LogoutAsync(CancellationToken cancellationToken)
    {
        var remotelyRevoked = false;
        try
        {
            remotelyRevoked = await api.LogoutAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            // Local logout must still complete when the API is unavailable.
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Local logout must still complete after an API timeout.
        }
        finally
        {
            await session.ClearAsync(CancellationToken.None).ConfigureAwait(false);
            DesktopLog.Logout(logger, remotelyRevoked);
        }

        return new LogoutOutcome(remotelyRevoked);
    }
}

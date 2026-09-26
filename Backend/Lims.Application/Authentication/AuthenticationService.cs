using Lims.Application.Authentication.Ports;
using Lims.Application.Common;
using Lims.Contracts.Authentication;
using Lims.Contracts.Errors;
using Lims.Domain.Authentication;
using Lims.Domain.Identity;

namespace Lims.Application.Authentication;

public sealed class AuthenticationService(
    IUserRepository users,
    IAuthenticationSessionStore sessions,
    IPasswordHashService passwords,
    IAccessTokenService accessTokens,
    IRefreshTokenService refreshTokens,
    UserIdentifierNormalizer identifierNormalizer,
    AuthenticationPolicy policy,
    TimeProvider timeProvider) : IAuthenticationService
{
    private const string InvalidCredentialsMessage = "El usuario o la contraseña no son válidos.";
    private const string InvalidSessionMessage = "La sesión no es válida o expiró.";

    public async Task<OperationResult<AuthenticationResponse>> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalized = identifierNormalizer.Normalize(request.Identifier);
        if (!normalized.IsValid)
        {
            return ValidationFailure("identifier", normalized.Error ?? "El usuario no es válido.");
        }

        if (string.IsNullOrEmpty(request.Password))
        {
            return ValidationFailure("password", "La contraseña es obligatoria.");
        }

        var user = await users.FindByEmailAsync(normalized.Email!, cancellationToken).ConfigureAwait(false);
        if (user is null || !user.IsActive)
        {
            return InvalidCredentials();
        }

        var passwordCheck = passwords.Verify(request.Password, user.PasswordHash);
        if (!passwordCheck.Verified)
        {
            return InvalidCredentials();
        }

        if (passwordCheck.NeedsRehash)
        {
            await users.UpdatePasswordHashAsync(user, passwords.Hash(request.Password), cancellationToken)
                .ConfigureAwait(false);
        }

        var now = timeProvider.GetUtcNow();
        var session = new AuthSession(
            Guid.NewGuid(),
            user.Id,
            Guid.NewGuid(),
            now,
            now.Add(policy.RefreshTokenLifetime),
            request.ClientName);
        var generatedRefresh = refreshTokens.Generate();
        var refreshToken = new RefreshToken(
            Guid.NewGuid(),
            session.Id,
            generatedRefresh.Hash,
            now,
            session.ExpiresAt);
        var accessToken = accessTokens.Issue(user, session, now, policy.AccessTokenLifetime);

        await sessions.CreateAsync(session, refreshToken, cancellationToken).ConfigureAwait(false);

        return OperationResult.Success(
            CreateResponse(user, accessToken, generatedRefresh.PlainText, refreshToken.ExpiresAt));
    }

    public async Task<OperationResult<AuthenticationResponse>> RefreshAsync(
        RefreshRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return InvalidSession();
        }

        var tokenHash = refreshTokens.Hash(request.RefreshToken);
        var snapshot = await sessions.FindByRefreshTokenHashAsync(tokenHash, cancellationToken)
            .ConfigureAwait(false);
        if (snapshot is null)
        {
            return InvalidSession();
        }

        var now = timeProvider.GetUtcNow();
        if (!snapshot.Session.IsActiveAt(now))
        {
            return InvalidSession();
        }

        if (snapshot.RefreshToken.ConsumedAt is not null)
        {
            await sessions.RevokeSessionAsync(
                    snapshot.Session.Id,
                    now,
                    "refresh_token_reuse",
                    cancellationToken)
                .ConfigureAwait(false);
            return InvalidSession();
        }

        if (!snapshot.RefreshToken.IsUsableAt(now))
        {
            return InvalidSession();
        }

        var user = await users.FindByIdAsync(snapshot.Session.UserId, cancellationToken).ConfigureAwait(false);
        if (user is null || !user.IsActive)
        {
            await sessions.RevokeSessionAsync(snapshot.Session.Id, now, "user_inactive", cancellationToken)
                .ConfigureAwait(false);
            return InvalidSession();
        }

        var generatedRefresh = refreshTokens.Generate();
        var replacement = new RefreshToken(
            Guid.NewGuid(),
            snapshot.Session.Id,
            generatedRefresh.Hash,
            now,
            snapshot.Session.ExpiresAt);
        var accessToken = accessTokens.Issue(user, snapshot.Session, now, policy.AccessTokenLifetime);

        var rotation = await sessions.RotateAsync(
                snapshot.RefreshToken.Id,
                replacement,
                now,
                cancellationToken)
            .ConfigureAwait(false);
        if (rotation != Security.RefreshRotationStatus.Rotated)
        {
            if (rotation == Security.RefreshRotationStatus.AlreadyUsed)
            {
                await sessions.RevokeSessionAsync(
                        snapshot.Session.Id,
                        now,
                        "refresh_token_reuse",
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            return InvalidSession();
        }

        return OperationResult.Success(
            CreateResponse(user, accessToken, generatedRefresh.PlainText, replacement.ExpiresAt));
    }

    public async Task<OperationResult<LogoutResponse>> LogoutAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        if (sessionId == Guid.Empty)
        {
            return InvalidSession<LogoutResponse>();
        }

        var revoked = await sessions.RevokeSessionAsync(
                sessionId,
                timeProvider.GetUtcNow(),
                "user_logout",
                cancellationToken)
            .ConfigureAwait(false);
        return OperationResult.Success(new LogoutResponse(revoked));
    }

    public async Task<OperationResult<UserProfile>> GetCurrentUserAsync(
        Guid sessionId,
        int userId,
        CancellationToken cancellationToken)
    {
        if (sessionId == Guid.Empty || userId <= 0)
        {
            return InvalidSession<UserProfile>();
        }

        var now = timeProvider.GetUtcNow();
        var session = await sessions.FindActiveSessionAsync(sessionId, now, cancellationToken)
            .ConfigureAwait(false);
        if (session is null || session.UserId != userId)
        {
            return InvalidSession<UserProfile>();
        }

        var user = await users.FindByIdAsync(userId, cancellationToken).ConfigureAwait(false);
        return user is null || !user.IsActive
            ? InvalidSession<UserProfile>()
            : OperationResult.Success(CreateProfile(user));
    }

    private static AuthenticationResponse CreateResponse(
        User user,
        Security.IssuedAccessToken accessToken,
        string refreshToken,
        DateTimeOffset refreshExpiresAt) =>
        new(
            accessToken.Value,
            accessToken.ExpiresAt,
            refreshToken,
            refreshExpiresAt,
            CreateProfile(user));

    private static UserProfile CreateProfile(User user) =>
        new(
            user.Id,
            user.Name,
            user.Email,
            user.Role?.Name ?? string.Empty,
            user.Department?.Name,
            user.Permissions
                .Select(permission => permission.Key)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray());

    private static OperationResult<AuthenticationResponse> InvalidCredentials() =>
        OperationResult.Failure<AuthenticationResponse>(
            new OperationError(ErrorCodes.InvalidCredentials, InvalidCredentialsMessage));

    private static OperationResult<AuthenticationResponse> InvalidSession() =>
        InvalidSession<AuthenticationResponse>();

    private static OperationResult<T> InvalidSession<T>() =>
        OperationResult.Failure<T>(new OperationError(ErrorCodes.InvalidSession, InvalidSessionMessage));

    private static OperationResult<AuthenticationResponse> ValidationFailure(string field, string message) =>
        OperationResult.Failure<AuthenticationResponse>(
            new OperationError(
                ErrorCodes.ValidationError,
                "Los datos enviados no son válidos.",
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    [field] = [message],
                }));
}

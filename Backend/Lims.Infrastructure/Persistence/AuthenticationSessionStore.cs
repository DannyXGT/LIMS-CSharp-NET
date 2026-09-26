using Lims.Application.Authentication.Ports;
using Lims.Application.Authentication.Security;
using Lims.Domain.Authentication;
using Microsoft.EntityFrameworkCore;

namespace Lims.Infrastructure.Persistence;

public sealed class AuthenticationSessionStore(LimsDbContext dbContext) : IAuthenticationSessionStore
{
    public async Task CreateAsync(
        AuthSession session,
        RefreshToken refreshToken,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        dbContext.AuthSessions.Add(session);
        dbContext.RefreshTokens.Add(refreshToken);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<RefreshSessionSnapshot?> FindByRefreshTokenHashAsync(
        string tokenHash,
        CancellationToken cancellationToken)
    {
        var token = await dbContext.RefreshTokens
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.TokenHash == tokenHash, cancellationToken)
            .ConfigureAwait(false);
        if (token is null)
        {
            return null;
        }

        var session = await dbContext.AuthSessions
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == token.SessionId, cancellationToken)
            .ConfigureAwait(false);
        return session is null ? null : new RefreshSessionSnapshot(session, token);
    }

    public async Task<RefreshRotationStatus> RotateAsync(
        Guid currentTokenId,
        RefreshToken replacement,
        DateTimeOffset instant,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        dbContext.RefreshTokens.Add(replacement);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var updated = await dbContext.RefreshTokens
            .Where(token =>
                token.Id == currentTokenId &&
                token.ConsumedAt == null &&
                token.RevokedAt == null &&
                token.ExpiresAt > instant)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(token => token.ConsumedAt, instant)
                    .SetProperty(token => token.ReplacedByTokenId, replacement.Id),
                cancellationToken)
            .ConfigureAwait(false);

        if (updated == 0)
        {
            var current = await dbContext.RefreshTokens
                .AsNoTracking()
                .SingleOrDefaultAsync(token => token.Id == currentTokenId, cancellationToken)
                .ConfigureAwait(false);
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return Classify(current, instant);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return RefreshRotationStatus.Rotated;
    }

    public Task<AuthSession?> FindActiveSessionAsync(
        Guid sessionId,
        DateTimeOffset instant,
        CancellationToken cancellationToken) =>
        dbContext.AuthSessions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                session =>
                    session.Id == sessionId &&
                    session.RevokedAt == null &&
                    session.ExpiresAt > instant,
                cancellationToken);

    public async Task<bool> RevokeSessionAsync(
        Guid sessionId,
        DateTimeOffset instant,
        string reason,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        var updated = await dbContext.AuthSessions
            .Where(session => session.Id == sessionId && session.RevokedAt == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(session => session.RevokedAt, instant)
                    .SetProperty(session => session.RevocationReason, reason),
                cancellationToken)
            .ConfigureAwait(false);

        await dbContext.RefreshTokens
            .Where(token => token.SessionId == sessionId && token.RevokedAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(token => token.RevokedAt, instant),
                cancellationToken)
            .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return updated > 0;
    }

    private static RefreshRotationStatus Classify(RefreshToken? token, DateTimeOffset instant)
    {
        if (token is null)
        {
            return RefreshRotationStatus.NotFound;
        }

        if (token.ConsumedAt is not null)
        {
            return RefreshRotationStatus.AlreadyUsed;
        }

        if (token.RevokedAt is not null)
        {
            return RefreshRotationStatus.Revoked;
        }

        return token.ExpiresAt <= instant
            ? RefreshRotationStatus.Expired
            : RefreshRotationStatus.NotFound;
    }
}

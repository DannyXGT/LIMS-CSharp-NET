namespace Lims.Domain.Authentication;

public sealed class RefreshToken
{
    private RefreshToken()
    {
    }

    public RefreshToken(
        Guid id,
        Guid sessionId,
        string tokenHash,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Refresh token id cannot be empty.", nameof(id));
        }

        if (sessionId == Guid.Empty)
        {
            throw new ArgumentException("Session id cannot be empty.", nameof(sessionId));
        }

        var normalizedHash = tokenHash.Trim();
        if (normalizedHash.Length is 0 or > 128)
        {
            throw new ArgumentException("Refresh token hash is invalid.", nameof(tokenHash));
        }

        if (expiresAt <= createdAt)
        {
            throw new ArgumentException("Refresh token expiration must be after creation.", nameof(expiresAt));
        }

        Id = id;
        SessionId = sessionId;
        TokenHash = normalizedHash;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    public Guid Id { get; private set; }

    public Guid SessionId { get; private set; }

    public string TokenHash { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? ConsumedAt { get; private set; }

    public Guid? ReplacedByTokenId { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public bool IsUsableAt(DateTimeOffset instant) =>
        ConsumedAt is null && RevokedAt is null && instant < ExpiresAt;

    public void Consume(DateTimeOffset instant, Guid replacementTokenId)
    {
        if (replacementTokenId == Guid.Empty)
        {
            throw new ArgumentException("Replacement token id cannot be empty.", nameof(replacementTokenId));
        }

        if (!IsUsableAt(instant))
        {
            throw new InvalidOperationException("Refresh token is no longer usable.");
        }

        ConsumedAt = instant;
        ReplacedByTokenId = replacementTokenId;
    }

    public void Revoke(DateTimeOffset instant)
    {
        RevokedAt ??= instant;
    }
}

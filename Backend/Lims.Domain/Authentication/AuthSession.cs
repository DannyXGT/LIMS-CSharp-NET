namespace Lims.Domain.Authentication;

public sealed class AuthSession
{
    private AuthSession()
    {
    }

    public AuthSession(
        Guid id,
        int userId,
        Guid tokenFamilyId,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        string? clientName = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Session id cannot be empty.", nameof(id));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(userId);

        if (tokenFamilyId == Guid.Empty)
        {
            throw new ArgumentException("Token family id cannot be empty.", nameof(tokenFamilyId));
        }

        if (expiresAt <= createdAt)
        {
            throw new ArgumentException("Session expiration must be after creation.", nameof(expiresAt));
        }

        Id = id;
        UserId = userId;
        TokenFamilyId = tokenFamilyId;
        CreatedAt = createdAt;
        LastSeenAt = createdAt;
        ExpiresAt = expiresAt;
        ClientName = NormalizeOptional(clientName, 120);
    }

    public Guid Id { get; private set; }

    public int UserId { get; private set; }

    public Guid TokenFamilyId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset LastSeenAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public string? RevocationReason { get; private set; }

    public string? ClientName { get; private set; }

    public bool IsActiveAt(DateTimeOffset instant) => RevokedAt is null && instant < ExpiresAt;

    public void MarkSeen(DateTimeOffset instant)
    {
        if (instant > LastSeenAt)
        {
            LastSeenAt = instant;
        }
    }

    public void Revoke(DateTimeOffset instant, string reason)
    {
        if (RevokedAt is not null)
        {
            return;
        }

        var normalizedReason = reason.Trim();
        if (normalizedReason.Length is 0 or > 120)
        {
            throw new ArgumentException("Revocation reason must contain between 1 and 120 characters.", nameof(reason));
        }

        RevokedAt = instant;
        RevocationReason = normalizedReason;
    }

    private static string? NormalizeOptional(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : normalized[..maximumLength];
    }
}

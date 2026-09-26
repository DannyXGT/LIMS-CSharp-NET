using Lims.Domain.Authentication;

namespace Lims.Application.Tests.Authentication;

public sealed class AuthenticationDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RevokedSessionIsNotActive()
    {
        var session = new AuthSession(
            Guid.NewGuid(),
            10,
            Guid.NewGuid(),
            Now,
            Now.AddHours(12));

        session.Revoke(Now.AddMinutes(1), "user_logout");

        Assert.False(session.IsActiveAt(Now.AddMinutes(2)));
        Assert.Equal("user_logout", session.RevocationReason);
    }

    [Fact]
    public void ConsumedRefreshTokenIsUnusableAndTracksReplacement()
    {
        var replacementId = Guid.NewGuid();
        var token = new RefreshToken(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "A_VALID_SHA256_HASH",
            Now,
            Now.AddHours(12));

        token.Consume(Now.AddMinutes(1), replacementId);

        Assert.False(token.IsUsableAt(Now.AddMinutes(2)));
        Assert.Equal(replacementId, token.ReplacedByTokenId);
    }
}

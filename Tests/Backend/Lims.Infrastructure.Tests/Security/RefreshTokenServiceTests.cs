using Lims.Infrastructure.Security;

namespace Lims.Infrastructure.Tests.Security;

public sealed class RefreshTokenServiceTests
{
    [Fact]
    public void GenerateReturnsOpaqueTokenAndOnlyItsSha256HashForPersistence()
    {
        var service = new RefreshTokenService();

        var generated = service.Generate();

        Assert.DoesNotContain('=', generated.PlainText);
        Assert.Matches("^[A-Za-z0-9_-]{43}$", generated.PlainText);
        Assert.Matches("^[0-9A-F]{64}$", generated.Hash);
        Assert.NotEqual(generated.PlainText, generated.Hash);
        Assert.Equal(service.Hash(generated.PlainText), generated.Hash);
    }

    [Fact]
    public void GenerateCreatesDistinctTokens()
    {
        var service = new RefreshTokenService();

        var first = service.Generate();
        var second = service.Generate();

        Assert.NotEqual(first.PlainText, second.PlainText);
        Assert.NotEqual(first.Hash, second.Hash);
    }

    [Fact]
    public void HashIsDeterministicAndCaseSensitive()
    {
        var service = new RefreshTokenService();

        Assert.Equal(service.Hash("token"), service.Hash("token"));
        Assert.NotEqual(service.Hash("token"), service.Hash("Token"));
    }
}

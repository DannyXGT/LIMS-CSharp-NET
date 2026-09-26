using Lims.Application.Authentication;

namespace Lims.Application.Tests.Authentication;

public sealed class UserIdentifierNormalizerTests
{
    private readonly UserIdentifierNormalizer _sut = new(
        new CorporateIdentityPolicy { AllowedDomain = "intertek.com" });

    [Theory]
    [InlineData("danny.jimenez", "danny.jimenez@intertek.com")]
    [InlineData(" DANNY.JIMENEZ@INTERTEK.COM ", "danny.jimenez@intertek.com")]
    public void NormalizeValidIdentifierReturnsCanonicalEmail(string identifier, string expected)
    {
        var result = _sut.Normalize(identifier);

        Assert.True(result.IsValid);
        Assert.Equal(expected, result.Email);
    }

    [Theory]
    [InlineData("")]
    [InlineData("danny jimenez")]
    [InlineData("danny.jimenez@example.com")]
    [InlineData("danny..jimenez")]
    public void NormalizeInvalidIdentifierReturnsValidationFailure(string identifier)
    {
        var result = _sut.Normalize(identifier);

        Assert.False(result.IsValid);
        Assert.Null(result.Email);
        Assert.False(string.IsNullOrEmpty(result.Error));
    }
}

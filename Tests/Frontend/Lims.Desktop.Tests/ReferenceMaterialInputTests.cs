using Lims.Desktop.Validation;

namespace Lims.Desktop.Tests;

public sealed class ReferenceMaterialInputTests
{
    [Theory]
    [InlineData("98.09", 98.09)]
    [InlineData("98,09", 98.09)]
    [InlineData("100", 100)]
    [InlineData("0.0001", 0.0001)]
    public void DecimalParserAcceptsDotOrCommaWithoutUsingThousandsSeparators(string text, decimal expected)
    {
        var parsed = ReferenceMaterialInput.TryParseDecimal(text, 4, out var value);

        Assert.True(parsed);
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("98.12345")]
    [InlineData("1,234.5")]
    [InlineData("1 000")]
    [InlineData("1e2")]
    [InlineData("-1")]
    public void DecimalParserRejectsAmbiguousOrOverPrecisionValues(string text)
    {
        Assert.False(ReferenceMaterialInput.TryParseDecimal(text, 4, out _));
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("100", true)]
    [InlineData("0", false)]
    [InlineData("1.0", false)]
    [InlineData("-1", false)]
    public void PackageCountParserRequiresPositiveInteger(string text, bool expected)
    {
        Assert.Equal(expected, ReferenceMaterialInput.TryParsePackageCount(text, out _));
    }
}

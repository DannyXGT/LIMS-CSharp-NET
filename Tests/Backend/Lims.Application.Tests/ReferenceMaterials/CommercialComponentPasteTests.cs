using Lims.Contracts.ReferencePreparations;
namespace Lims.Application.Tests.ReferenceMaterials;
public sealed class CommercialComponentPasteTests
{
    [Fact]
    public void ParsesTwentyRowsWithoutCertificateMetadataOrCasAndAcceptsDecimalComma()
    {
        var text = "Compuesto\tCAS\tConcentración\tUnidad\n" + string.Join("\n", Enumerable.Range(1, 20).Select(i => $"Analito {i}\t\t49,9\tµg/mL"));
        var rows = CommercialComponentPaste.Parse(text);
        Assert.Equal(20, rows.Count); Assert.All(rows, r => { Assert.Equal(49.9m, r.CertifiedConcentration); Assert.Null(r.CasNumber); });
        Assert.Equal(20, rows.Select(r => r.Id).Distinct().Count());
    }
    [Theory]
    [InlineData("Analito\t92-67-2\t50\tµg/mL")]
    [InlineData("Analito\t\t0\tµg/mL")]
    [InlineData("Analito\t\t50\tµg/g")]
    [InlineData("Analito\t\t50")]
    public void RejectsInvalidTableBeforeReturningAnyRows(string invalid)
        => Assert.Throws<ArgumentException>(() => CommercialComponentPaste.Parse("Válido\t92-67-1\t50\tµg/mL\n" + invalid));
}

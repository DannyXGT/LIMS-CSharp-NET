using Lims.Contracts.ReferencePreparations;

namespace Lims.Application.Tests.ReferenceMaterials;

public sealed class IntermediateCalculatorTests
{
    [Theory]
    [InlineData(0, 0)] [InlineData(25, 25)] [InlineData(75, 75)] [InlineData(100, 100)] [InlineData(110, 110)]
    public void CylinderRetainsUnclampedPercentIncludingExcess(decimal taken, decimal expected) =>
        Assert.Equal(expected, IntermediateCalculator.OccupiedPercent(taken, 100));
    [Fact]
    public void SingleAliquotUsesDecimalAndExplicitVolumeAndConcentrationConversions()
    {
        var source = Source("A", 2, "g/L", "L", 1);
        var result = IntermediateCalculator.Calculate(new(1, 100, "mL", [new(source.Id, source.Version, .025m, "L")]), [source]);
        Assert.Equal(25, result.TotalVolumeTaken); Assert.Equal(75, result.RemainingToFill);
        Assert.Equal(500, Assert.Single(result.Results).Concentration); Assert.Equal(.975m, Assert.Single(result.Components).RemainingVolume);
    }
    [Fact]
    public void DistinctAnalyticalComponentsKeepIndependentResultsAndTotalVolume()
    {
        var a = Source("A", 1000); var b = Source("B", 200);
        var result = IntermediateCalculator.Calculate(new(1, 100, "mL", [new(a.Id, a.Version, 10, "mL"), new(b.Id, b.Version, 15, "mL")]), [a, b]);
        Assert.Equal(25, result.TotalVolumeTaken); Assert.Equal(75, result.RemainingToFill);
        Assert.Equal(100, result.Results.Single(r => r.Name == "A").Concentration);
        Assert.Equal(30, result.Results.Single(r => r.Name == "B").Concentration);
        Assert.Equal(2, result.Components.Count); Assert.False(result.ExceedsFinalVolume);
    }
    [Fact]
    public void DilutingWholeIntermediateTakesOneAliquotAndDilutesEveryAnalyte()
    {
        var source = Source("A", 100) with { Kind = "Intermedia", Analytes = [new(Guid.NewGuid(), "A", null, "A", 100, "mg/L"), new(Guid.NewGuid(), "B", null, "B", 30, "mg/L")] };
        var result = IntermediateCalculator.Calculate(new(1, 100, "mL", [new(source.Id, source.Version, 10, "mL")], source.Id), [source]);
        Assert.Equal(10, result.TotalVolumeTaken); Assert.Equal(2, result.Results.Count);
        Assert.Equal(10, result.Results.Single(a => a.Name == "A").Concentration); Assert.Equal(3, result.Results.Single(a => a.Name == "B").Concentration);
    }
    [Fact]
    public void AvailabilityAndExcessRemainSeparateValidationStates()
    {
        var source = Source("A", 100, available: 50);
        var result = IntermediateCalculator.Calculate(new(1, 100, "mL", [new(source.Id, source.Version, 110, "mL")]), [source]);
        Assert.True(result.ExceedsFinalVolume); Assert.False(result.HasSufficientVolume); Assert.Equal(-10, result.RemainingToFill);
        Assert.Equal(-60, result.Components[0].RemainingVolume);
    }
    [Fact]
    public void DuplicateOriginsStaleVersionsMethodAndUnrepresentableDebitsAreRejected()
    {
        var source = Source("A", 100);
        var row = new IntermediateComponentRequest(source.Id, source.Version, 10, "mL");
        foreach (var request in new[] {
            new IntermediateCalculationRequest(1, 100, "mL", [row, row]),
            new IntermediateCalculationRequest(2, 100, "mL", [row]),
            new IntermediateCalculationRequest(1, 100, "mL", [row with { SourceVersion = Guid.NewGuid() }]),
            new IntermediateCalculationRequest(1, 100, "mL", [row with { VolumeTaken = 0 }]),
            new IntermediateCalculationRequest(1, 100, "mL", [row with { VolumeUnit = "g" }]),
        }) Assert.Throws<ArgumentException>(() => IntermediateCalculator.Calculate(request, [source]));
        var liters = source with { VolumeUnit = "L" };
        Assert.Throws<ArgumentException>(() => IntermediateCalculator.Calculate(new(1, 100, "mL", [row with { VolumeTaken = .000001m }]), [liters]));
    }
    [Fact]
    public void FormatterRemovesStorageScaleButRetainsSmallConcentrations()
    {
        Assert.Equal("25 mL", StockPresentation.Quantity(25.000000m, "mL"));
        Assert.Equal("100 mg/L", StockPresentation.Quantity(100.000008m, "mg/L"));
        Assert.NotEqual("0 mg/L", StockPresentation.Quantity(.000001m, "mg/L"));
    }
    [Fact]
    public void SummaryAndAccessibleRowNameUseHumanIdentityAndKeepDetailedResultsAvailable()
    {
        var id = Guid.NewGuid();
        var summary = new IntermediateSummary(id, "INT-20261007-001", "Mezcla", "Ensayo", 2, 100, "mL", 100,
            [new(Guid.NewGuid(), "A", null, "LOTE-A", 25, "mg/L"), new(Guid.NewGuid(), "B", null, "LOTE-B", 10, "mg/L")],
            new(2026, 10, 7), new(2027, 1, 1), "Ana López", "Active");
        Assert.Equal("2 analitos", summary.ConcentrationDisplay);
        Assert.Contains("25 mg/L", summary.ConcentrationDetails, StringComparison.Ordinal);
        Assert.Contains("Disponible", summary.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(id.ToString("D"), summary.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Active", summary.ToString(), StringComparison.Ordinal);
    }
    private static IntermediateSource Source(string name, decimal concentration, string unit = "mg/L", string volumeUnit = "mL", decimal available = 100) =>
        new(Guid.NewGuid(), "Stock", "STK-TEST", name, 1, "Ensayo", available, available, volumeUnit, new(2026, 1, 1), new(2099, 1, 1), Guid.NewGuid(), [new(Guid.NewGuid(), name, null, "LOTE", concentration, unit)]);
}

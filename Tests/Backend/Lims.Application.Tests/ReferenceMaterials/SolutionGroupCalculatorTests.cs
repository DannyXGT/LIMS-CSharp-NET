using Lims.Contracts.ReferencePreparations;

namespace Lims.Application.Tests.ReferenceMaterials;

public sealed class SolutionGroupCalculatorTests
{
    [Fact]
    public void IndependentSolutionsRetainOwnAforoEveryAnalyteAndExplicitInternalRole()
    {
        var source = Source();
        var result = SolutionGroupCalculator.Calculate(new(1, [Part(source, 10, 100, true), Part(source, 15, 50)]), [source]);
        Assert.Equal(2, result.Parts.Count);
        Assert.Equal(10, result.Parts[0].Results[0].Concentration);
        Assert.Equal(30, result.Parts[1].Results[0].Concentration);
        Assert.Equal(3, result.Parts[0].Results[1].Concentration);
        Assert.True(result.Parts[0].IsInternalStandard); Assert.False(result.Parts[1].IsInternalStandard);
        var debit = Assert.Single(result.Debits); Assert.Equal(25, debit.Quantity); Assert.Equal(75, debit.RemainingVolume);
        Assert.Equal(source.Analytes[0].MaterialId, result.Parts[1].Results[0].MaterialId);
        Assert.Equal("LOT-A", result.Parts[0].Results[0].Lot);
    }
    [Fact]
    public void ConversionsPreserveOriginalConcentrationUnitAndDecimalPrecision()
    {
        var source = Source() with { VolumeUnit = "L", AvailableVolume = 1, Analytes = [new(Guid.NewGuid(), "A", null, "L", 1m, "g/L")] };
        var part = Part(source, .001m, 3) with { VolumeUnit = "L" };
        var result = SolutionGroupCalculator.Calculate(new(1, [part]), [source]);
        Assert.Equal(1m / 3m, result.Parts[0].Results[0].Concentration);
        Assert.Equal("g/L", result.Parts[0].Results[0].Unit);
        Assert.Equal(.999m, result.Debits[0].RemainingVolume);
    }
    [Fact]
    public void RepeatedOriginIsValidatedByAggregateDebitAndNotPerRowOrAnalyte()
    {
        var source = Source(); var part = Part(source, 60, 100);
        var result = SolutionGroupCalculator.Calculate(new(1, [part, part]), [source]);
        Assert.False(result.HasSufficientVolume); Assert.Equal(-20, Assert.Single(result.Debits).RemainingVolume);
    }
    [Fact]
    public void UnsupportedSourcesUnitsVersionsQuantitiesAndIncompleteGroupsAreRejected()
    {
        var source = Source(); var part = Part(source, 10, 100);
        foreach (var invalid in new[] { part with { VolumeTaken = -1 }, part with { VolumeTaken = 101 }, part with { FinalVolume = 0 },
            part with { VolumeUnit = "g" }, part with { FinalVolumeUnit = "mg/L" }, part with { SourceVersion = Guid.NewGuid() } })
            Assert.Throws<ArgumentException>(() => SolutionGroupCalculator.Calculate(new(1, [invalid]), [source]));
        Assert.Throws<ArgumentException>(() => SolutionGroupCalculator.Calculate(new(1, []), [source]));
        Assert.Throws<ArgumentException>(() => SolutionGroupCalculator.Calculate(new(2, [part]), [source]));
        Assert.Throws<ArgumentException>(() => SolutionGroupCalculator.Calculate(new(1, [part]), [source with { Kind = "Stock" }]));
        Assert.Throws<ArgumentException>(() => SolutionGroupCalculator.Calculate(new(1, [part]), [source with { Analytes = [source.Analytes[0] with { Unit = "%" }] }]));
    }
    private static SolutionPartRequest Part(IntermediateSource s, decimal taken, decimal final, bool internalStandard = false) => new(s.Id, s.Version, taken, "mL", final, "mL", internalStandard);
    private static IntermediateSource Source() => new(Guid.NewGuid(), "Intermedia", "INT-TEST", "Multianalito", 1, "Ensayo", 100, 100, "mL", new(2026, 1, 1), new(2099, 1, 1), Guid.NewGuid(),
        [new(Guid.NewGuid(), "A", "64-17-5", "LOT-A", 100, "mg/L"), new(Guid.NewGuid(), "B", "50-00-0", "LOT-B", 30, "mg/L")]);
}

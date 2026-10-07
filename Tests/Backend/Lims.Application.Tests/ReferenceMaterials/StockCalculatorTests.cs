using Lims.Application.ReferencePreparations;
using Lims.Contracts.ReferencePreparations;
using Lims.Domain.ReferenceMaterials;

namespace Lims.Application.Tests.ReferenceMaterials;

public sealed class StockCalculatorTests
{
    [Fact]
    public void LegacyMeasuredWeightProduces970MgPerLiter()
    {
        var source = Material("mg", 97, 150);
        var result = StockCalculator.Calculate(source, Request(source, 1000, 50) with { ActualWeight = 0.050m, ActualWeightUnit = "g" });
        Assert.Equal(970, result.ActualConcentration);
        Assert.Equal(50, result.ActualWeight);
        Assert.Equal(100, result.RemainingQuantity);
        Assert.Equal(-3, result.DifferencePercent);
    }
    [Fact]
    public void TheoreticalWeightUsesTargetPurityAndVolumeWithoutPrematureRounding()
    {
        var source = Material("g", 99, 1);
        var result = StockCalculator.Calculate(source, Request(source, 1000, 25));
        Assert.Equal(0.025m / 0.99m, result.CalculatedWeight);
        Assert.Null(result.ActualWeight);
        Assert.Null(result.ActualConcentration);
        Assert.Null(result.RemainingQuantity);
        Assert.Null(result.DifferencePercent);
    }
    [Fact]
    public void ActualWeightChangesConcentrationConsumptionAndBalanceButNeverTheoreticalWeight()
    {
        var source = Material("mg", 99.11m, 149.102007m);
        var preview = StockCalculator.Calculate(source, Request(source, 100, 10));
        var actual = StockCalculator.Calculate(source, Request(source, 100, 10) with { ActualWeight = 1.008980m });
        Assert.Equal(preview.CalculatedWeight, actual.CalculatedWeight);
        Assert.Equal(1.008980m, actual.ActualWeight);
        Assert.Equal(148.093027m, actual.RemainingQuantity);
        Assert.Equal(1.008980m * 0.9911m / 0.010m, actual.ActualConcentration);
    }
    [Fact]
    public void LowerActualWeightCanBeSavedEvenWhenTheoreticalWeightExceedsAvailable()
    {
        var source = Material("mg", 100, 80);
        var actual = StockCalculator.Calculate(source, Request(source) with { ActualWeight = 75 });
        Assert.Equal(100, actual.CalculatedWeight);
        Assert.True(actual.HasSufficientMaterial);
        Assert.Equal(5, actual.RemainingQuantity);
    }
    [Fact]
    public void OverdrawReportsShortfallAndCannotBeConsumed()
    {
        var source = Material("mg", 100, 80);
        var result = StockCalculator.Calculate(source, Request(source) with { ActualWeight = 100 });
        Assert.False(result.HasSufficientMaterial);
        Assert.Equal(20, result.ShortfallQuantity);
        Assert.Throws<ArgumentOutOfRangeException>(() => source.Consume(result.ActualWeight!.Value, 42, DateTimeOffset.UtcNow, new DateOnly(2026, 10, 6)));
        Assert.Equal(80, source.AvailableQuantity);
    }
    [Theory]
    [InlineData("g", 0.050, "mg", 50)]
    [InlineData("mg", 50, "g", 0.050)]
    [InlineData("mg", 50, "µg", 50000)]
    [InlineData("µg", 50000, "mg", 50)]
    [InlineData("g", 0.050, "µg", 50000)]
    [InlineData("µg", 50000, "g", 0.050)]
    public void ConvertsMassExplicitly(string from, decimal input, string to, decimal expected) => Assert.Equal(expected, StockUnits.ConvertMass(input, from, to));
    [Theory]
    [InlineData("L", 1, "mL", 1000)]
    [InlineData("mL", 50, "L", 0.050)]
    public void ConvertsVolumeExplicitly(string from, decimal input, string to, decimal expected) => Assert.Equal(expected, StockUnits.ConvertVolume(input, from, to));
    [Theory]
    [InlineData("mg/L", 1000)]
    [InlineData("g/L", 1)]
    [InlineData("µg/L", 1000000)]
    [InlineData("µg/mL", 1000)]
    public void EquivalentConcentrationAndVolumeUnitsGiveSameWeights(string unit, decimal target)
    {
        var source = Material("mg", 100, 150);
        var result = StockCalculator.Calculate(source, Request(source, target, 0.05m) with { ConcentrationUnit = unit, FinalVolumeUnit = "L", ActualWeight = 50 });
        Assert.Equal(50, result.CalculatedWeight);
        Assert.Equal(target, result.ActualConcentration);
    }
    [Fact]
    public void DifferenceIsDerivedAndDisplayedWithSign()
    {
        var source = Material("mg", 100, 2000);
        var result = StockCalculator.Calculate(source, Request(source, 1000, 1000) with { ActualWeight = 1008.8m });
        Assert.Equal(0.88m, result.DifferencePercent);
        Assert.Equal("+0.88 %", result.DifferenceDisplay);
    }
    [Theory]
    [InlineData(0, 1000)]
    [InlineData(-1, 1000)]
    [InlineData(100, 0)]
    [InlineData(0.1234567, 1000)]
    public void RejectsInvalidInputs(decimal concentration, decimal volume)
    {
        var source = Material("mg", 100, 100);
        Assert.Throws<ArgumentException>(() => StockCalculator.Calculate(source, Request(source, concentration, volume)));
    }
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RejectsInvalidActualWeights(decimal weight)
    {
        var source = Material("mg", 100, 100);
        Assert.Throws<ArgumentException>(() => StockCalculator.Calculate(source, Request(source) with { ActualWeight = weight }));
    }
    [Fact]
    public void DoesNotGuessPpmLiquidStandardsOrRoundPhysicalDebits()
    {
        var solid = Material("mg", 100, 100);
        var liquid = Material("mL", 100, 100);
        Assert.Throws<ArgumentException>(() => StockCalculator.Calculate(solid, Request(solid) with { ConcentrationUnit = "ppm" }));
        Assert.Throws<ArgumentException>(() => StockCalculator.Calculate(liquid, Request(liquid)));
        var grams = Material("g", 100, 1);
        Assert.Throws<ArgumentException>(() => StockCalculator.Calculate(grams, Request(grams) with { ActualWeight = 0.000001m, ActualWeightUnit = "µg" }));
    }
    [Fact]
    public void ExactConsumptionDepletesMaterialAndRecordsActorAndVersion()
    {
        var source = Material("mg", 100, 100);
        var version = source.Version;
        var result = StockCalculator.Calculate(source, Request(source) with { ActualWeight = 100 });
        source.Consume(result.ActualWeight!.Value, 84, DateTimeOffset.UtcNow, new DateOnly(2026, 10, 6));
        Assert.Equal(0, source.AvailableQuantity);
        Assert.Equal(84, source.UpdatedByUserId);
        Assert.NotEqual(version, source.Version);
        Assert.Equal(ReferenceMaterialStatus.Depleted, source.EffectiveStatus(new DateOnly(2026, 10, 6)));
    }
    internal static ReferenceMaterial Material(string unit, decimal purity, decimal amount) => new(Guid.NewGuid(),
        "Estándar de prueba", "64-17-5", "CAT-001", new ReferenceMethod(1, "Ensayo", true), purity, "LOT-001", "Marca",
        new DateOnly(2026, 1, 1), new DateOnly(2027, 12, 31), amount, new ReferenceUnit(1, "Unidad", unit, true),
        1, "Ambiente", new ReferenceLocation(1, "Laboratorio", true), 42, DateTimeOffset.UtcNow);
    internal static StockCalculationRequest Request(ReferenceMaterial source, decimal concentration = 100, decimal volume = 1000) =>
        new(source.Id, source.Version, concentration, "mg/L", volume, "mL");
}

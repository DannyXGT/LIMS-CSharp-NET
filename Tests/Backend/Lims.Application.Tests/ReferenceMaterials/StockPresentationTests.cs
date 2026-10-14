using Lims.Contracts.ReferencePreparations;

namespace Lims.Application.Tests.ReferenceMaterials;

public sealed class StockPresentationTests
{
    [Theory]
    [InlineData(98.99, 1.01, 98.99, "Information")]
    [InlineData(61, 39, 61, "Information")]
    [InlineData(60, 40, 60, "Warning")]
    [InlineData(25, 75, 25, "Warning")]
    [InlineData(24.99, 75.01, 24.99, "Danger")]
    [InlineData(0, 100, 0, "Danger")]
    [InlineData(-1, 100, 0, "Danger")]
    public void UsesPersistedBalanceAndCorrectThresholds(decimal available, decimal consumed, decimal percent, string tone)
    {
        var availability = new StockAvailability(100, "mL", available);
        Assert.True(availability.IsCalculated);
        Assert.Equal(consumed, availability.ConsumedVolume);
        Assert.Equal(percent, availability.RemainingPercent);
        Assert.Equal(tone, availability.Tone);
        Assert.InRange(availability.ProgressValue, 0, 100);
    }

    [Theory]
    [InlineData(0, "mL", 0)]
    [InlineData(-1, "mL", 0)]
    [InlineData(100, "mL", 101)]
    [InlineData(100, "mg", 50)]
    public void InvalidAvailabilityUsesControlledFallback(decimal initial, string unit, decimal available)
    {
        var availability = new StockAvailability(initial, unit, available);
        Assert.False(availability.IsCalculated);
        Assert.Null(availability.ConsumedVolume);
        Assert.Null(availability.RemainingPercent);
        Assert.Equal("Sin disponibilidad calculada", availability.AvailableDisplay);
        Assert.Equal("— %", availability.PercentDisplay);
        Assert.Equal("Neutral", availability.Tone);
    }

    [Fact]
    public void MissingBalanceIsNotAssumedToBeFullAndLitersKeepTheirUnit()
    {
        Assert.False(new StockAvailability(100, "mL", null).IsCalculated);
        var liters = new StockAvailability(1, "L", 0.9899m);
        Assert.Equal(98.99m, liters.RemainingPercent);
        Assert.Equal("0.99 L", liters.AvailableDisplay);
        Assert.Equal("98.99 %", liters.PercentDisplay);
        var large = new StockAvailability(decimal.MaxValue, "L", decimal.MaxValue);
        Assert.Equal(100m, large.RemainingPercent);
    }

    [Fact]
    public void ExhaustedStockStillHasAnAccessibleHistoricalIdentity()
    {
        var row = new StockSummary(Guid.NewGuid(), "STK", "Stock", "Origen", "Lote", 100, 100, "mg/L", 100, "mL", new(2026, 10, 12), "Usuario", "Active", AvailableVolume: 0);
        Assert.Equal("Agotado", row.StatusLabel);
        Assert.Equal("Danger", row.StatusTone);
        Assert.Equal("0 mL", row.Availability.AvailableDisplay);
    }

    [Fact]
    public void FormatsHumanQuantitiesDatesAndLocalizedAudit()
    {
        var row = new StockSummary(Guid.NewGuid(), "STK-20261006-001", "Stock de prueba", "Origen", "L-1",
            100.000000m, 100.000000m, "mg/L", 1000.000000m, "mL", new DateOnly(2026, 10, 6), "Ana López", "Active");
        Assert.Equal("100 mg/L", row.ConcentrationDisplay);
        Assert.Equal("1000 mL", row.FinalDisplay);
        Assert.Equal("06/10/2026", row.DateDisplay);
        Assert.Equal("Disponible", row.StatusLabel);
        Assert.DoesNotContain(row.Id.ToString(), row.ToString(), StringComparison.Ordinal);
        var movement = new StockMovement(Guid.NewGuid(), 0.102000m, "g", 1m, 0.898000m, "Ana López", new DateTimeOffset(2026, 10, 6, 18, 0, 0, TimeSpan.Zero));
        Assert.Equal("0.102 g", movement.QuantityDisplay);
        Assert.Contains("12:00", movement.OccurredDisplay, StringComparison.Ordinal);
        Assert.DoesNotContain("+00", movement.OccurredDisplay, StringComparison.Ordinal);
    }
}

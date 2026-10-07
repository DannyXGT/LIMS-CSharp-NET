using Lims.Contracts.ReferencePreparations;

namespace Lims.Application.Tests.ReferenceMaterials;

public sealed class StockPresentationTests
{
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

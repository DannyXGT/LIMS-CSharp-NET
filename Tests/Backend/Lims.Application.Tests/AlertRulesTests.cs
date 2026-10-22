using Lims.Application.Alerts;
using Lims.Contracts.Alerts;

namespace Lims.Application.Tests;

public sealed class AlertRulesTests
{
    private static readonly DateOnly Today = new(2026, 10, 21);
    private static AlertSource Source(decimal? percent, int days = 100) => new(Guid.NewGuid(), "ReferenceMaterial", AlertModule.Standards, 7,
        "Material ficticio", "TEST-01", "LOTE-TEST", "Estándar", percent, percent ?? 0, "mg", Today.AddDays(days), true);
    [Theory]
    [InlineData(100, null)] [InlineData(25.001, null)] [InlineData(25, "Low")] [InlineData(10.001, "Low")]
    [InlineData(10, "Critical")] [InlineData(0.001, "Critical")] [InlineData(0, "Depleted")]
    public void AvailabilityUsesInclusiveBoundariesAndOnlyOneCondition(decimal percent, string? expected)
    {
        var rows = AlertRules.Evaluate(Source(percent), AlertSettings.Default, Today);
        if (expected is null) Assert.Empty(rows); else Assert.Equal(expected, Assert.Single(rows).Code);
    }
    [Theory]
    [InlineData(31, null)] [InlineData(30, "Due30")] [InlineData(16, "Due30")] [InlineData(15, "Due15")]
    [InlineData(7, "Due7")] [InlineData(1, "Due1")] [InlineData(0, "Due1")] [InlineData(-1, "Expired")]
    public void ExpirationPreservesDomainDateBoundaryAndChoosesNearestThreshold(int days, string? expected)
    {
        var rows = AlertRules.Evaluate(Source(100, days), AlertSettings.Default, Today);
        if (expected is null) Assert.Empty(rows); else Assert.Equal(expected, Assert.Single(rows).Code);
    }
    [Fact] public void UnknownInventoryDoesNotBecomeAnInventedDepletion()
    { Assert.Empty(AlertRules.Evaluate(Source(null), AlertSettings.Default, Today)); }
    [Fact] public void ArchivedOrFutureSourcesDoNotGenerateAlerts()
    { Assert.Empty(AlertRules.Evaluate(Source(0, -1) with { Operational = false }, AlertSettings.Default, Today)); }
    [Fact] public void ConfigurableThresholdsAndDaysReplaceDefaults()
    {
        var result = AlertRules.Evaluate(Source(18, 45), AlertSettings.Default with { ExpirationDays = [60, 10], LowPercent = 20, CriticalPercent = 19 }, Today);
        Assert.Contains(result, a => a.Code == "Due60"); Assert.Contains(result, a => a.Code == "Critical");
    }
    [Fact] public void InvalidConfigurationCannotInvertSeverityOrAcceptUnknownDepartments()
    {
        Assert.NotNull(AlertRules.Validate(AlertSettings.Default with { CriticalPercent = 26 }));
        Assert.NotNull(AlertRules.Validate(AlertSettings.Default with { ExpirationDays = [7, 7] }));
        Assert.NotNull(AlertRules.Validate(AlertSettings.Default with { Scopes = [new(AlertModule.Stock | AlertModule.Intermedia, 7)] }));
        Assert.NotNull(AlertRules.Validate(AlertSettings.Default with { TimeZoneId = "invented-zone" }));
        Assert.Null(AlertRules.Validate(AlertSettings.Default));
    }
}

using Lims.Contracts.Dashboard;
using Lims.Desktop.Presentation;

namespace Lims.Desktop.Tests;

public sealed class DashboardAnalyticsTests
{
    private static readonly int[] ExpectedBuckets = [7, 3, 2, 3, 5];
    [Fact]
    public void AvailabilityBucketsDoNotCountLowAndCriticalTwice()
    {
        var data = new DashboardStandards(20, 12, 2, 5, null, 3, []);
        var buckets = DashboardAnalytics.Availability(data);
        Assert.Equal(ExpectedBuckets, buckets.Select(p => p.Count));
        Assert.Equal(data.Registered, buckets.Sum(p => p.Count));
    }

    private static DashboardResponse Catalogue()
    {
        var priorities = Enumerable.Range(0, 6).Select(i => new DashboardMaterial(Guid.NewGuid(), "Standard",
            $"Prioridad {i}", "Lote", "Método A", new(2027, 1, 1), 20, 10)).ToArray();
        return new("Administrator", "Persona", "Administrador", null, DateTimeOffset.UtcNow, null, [],
            new(53, 46, 0, 0, null, 3, []), null, null, priorities, [priorities[0]], [],
            new("Standards", 53, 46, [new("Método A", 16, 10, 70), new("Método B", 37, 36, 90)]));
    }

    [Fact]
    public void SixPriorityRowsNeverLimitTheFiftyThreeRecordMethodDistribution()
    {
        var data = Catalogue();
        Assert.Equal(6, DashboardAnalytics.PrioritySample(data).Count);
        var methods = DashboardAnalytics.Methods(data);
        Assert.Equal(53, methods.Sum(m => m.Count));
        Assert.Equal(37, methods.Single(m => m.Label == "Método B").Count);
    }

    [Fact]
    public void AvailabilityUsesServerMeansFromAllApplicableMaterials()
    {
        var means = DashboardAnalytics.MethodAvailability(Catalogue());
        Assert.Equal(46, means.Sum(m => m.SampleCount));
        Assert.Equal(70, means.Single(m => m.Label == "Método A").Percent);
        Assert.Equal(90, means.Single(m => m.Label == "Método B").Percent);
    }

    [Fact]
    public void MissingServerAggregatesNeverFallBackToPriorityRows()
    {
        var legacy = Catalogue() with { MethodStatistics = null };
        Assert.NotEmpty(legacy.Expirations);
        Assert.Empty(DashboardAnalytics.Methods(legacy));
        Assert.Empty(DashboardAnalytics.MethodAvailability(legacy));
    }

    [Fact]
    public void ZeroApplicableBalancesAreExcludedAndUnassignedMethodsRemainCounted()
    {
        var data = Catalogue() with { MethodStatistics = new("Standards", 53, 0, [new("Sin método", 53, 0, null)]) };
        Assert.Equal("Sin método", Assert.Single(DashboardAnalytics.Methods(data)).Label);
        Assert.Empty(DashboardAnalytics.MethodAvailability(data));
    }

    [Theory]
    [InlineData(46, 3, 1, 3)]
    [InlineData(1, 1, 1, 0)]
    [InlineData(16, 37, 0, 0)]
    public void DisplayedPercentagesSumToOneHundredAtOneDecimal(int a, int b, int c, int d)
    {
        var points = new[] { a, b, c, d }.Select((count, i) => new DashboardChartPoint(i.ToString(System.Globalization.CultureInfo.InvariantCulture), count, "")).ToArray();
        var percentages = DashboardAnalytics.DisplayPercentages(points);
        Assert.Equal(100m, percentages.Sum());
        Assert.All(percentages, p => Assert.Equal(p, decimal.Round(p, 1)));
        Assert.All(percentages, p => Assert.InRange(p, 0, 100));
    }
}

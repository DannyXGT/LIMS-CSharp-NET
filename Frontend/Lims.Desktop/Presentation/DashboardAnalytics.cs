using Lims.Contracts.Dashboard;

namespace Lims.Desktop.Presentation;

public sealed record DashboardChartPoint(string Label, int Count, string Brush);
public sealed record DashboardAvailabilityPoint(string Label, double Percent, int SampleCount, string Brush);

/// <summary>Presentation of the existing server aggregates. No inventory download or new business thresholds.</summary>
public static class DashboardAnalytics
{
    public static IReadOnlyList<DashboardChartPoint> States(DashboardStandards standards) => standards.Distribution
        .Select(s => new DashboardChartPoint(s.Label, s.Count, s.Status switch
        {
            "Active" => "LimsAccentBrush", "Expired" => "LimsDangerBrush",
            "Archived" => "LimsTextTertiaryBrush", "Depleted" => "LimsWarningBrush", _ => "LimsTextSecondaryBrush"
        })).ToArray();

    public static IReadOnlyList<DashboardChartPoint> Availability(DashboardStandards standards) =>
    [
        new("Activos sin alerta baja", standards.Active - standards.Low, "LimsSuccessBrush"),
        new("Disponibilidad baja", standards.Low - standards.Critical, "LimsWarningBrush"),
        new("Disponibilidad crítica", standards.Critical, "LimsDangerBrush"),
        new("Vencidos", standards.Expired, "LimsTextTertiaryBrush"),
        new("Otros estados", standards.Registered - standards.Active - standards.Expired, "LimsTextSecondaryBrush")
    ];

    public static IReadOnlyList<DashboardAvailabilityPoint> MethodAvailability(DashboardResponse data) => (data.MethodStatistics?.Methods ?? [])
        .Where(m => m.AvailabilityCount > 0 && m.AverageAvailabilityPercent is >= 0 and <= 100)
        .OrderBy(m => m.Method, StringComparer.CurrentCulture)
        .Select((m, i) => new DashboardAvailabilityPoint(m.Method, (double)m.AverageAvailabilityPercent!.Value, m.AvailabilityCount, Color(i))).ToArray();

    private static string Color(int index) => (index % 4) switch
    { 0 => "LimsAccentBrush", 1 => "LimsSuccessBrush", 2 => "LimsTextTertiaryBrush", _ => "LimsWarningBrush" };

    public static IReadOnlyList<DashboardMaterial> PrioritySample(DashboardResponse data) => data.Expirations
        .Concat(data.LowAvailability).DistinctBy(m => (m.Kind, m.Id)).ToArray();

    public static IReadOnlyList<DashboardChartPoint> Methods(DashboardResponse data) => (data.MethodStatistics?.Methods ?? [])
        .OrderByDescending(m => m.Count).ThenBy(m => m.Method, StringComparer.CurrentCulture)
        .Select((m, i) => new DashboardChartPoint(m.Method, m.Count, Color(i))).ToArray();

    // Largest-remainder rounding: displayed parts sum to 100.0%, while chart
    // geometry and bar lengths continue to use the original unrounded counts.
    public static IReadOnlyList<decimal> DisplayPercentages(IReadOnlyList<DashboardChartPoint> points)
    {
        var total = points.Sum(p => p.Count);
        if (total == 0) return points.Select(_ => 0m).ToArray();
        var exact = points.Select(p => p.Count * 1000m / total).ToArray();
        var units = exact.Select(decimal.Floor).ToArray();
        var remaining = (int)(1000m - units.Sum());
        foreach (var index in Enumerable.Range(0, points.Count).OrderByDescending(i => exact[i] - units[i]).ThenBy(i => i).Take(remaining))
            units[index]++;
        return units.Select(u => u / 10m).ToArray();
    }
}

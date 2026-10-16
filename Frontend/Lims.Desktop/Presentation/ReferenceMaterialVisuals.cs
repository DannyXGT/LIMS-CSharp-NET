using Lims.Contracts.ReferenceMaterials;
using Microsoft.UI.Xaml;

namespace Lims.Desktop.Presentation;

/// <summary>Visual projections only; no inventory or expiration policy.</summary>
public static class ReferenceMaterialVisuals
{
    public static double? Availability(decimal? available, decimal total) =>
        available.HasValue && total > 0 && available >= 0 && available <= total
            ? (double)(available.Value / total * 100)
            : null;

    public static Visibility AvailabilityVisibility(decimal? available, decimal total) =>
        Availability(available, total).HasValue ? Visibility.Visible : Visibility.Collapsed;

    public static double AvailabilityValue(decimal? available, decimal total) => Availability(available, total) ?? 0;

    public static string AvailabilityLabel(decimal? available, decimal total) =>
        Availability(available, total) is { } percent
            ? $"{ReferenceMaterialPresentation.Number(decimal.Round((decimal)percent, 2))} %"
            : string.Empty;

    public static string RowName(ReferenceMaterialSummary row)
    {
        var unit = row.Unit switch
        {
            "mg" => "miligramos",
            "g" => "gramos",
            "mL" => "mililitros",
            "L" => "litros",
            "µg" or "ug" => "microgramos",
            _ => row.Unit,
        };
        var cas = ReferenceMaterialPresentation.HasValue(row.CasNumber) ? $", CAS {row.CasDisplay}" : string.Empty;
        var availability = row.AvailableQuantity.HasValue
            ? $"{ReferenceMaterialPresentation.Quantity(row.AvailableQuantity, unit)} disponibles"
            : "disponibilidad no informada";
        return $"{row.NameDisplay}{cas}, {row.StatusLabel}, {availability}";
    }
}

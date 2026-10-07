using Lims.Contracts.ReferenceMaterials;

namespace Lims.Desktop.Presentation;

/// <summary>Visual projections only; no inventory or expiration policy.</summary>
public static class ReferenceMaterialVisuals
{
    public static double? Availability(decimal? available, decimal total) =>
        available.HasValue && total > 0 && available >= 0 && available <= total
            ? (double)(available.Value / total * 100)
            : null;

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

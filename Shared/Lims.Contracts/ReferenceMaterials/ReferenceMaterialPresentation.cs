using System.Globalization;

namespace Lims.Contracts.ReferenceMaterials;

/// <summary>Shared presentation conventions for Estándares; never changes stored values.</summary>
public static class ReferenceMaterialPresentation
{
    private static readonly CultureInfo ApplicationCulture = CultureInfo.GetCultureInfo("es-GT");
    // Application timezone is explicit; the workstation timezone must not affect audit dates.
    private static readonly TimeZoneInfo ApplicationTimeZone = TimeZoneInfo.FindSystemTimeZoneById("America/Guatemala");

    public static string Number(decimal value) => value.ToString("0.############################", ApplicationCulture);
    public static string Purity(decimal value) => $"{Number(value)} %";
    public static string Quantity(decimal? value, string unit) => value.HasValue
        ? $"{Number(value.Value)} {unit}".TrimEnd()
        : "No disponible";
    public static string Packages(decimal quantity, string unit, int count) =>
        $"{Quantity(quantity, unit)} × {count.ToString(ApplicationCulture)} {(count == 1 ? "unidad" : "unidades")}";
    public static string AvailabilityPercent(decimal? available, decimal perPackage, int count) =>
        available.HasValue && perPackage > 0 && count > 0
            ? $"{(available.Value / (perPackage * count) * 100).ToString("0.##", ApplicationCulture)} % disponible"
            : string.Empty;
    public static string Date(DateOnly value) => value.ToString("dd/MM/yyyy", ApplicationCulture);
    public static string LocalTimestamp(DateTimeOffset? value) => value.HasValue
        ? TimeZoneInfo.ConvertTime(value.Value, ApplicationTimeZone).ToString("dd/MM/yyyy · HH:mm", ApplicationCulture)
        : string.Empty;
    public static DateOnly ApplicationToday() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, ApplicationTimeZone).Date);
    public static bool HasValue(string? value) => !string.IsNullOrWhiteSpace(value) && !string.Equals(value.Trim(), "NULL", StringComparison.OrdinalIgnoreCase);
    public static string Optional(string? value, string fallback = "No informado") => HasValue(value) ? value!.Trim() : fallback;
    public static string User(string? name) => HasValue(name) ? name!.Trim() : "Usuario no disponible";
    public static string StatusLabel(string? status) => status switch
    {
        "Active" => "Activo",
        "ExpiringSoon" => "Por vencer",
        "Depleted" => "Agotado",
        "Expired" => "Vencido",
        "Blocked" => "Bloqueado",
        "Replaced" => "Reemplazado",
        "Archived" => "Archivado",
        "Retired" => "Retirado",
        _ => "Estado no disponible",
    };
    public static string StatusTone(string? status) => status switch
    {
        "Active" => "Success",
        "ExpiringSoon" or "Depleted" => "Warning",
        "Expired" or "Blocked" => "Danger",
        "Replaced" => "Information",
        _ => "Neutral",
    };
}

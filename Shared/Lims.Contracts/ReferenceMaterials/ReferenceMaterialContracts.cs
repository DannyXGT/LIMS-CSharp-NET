using System.Globalization;

namespace Lims.Contracts.ReferenceMaterials;

public sealed record ReferenceMethodOption(int Id, string Name);

public sealed record ReferenceUnitOption(int Id, string Name, string Symbol)
{
    public string DisplayName => $"{Symbol} — {Name}";
}

public sealed record ReferenceLocationOption(int Id, string Name);

public sealed record ReferenceMaterialSummary(
    Guid Id,
    string Name,
    string? CasNumber,
    string? CatalogNumber,
    string Method,
    string Lot,
    string Brand,
    decimal PurityPercent,
    DateOnly ExpirationDate,
    string Status,
    decimal TotalQuantity,
    string Unit,
    decimal? AvailableQuantity,
    Guid Version)
{
    public string StatusLabel => ReferenceMaterialPresentation.StatusLabel(Status);
    public string AvailableDisplay => ReferenceMaterialPresentation.Quantity(AvailableQuantity, Unit);
}

public sealed record ReferenceMaterialDetail(
    Guid Id,
    string Name,
    string? CasNumber,
    string? CatalogNumber,
    int MethodId,
    string Method,
    decimal PurityPercent,
    string Lot,
    string Brand,
    DateOnly ReceivedDate,
    DateOnly ExpirationDate,
    decimal PresentationQuantity,
    int UnitId,
    string Unit,
    int PackageCount,
    decimal TotalQuantity,
    decimal? AvailableQuantity,
    string StorageTemperature,
    int LocationId,
    string StorageLocation,
    string Status,
    int CreatedByUserId,
    DateTimeOffset CreatedAt,
    int UpdatedByUserId,
    DateTimeOffset UpdatedAt,
    int? ArchivedByUserId,
    DateTimeOffset? ArchivedAt,
    string? ArchiveReason,
    Guid? ReplacedByMaterialId,
    Guid Version)
{
    public string StatusLabel => ReferenceMaterialPresentation.StatusLabel(Status);
    public string TotalDisplay => ReferenceMaterialPresentation.Quantity(TotalQuantity, Unit);
    public string AvailableDisplay => ReferenceMaterialPresentation.Quantity(AvailableQuantity, Unit);
}

public sealed record ReferenceMaterialPage(
    IReadOnlyList<ReferenceMaterialSummary> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record CreateReferenceMaterialRequest(
    string Name,
    string? CasNumber,
    string? CatalogNumber,
    int MethodId,
    decimal PurityPercent,
    string Lot,
    string Brand,
    DateOnly ReceivedDate,
    DateOnly ExpirationDate,
    decimal PresentationQuantity,
    int UnitId,
    int PackageCount,
    string StorageTemperature,
    int LocationId);

public sealed record UpdateReferenceMaterialRequest(
    string Name,
    string? CasNumber,
    string? CatalogNumber,
    int MethodId,
    decimal PurityPercent,
    string Lot,
    string Brand,
    DateOnly ReceivedDate,
    DateOnly ExpirationDate,
    decimal PresentationQuantity,
    int UnitId,
    int PackageCount,
    string StorageTemperature,
    int LocationId,
    Guid Version);

public sealed record ArchiveReferenceMaterialRequest(string Reason, Guid Version);

public sealed record ReplaceReferenceMaterialRequest(
    CreateReferenceMaterialRequest Replacement,
    string Reason,
    Guid Version);

internal static class ReferenceMaterialPresentation
{
    public static string StatusLabel(string status) => status switch
    {
        "Active" => "Activo",
        "Depleted" => "Agotado",
        "Expired" => "Vencido",
        "Blocked" => "Bloqueado",
        "Replaced" => "Reemplazado",
        "Archived" => "Archivado",
        "Retired" => "Retirado",
        _ => status,
    };

    public static string Quantity(decimal? quantity, string unit) => quantity is null
        ? "No disponible"
        : $"{quantity.Value.ToString("0.######", CultureInfo.CurrentCulture)} {unit}";
}

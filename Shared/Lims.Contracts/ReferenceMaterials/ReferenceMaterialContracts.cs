using System.Text.Json.Serialization;

namespace Lims.Contracts.ReferenceMaterials;

public sealed record ReferenceMethodOption(int Id, string Name)
{
    public override string ToString() => ReferenceMaterialPresentation.Optional(Name);
}

public sealed record ReferenceUnitOption(int Id, string Name, string Symbol)
{
    public string DisplayName => $"{Symbol} — {Name}";
    public override string ToString() => DisplayName;
}

public sealed record ReferenceLocationOption(int Id, string Name)
{
    public override string ToString() => ReferenceMaterialPresentation.Optional(Name);
}

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
    [JsonIgnore] public string PurityDisplay => ReferenceMaterialPresentation.Purity(PurityPercent);
    [JsonIgnore] public string ExpirationDisplay => ReferenceMaterialPresentation.Date(ExpirationDate);
    [JsonIgnore] public string StatusTone => ReferenceMaterialPresentation.StatusTone(Status);
    [JsonIgnore] public string CasDisplay => ReferenceMaterialPresentation.HasValue(CasNumber) ? CasNumber!.Trim() : string.Empty;
    [JsonIgnore] public string CatalogDisplay => ReferenceMaterialPresentation.Optional(CatalogNumber);
    [JsonIgnore] public string NameDisplay => ReferenceMaterialPresentation.Optional(Name);
    [JsonIgnore] public string LotDisplay => ReferenceMaterialPresentation.Optional(Lot);
    [JsonIgnore] public string BrandDisplay => ReferenceMaterialPresentation.Optional(Brand);
    public override string ToString() => NameDisplay;
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
    Guid Version,
    string? CreatedByName = null,
    string? UpdatedByName = null,
    string? ArchivedByName = null,
    string? ReplacedByMaterialName = null)
{
    public string StatusLabel => ReferenceMaterialPresentation.StatusLabel(Status);
    public string TotalDisplay => ReferenceMaterialPresentation.Quantity(TotalQuantity, Unit);
    public string AvailableDisplay => ReferenceMaterialPresentation.Quantity(AvailableQuantity, Unit);
    [JsonIgnore] public string NameDisplay => ReferenceMaterialPresentation.Optional(Name);
    [JsonIgnore] public string MethodDisplay => ReferenceMaterialPresentation.Optional(Method);
    [JsonIgnore] public string LotDisplay => ReferenceMaterialPresentation.Optional(Lot);
    [JsonIgnore] public string BrandDisplay => ReferenceMaterialPresentation.Optional(Brand);
    [JsonIgnore] public string LocationDisplay => ReferenceMaterialPresentation.Optional(StorageLocation);
    [JsonIgnore] public string TemperatureDisplay => ReferenceMaterialPresentation.Optional(StorageTemperature);
    [JsonIgnore] public string PurityDisplay => ReferenceMaterialPresentation.Purity(PurityPercent);
    [JsonIgnore] public string StatusTone => ReferenceMaterialPresentation.StatusTone(Status);
    [JsonIgnore] public string CasDisplay => ReferenceMaterialPresentation.HasValue(CasNumber) ? $"CAS {CasNumber}" : string.Empty;
    [JsonIgnore] public bool HasCas => ReferenceMaterialPresentation.HasValue(CasNumber);
    [JsonIgnore] public bool HasCatalog => ReferenceMaterialPresentation.HasValue(CatalogNumber);
    [JsonIgnore] public string ValidityDisplay => $"{ReferenceMaterialPresentation.Date(ReceivedDate)} → {ReferenceMaterialPresentation.Date(ExpirationDate)}";
    [JsonIgnore] public string PresentationDisplay => ReferenceMaterialPresentation.Packages(PresentationQuantity, Unit, PackageCount);
    [JsonIgnore] public string AvailabilityHeadline => AvailableQuantity is null ? "Disponibilidad no informada" : $"{AvailableDisplay} disponibles";
    [JsonIgnore] public string AvailabilityPercentDisplay => ReferenceMaterialPresentation.AvailabilityPercent(AvailableQuantity, PresentationQuantity, PackageCount);
    [JsonIgnore] public bool HasAvailabilityPercent => AvailabilityPercentDisplay.Length > 0;
    [JsonIgnore] public string CreatedByDisplay => ReferenceMaterialPresentation.User(CreatedByName);
    [JsonIgnore] public string UpdatedByDisplay => ReferenceMaterialPresentation.User(UpdatedByName);
    [JsonIgnore] public string CreatedAtDisplay => ReferenceMaterialPresentation.LocalTimestamp(CreatedAt);
    [JsonIgnore] public string UpdatedAtDisplay => ReferenceMaterialPresentation.LocalTimestamp(UpdatedAt);
    [JsonIgnore] public bool HasArchiveReason => Status == "Archived" && ReferenceMaterialPresentation.HasValue(ArchiveReason);
    [JsonIgnore] public bool HasArchiveActor => Status == "Archived" && ArchivedByUserId.HasValue;
    [JsonIgnore] public bool HasArchiveTimestamp => Status == "Archived" && ArchivedAt.HasValue;
    [JsonIgnore] public string ArchivedByDisplay => ReferenceMaterialPresentation.User(ArchivedByName);
    [JsonIgnore] public string ArchivedAtDisplay => ReferenceMaterialPresentation.LocalTimestamp(ArchivedAt);
    [JsonIgnore] public bool HasReplacement => Status == "Replaced" && ReplacedByMaterialId.HasValue;
    [JsonIgnore] public string ReplacementDisplay => ReferenceMaterialPresentation.HasValue(ReplacedByMaterialName) ? ReplacedByMaterialName! : "Estándar no disponible";
    [JsonIgnore] public bool HasReplacementReason => HasReplacement && ReferenceMaterialPresentation.HasValue(ArchiveReason);
    public override string ToString() => NameDisplay;
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

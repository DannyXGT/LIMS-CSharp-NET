using System.Text.Json.Serialization;
using Lims.Contracts.ReferenceMaterials;

namespace Lims.Contracts.ReferencePreparations;

public sealed record StockSource(
    Guid Id, string Name, string? CasNumber, string? CatalogNumber, string Lot, string Brand,
    string Method, decimal PurityPercent, decimal AvailableQuantity, decimal TotalQuantity,
    string Unit, DateOnly ExpirationDate, string Location, Guid Version, DateOnly? ReceivedDate = null)
{
    [JsonIgnore] public string SearchDisplay => $"{Name} · CAS {ReferenceMaterialPresentation.Optional(CasNumber)} · {Lot} · {ReferenceMaterialPresentation.Optional(CatalogNumber)}";
    [JsonIgnore] public string AvailableDisplay => StockPresentation.Quantity(AvailableQuantity, Unit);
    [JsonIgnore] public string PurityDisplay => ReferenceMaterialPresentation.Purity(PurityPercent);
    [JsonIgnore] public string ExpirationDisplay => ReferenceMaterialPresentation.Date(ExpirationDate);
    [JsonIgnore] public string AvailabilityPercentDisplay => ReferenceMaterialPresentation.Purity(AvailableQuantity / TotalQuantity * 100m);
    [JsonIgnore] public double AvailabilityPercent => (double)(AvailableQuantity / TotalQuantity * 100m);
    public override string ToString() => SearchDisplay;
}

public sealed record StockSourcePage(IReadOnlyList<StockSource> Items, int Page, int PageSize, int TotalCount);
public sealed record StockOptions(IReadOnlyList<string> ConcentrationUnits, IReadOnlyList<string> FinalVolumeUnits);
public sealed record StockCalculationRequest(Guid SourceMaterialId, Guid SourceVersion,
    decimal TargetConcentration, string ConcentrationUnit, decimal FinalVolume, string FinalVolumeUnit,
    decimal? ActualWeight = null, string? ActualWeightUnit = null);
public sealed record CreateStockRequest(Guid RequestId, string Name, StockCalculationRequest Calculation,
    DateOnly PreparationDate, string? Notes, DateOnly? ExpirationDate = null, string? StorageTemperature = null);
public sealed record StockCalculation(
    decimal CalculatedWeight, decimal? ActualWeight, string SourceUnit, decimal AvailableQuantity,
    decimal? RemainingQuantity, decimal ShortfallQuantity, bool HasSufficientMaterial,
    decimal PurityPercent, decimal TargetConcentration, decimal? ActualConcentration, string ConcentrationUnit,
    decimal FinalVolume, string FinalVolumeUnit, string Formula)
{
    [JsonIgnore] public decimal? DifferencePercent => (ActualConcentration - TargetConcentration) / TargetConcentration * 100m;
    [JsonIgnore] public string DifferenceDisplay => StockPresentation.Difference(DifferencePercent);
    [JsonIgnore] public string TargetDisplay => StockPresentation.Quantity(TargetConcentration, ConcentrationUnit);
    [JsonIgnore] public string CalculatedWeightUnit => SourceUnit;
    [JsonIgnore] public string ActualWeightUnit => SourceUnit;
    [JsonIgnore] public string ActualConcentrationUnit => ConcentrationUnit;
    [JsonIgnore] public string RequiredDisplay => StockPresentation.Quantity(CalculatedWeight, SourceUnit);
    [JsonIgnore] public string ConsumedDisplay => StockPresentation.Quantity(ActualWeight, SourceUnit);
    [JsonIgnore] public string AvailableDisplay => StockPresentation.Quantity(AvailableQuantity, SourceUnit);
    [JsonIgnore] public string RemainingDisplay => StockPresentation.Quantity(RemainingQuantity, SourceUnit);
    [JsonIgnore] public string ShortfallDisplay => StockPresentation.Quantity(ShortfallQuantity, SourceUnit);
    [JsonIgnore] public string FinalDisplay => StockPresentation.Quantity(FinalVolume, FinalVolumeUnit);
    [JsonIgnore] public string ConcentrationDisplay => StockPresentation.Quantity(ActualConcentration, ConcentrationUnit);
    [JsonIgnore] public string PurityDisplay => ReferenceMaterialPresentation.Purity(PurityPercent);
}

public sealed record StockSummary(Guid Id, string Code, string Name, string SourceName, string SourceLot,
    decimal TargetConcentration, decimal ActualConcentration, string ConcentrationUnit,
    decimal FinalVolume, string FinalVolumeUnit, DateOnly PreparationDate, string PreparedBy, string Status,
    decimal ActualWeight = 0, string ActualWeightUnit = "mg", DateOnly? ExpirationDate = null)
{
    [JsonIgnore] public string WeightDisplay => StockPresentation.Quantity(ActualWeight, ActualWeightUnit);
    [JsonIgnore] public string ExpirationDisplay => ExpirationDate is { } date ? ReferenceMaterialPresentation.Date(date) : "No informada";
    [JsonIgnore] public string ConcentrationDisplay => StockPresentation.Quantity(ActualConcentration, ConcentrationUnit);
    [JsonIgnore] public string FinalDisplay => StockPresentation.Quantity(FinalVolume, FinalVolumeUnit);
    [JsonIgnore] public string DateDisplay => ReferenceMaterialPresentation.Date(PreparationDate);
    [JsonIgnore] public string StatusLabel => Status switch { "Active" => "Disponible", "Expired" => "Expirado", _ => "No disponible" };
    [JsonIgnore] public string StatusTone => Status == "Active" ? "Success" : "Warning";
    public override string ToString() => $"{Code} · {Name}";
}
public sealed record StockPage(IReadOnlyList<StockSummary> Items, int Page, int PageSize, int TotalCount);
public sealed record StockMovement(Guid Id, decimal Quantity, string Unit, decimal BalanceBefore,
    decimal BalanceAfter, string Actor, DateTimeOffset OccurredAt)
{
    [JsonIgnore] public string QuantityDisplay => StockPresentation.Quantity(Quantity, Unit);
    [JsonIgnore] public string BeforeDisplay => StockPresentation.Quantity(BalanceBefore, Unit);
    [JsonIgnore] public string AfterDisplay => StockPresentation.Quantity(BalanceAfter, Unit);
    [JsonIgnore] public string OccurredDisplay => ReferenceMaterialPresentation.LocalTimestamp(OccurredAt);
}
public sealed record StockDetail(StockSummary Preparation, StockSource Source, StockCalculation Calculation,
    StockMovement Consumption, string? Notes, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, Guid Version,
    string? StorageTemperature = null)
{
    [JsonIgnore] public string CreatedDisplay => ReferenceMaterialPresentation.LocalTimestamp(CreatedAt);
    [JsonIgnore] public string UpdatedDisplay => ReferenceMaterialPresentation.LocalTimestamp(UpdatedAt);
    [JsonIgnore] public string NotesDisplay => ReferenceMaterialPresentation.Optional(Notes);
}

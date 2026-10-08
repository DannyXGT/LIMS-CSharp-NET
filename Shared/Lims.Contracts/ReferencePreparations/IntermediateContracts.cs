using System.Text.Json.Serialization;
using Lims.Contracts.ReferenceMaterials;

namespace Lims.Contracts.ReferencePreparations;

public sealed record IntermediateMethod(int Id, string Name) { public override string ToString() => Name; }
public sealed record IntermediateAnalyte(Guid MaterialId, string Name, string? CasNumber, string Lot, decimal Concentration, string Unit)
{
    [JsonIgnore] public string Display => $"{Name} · {StockPresentation.Quantity(Concentration, Unit)}";
}
public sealed record IntermediateSource(Guid Id, string Kind, string Code, string Name, int MethodId, string Method,
    decimal AvailableVolume, decimal InitialVolume, string VolumeUnit, DateOnly PreparationDate, DateOnly ExpirationDate,
    Guid Version, IReadOnlyList<IntermediateAnalyte> Analytes)
{
    [JsonIgnore] public string KindLabel => Kind == "Stock" ? "Stock" : "Intermedia";
    [JsonIgnore] public string SearchDisplay => $"{Code} · {Name} · {string.Join(" · ", Analytes.Select(a => $"{(a.Name == Name ? "" : a.Name + " · ")}CAS {ReferenceMaterialPresentation.Optional(a.CasNumber)} · Lote {a.Lot}"))}";
    [JsonIgnore] public string AvailableDisplay => StockPresentation.Quantity(AvailableVolume, VolumeUnit);
    [JsonIgnore] public string ConcentrationDisplay => string.Join("; ", Analytes.Select(a => a.Display));
    public override string ToString() => SearchDisplay;
}
public sealed record IntermediateOptions(IReadOnlyList<IntermediateMethod> Methods, IReadOnlyList<IntermediateSource> Sources);
public sealed record IntermediateComponentRequest(Guid SourceId, Guid SourceVersion, decimal VolumeTaken, string VolumeUnit);
public sealed record IntermediateCalculationRequest(int MethodId, decimal FinalVolume, string FinalVolumeUnit,
    IReadOnlyList<IntermediateComponentRequest> Components, Guid? DilutedPreparationId = null);
public sealed record CreateIntermediateRequest(Guid RequestId, string Name, IntermediateCalculationRequest Calculation,
    DateOnly PreparationDate, DateOnly ExpirationDate, string? Notes);
public sealed record IntermediateContribution(IntermediateSource Source, decimal VolumeTaken, string VolumeUnit,
    decimal RemainingVolume, IReadOnlyList<IntermediateAnalyte> Results)
{
    [JsonIgnore] public string TakenDisplay => StockPresentation.Quantity(VolumeTaken, VolumeUnit);
    [JsonIgnore] public string AfterDisplay => StockPresentation.Quantity(RemainingVolume, Source.VolumeUnit);
}
public sealed record IntermediateCalculation(decimal TotalVolumeTaken, decimal FinalVolume, string VolumeUnit,
    decimal RemainingToFill, decimal OccupiedPercent, bool ExceedsFinalVolume, bool HasSufficientVolume,
    IReadOnlyList<IntermediateContribution> Components, IReadOnlyList<IntermediateAnalyte> Results)
{
    [JsonIgnore] public string TakenDisplay => StockPresentation.Quantity(TotalVolumeTaken, VolumeUnit);
    [JsonIgnore] public string FinalDisplay => StockPresentation.Quantity(FinalVolume, VolumeUnit);
    [JsonIgnore] public string RemainingDisplay => StockPresentation.Quantity(RemainingToFill, VolumeUnit);
    [JsonIgnore] public string ConcentrationDisplay => string.Join("; ", Results.Select(a => a.Display));
}
public sealed record IntermediateSummary(Guid Id, string Code, string Name, string Method, int ComponentCount,
    decimal FinalVolume, string VolumeUnit, decimal AvailableVolume, IReadOnlyList<IntermediateAnalyte> Results,
    DateOnly PreparationDate, DateOnly ExpirationDate, string PreparedBy, string Status)
{
    [JsonIgnore] public string FinalDisplay => StockPresentation.Quantity(FinalVolume, VolumeUnit);
    [JsonIgnore] public string AvailableDisplay => StockPresentation.Quantity(AvailableVolume, VolumeUnit);
    [JsonIgnore] public string ConcentrationDisplay => Results.Count == 1 ? StockPresentation.Quantity(Results[0].Concentration, Results[0].Unit) : $"{Results.Count} analitos";
    [JsonIgnore] public string ConcentrationDetails => string.Join("; ", Results.Select(a => a.Display));
    [JsonIgnore] public string DateDisplay => ReferenceMaterialPresentation.Date(PreparationDate);
    [JsonIgnore] public string ExpirationDisplay => ReferenceMaterialPresentation.Date(ExpirationDate);
    [JsonIgnore] public string StatusLabel => Status switch { "Active" => "Disponible", "Depleted" => "Agotado", "Expired" => "Expirado", _ => "Archivado" };
    [JsonIgnore] public string StatusTone => Status == "Active" ? "Success" : "Warning";
    public override string ToString() => $"{Code} · {Name} · {StatusLabel}";
}
public sealed record IntermediatePage(IReadOnlyList<IntermediateSummary> Items, int Page, int PageSize, int TotalCount);
public sealed record IntermediateConsumption(Guid Id, Guid SourceId, string SourceCode, decimal Quantity, string Unit,
    decimal BalanceBefore, decimal BalanceAfter, string Actor, DateTimeOffset OccurredAt)
{
    [JsonIgnore] public string Display => $"{SourceCode} · {StockPresentation.Quantity(Quantity, Unit)} · saldo {StockPresentation.Quantity(BalanceAfter, Unit)}";
}
public sealed record IntermediateDetail(IntermediateSummary Preparation, IntermediateCalculation Calculation,
    IReadOnlyList<IntermediateConsumption> Consumptions, Guid? DilutedPreparationId, string? Notes,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, string UpdatedBy, Guid Version)
{
    [JsonIgnore] public string CreatedDisplay => ReferenceMaterialPresentation.LocalTimestamp(CreatedAt);
    [JsonIgnore] public string UpdatedDisplay => ReferenceMaterialPresentation.LocalTimestamp(UpdatedAt);
    [JsonIgnore] public string NotesDisplay => ReferenceMaterialPresentation.Optional(Notes);
}

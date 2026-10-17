using System.Text.Json.Serialization;
using Lims.Contracts.ReferenceMaterials;

namespace Lims.Contracts.ReferencePreparations;

public static class SolutionKinds
{
    public static bool IsGroup(string kind) => kind is "Curva" or "AQS";
}
public sealed record SolutionPartRequest(Guid SourceId, Guid SourceVersion, decimal VolumeTaken, string VolumeUnit,
    decimal FinalVolume, string FinalVolumeUnit, bool IsInternalStandard = false);
public sealed record SolutionCalculationRequest(int MethodId, IReadOnlyList<SolutionPartRequest> Parts);
public sealed record CreateSolutionGroupRequest(Guid RequestId, string Code, string? Name, SolutionCalculationRequest Calculation,
    DateOnly PreparationDate, DateOnly ExpirationDate, string? Notes, string? StorageTemperature = null);
public sealed record SolutionPart(int Position, IntermediateSource Source, decimal VolumeTaken, string VolumeUnit,
    decimal FinalVolume, string FinalVolumeUnit, bool IsInternalStandard, IReadOnlyList<IntermediateAnalyte> Results)
{
    [JsonIgnore] public string Label => $"Solución {Position + 1}" + (IsInternalStandard ? " · Estándar interno" : string.Empty);
    [JsonIgnore] public string TakenDisplay => StockPresentation.Quantity(VolumeTaken, VolumeUnit);
    [JsonIgnore] public string FinalDisplay => StockPresentation.Quantity(FinalVolume, FinalVolumeUnit);
    [JsonIgnore] public string ResultDisplay => string.Join("; ", Results.Select(a => a.Display));
    [JsonIgnore] public string TraceDisplay => $"{Source.Code} · {Source.Name} · {string.Join("; ", Source.Analytes.Select(a => $"{a.Name} · CAS {a.CasNumber} · Lote {a.Lot}"))}";
}
public sealed record SolutionSourceDebit(IntermediateSource Source, decimal Quantity, decimal RemainingVolume)
{
    [JsonIgnore] public string Display => $"{Source.Code} · Disponible {Source.AvailableDisplay} · Consumo {StockPresentation.Quantity(Quantity, Source.VolumeUnit)} · Después {StockPresentation.Quantity(RemainingVolume, Source.VolumeUnit)}";
}
public sealed record SolutionCalculation(IReadOnlyList<SolutionPart> Parts, IReadOnlyList<SolutionSourceDebit> Debits,
    bool HasSufficientVolume)
{
    public const string Formula = "C₂ = C₁ × V tomado / V aforo, por analito y solución";
    [JsonIgnore] public string ResultDisplay => string.Join("\n", Parts.Select(p => $"{p.Label}: {p.ResultDisplay}"));
}
public sealed record SolutionGroupSummary(Guid Id, string Kind, string Code, string Name, string Method, int ComponentCount,
    string Origins, DateOnly PreparationDate, DateOnly ExpirationDate, string PreparedBy, string Status)
{
    [JsonIgnore] public string DateDisplay => ReferenceMaterialPresentation.Date(PreparationDate);
    [JsonIgnore] public string ExpirationDisplay => ReferenceMaterialPresentation.Date(ExpirationDate);
    [JsonIgnore] public string StatusLabel => Status switch { "Archived" => "Archivado", "Expired" => "Vencido", "Expiring" => "Próximo a vencer", _ => "Disponible" };
    [JsonIgnore] public string StatusTone => Status switch { "Expired" => "Danger", "Expiring" => "Warning", "Archived" => "Neutral", _ => "Success" };
}
public sealed record SolutionGroupPage(IReadOnlyList<SolutionGroupSummary> Items, int Page, int PageSize, int TotalCount);
public sealed record SolutionGroupDetail(SolutionGroupSummary Preparation, SolutionCalculation Calculation,
    IReadOnlyList<IntermediateConsumption> Consumptions, string? Notes, string? StorageTemperature, DateTimeOffset CreatedAt, Guid Version)
{
    [JsonIgnore] public string CreatedDisplay => ReferenceMaterialPresentation.LocalTimestamp(CreatedAt);
}

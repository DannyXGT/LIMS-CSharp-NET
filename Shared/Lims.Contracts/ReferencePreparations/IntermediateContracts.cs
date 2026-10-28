using System.Text.Json.Serialization;
using Lims.Contracts.ReferenceMaterials;

namespace Lims.Contracts.ReferencePreparations;

public sealed record IntermediateMethod(int Id, string Name) { public override string ToString() => Name; }
public sealed record IntermediateAnalyte(Guid MaterialId, string Name, string? CasNumber, string Lot, decimal Concentration, string Unit, Guid? PhysicalMaterialId = null, Guid? CertifiedComponentId = null, decimal? ExpandedUncertainty = null)
{
    [JsonIgnore] public string Display => $"{Name} · {StockPresentation.Quantity(Concentration, Unit)}";
}
public sealed record IntermediateSource(Guid Id, string Kind, string Code, string Name, int MethodId, string Method,
    decimal AvailableVolume, decimal InitialVolume, string VolumeUnit, DateOnly PreparationDate, DateOnly ExpirationDate,
    Guid Version, IReadOnlyList<IntermediateAnalyte> Analytes, string? Lot = null, string? Brand = null, string? Catalog = null, string? Matrix = null, string? Certificate = null,
    bool IsCommercialIntermediate = false)
{
    [JsonIgnore] public bool CanBeInternalStandard => Kind == "Intermedia" || (MaterialPresentations.IsCommercial(Kind) && IsCommercialIntermediate);
    [JsonIgnore] public string KindLabel => Kind switch { "Stock" => "Stock interno", "Intermedia" => "Intermedia interna", _ => MaterialPresentations.Label(Kind) };
    [JsonIgnore] public string SearchDisplay => $"{Code} · {Name} · {string.Join(" · ", Analytes.Select(a => $"{(a.Name == Name ? "" : a.Name + " · ")}CAS {ReferenceMaterialPresentation.Optional(a.CasNumber)} · Lote {a.Lot}"))}";
    [JsonIgnore] public string AvailableDisplay => StockPresentation.Quantity(AvailableVolume, VolumeUnit);
    [JsonIgnore] public string MetadataDisplay
    {
        get
        {
            var values = new List<string>();
            if (!string.IsNullOrWhiteSpace(Brand)) values.Add(Brand);
            if (!string.IsNullOrWhiteSpace(Catalog)) values.Add($"Catálogo {Catalog}");
            if (!string.IsNullOrWhiteSpace(Lot)) values.Add($"Lote {Lot}");
            values.Add($"{AvailableDisplay} · Vence {ReferenceMaterialPresentation.Date(ExpirationDate)}");
            if (!string.IsNullOrWhiteSpace(Matrix)) values.Add($"Matriz {Matrix}");
            return string.Join(" · ", values);
        }
    }
    [JsonIgnore] public string ConcentrationDisplay => string.Join("; ", Analytes.Select(a => a.Display));
    public override string ToString() => SearchDisplay;
}
public sealed record IntermediateOptions(IReadOnlyList<IntermediateMethod> Methods, IReadOnlyList<IntermediateSource> Sources,
    IReadOnlyList<SolventOption>? Solvents = null, bool SupportsDilutionAdditives = false);
public sealed record IntermediateComponentRequest(Guid SourceId, Guid SourceVersion, decimal VolumeTaken, string VolumeUnit);
public sealed record IntermediateCalculationRequest(int MethodId, decimal FinalVolume, string FinalVolumeUnit,
    IReadOnlyList<IntermediateComponentRequest> Components, Guid? DilutedPreparationId = null);
public sealed record CreateIntermediateRequest(Guid RequestId, string Name, IntermediateCalculationRequest Calculation,
    DateOnly PreparationDate, DateOnly ExpirationDate, string? Notes, int? SolventId = null);
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
    DateOnly PreparationDate, DateOnly ExpirationDate, string PreparedBy, string Status, bool IsCommercial = false)
{
    [JsonIgnore] public string OriginLabel => IsCommercial ? "Comercial" : "Interna";
    [JsonIgnore] public string EntryCodeDisplay => $"{Code} · {OriginLabel}";
    [JsonIgnore] public string ComponentCountLabel => IsCommercial ? "analitos certificados" : "orígenes trazados";
    [JsonIgnore] public StockAvailability Availability => new(FinalVolume, VolumeUnit, AvailableVolume);
    [JsonIgnore] public string FinalDisplay => StockPresentation.Quantity(FinalVolume, VolumeUnit);
    [JsonIgnore] public string AvailableDisplay => StockPresentation.Quantity(AvailableVolume, VolumeUnit);
    [JsonIgnore] public string ConcentrationDisplay => Results.Count == 1 ? StockPresentation.Quantity(Results[0].Concentration, Results[0].Unit) : $"{Results.Count} analitos";
    [JsonIgnore] public string ConcentrationDetails => string.Join("; ", Results.Select(a => a.Display));
    [JsonIgnore] public string DateDisplay => ReferenceMaterialPresentation.Date(PreparationDate);
    [JsonIgnore] public string ExpirationDisplay => ReferenceMaterialPresentation.Date(ExpirationDate);
    [JsonIgnore] public string StatusLabel => Status switch { "Active" or "Depleted" => "Vigente", "Expired" => "Vencido", "Archived" => "Archivado", _ => "No disponible" };
    [JsonIgnore] public string StatusTone => Status switch { "Active" or "Depleted" => "Success", "Expired" => "Danger", _ => "Neutral" };
    [JsonIgnore] public string InventoryLabel => !Availability.IsCalculated ? "Saldo no informado" : Availability.IsDepleted ? "Agotado" : Availability.RemainingPercent <= 60 ? "Disponibilidad baja" : "Con volumen";
    public override string ToString() => $"{Code} · {Name} · {StatusLabel}";
}
public sealed record IntermediatePage(IReadOnlyList<IntermediateSummary> Items, int Page, int PageSize, int TotalCount, bool FiltersApplied = false);
public sealed record IntermediateConsumption(Guid Id, Guid SourceId, string SourceCode, decimal Quantity, string Unit,
    decimal BalanceBefore, decimal BalanceAfter, string Actor, DateTimeOffset OccurredAt)
{
    [JsonIgnore] public string Display => $"{SourceCode} · {StockPresentation.Quantity(Quantity, Unit)} · saldo {StockPresentation.Quantity(BalanceAfter, Unit)}";
}
public sealed record IntermediateDetail(IntermediateSummary Preparation, IntermediateCalculation Calculation,
    IReadOnlyList<IntermediateConsumption> Consumptions, Guid? DilutedPreparationId, string? Notes,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, string UpdatedBy, Guid Version, IReadOnlyList<PreparationUsage>? UsageHistory = null,
    SolventOption? Solvent = null)
{
    [JsonIgnore] public string CreatedDisplay => ReferenceMaterialPresentation.LocalTimestamp(CreatedAt);
    [JsonIgnore] public string UpdatedDisplay => ReferenceMaterialPresentation.LocalTimestamp(UpdatedAt);
    [JsonIgnore] public string NotesDisplay => ReferenceMaterialPresentation.Optional(Notes);
}

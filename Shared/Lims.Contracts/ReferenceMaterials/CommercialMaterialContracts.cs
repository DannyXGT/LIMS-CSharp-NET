using System.Text.Json.Serialization;
using Lims.Contracts.ReferencePreparations;

namespace Lims.Contracts.ReferenceMaterials;

public static class MaterialPresentations
{
    public const string Solid = "Solid";
    public const string Solution = "CommercialSolution";
    public const string Mix = "CommercialMix";
    public static bool IsCommercial(string value) => value is Solution or Mix;
    public static string Label(string value) => value switch
    {
        Solution => "Solución comercial", Mix => "Mix comercial", _ => "Estándar sólido"
    };
}

public sealed record CertifiedExpression(decimal Value, string Unit, decimal? ExpandedUncertainty = null);
public sealed record CertifiedComponent(Guid Id, string Name, string? CasNumber, decimal CertifiedConcentration,
    string ConcentrationUnit, decimal? ExpandedUncertainty = null, decimal? PurityPercent = null,
    string? Lot = null, string? AdditionalData = null)
{
    [JsonIgnore] public string Display => $"{Name} · {CertifiedConcentration:G29} {ConcentrationUnit}";
}
public sealed record CommercialCertificate(string Reference, string Matrix, string? Traceability,
    decimal? NominalConcentration, string? NominalUnit, decimal? CoverageFactor,
    DateOnly? OpenedDate, int? UseAfterOpeningMonths,
    IReadOnlyList<CertifiedComponent> Components, IReadOnlyList<CertifiedExpression>? OtherExpressions = null,
    int? UseAfterOpeningDays = null, bool UseNominalConcentration = false, int? MatrixSolventId = null,
    IReadOnlyList<StockSolventInput>? Solvents = null, string? CompositionBasis = null,
    IReadOnlyList<StockSolventDetail>? SolventDetails = null)
{
    // The certified values remain documented even when the method uses the nominal value.
    public IReadOnlyList<CertifiedComponent> ComponentsForUse() => UseNominalConcentration && Components.Count == 1 && NominalConcentration is { } value && NominalUnit is { } unit
        ? [Components[0] with { CertifiedConcentration = value, ConcentrationUnit = unit, ExpandedUncertainty = null }] : Components;
}
public sealed record MaterialOpeningAudit(Guid Id, DateOnly? PreviousDate, DateOnly? OpenedDate,
    int ActorUserId, DateTimeOffset ChangedAt);
public sealed record MaterialConsumption(Guid Id, Guid PreparationId, string PreparationCode, string PreparationKind,
    decimal Quantity, string Unit, decimal BalanceBefore, decimal BalanceAfter, int ActorUserId, DateTimeOffset OccurredAt);

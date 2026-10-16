using System.Text.Json.Serialization;
using Lims.Contracts.ReferenceMaterials;

namespace Lims.Contracts.ReferencePreparations;

public sealed record PreparationUsage(Guid Id, Guid DerivedPreparationId, string DerivedCode, string DerivedName,
    decimal Quantity, string Unit, decimal BalanceAfter, string Actor, DateTimeOffset OccurredAt)
{
    [JsonIgnore] public string QuantityDisplay => StockPresentation.Quantity(Quantity, Unit);
    [JsonIgnore] public string BalanceDisplay => StockPresentation.Quantity(BalanceAfter, Unit);
    [JsonIgnore] public string OccurredDisplay => ReferenceMaterialPresentation.LocalTimestamp(OccurredAt);
}

using Lims.Contracts.ReferenceMaterials;

namespace Lims.Contracts.ReferencePreparations;

public sealed record CommercialPreparationOptions(IReadOnlyList<ReferenceMethodOption> Methods,
    IReadOnlyList<ReferenceUnitOption> Units, IReadOnlyList<ReferenceLocationOption> Locations,
    IReadOnlyList<ReferenceMaterialDetail> Materials);
public sealed record RegisterCommercialPreparationRequest(Guid RequestId, Guid? ExistingMaterialId,
    Guid? MaterialVersion, CreateReferenceMaterialRequest? NewMaterial);
public sealed record CommercialPreparationDetail(string Kind, string Code, ReferenceMaterialDetail Material,
    string RegisteredBy, DateTimeOffset RegisteredAt)
{
    public string OriginLabel => Kind == "Stock" ? "Stock comercial" : "Intermedia comercial";
    public string ResponsibleDisplay => $"Registrado por {RegisteredBy}";
    public StockAvailability Availability => new(Material.TotalQuantity, Material.Unit, Material.AvailableQuantity);
}

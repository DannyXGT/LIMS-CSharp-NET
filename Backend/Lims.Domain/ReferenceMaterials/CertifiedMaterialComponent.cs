namespace Lims.Domain.ReferenceMaterials;

/// <summary>Certificate identity belonging to a bottle; never an inventory item.</summary>
public sealed class CertifiedMaterialComponent
{
    private CertifiedMaterialComponent() { }
    public CertifiedMaterialComponent(Guid id, Guid materialId, string name, string? casNumber,
        decimal concentration, string unit, decimal? uncertainty, decimal? purity, string? lot, string? additionalData)
    {
        if (id == Guid.Empty || materialId == Guid.Empty || string.IsNullOrWhiteSpace(name) || concentration <= 0 ||
            string.IsNullOrWhiteSpace(unit) || uncertainty < 0 || purity is <= 0 or > 100 ||
            (!string.IsNullOrWhiteSpace(casNumber) && !CasRegistryNumber.IsValid(casNumber)))
            throw new ArgumentException("Componente certificado inválido.");
        Id = id; MaterialId = materialId; Name = name.Trim(); CasNumber = casNumber;
        CertifiedConcentration = concentration; ConcentrationUnit = unit;
        ExpandedUncertainty = uncertainty; PurityPercent = purity; Lot = lot; AdditionalData = additionalData;
    }
    public Guid Id { get; private set; }
    public Guid MaterialId { get; private set; }
    public string Name { get; private set; } = "";
    public string? CasNumber { get; private set; }
    public decimal CertifiedConcentration { get; private set; }
    public string ConcentrationUnit { get; private set; } = "";
    public decimal? ExpandedUncertainty { get; private set; }
    public decimal? PurityPercent { get; private set; }
    public string? Lot { get; private set; }
    public string? AdditionalData { get; private set; }
    public void Revise(CertifiedMaterialComponent value)
    {
        if (Id != value.Id || MaterialId != value.MaterialId) throw new ArgumentException("Identidad certificada inválida.");
        Name = value.Name; CasNumber = value.CasNumber; CertifiedConcentration = value.CertifiedConcentration;
        ConcentrationUnit = value.ConcentrationUnit; ExpandedUncertainty = value.ExpandedUncertainty;
        PurityPercent = value.PurityPercent; Lot = value.Lot; AdditionalData = value.AdditionalData;
    }
}

public sealed class ReferenceMaterialOpeningAudit
{
    private ReferenceMaterialOpeningAudit() { }
    public ReferenceMaterialOpeningAudit(Guid materialId, DateOnly? previous, DateOnly? current, int actor, DateTimeOffset now)
    { Id = Guid.NewGuid(); MaterialId = materialId; PreviousDate = previous; OpenedDate = current; ActorUserId = actor; ChangedAt = now; }
    public Guid Id { get; private set; }
    public Guid MaterialId { get; private set; }
    public DateOnly? PreviousDate { get; private set; }
    public DateOnly? OpenedDate { get; private set; }
    public int ActorUserId { get; private set; }
    public DateTimeOffset ChangedAt { get; private set; }
}

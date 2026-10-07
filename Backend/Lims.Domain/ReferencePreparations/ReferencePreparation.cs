using Lims.Domain.ReferenceMaterials;

namespace Lims.Domain.ReferencePreparations;

/// <summary>Immutable preparation and calculation snapshot. Downstream consumption is a separate phase.</summary>
public sealed class ReferencePreparation
{
    private ReferencePreparation() { }

    public ReferencePreparation(Guid id, string code, string name, ReferenceMaterial source,
        decimal targetConcentration, decimal resultConcentration, string concentrationUnit,
        decimal finalQuantity, string finalUnit, decimal calculatedWeight, decimal actualWeight,
        string formula, DateOnly preparationDate, int actorUserId, string? notes,
        DateTimeOffset now, string requestFingerprint, DateOnly expirationDate, string? storageTemperature)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (id == Guid.Empty) throw new ArgumentException("La solicitud es inválida.", nameof(id));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(actorUserId);
        Code = Text(code, 40);
        Name = Text(name, 200);
        if (targetConcentration <= 0 || resultConcentration <= 0 || finalQuantity <= 0 ||
            calculatedWeight <= 0 || actualWeight <= 0)
            throw new ArgumentException("Las cantidades deben ser mayores que cero.");
        if (notes?.Trim().Length > 2000) throw new ArgumentException("Las observaciones admiten hasta 2000 caracteres.", nameof(notes));
        Id = id;
        SourceMaterialId = source.Id;
        SourceMaterial = source;
        SourceVersion = source.Version;
        SourceName = source.Name;
        SourceLot = source.Lot;
        SourceCasNumber = source.CasNumber;
        SourceCatalogNumber = source.CatalogNumber;
        SourceBrand = source.Brand;
        SourceMethod = source.Method.Name;
        SourceLocation = source.Location.Name;
        SourceExpirationDate = source.ExpirationDate;
        SourceTotalQuantity = source.TotalQuantity;
        PurityPercentUsed = source.PurityPercent;
        SourceUnit = source.Unit.Symbol;
        TargetConcentration = targetConcentration;
        ActualConcentration = resultConcentration;
        ConcentrationUnit = Text(concentrationUnit, 16);
        FinalVolume = finalQuantity;
        FinalVolumeUnit = Text(finalUnit, 16);
        CalculatedWeight = calculatedWeight;
        ActualWeight = actualWeight;
        Formula = Text(formula, 240);
        PreparationDate = preparationDate;
        if (expirationDate < preparationDate) throw new ArgumentException("La expiración debe ser igual o posterior a la preparación.", nameof(expirationDate));
        ExpirationDate = expirationDate;
        if (storageTemperature?.Trim().Length > 200) throw new ArgumentException("La temperatura admite hasta 200 caracteres.", nameof(storageTemperature));
        StorageTemperature = string.IsNullOrWhiteSpace(storageTemperature) ? null : storageTemperature.Trim();
        CalculatedWeightUnit = SourceUnit;
        ActualWeightUnit = SourceUnit;
        ActualConcentrationUnit = ConcentrationUnit;
        PreparedByUserId = actorUserId;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        CreatedAt = now;
        UpdatedAt = now;
        Version = Guid.NewGuid();
        RequestFingerprint = Text(requestFingerprint, 64);
    }

    public Guid Id { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Kind { get; private set; } = "Stock";
    public string Status { get; private set; } = "Active";
    public Guid SourceMaterialId { get; private set; }
    public ReferenceMaterial SourceMaterial { get; private set; } = null!;
    public Guid SourceVersion { get; private set; }
    public string SourceName { get; private set; } = string.Empty;
    public string SourceLot { get; private set; } = string.Empty;
    public string? SourceCasNumber { get; private set; }
    public string? SourceCatalogNumber { get; private set; }
    public string SourceBrand { get; private set; } = string.Empty;
    public string SourceMethod { get; private set; } = string.Empty;
    public string SourceLocation { get; private set; } = string.Empty;
    public DateOnly SourceExpirationDate { get; private set; }
    public decimal SourceTotalQuantity { get; private set; }
    public decimal PurityPercentUsed { get; private set; }
    public string SourceUnit { get; private set; } = string.Empty;
    public decimal TargetConcentration { get; private set; }
    public decimal ActualConcentration { get; private set; }
    public string ConcentrationUnit { get; private set; } = string.Empty;
    public decimal FinalVolume { get; private set; }
    public string FinalVolumeUnit { get; private set; } = string.Empty;
    public decimal CalculatedWeight { get; private set; }
    public decimal ActualWeight { get; private set; }
    public string Formula { get; private set; } = string.Empty;
    public DateOnly PreparationDate { get; private set; }
    public DateOnly ExpirationDate { get; private set; }
    public string? StorageTemperature { get; private set; }
    public string CalculatedWeightUnit { get; private set; } = string.Empty;
    public string ActualWeightUnit { get; private set; } = string.Empty;
    public string ActualConcentrationUnit { get; private set; } = string.Empty;
    public int PreparedByUserId { get; private set; }
    public string? Notes { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid Version { get; private set; }
    public string RequestFingerprint { get; private set; } = string.Empty;

    private static string Text(string value, int maximum)
    {
        var text = value?.Trim() ?? string.Empty;
        if (text.Length == 0 || text.Length > maximum) throw new ArgumentException("El texto es inválido.", nameof(value));
        return text;
    }
}

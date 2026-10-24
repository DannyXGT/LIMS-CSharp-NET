using Lims.Domain.ReferenceMaterials;

namespace Lims.Domain.ReferencePreparations;

/// <summary>Historical preparation and calculation snapshots, with a versioned volume balance and consumption audit.</summary>
public sealed class ReferencePreparation
{
    private ReferencePreparation() { }

    public ReferencePreparation(Guid id, string code, string name, ReferenceMaterial source,
        decimal targetConcentration, decimal resultConcentration, string concentrationUnit,
        decimal finalQuantity, string finalUnit, decimal calculatedWeight, decimal actualWeight,
        string formula, DateOnly preparationDate, int actorUserId, string? notes,
        DateTimeOffset now, string requestFingerprint, DateOnly expirationDate, string? storageTemperature, bool enforceDateValidation = true)
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
        PurityPercentUsed = source.PurityPercent ?? throw new ArgumentException("El Stock por pesada requiere pureza documentada.");
        SourceUnit = source.Unit.Symbol;
        TargetConcentration = targetConcentration;
        ActualConcentration = resultConcentration;
        ConcentrationUnit = Text(concentrationUnit, 16);
        FinalVolume = finalQuantity;
        AvailableVolume = finalQuantity;
        MethodId = source.MethodId;
        UpdatedByUserId = actorUserId;
        FinalVolumeUnit = Text(finalUnit, 16);
        CalculatedWeight = calculatedWeight;
        ActualWeight = actualWeight;
        Formula = Text(formula, 240);
        PreparationDate = preparationDate;
        if (enforceDateValidation && expirationDate < preparationDate) throw new ArgumentException("La expiración debe ser igual o posterior a la preparación.", nameof(expirationDate));
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

    public string? Compound { get; private set; }
    public string? CompositionBasis { get; private set; }
    public ICollection<StockSolvent> Solvents { get; private set; } = new List<StockSolvent>();

    public void SetStockComposition(string compound, IEnumerable<(int SolventId, decimal Percent)> composition)
    {
        if (Kind != "Stock" || Solvents.Count != 0) throw new InvalidOperationException("La composición solo se registra al crear Stock.");
        var rows = composition.ToArray();
        if (rows.Length == 0 || rows.Select(r => r.SolventId).Distinct().Count() != rows.Length || rows.Sum(r => r.Percent) != 100m)
            throw new ArgumentException("La composición debe ser ?nica y sumar 100 %.");
        Compound = Text(compound, 200);
        CompositionBasis = "CompositionPercent";
        foreach (var (row, position) in rows.Select((row, index) => (row, index)))
            Solvents.Add(new StockSolvent(Id, row.SolventId, row.Percent, position));
    }

    public Guid Id { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Kind { get; private set; } = "Stock";
    public string Status { get; private set; } = "Active";
    public Guid? SourceMaterialId { get; private set; }
    public ReferenceMaterial? SourceMaterial { get; private set; }
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
    public int? MethodId { get; private set; }
    public decimal AvailableVolume { get; private set; }
    public int? UpdatedByUserId { get; private set; }
    public Guid? DilutedPreparationId { get; private set; }
    public string ResultsJson { get; private set; } = "[]";

    public static ReferencePreparation Intermediate(Guid id, string code, string name, int methodId, string methodName,
        decimal finalVolume, string unit, string resultsJson, DateOnly date, DateOnly expiration, int actor,
        string? notes, DateTimeOffset now, string fingerprint, Guid? parent)
    {
        if (id == Guid.Empty || methodId <= 0 || actor <= 0 || finalVolume <= 0 || expiration < date)
            throw new ArgumentException("Revise método, volumen, usuario y fechas de la Intermedia.");
        if (notes?.Trim().Length > 2000) throw new ArgumentException("Las observaciones admiten hasta 2000 caracteres.");
        return new ReferencePreparation
        {
            Id = id, Code = Text(code, 40), Name = Text(name, 200), Kind = "Intermedia", MethodId = methodId,
            SourceMethod = Text(methodName, 200), FinalVolume = finalVolume, FinalVolumeUnit = Text(unit, 16),
            AvailableVolume = finalVolume, ResultsJson = resultsJson, PreparationDate = date, ExpirationDate = expiration,
            PreparedByUserId = actor, UpdatedByUserId = actor, Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            CreatedAt = now, UpdatedAt = now, Version = Guid.NewGuid(), RequestFingerprint = Text(fingerprint, 64),
            DilutedPreparationId = parent, Formula = "C₂ por analito = C₁ × V tomado / V aforo; se conservan los analitos de cada origen"
        };
    }

    public static ReferencePreparation SolutionGroup(Guid id, string kind, string code, string name, int methodId, string methodName,
        decimal totalProducedMl, string calculationJson, DateOnly date, DateOnly expiration, int actor, string? notes,
        string? storageTemperature, DateTimeOffset now, string fingerprint)
    {
        if (kind is not ("Curva" or "AQS")) throw new ArgumentException("Tipo de preparación inválido.");
        var group = Intermediate(id, code, name, methodId, methodName, totalProducedMl, "mL", calculationJson,
            date, expiration, actor, notes, now, fingerprint, null);
        group.Kind = kind;
        // Produced solutions have no downstream inventory in the verified Works/AQS procedure.
        group.AvailableVolume = 0;
        group.Formula = "C₂ = C₁ × V tomado / V aforo, por analito y solución";
        group.StorageTemperature = string.IsNullOrWhiteSpace(storageTemperature) ? null : Text(storageTemperature, 200);
        return group;
    }

    public void ConsumeVolume(decimal quantity, int actor, DateTimeOffset now, DateOnly today)
    {
        if (Kind is not ("Stock" or "Intermedia") || Status != "Active" || ExpirationDate < today || quantity <= 0 || quantity > AvailableVolume || actor <= 0)
            throw new InvalidOperationException("El origen no tiene volumen disponible suficiente.");
        AvailableVolume -= quantity;
        UpdatedAt = now;
        UpdatedByUserId = actor;
        Version = Guid.NewGuid();
    }

    private static string Text(string value, int maximum)
    {
        var text = value?.Trim() ?? string.Empty;
        if (text.Length == 0 || text.Length > maximum) throw new ArgumentException("El texto es inválido.", nameof(value));
        return text;
    }
}

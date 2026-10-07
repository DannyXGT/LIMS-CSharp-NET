namespace Lims.Domain.ReferenceMaterials;

public sealed class ReferenceMaterial
{
    public const int NameMaximumLength = 200;
    public const int IdentifierMaximumLength = 80;
    public const int LotMaximumLength = 120;
    public const int BrandMaximumLength = 120;
    public const int StorageTemperatureMaximumLength = 160;
    public const int ArchiveReasonMaximumLength = 500;

    private ReferenceMaterial()
    {
    }

    public ReferenceMaterial(
        Guid id,
        string name,
        string? casNumber,
        string? catalogNumber,
        ReferenceMethod method,
        decimal purityPercent,
        string lot,
        string brand,
        DateOnly receivedDate,
        DateOnly expirationDate,
        decimal presentationQuantity,
        ReferenceUnit unit,
        int packageCount,
        string storageTemperature,
        ReferenceLocation location,
        int actorUserId,
        DateTimeOffset now)
    {
        ValidateId(id);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(actorUserId);
        EnsureActiveCatalogs(method, unit, location);

        Id = id;
        ApplyDetails(
            name,
            casNumber,
            catalogNumber,
            method,
            purityPercent,
            lot,
            brand,
            receivedDate,
            expirationDate,
            presentationQuantity,
            unit,
            packageCount,
            storageTemperature,
            location);
        AvailableQuantity = TotalQuantity;
        Status = ReferenceMaterialStatus.Active;
        CreatedByUserId = actorUserId;
        CreatedAt = now;
        UpdatedByUserId = actorUserId;
        UpdatedAt = now;
        Version = Guid.NewGuid();
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? CasNumber { get; private set; }
    public string? CatalogNumber { get; private set; }
    public int MethodId { get; private set; }
    public ReferenceMethod Method { get; private set; } = null!;
    public decimal PurityPercent { get; private set; }
    public string Lot { get; private set; } = string.Empty;
    public string Brand { get; private set; } = string.Empty;
    public DateOnly ReceivedDate { get; private set; }
    public DateOnly ExpirationDate { get; private set; }
    public decimal PresentationQuantity { get; private set; }
    public int UnitId { get; private set; }
    public ReferenceUnit Unit { get; private set; } = null!;
    public int PackageCount { get; private set; }
    public decimal AvailableQuantity { get; private set; }
    public int LocationId { get; private set; }
    public ReferenceLocation Location { get; private set; } = null!;
    public string StorageTemperature { get; private set; } = string.Empty;
    public ReferenceMaterialStatus Status { get; private set; }
    public int CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public int UpdatedByUserId { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public int? ArchivedByUserId { get; private set; }
    public DateTimeOffset? ArchivedAt { get; private set; }
    public string? ArchiveReason { get; private set; }
    public Guid? ReplacedByMaterialId { get; private set; }
    public Guid Version { get; private set; }
    public decimal TotalQuantity => PresentationQuantity * PackageCount;
    public decimal RemainingPercentage => AvailableQuantity / TotalQuantity * 100m;

    public void Update(
        string name,
        string? casNumber,
        string? catalogNumber,
        ReferenceMethod method,
        decimal purityPercent,
        string lot,
        string brand,
        DateOnly receivedDate,
        DateOnly expirationDate,
        decimal presentationQuantity,
        ReferenceUnit unit,
        int packageCount,
        string storageTemperature,
        ReferenceLocation location,
        int actorUserId,
        DateTimeOffset now)
    {
        EnsureEditable();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(actorUserId);
        EnsureActiveCatalogs(method, unit, location);
        var consumedQuantity = TotalQuantity - AvailableQuantity;
        if (consumedQuantity > 0 && unit.Id != UnitId)
        {
            throw new InvalidOperationException("The unit cannot change after the material has been consumed.");
        }

        var newTotalQuantity = presentationQuantity * packageCount;
        if (newTotalQuantity < consumedQuantity)
        {
            throw new InvalidOperationException("The presentation cannot be lower than the quantity already consumed.");
        }

        ApplyDetails(
            name,
            casNumber,
            catalogNumber,
            method,
            purityPercent,
            lot,
            brand,
            receivedDate,
            expirationDate,
            presentationQuantity,
            unit,
            packageCount,
            storageTemperature,
            location);
        AvailableQuantity = TotalQuantity - consumedQuantity;
        UpdatedByUserId = actorUserId;
        UpdatedAt = now;
        Version = Guid.NewGuid();
    }

    public void Consume(decimal quantity, int actorUserId, DateTimeOffset now, DateOnly today)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(actorUserId);
        if (EffectiveStatus(today) != ReferenceMaterialStatus.Active)
            throw new InvalidOperationException("El estándar no está disponible para uso.");
        if (quantity <= 0 || quantity > AvailableQuantity || decimal.Round(quantity, 6) != quantity)
            throw new ArgumentOutOfRangeException(nameof(quantity), "No hay suficiente material disponible o la cantidad es inválida.");
        AvailableQuantity -= quantity;
        UpdatedByUserId = actorUserId;
        UpdatedAt = now;
        Version = Guid.NewGuid();
    }

    public void Archive(string reason, int actorUserId, DateTimeOffset now)
    {
        EnsureEditable();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(actorUserId);
        ArchiveReason = RequireText(reason, nameof(reason), ArchiveReasonMaximumLength);
        Status = ReferenceMaterialStatus.Archived;
        ArchivedByUserId = actorUserId;
        ArchivedAt = now;
        UpdatedByUserId = actorUserId;
        UpdatedAt = now;
        Version = Guid.NewGuid();
    }

    public void ReplaceWith(Guid replacementMaterialId, string reason, int actorUserId, DateTimeOffset now)
    {
        EnsureEditable();
        if (replacementMaterialId == Guid.Empty || replacementMaterialId == Id)
        {
            throw new ArgumentException("Replacement material id is invalid.", nameof(replacementMaterialId));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(actorUserId);
        ArchiveReason = RequireText(reason, nameof(reason), ArchiveReasonMaximumLength);
        ReplacedByMaterialId = replacementMaterialId;
        Status = ReferenceMaterialStatus.Replaced;
        ArchivedByUserId = actorUserId;
        ArchivedAt = now;
        UpdatedByUserId = actorUserId;
        UpdatedAt = now;
        Version = Guid.NewGuid();
    }

    public ReferenceMaterialStatus EffectiveStatus(DateOnly today)
    {
        if (Status != ReferenceMaterialStatus.Active)
        {
            return Status;
        }

        if (AvailableQuantity <= 0)
        {
            return ReferenceMaterialStatus.Depleted;
        }

        return ExpirationDate < today
            ? ReferenceMaterialStatus.Expired
            : ReferenceMaterialStatus.Active;
    }

    private void ApplyDetails(
        string name,
        string? casNumber,
        string? catalogNumber,
        ReferenceMethod method,
        decimal purityPercent,
        string lot,
        string brand,
        DateOnly receivedDate,
        DateOnly expirationDate,
        decimal presentationQuantity,
        ReferenceUnit unit,
        int packageCount,
        string storageTemperature,
        ReferenceLocation location)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(location);

        Name = RequireText(name, nameof(name), NameMaximumLength);
        CasNumber = NormalizeOptional(casNumber, nameof(casNumber), IdentifierMaximumLength);
        if (CasNumber is not null && !CasRegistryNumber.IsValid(CasNumber))
        {
            throw new ArgumentException("CAS must have a valid format and check digit.", nameof(casNumber));
        }

        CatalogNumber = NormalizeOptional(catalogNumber, nameof(catalogNumber), IdentifierMaximumLength);
        if (purityPercent <= 0 || purityPercent > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(purityPercent), "Purity must be greater than 0 and at most 100.");
        }

        if (expirationDate < receivedDate)
        {
            throw new ArgumentException("Expiration date cannot be before received date.", nameof(expirationDate));
        }

        if (presentationQuantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(presentationQuantity), "Presentation quantity must be greater than 0.");
        }

        if (packageCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(packageCount), "Package count must be greater than 0.");
        }

        MethodId = method.Id;
        Method = method;
        PurityPercent = purityPercent;
        Lot = RequireText(lot, nameof(lot), LotMaximumLength);
        Brand = RequireText(brand, nameof(brand), BrandMaximumLength);
        ReceivedDate = receivedDate;
        ExpirationDate = expirationDate;
        PresentationQuantity = presentationQuantity;
        UnitId = unit.Id;
        Unit = unit;
        PackageCount = packageCount;
        StorageTemperature = RequireText(
            storageTemperature,
            nameof(storageTemperature),
            StorageTemperatureMaximumLength);
        LocationId = location.Id;
        Location = location;
    }

    private void EnsureEditable()
    {
        if (Status is ReferenceMaterialStatus.Archived or ReferenceMaterialStatus.Replaced or ReferenceMaterialStatus.Retired)
        {
            throw new InvalidOperationException("Reference material is not editable in its current state.");
        }
    }

    private static void EnsureActiveCatalogs(
        ReferenceMethod method,
        ReferenceUnit unit,
        ReferenceLocation location)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(location);
        if (!method.IsActive || !unit.IsActive || !location.IsActive)
        {
            throw new InvalidOperationException("Native reference materials require active catalog values.");
        }
    }

    private static void ValidateId(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Reference material id cannot be empty.", nameof(id));
        }
    }

    private static string RequireText(string value, string parameterName, int maximumLength)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is 0 || normalized.Length > maximumLength)
        {
            throw new ArgumentException($"Value must contain between 1 and {maximumLength} characters.", parameterName);
        }

        return normalized;
    }

    private static string? NormalizeOptional(string? value, string parameterName, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
        {
            throw new ArgumentException($"Value cannot exceed {maximumLength} characters.", parameterName);
        }

        return normalized;
    }
}

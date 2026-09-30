namespace Lims.Domain.ReferenceMaterials;

public sealed class ReferenceMaterial
{
    public const int NameMaximumLength = 200;
    public const int IdentifierMaximumLength = 80;
    public const int MethodMaximumLength = 120;
    public const int LotMaximumLength = 120;
    public const int BrandMaximumLength = 120;
    public const int StorageConditionsMaximumLength = 500;
    public const int StorageLocationMaximumLength = 160;
    public const int ArchiveReasonMaximumLength = 500;

    private ReferenceMaterial()
    {
    }

    public ReferenceMaterial(
        Guid id,
        string name,
        string? casNumber,
        string? catalogNumber,
        string method,
        decimal purityPercent,
        string lot,
        string brand,
        DateOnly receivedDate,
        DateOnly expirationDate,
        decimal presentationQuantity,
        MeasurementUnit unit,
        int packageCount,
        string storageConditions,
        string storageLocation,
        int actorUserId,
        DateTimeOffset now)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Reference material id cannot be empty.", nameof(id));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(actorUserId);
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
            storageConditions,
            storageLocation);
        AvailableQuantity = TotalQuantity;
        Status = ReferenceMaterialStatus.Active;
        CreatedByUserId = actorUserId;
        CreatedAt = now;
        UpdatedByUserId = actorUserId;
        UpdatedAt = now;
        Version = Guid.NewGuid();
    }

    public Guid Id { get; private set; }
    public long? LegacyId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? CasNumber { get; private set; }
    public string? CatalogNumber { get; private set; }
    public string Method { get; private set; } = string.Empty;
    public decimal PurityPercent { get; private set; }
    public string Lot { get; private set; } = string.Empty;
    public string Brand { get; private set; } = string.Empty;
    public DateOnly ReceivedDate { get; private set; }
    public DateOnly ExpirationDate { get; private set; }
    public decimal PresentationQuantity { get; private set; }
    public MeasurementUnit Unit { get; private set; }
    public int PackageCount { get; private set; }
    public string StorageConditions { get; private set; } = string.Empty;
    public string StorageLocation { get; private set; } = string.Empty;
    public decimal AvailableQuantity { get; private set; }
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

    public void Update(
        string name,
        string? casNumber,
        string? catalogNumber,
        string method,
        decimal purityPercent,
        string lot,
        string brand,
        DateOnly receivedDate,
        DateOnly expirationDate,
        decimal presentationQuantity,
        MeasurementUnit unit,
        int packageCount,
        string storageConditions,
        string storageLocation,
        int actorUserId,
        DateTimeOffset now)
    {
        EnsureEditable();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(actorUserId);
        var consumedQuantity = TotalQuantity - AvailableQuantity;
        if (consumedQuantity > 0 && unit != Unit)
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
            storageConditions,
            storageLocation);
        AvailableQuantity = TotalQuantity - consumedQuantity;
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
        string method,
        decimal purityPercent,
        string lot,
        string brand,
        DateOnly receivedDate,
        DateOnly expirationDate,
        decimal presentationQuantity,
        MeasurementUnit unit,
        int packageCount,
        string storageConditions,
        string storageLocation)
    {
        Name = RequireText(name, nameof(name), NameMaximumLength);
        CasNumber = NormalizeOptional(casNumber, nameof(casNumber), IdentifierMaximumLength);
        if (CasNumber is not null && !CasRegistryNumber.IsValid(CasNumber))
        {
            throw new ArgumentException("CAS must have a valid format and check digit.", nameof(casNumber));
        }

        CatalogNumber = NormalizeOptional(catalogNumber, nameof(catalogNumber), IdentifierMaximumLength);
        Method = RequireText(method, nameof(method), MethodMaximumLength);
        if (purityPercent <= 0 || purityPercent > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(purityPercent), "Purity must be greater than 0 and at most 100.");
        }

        PurityPercent = purityPercent;
        Lot = RequireText(lot, nameof(lot), LotMaximumLength);
        Brand = RequireText(brand, nameof(brand), BrandMaximumLength);
        if (expirationDate < receivedDate)
        {
            throw new ArgumentException("Expiration date cannot be before received date.", nameof(expirationDate));
        }

        ReceivedDate = receivedDate;
        ExpirationDate = expirationDate;
        if (presentationQuantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(presentationQuantity), "Presentation quantity must be greater than 0.");
        }

        if (!Enum.IsDefined(unit))
        {
            throw new ArgumentOutOfRangeException(nameof(unit), "Measurement unit is invalid.");
        }

        if (packageCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(packageCount), "Package count must be greater than 0.");
        }

        PresentationQuantity = presentationQuantity;
        Unit = unit;
        PackageCount = packageCount;
        StorageConditions = RequireText(storageConditions, nameof(storageConditions), StorageConditionsMaximumLength);
        StorageLocation = RequireText(storageLocation, nameof(storageLocation), StorageLocationMaximumLength);
    }

    private void EnsureEditable()
    {
        if (Status is ReferenceMaterialStatus.Archived or ReferenceMaterialStatus.Replaced or ReferenceMaterialStatus.Retired)
        {
            throw new InvalidOperationException("Reference material is not editable in its current state.");
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

using Lims.Application.Common;
using Lims.Application.ReferenceMaterials.Ports;
using Lims.Contracts.Errors;
using Lims.Contracts.ReferenceMaterials;
using Lims.Domain.ReferenceMaterials;

namespace Lims.Application.ReferenceMaterials;

public sealed class ReferenceMaterialService(
    IReferenceMaterialRepository repository,
    TimeProvider timeProvider) : IReferenceMaterialService
{
    private const int MaximumPageSize = 100;

    public async Task<OperationResult<ReferenceMaterialPage>> ListAsync(
        string? search,
        string? status,
        string? method,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (page <= 0 || pageSize is <= 0 or > MaximumPageSize)
        {
            return ValidationFailure<ReferenceMaterialPage>(
                page <= 0 ? "page" : "pageSize",
                $"Page must be positive and pageSize must be between 1 and {MaximumPageSize}.");
        }

        ReferenceMaterialStatus? parsedStatus = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<ReferenceMaterialStatus>(status, true, out var value))
            {
                return ValidationFailure<ReferenceMaterialPage>("status", "Reference material status is invalid.");
            }

            parsedStatus = value;
        }

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var (items, totalCount) = await repository.SearchAsync(
                NormalizeOptional(search),
                parsedStatus,
                NormalizeOptional(method),
                today,
                page,
                pageSize,
                cancellationToken)
            .ConfigureAwait(false);
        return OperationResult.Success(new ReferenceMaterialPage(
            items.Select(item => ToSummary(item, today)).ToArray(),
            page,
            pageSize,
            totalCount));
    }

    public async Task<OperationResult<ReferenceMaterialDetail>> GetAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        if (id == Guid.Empty)
        {
            return NotFound();
        }

        var material = await repository.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        return material is null
            ? NotFound()
            : OperationResult.Success(ToDetail(material, today));
    }

    public async Task<OperationResult<ReferenceMaterialDetail>> CreateAsync(
        CreateReferenceMaterialRequest request,
        int actorUserId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            var material = new ReferenceMaterial(
                Guid.NewGuid(),
                request.Name,
                request.CasNumber,
                request.CatalogNumber,
                request.Method,
                request.PurityPercent,
                request.Lot,
                request.Brand,
                request.ReceivedDate,
                request.ExpirationDate,
                request.PresentationQuantity,
                ParseUnit(request.Unit),
                request.PackageCount,
                request.StorageConditions,
                request.StorageLocation,
                actorUserId,
                timeProvider.GetUtcNow());

            // Automatic replacement is intentionally disabled until the laboratory confirms
            // how CAS, catalog number, lot, and multiple active lots must interact.
            repository.Add(material);
            if (!await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false))
            {
                return Conflict();
            }

            return OperationResult.Success(ToDetail(material, Today()));
        }
        catch (ArgumentException exception)
        {
            return ValidationFailure<ReferenceMaterialDetail>(
                ToRequestField(exception.ParamName),
                exception.Message);
        }
    }

    public async Task<OperationResult<ReferenceMaterialDetail>> UpdateAsync(
        Guid id,
        UpdateReferenceMaterialRequest request,
        int actorUserId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var material = await repository.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (material is null)
        {
            return NotFound();
        }

        if (request.Version == Guid.Empty || material.Version != request.Version)
        {
            return Conflict();
        }

        try
        {
            material.Update(
                request.Name,
                request.CasNumber,
                request.CatalogNumber,
                request.Method,
                request.PurityPercent,
                request.Lot,
                request.Brand,
                request.ReceivedDate,
                request.ExpirationDate,
                request.PresentationQuantity,
                ParseUnit(request.Unit),
                request.PackageCount,
                request.StorageConditions,
                request.StorageLocation,
                actorUserId,
                timeProvider.GetUtcNow());
        }
        catch (InvalidOperationException exception)
        {
            return InvalidState(exception.Message);
        }
        catch (ArgumentException exception)
        {
            return ValidationFailure<ReferenceMaterialDetail>(
                ToRequestField(exception.ParamName),
                exception.Message);
        }

        return await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false)
            ? OperationResult.Success(ToDetail(material, Today()))
            : Conflict();
    }

    public async Task<OperationResult<ReferenceMaterialDetail>> ArchiveAsync(
        Guid id,
        ArchiveReferenceMaterialRequest request,
        int actorUserId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var material = await repository.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (material is null)
        {
            return NotFound();
        }

        if (request.Version == Guid.Empty || material.Version != request.Version)
        {
            return Conflict();
        }

        try
        {
            material.Archive(request.Reason, actorUserId, timeProvider.GetUtcNow());
        }
        catch (InvalidOperationException exception)
        {
            return InvalidState(exception.Message);
        }
        catch (ArgumentException exception)
        {
            return ValidationFailure<ReferenceMaterialDetail>(
                ToRequestField(exception.ParamName),
                exception.Message);
        }

        return await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false)
            ? OperationResult.Success(ToDetail(material, Today()))
            : Conflict();
    }

    public async Task<OperationResult<ReferenceMaterialDetail>> ReplaceAsync(
        Guid id,
        ReplaceReferenceMaterialRequest request,
        int actorUserId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Replacement);
        var source = await repository.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (source is null)
        {
            return NotFound();
        }

        if (request.Version == Guid.Empty || source.Version != request.Version)
        {
            return Conflict();
        }

        try
        {
            var now = timeProvider.GetUtcNow();
            var replacementRequest = request.Replacement;
            var replacement = new ReferenceMaterial(
                Guid.NewGuid(),
                replacementRequest.Name,
                replacementRequest.CasNumber,
                replacementRequest.CatalogNumber,
                replacementRequest.Method,
                replacementRequest.PurityPercent,
                replacementRequest.Lot,
                replacementRequest.Brand,
                replacementRequest.ReceivedDate,
                replacementRequest.ExpirationDate,
                replacementRequest.PresentationQuantity,
                ParseUnit(replacementRequest.Unit),
                replacementRequest.PackageCount,
                replacementRequest.StorageConditions,
                replacementRequest.StorageLocation,
                actorUserId,
                now);

            source.ReplaceWith(replacement.Id, request.Reason, actorUserId, now);
            repository.Add(replacement);

            // Both entity changes are committed by one SaveChanges call. Npgsql's configured
            // execution strategy can retry it safely because no user transaction is opened here.
            if (!await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false))
            {
                return Conflict();
            }

            return OperationResult.Success(ToDetail(replacement, Today()));
        }
        catch (InvalidOperationException exception)
        {
            return InvalidState(exception.Message);
        }
        catch (ArgumentException exception)
        {
            return ValidationFailure<ReferenceMaterialDetail>(
                ToRequestField(exception.ParamName),
                exception.Message);
        }
    }

    private static MeasurementUnit ParseUnit(string unit)
    {
        var normalized = unit?.Trim() ?? string.Empty;
        return normalized.ToLowerInvariant() switch
        {
            "µg" or "ug" or "microgram" => MeasurementUnit.Microgram,
            "mg" or "milligram" => MeasurementUnit.Milligram,
            "g" or "gram" => MeasurementUnit.Gram,
            "kg" or "kilogram" => MeasurementUnit.Kilogram,
            "ml" or "milliliter" => MeasurementUnit.Milliliter,
            "l" or "liter" => MeasurementUnit.Liter,
            _ => throw new ArgumentException("Measurement unit is invalid.", nameof(unit)),
        };
    }

    private static string UnitSymbol(MeasurementUnit unit) => unit switch
    {
        MeasurementUnit.Microgram => "µg",
        MeasurementUnit.Milligram => "mg",
        MeasurementUnit.Gram => "g",
        MeasurementUnit.Kilogram => "kg",
        MeasurementUnit.Milliliter => "mL",
        MeasurementUnit.Liter => "L",
        _ => throw new ArgumentOutOfRangeException(nameof(unit)),
    };

    private static ReferenceMaterialSummary ToSummary(ReferenceMaterial material, DateOnly today) => new(
        material.Id,
        material.Name,
        material.CasNumber,
        material.CatalogNumber,
        material.Method,
        material.Lot,
        material.Brand,
        material.PurityPercent,
        material.ExpirationDate,
        material.EffectiveStatus(today).ToString(),
        material.TotalQuantity,
        UnitSymbol(material.Unit),
        material.AvailableQuantity,
        material.Version);

    private static ReferenceMaterialDetail ToDetail(ReferenceMaterial material, DateOnly today) => new(
        material.Id,
        material.Name,
        material.CasNumber,
        material.CatalogNumber,
        material.Method,
        material.PurityPercent,
        material.Lot,
        material.Brand,
        material.ReceivedDate,
        material.ExpirationDate,
        material.PresentationQuantity,
        UnitSymbol(material.Unit),
        material.PackageCount,
        material.TotalQuantity,
        material.AvailableQuantity,
        material.StorageConditions,
        material.StorageLocation,
        material.EffectiveStatus(today).ToString(),
        material.CreatedByUserId,
        material.CreatedAt,
        material.UpdatedByUserId,
        material.UpdatedAt,
        material.ArchivedByUserId,
        material.ArchivedAt,
        material.ArchiveReason,
        material.ReplacedByMaterialId,
        material.Version);

    private DateOnly Today() => DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string ToRequestField(string? parameterName) => parameterName switch
    {
        null or "" => "request",
        "unit" => "unit",
        _ => parameterName,
    };

    private static OperationResult<T> ValidationFailure<T>(string field, string message) =>
        OperationResult.Failure<T>(new OperationError(
            ErrorCodes.ValidationError,
            "Los datos enviados no son válidos.",
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [field] = [message],
            }));

    private static OperationResult<ReferenceMaterialDetail> NotFound() =>
        OperationResult.Failure<ReferenceMaterialDetail>(new OperationError(
            ErrorCodes.NotFound,
            "El estándar solicitado no existe."));

    private static OperationResult<ReferenceMaterialDetail> Conflict() =>
        OperationResult.Failure<ReferenceMaterialDetail>(new OperationError(
            ErrorCodes.Conflict,
            "El estándar cambió desde que fue consultado. Actualice los datos e intente nuevamente."));

    private static OperationResult<ReferenceMaterialDetail> InvalidState(string message) =>
        OperationResult.Failure<ReferenceMaterialDetail>(new OperationError(
            ErrorCodes.InvalidState,
            message));
}

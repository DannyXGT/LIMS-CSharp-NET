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

    public async Task<IReadOnlyList<ReferenceMethodOption>> GetMethodsAsync(
        CancellationToken cancellationToken) =>
        (await repository.ListActiveMethodsAsync(cancellationToken).ConfigureAwait(false))
        .Select(method => new ReferenceMethodOption(method.Id, method.Name))
        .ToArray();

    public async Task<IReadOnlyList<ReferenceUnitOption>> GetUnitsAsync(
        CancellationToken cancellationToken) =>
        (await repository.ListActiveUnitsAsync(cancellationToken).ConfigureAwait(false))
        .Select(unit => new ReferenceUnitOption(unit.Id, unit.Name, unit.Symbol))
        .ToArray();

    public async Task<IReadOnlyList<ReferenceLocationOption>> GetLocationsAsync(
        CancellationToken cancellationToken) =>
        (await repository.ListActiveLocationsAsync(cancellationToken).ConfigureAwait(false))
        .Select(location => new ReferenceLocationOption(location.Id, location.Name))
        .ToArray();

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

        var today = Today();
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
        return material is null
            ? NotFound()
            : OperationResult.Success(await ToDetailAsync(material, cancellationToken).ConfigureAwait(false));
    }

    public async Task<OperationResult<ReferenceMaterialDetail>> CreateAsync(
        CreateReferenceMaterialRequest request,
        int actorUserId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var catalogResult = await ResolveCatalogsAsync(
                request.MethodId,
                request.UnitId,
                request.LocationId,
                cancellationToken)
            .ConfigureAwait(false);
        if (!catalogResult.IsSuccess)
        {
            return OperationResult.Failure<ReferenceMaterialDetail>(catalogResult.Error!);
        }

        var catalogs = catalogResult.Value!;
        try
        {
            var material = new ReferenceMaterial(
                Guid.NewGuid(),
                request.Name,
                request.CasNumber,
                request.CatalogNumber,
                catalogs.Method,
                request.PurityPercent,
                request.Lot,
                request.Brand,
                request.ReceivedDate,
                request.ExpirationDate,
                request.PresentationQuantity,
                catalogs.Unit,
                request.PackageCount,
                request.StorageTemperature,
                catalogs.Location,
                actorUserId,
                timeProvider.GetUtcNow());

            repository.Add(material);
            if (!await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false))
            {
                return Conflict();
            }

            return OperationResult.Success(await ToDetailAsync(material, cancellationToken).ConfigureAwait(false));
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

        var catalogResult = await ResolveCatalogsAsync(
                request.MethodId,
                request.UnitId,
                request.LocationId,
                cancellationToken)
            .ConfigureAwait(false);
        if (!catalogResult.IsSuccess)
        {
            return OperationResult.Failure<ReferenceMaterialDetail>(catalogResult.Error!);
        }

        var catalogs = catalogResult.Value!;
        try
        {
            material.Update(
                request.Name,
                request.CasNumber,
                request.CatalogNumber,
                catalogs.Method,
                request.PurityPercent,
                request.Lot,
                request.Brand,
                request.ReceivedDate,
                request.ExpirationDate,
                request.PresentationQuantity,
                catalogs.Unit,
                request.PackageCount,
                request.StorageTemperature,
                catalogs.Location,
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
            ? OperationResult.Success(await ToDetailAsync(material, cancellationToken).ConfigureAwait(false))
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
            ? OperationResult.Success(await ToDetailAsync(material, cancellationToken).ConfigureAwait(false))
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

        var replacementRequest = request.Replacement;
        var catalogResult = await ResolveCatalogsAsync(
                replacementRequest.MethodId,
                replacementRequest.UnitId,
                replacementRequest.LocationId,
                cancellationToken)
            .ConfigureAwait(false);
        if (!catalogResult.IsSuccess)
        {
            return OperationResult.Failure<ReferenceMaterialDetail>(catalogResult.Error!);
        }

        var catalogs = catalogResult.Value!;
        try
        {
            var now = timeProvider.GetUtcNow();
            var replacement = new ReferenceMaterial(
                Guid.NewGuid(),
                replacementRequest.Name,
                replacementRequest.CasNumber,
                replacementRequest.CatalogNumber,
                catalogs.Method,
                replacementRequest.PurityPercent,
                replacementRequest.Lot,
                replacementRequest.Brand,
                replacementRequest.ReceivedDate,
                replacementRequest.ExpirationDate,
                replacementRequest.PresentationQuantity,
                catalogs.Unit,
                replacementRequest.PackageCount,
                replacementRequest.StorageTemperature,
                catalogs.Location,
                actorUserId,
                now);

            source.ReplaceWith(replacement.Id, request.Reason, actorUserId, now);
            repository.Add(replacement);
            if (!await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false))
            {
                return Conflict();
            }

            return OperationResult.Success(await ToDetailAsync(replacement, cancellationToken).ConfigureAwait(false));
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

    private async Task<OperationResult<ResolvedCatalogs>> ResolveCatalogsAsync(
        int methodId,
        int unitId,
        int locationId,
        CancellationToken cancellationToken)
    {
        if (methodId <= 0)
        {
            return ValidationFailure<ResolvedCatalogs>("methodId", "Reference method is required.");
        }

        if (unitId <= 0)
        {
            return ValidationFailure<ResolvedCatalogs>("unitId", "Reference unit is required.");
        }

        if (locationId <= 0)
        {
            return ValidationFailure<ResolvedCatalogs>("locationId", "Reference location is required.");
        }

        var selection = await repository.ResolveCatalogsAsync(
                methodId,
                unitId,
                locationId,
                cancellationToken)
            .ConfigureAwait(false);
        if (selection.Method is null)
        {
            return ValidationFailure<ResolvedCatalogs>("methodId", "Reference method is not active or does not exist.");
        }

        if (selection.Unit is null)
        {
            return ValidationFailure<ResolvedCatalogs>("unitId", "Reference unit is not active or does not exist.");
        }

        if (selection.Location is null)
        {
            return ValidationFailure<ResolvedCatalogs>("locationId", "Reference location is not active or does not exist.");
        }

        return OperationResult.Success(new ResolvedCatalogs(
            selection.Method,
            selection.Unit,
            selection.Location));
    }

    private static ReferenceMaterialSummary ToSummary(ReferenceMaterial material, DateOnly today) => new(
        material.Id,
        material.Name,
        material.CasNumber,
        material.CatalogNumber,
        material.Method.Name,
        material.Lot,
        material.Brand,
        material.PurityPercent,
        material.ExpirationDate,
        material.EffectiveStatus(today).ToString(),
        material.TotalQuantity,
        material.Unit.Symbol,
        material.AvailableQuantity,
        material.Version);

    private async Task<ReferenceMaterialDetail> ToDetailAsync(ReferenceMaterial material, CancellationToken cancellationToken)
    {
        var display = await repository.ResolveDisplayContextAsync(material, cancellationToken).ConfigureAwait(false);
        return new ReferenceMaterialDetail(
            material.Id,
            material.Name,
            material.CasNumber,
            material.CatalogNumber,
            material.MethodId,
            material.Method.Name,
            material.PurityPercent,
            material.Lot,
            material.Brand,
            material.ReceivedDate,
            material.ExpirationDate,
            material.PresentationQuantity,
            material.UnitId,
            material.Unit.Symbol,
            material.PackageCount,
            material.TotalQuantity,
            material.AvailableQuantity,
            material.StorageTemperature,
            material.LocationId,
            material.Location.Name,
            material.EffectiveStatus(Today()).ToString(),
            material.CreatedByUserId,
            material.CreatedAt,
            material.UpdatedByUserId,
            material.UpdatedAt,
            material.ArchivedByUserId,
            material.ArchivedAt,
            material.ArchiveReason,
            material.ReplacedByMaterialId,
            material.Version,
            display.CreatedByName,
            display.UpdatedByName,
            display.ArchivedByName,
            display.ReplacedByMaterialName);
    }

    private DateOnly Today() => DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string ToRequestField(string? parameterName) => parameterName switch
    {
        null or "" => "request",
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

    private sealed record ResolvedCatalogs(
        ReferenceMethod Method,
        ReferenceUnit Unit,
        ReferenceLocation Location);
}

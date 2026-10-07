using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Lims.Application.Common;
using Lims.Contracts.Errors;
using Lims.Contracts.ReferencePreparations;
using Lims.Domain.ReferenceMaterials;
using Lims.Domain.ReferencePreparations;

namespace Lims.Application.ReferencePreparations;

public sealed class StockService(IStockRepository repository, TimeProvider timeProvider) : IStockService
{
    public StockOptions GetOptions() => new(["mg/L", "g/L", "µg/L", "µg/mL"], ["mL", "L"]);

    public async Task<OperationResult<StockSourcePage>> SourcesAsync(string? search, int page, int pageSize, CancellationToken cancellationToken)
    {
        if (!ValidPage(page, pageSize)) return Failure<StockSourcePage>(ErrorCodes.ValidationError, "La página solicitada es inválida.");
        return OperationResult.Success(await repository.SourcesAsync(search?.Trim(), Today(), page, pageSize, cancellationToken).ConfigureAwait(false));
    }

    public async Task<OperationResult<StockCalculation>> PreviewAsync(StockCalculationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var source = await repository.FindSourceAsync(request.SourceMaterialId, cancellationToken).ConfigureAwait(false);
        var error = SourceError(source, request, Today());
        if (error is not null) return OperationResult.Failure<StockCalculation>(error);
        try { return OperationResult.Success(StockCalculator.Calculate(source!, request)); }
        catch (ArgumentException exception) { return Failure<StockCalculation>(ErrorCodes.ValidationError, exception.Message); }
        catch (OverflowException) { return Failure<StockCalculation>(ErrorCodes.ValidationError, "Las cantidades exceden el rango admitido."); }
    }

    public Task<OperationResult<StockDetail>> CreateAsync(CreateStockRequest request, int actorUserId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (actorUserId <= 0) return Task.FromResult(Failure<StockDetail>(ErrorCodes.InvalidSession, "La sesión no es válida o expiró."));
        if (request.RequestId == Guid.Empty || request.Calculation is null || request.Name?.Trim().Length is not (> 0 and <= 200) ||
            request.Notes?.Trim().Length > 2000 || request.PreparationDate == default || request.PreparationDate > Today() || request.ExpirationDate is null || request.ExpirationDate < request.PreparationDate || request.StorageTemperature?.Trim().Length > 200 || request.Calculation?.ActualWeight is null)
            return Task.FromResult(Failure<StockDetail>(ErrorCodes.ValidationError, "Registre el peso tomado y revise nombre, fechas, temperatura y observaciones de la preparación."));
        var now = timeProvider.GetUtcNow();
        var today = Today();
        var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { request, actorUserId })));
        return repository.CreateAsync(request.RequestId, fingerprint, (source, sequence) =>
        {
            var error = SourceError(source, request.Calculation, today);
            if (error is not null) return OperationResult.Failure<ReferencePreparation>(error);
            if (request.PreparationDate < source.ReceivedDate || request.PreparationDate > source.ExpirationDate)
                return Failure<ReferencePreparation>(ErrorCodes.ValidationError, "La fecha de preparación debe estar dentro de la vigencia del estándar.");
            try
            {
                var calculation = StockCalculator.Calculate(source, request.Calculation);
                if (!calculation.HasSufficientMaterial)
                    return Failure<ReferencePreparation>(ErrorCodes.InvalidState, "No hay suficiente material disponible.");
                var code = string.Create(CultureInfo.InvariantCulture, $"STK-{request.PreparationDate:yyyyMMdd}-{sequence:D3}");
                return OperationResult.Success(new ReferencePreparation(request.RequestId, code, request.Name!, source,
                    calculation.TargetConcentration, calculation.ActualConcentration!.Value, calculation.ConcentrationUnit,
                    calculation.FinalVolume, calculation.FinalVolumeUnit, calculation.CalculatedWeight, calculation.ActualWeight!.Value,
                    calculation.Formula, request.PreparationDate, actorUserId, request.Notes, now, fingerprint, request.ExpirationDate.Value, request.StorageTemperature));
            }
            catch (ArgumentException exception) { return Failure<ReferencePreparation>(ErrorCodes.ValidationError, exception.Message); }
            catch (OverflowException) { return Failure<ReferencePreparation>(ErrorCodes.ValidationError, "Las cantidades exceden el rango admitido."); }
        }, request.Calculation.SourceMaterialId, today, now, cancellationToken);
    }

    public async Task<OperationResult<StockPage>> ListAsync(string? search, int page, int pageSize, CancellationToken cancellationToken)
    {
        if (!ValidPage(page, pageSize)) return Failure<StockPage>(ErrorCodes.ValidationError, "La página solicitada es inválida.");
        return OperationResult.Success(await repository.ListAsync(search?.Trim(), page, pageSize, cancellationToken).ConfigureAwait(false));
    }

    public async Task<OperationResult<StockDetail>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var detail = await repository.GetAsync(id, cancellationToken).ConfigureAwait(false);
        return detail is null ? Failure<StockDetail>(ErrorCodes.NotFound, "La preparación solicitada no existe.") : OperationResult.Success(detail);
    }

    private DateOnly Today() => DateOnly.FromDateTime(timeProvider.GetUtcNow().ToOffset(TimeSpan.FromHours(-6)).DateTime);
    private static bool ValidPage(int page, int pageSize) => page is > 0 and <= 1000000 && pageSize is > 0 and <= 100;
    private static OperationError? SourceError(ReferenceMaterial? source, StockCalculationRequest request, DateOnly today)
    {
        if (source is null) return new(ErrorCodes.NotFound, "El estándar solicitado no existe.");
        if (request.SourceVersion == Guid.Empty || source.Version != request.SourceVersion)
            return new(ErrorCodes.Conflict, "El estándar cambió. Vuelva a seleccionarlo y calcule nuevamente.");
        return source.EffectiveStatus(today) != ReferenceMaterialStatus.Active
            ? new(ErrorCodes.InvalidState, "El estándar no está disponible para uso.") : null;
    }
    private static OperationResult<T> Failure<T>(string code, string message) => OperationResult.Failure<T>(new OperationError(code, message));
}

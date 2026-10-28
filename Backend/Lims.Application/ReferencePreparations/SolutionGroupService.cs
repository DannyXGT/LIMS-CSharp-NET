using System.Security.Cryptography;
using System.Text.Json;
using Lims.Application.Common;
using Lims.Contracts.Errors;
using Lims.Contracts.ReferencePreparations;

namespace Lims.Application.ReferencePreparations;

public sealed class SolutionGroupService(ISolutionGroupRepository repository, TimeProvider clock)
{
    private DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().ToOffset(TimeSpan.FromHours(-6)).DateTime);
    public Task<IntermediateOptions> OptionsAsync(CancellationToken ct) => repository.OptionsAsync(Today, ct);
    public async Task<OperationResult<SolutionCalculation>> PreviewAsync(SolutionCalculationRequest request, CancellationToken ct)
    {
        try
        {
            var options = await OptionsAsync(ct).ConfigureAwait(false);
            return OperationResult.Success(SolutionGroupCalculator.Calculate(request, options.Sources, options.Solvents));
        }
        catch (ArgumentException e) { return Fail<SolutionCalculation>(ErrorCodes.ValidationError, e.Message); }
        catch (OverflowException) { return Fail<SolutionCalculation>(ErrorCodes.ValidationError, "Las cantidades exceden el rango admitido."); }
    }
    public Task<OperationResult<SolutionGroupDetail>> CreateAsync(string kind, CreateSolutionGroupRequest request, int actor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (actor <= 0) return Task.FromResult(Fail<SolutionGroupDetail>(ErrorCodes.InvalidSession, "La sesión no es válida."));
        if (!SolutionKinds.IsGroup(kind) || request.RequestId == Guid.Empty || request.Calculation is null ||
            request.Code?.Trim().Length is not (> 0 and <= 40) || request.Name?.Trim().Length > 200 ||
            request.Notes?.Trim().Length > 2000 || request.StorageTemperature?.Trim().Length > 200 ||
            request.PreparationDate == default || request.ExpirationDate == default || (HistoricalEntryPolicy.EnforcePreparationDates &&
            (request.PreparationDate > Today || request.ExpirationDate < Today || request.ExpirationDate < request.PreparationDate)))
            return Task.FromResult(Fail<SolutionGroupDetail>(ErrorCodes.ValidationError, "Revise código, fechas y datos de la preparación."));
        var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { kind, request, actor })));
        return repository.CreateAsync(kind, request, actor, fingerprint, clock.GetUtcNow(), ct);
    }
    public async Task<OperationResult<SolutionGroupPage>> ListAsync(string kind, string? search, int page, int pageSize, CancellationToken ct, PreparationListFilter? filter = null)
    {
        if (!SolutionKinds.IsGroup(kind) || page is < 1 or > 1000000 || pageSize is < 1 or > 100 || filter?.IsValid == false || !string.IsNullOrEmpty(filter?.Availability))
            return Fail<SolutionGroupPage>(ErrorCodes.ValidationError, "Revise página y filtros. Estas soluciones no controlan inventario posterior.");
        return OperationResult.Success(await repository.ListAsync(kind, search, page, pageSize, Today, ct, filter).ConfigureAwait(false));
    }
    public async Task<OperationResult<SolutionGroupDetail>> GetAsync(string kind, Guid id, CancellationToken ct)
    {
        var detail = await repository.GetAsync(kind, id, Today, ct).ConfigureAwait(false);
        return detail is null ? Fail<SolutionGroupDetail>(ErrorCodes.NotFound, "La preparación solicitada no existe.") : OperationResult.Success(detail);
    }
    private static OperationResult<T> Fail<T>(string code, string message) => OperationResult.Failure<T>(new(code, message));
}

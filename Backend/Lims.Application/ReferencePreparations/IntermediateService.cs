using System.Security.Cryptography;
using System.Text.Json;
using Lims.Application.Common;
using Lims.Contracts.Errors;
using Lims.Contracts.ReferencePreparations;

namespace Lims.Application.ReferencePreparations;

public sealed class IntermediateService(IIntermediateRepository repository, TimeProvider clock) : IIntermediateService
{
    private DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().ToOffset(TimeSpan.FromHours(-6)).DateTime);
    public async Task<OperationResult<IntermediateOptions>> OptionsAsync(CancellationToken cancellationToken) =>
        OperationResult.Success(await repository.OptionsAsync(Today, cancellationToken).ConfigureAwait(false));
    public async Task<OperationResult<IntermediateCalculation>> PreviewAsync(IntermediateCalculationRequest request, CancellationToken cancellationToken)
    {
        var options = await repository.OptionsAsync(Today, cancellationToken).ConfigureAwait(false);
        try { return OperationResult.Success(IntermediateCalculator.Calculate(request, options.Sources)); }
        catch (ArgumentException e) { return Fail<IntermediateCalculation>(ErrorCodes.ValidationError, e.Message); }
        catch (OverflowException) { return Fail<IntermediateCalculation>(ErrorCodes.ValidationError, "Las cantidades exceden el rango admitido."); }
    }
    public Task<OperationResult<IntermediateDetail>> CreateAsync(CreateIntermediateRequest request, int actorUserId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (actorUserId <= 0) return Task.FromResult(Fail<IntermediateDetail>(ErrorCodes.InvalidSession, "La sesión no es válida o expiró."));
        if (request.RequestId == Guid.Empty || request.Calculation is null || request.Name?.Trim().Length is not (> 0 and <= 200) ||
            request.Notes?.Trim().Length > 2000 || request.PreparationDate == default || request.PreparationDate > Today ||
            request.ExpirationDate < request.PreparationDate || request.ExpirationDate < Today)
            return Task.FromResult(Fail<IntermediateDetail>(ErrorCodes.ValidationError, "Revise nombre, fechas y observaciones de la Intermedia."));
        var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { request, actorUserId })));
        return repository.CreateAsync(request, actorUserId, fingerprint, clock.GetUtcNow(), cancellationToken);
    }
    public async Task<OperationResult<IntermediatePage>> ListAsync(string? search, int page, int pageSize, CancellationToken cancellationToken)
    {
        if (page is < 1 or > 1000000 || pageSize is < 1 or > 100) return Fail<IntermediatePage>(ErrorCodes.ValidationError, "La página solicitada es inválida.");
        return OperationResult.Success(await repository.ListAsync(search, page, pageSize, Today, cancellationToken).ConfigureAwait(false));
    }
    public async Task<OperationResult<IntermediateDetail>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var detail = await repository.GetAsync(id, Today, cancellationToken).ConfigureAwait(false);
        return detail is null ? Fail<IntermediateDetail>(ErrorCodes.NotFound, "La Intermedia solicitada no existe.") : OperationResult.Success(detail);
    }
    private static OperationResult<T> Fail<T>(string code, string message) => OperationResult.Failure<T>(new(code, message));
}

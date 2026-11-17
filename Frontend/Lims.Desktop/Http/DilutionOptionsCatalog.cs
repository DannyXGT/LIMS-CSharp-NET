using Lims.Contracts.Errors;
using Lims.Contracts.ReferencePreparations;

namespace Lims.Desktop.Http;

internal static class DilutionOptionsCatalog
{
    public static async Task<ApiCallResult<IntermediateOptions>> CompleteAsync(
        ApiCallResult<IntermediateOptions> response, IStockApiClient stock, CancellationToken ct)
    {
        // A catalog alone does not prove that an older API can persist dilution additives.
        if (!response.IsSuccess || response.Value is not { SupportsDilutionAdditives: true } options ||
            options.Solvents is { Count: > 0 }) return response;

        var catalog = await stock.SolventsAsync(null, ct).ConfigureAwait(false);
        if (!catalog.IsSuccess || catalog.Value is null)
            return new(false, null, catalog.Error ?? new ApiError(ErrorCodes.UnexpectedError,
                "No se pudo cargar el catálogo compartido de solventes. Reintente la consulta.", ""), catalog.StatusCode);
        return response with { Value = options with { Solvents = catalog.Value } };
    }
}

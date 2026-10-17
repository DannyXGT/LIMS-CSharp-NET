using System.Globalization;
using System.Net.Http.Json;
using Lims.Contracts.Errors;
using Lims.Contracts.ReferencePreparations;

namespace Lims.Desktop.Http;

internal sealed class SolutionGroupApiClient(IHttpClientFactory clients) : ISolutionGroupApiClient
{
    private static string Path(string kind) => "api/reference-materials/preparations/" + (kind == "Curva" ? "curve" : "aqs");
    public Task<ApiCallResult<IntermediateOptions>> OptionsAsync(string kind, CancellationToken cancellationToken) => SendAsync<IntermediateOptions>(HttpMethod.Get, Path(kind) + "/options", null, cancellationToken);
    public Task<ApiCallResult<SolutionCalculation>> PreviewAsync(string kind, SolutionCalculationRequest request, CancellationToken cancellationToken) =>
        SendAsync<SolutionCalculation>(HttpMethod.Post, Path(kind) + "/preview", JsonContent.Create(request), cancellationToken);
    public Task<ApiCallResult<SolutionGroupDetail>> CreateAsync(string kind, CreateSolutionGroupRequest request, CancellationToken cancellationToken) =>
        SendAsync<SolutionGroupDetail>(HttpMethod.Post, Path(kind), JsonContent.Create(request), cancellationToken);
    public Task<ApiCallResult<SolutionGroupPage>> ListAsync(string kind, string? search, int page, CancellationToken cancellationToken, PreparationListFilter? filter = null) =>
        SendAsync<SolutionGroupPage>(HttpMethod.Get, Query(Path(kind), search, page, filter), null, cancellationToken);
    public Task<ApiCallResult<SolutionGroupDetail>> GetAsync(string kind, Guid id, CancellationToken cancellationToken) =>
        SendAsync<SolutionGroupDetail>(HttpMethod.Get, $"{Path(kind)}/{id:D}", null, cancellationToken);

    private static string Query(string path, string? search, int page, PreparationListFilter? filter = null) =>
        string.Create(CultureInfo.InvariantCulture, $"{path}?page={page}&pageSize=25&search={Uri.EscapeDataString(search?.Trim() ?? string.Empty)}&status={Uri.EscapeDataString(filter?.Status ?? string.Empty)}&availability={Uri.EscapeDataString(filter?.Availability ?? string.Empty)}&method={Uri.EscapeDataString(filter?.Method ?? string.Empty)}");

    private async Task<ApiCallResult<T>> SendAsync<T>(HttpMethod method, string path, HttpContent? content, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        using var response = await clients.CreateClient(ApiClientNames.Authorized).SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
        {
            try
            {
                var value = await response.Content.ReadFromJsonAsync<T>(cancellationToken).ConfigureAwait(false);
                return new(value is not null, value, null, (int)response.StatusCode);
            }
            catch (System.Text.Json.JsonException)
            {
                return new(false, default, new ApiError(ErrorCodes.UnexpectedError,
                    "No se pudo interpretar la respuesta. Reintente esta misma solicitud.", string.Empty), (int)response.StatusCode);
            }
        }
        ApiError? error = null;
        try { error = await response.Content.ReadFromJsonAsync<ApiError>(cancellationToken).ConfigureAwait(false); }
        catch (System.Text.Json.JsonException) { }
        // An empty/HTML 404 is an absent API route, not an empty preparation list.
        // Preserve structured 404s: they can describe a genuinely missing record.
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound && error is null)
            error = new(ErrorCodes.NotFound,
                "El servidor no reconoce las rutas de Curva/AQS. Actualice la API del servidor para consultar y guardar estas preparaciones.", string.Empty);
        return new(false, default, error, (int)response.StatusCode);
    }
}

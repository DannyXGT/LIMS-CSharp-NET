using System.Net.Http.Json;
using Lims.Contracts.Errors;
using Lims.Contracts.ReferencePreparations;

namespace Lims.Desktop.Http;

internal sealed class CommercialPreparationApiClient(IHttpClientFactory clients) : ICommercialPreparationApiClient
{
    private static string Path(string kind) => "api/reference-materials/preparations/" + (kind == "Stock" ? "stock" : "intermediate") + "/commercial";
    public Task<ApiCallResult<CommercialPreparationOptions>> OptionsAsync(string kind, string? search, CancellationToken ct) =>
        SendAsync<CommercialPreparationOptions>(HttpMethod.Get, Path(kind) + "/options?search=" + Uri.EscapeDataString(search ?? ""), null, ct);
    public Task<ApiCallResult<CommercialPreparationDetail>> RegisterAsync(string kind, RegisterCommercialPreparationRequest request, CancellationToken ct) =>
        SendAsync<CommercialPreparationDetail>(HttpMethod.Post, Path(kind), JsonContent.Create(request), ct);
    public Task<ApiCallResult<CommercialPreparationDetail>> GetAsync(string kind, Guid materialId, CancellationToken ct) =>
        SendAsync<CommercialPreparationDetail>(HttpMethod.Get, $"{Path(kind)}/{materialId:D}", null, ct);

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
        return new(false, default, error, (int)response.StatusCode);
    }
}

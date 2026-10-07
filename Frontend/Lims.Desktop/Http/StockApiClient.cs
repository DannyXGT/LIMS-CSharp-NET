using System.Globalization;
using System.Net.Http.Json;
using Lims.Contracts.Errors;
using Lims.Contracts.ReferencePreparations;

namespace Lims.Desktop.Http;

internal sealed class StockApiClient(IHttpClientFactory clients) : IStockApiClient
{
    private const string Path = "api/reference-materials/preparations/stock";
    public Task<ApiCallResult<StockOptions>> OptionsAsync(CancellationToken cancellationToken) => SendAsync<StockOptions>(HttpMethod.Get, Path + "/options", null, cancellationToken);
    public Task<ApiCallResult<StockSourcePage>> SourcesAsync(string? search, int page, CancellationToken cancellationToken) =>
        SendAsync<StockSourcePage>(HttpMethod.Get, Query(Path + "/sources", search, page), null, cancellationToken);
    public Task<ApiCallResult<StockCalculation>> PreviewAsync(StockCalculationRequest request, CancellationToken cancellationToken) =>
        SendAsync<StockCalculation>(HttpMethod.Post, Path + "/preview", JsonContent.Create(request), cancellationToken);
    public Task<ApiCallResult<StockDetail>> CreateAsync(CreateStockRequest request, CancellationToken cancellationToken) =>
        SendAsync<StockDetail>(HttpMethod.Post, Path, JsonContent.Create(request), cancellationToken);
    public Task<ApiCallResult<StockPage>> ListAsync(string? search, int page, CancellationToken cancellationToken) =>
        SendAsync<StockPage>(HttpMethod.Get, Query(Path, search, page), null, cancellationToken);
    public Task<ApiCallResult<StockDetail>> GetAsync(Guid id, CancellationToken cancellationToken) =>
        SendAsync<StockDetail>(HttpMethod.Get, $"{Path}/{id:D}", null, cancellationToken);

    private static string Query(string path, string? search, int page) =>
        string.Create(CultureInfo.InvariantCulture, $"{path}?page={page}&pageSize=25&search={Uri.EscapeDataString(search?.Trim() ?? string.Empty)}");

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

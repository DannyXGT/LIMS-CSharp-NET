using System.Globalization;
using System.Net.Http.Json;
using Lims.Contracts.Errors;
using Lims.Contracts.ReferenceMaterials;

namespace Lims.Desktop.Http;

internal sealed class ReferenceMaterialsApiClient(IHttpClientFactory clients) : IReferenceMaterialsApiClient
{
    public Task<ApiCallResult<IReadOnlyList<ReferenceMethodOption>>> GetMethodsAsync(
        CancellationToken cancellationToken) => SendAsync<IReadOnlyList<ReferenceMethodOption>>(
        new HttpRequestMessage(HttpMethod.Get, "api/reference-materials/methods"),
        cancellationToken);

    public Task<ApiCallResult<IReadOnlyList<ReferenceUnitOption>>> GetUnitsAsync(
        CancellationToken cancellationToken) => SendAsync<IReadOnlyList<ReferenceUnitOption>>(
        new HttpRequestMessage(HttpMethod.Get, "api/reference-materials/units"),
        cancellationToken);

    public Task<ApiCallResult<IReadOnlyList<ReferenceLocationOption>>> GetLocationsAsync(
        CancellationToken cancellationToken) => SendAsync<IReadOnlyList<ReferenceLocationOption>>(
        new HttpRequestMessage(HttpMethod.Get, "api/reference-materials/locations"),
        cancellationToken);

    public Task<ApiCallResult<ReferenceMaterialPage>> ListAsync(
        string? search,
        string? status,
        string? method,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var parameters = new List<string>
        {
            $"page={page.ToString(CultureInfo.InvariantCulture)}",
            $"pageSize={pageSize.ToString(CultureInfo.InvariantCulture)}",
        };
        AddParameter(parameters, "search", search);
        AddParameter(parameters, "status", status);
        AddParameter(parameters, "method", method);
        return SendAsync<ReferenceMaterialPage>(
            new HttpRequestMessage(HttpMethod.Get, $"api/reference-materials?{string.Join('&', parameters)}"),
            cancellationToken);
    }

    public Task<ApiCallResult<ReferenceMaterialDetail>> GetAsync(
        Guid id,
        CancellationToken cancellationToken) => SendAsync<ReferenceMaterialDetail>(
        new HttpRequestMessage(HttpMethod.Get, $"api/reference-materials/{id:D}"),
        cancellationToken);

    public Task<ApiCallResult<ReferenceMaterialDetail>> CreateAsync(
        CreateReferenceMaterialRequest request,
        CancellationToken cancellationToken) => SendJsonAsync<CreateReferenceMaterialRequest, ReferenceMaterialDetail>(
        HttpMethod.Post,
        "api/reference-materials",
        request,
        cancellationToken);

    public Task<ApiCallResult<ReferenceMaterialDetail>> UpdateAsync(
        Guid id,
        UpdateReferenceMaterialRequest request,
        CancellationToken cancellationToken) => SendJsonAsync<UpdateReferenceMaterialRequest, ReferenceMaterialDetail>(
        HttpMethod.Put,
        $"api/reference-materials/{id:D}",
        request,
        cancellationToken);

    public Task<ApiCallResult<ReferenceMaterialDetail>> ArchiveAsync(
        Guid id,
        ArchiveReferenceMaterialRequest request,
        CancellationToken cancellationToken) => SendJsonAsync<ArchiveReferenceMaterialRequest, ReferenceMaterialDetail>(
        HttpMethod.Post,
        $"api/reference-materials/{id:D}/archive",
        request,
        cancellationToken);

    public Task<ApiCallResult<ReferenceMaterialDetail>> ReplaceAsync(
        Guid id,
        ReplaceReferenceMaterialRequest request,
        CancellationToken cancellationToken) => SendJsonAsync<ReplaceReferenceMaterialRequest, ReferenceMaterialDetail>(
        HttpMethod.Post,
        $"api/reference-materials/{id:D}/replacement",
        request,
        cancellationToken);

    private Task<ApiCallResult<TResponse>> SendJsonAsync<TRequest, TResponse>(
        HttpMethod method,
        string path,
        TRequest body,
        CancellationToken cancellationToken)
    {
        var message = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body),
        };
        return SendAsync<TResponse>(message, cancellationToken);
    }

    private async Task<ApiCallResult<T>> SendAsync<T>(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        using (request)
        using (var response = await clients.CreateClient(ApiClientNames.Authorized)
                   .SendAsync(request, cancellationToken)
                   .ConfigureAwait(false))
        {
            if (response.IsSuccessStatusCode)
            {
                var value = await response.Content.ReadFromJsonAsync<T>(cancellationToken).ConfigureAwait(false);
                return new ApiCallResult<T>(value is not null, value, null, (int)response.StatusCode);
            }

            ApiError? error = null;
            try
            {
                error = await response.Content.ReadFromJsonAsync<ApiError>(cancellationToken).ConfigureAwait(false);
            }
            catch (System.Text.Json.JsonException)
            {
                // The ViewModel maps malformed responses to a safe generic message.
            }

            return new ApiCallResult<T>(false, default, error, (int)response.StatusCode);
        }
    }

    private static void AddParameter(List<string> parameters, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parameters.Add($"{name}={Uri.EscapeDataString(value.Trim())}");
        }
    }
}

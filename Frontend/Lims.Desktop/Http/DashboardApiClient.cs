using System.Net.Http.Json;
using Lims.Contracts.Dashboard;
using Lims.Contracts.Errors;
namespace Lims.Desktop.Http;
internal sealed class DashboardApiClient(IHttpClientFactory clients) : IDashboardApiClient
{
    public async Task<ApiCallResult<DashboardResponse>> GetAsync(CancellationToken ct)
    {
        using var response = await clients.CreateClient(ApiClientNames.Authorized).GetAsync("api/dashboard", ct).ConfigureAwait(false);
        try
        {
            if (response.IsSuccessStatusCode)
            {
                var value = await response.Content.ReadFromJsonAsync<DashboardResponse>(ct).ConfigureAwait(false);
                return new(value is not null, value, null, (int)response.StatusCode);
            }
            return new(false, null, await response.Content.ReadFromJsonAsync<ApiError>(ct).ConfigureAwait(false), (int)response.StatusCode);
        }
        catch (System.Text.Json.JsonException) { return new(false, null, null, (int)response.StatusCode); }
    }
}

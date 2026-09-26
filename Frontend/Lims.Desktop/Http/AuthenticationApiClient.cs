using System.Net.Http.Json;
using Lims.Contracts.Authentication;
using Lims.Contracts.Errors;

namespace Lims.Desktop.Http;

internal sealed class AuthenticationApiClient(IHttpClientFactory clients) : IAuthenticationApiClient
{
    public Task<ApiCallResult<AuthenticationResponse>> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken) => SendAsync(
            clients.CreateClient(ApiClientNames.Anonymous),
            "api/auth/login",
            request,
            cancellationToken);

    public Task<ApiCallResult<AuthenticationResponse>> RefreshAsync(
        RefreshRequest request,
        CancellationToken cancellationToken) => SendAsync(
            clients.CreateClient(ApiClientNames.Anonymous),
            "api/auth/refresh",
            request,
            cancellationToken);

    public async Task<bool> LogoutAsync(CancellationToken cancellationToken)
    {
        using var response = await clients.CreateClient(ApiClientNames.Authorized)
            .PostAsync("api/auth/logout", null, cancellationToken)
            .ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }

    private static async Task<ApiCallResult<AuthenticationResponse>> SendAsync<TRequest>(
        HttpClient client,
        string path,
        TRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync(path, request, cancellationToken).ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
        {
            var value = await response.Content.ReadFromJsonAsync<AuthenticationResponse>(cancellationToken)
                .ConfigureAwait(false);
            return new ApiCallResult<AuthenticationResponse>(
                value is not null,
                value,
                null,
                (int)response.StatusCode);
        }

        ApiError? error = null;
        try
        {
            error = await response.Content.ReadFromJsonAsync<ApiError>(cancellationToken).ConfigureAwait(false);
        }
        catch (System.Text.Json.JsonException)
        {
            // The UI maps malformed server responses to a safe unexpected-error state.
        }

        return new ApiCallResult<AuthenticationResponse>(false, null, error, (int)response.StatusCode);
    }
}

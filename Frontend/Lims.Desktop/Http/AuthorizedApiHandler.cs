using System.Net;
using System.Net.Http.Headers;
using Lims.Desktop.Services;

namespace Lims.Desktop.Http;

internal sealed class AuthorizedApiHandler(
    ISessionService session,
    ITokenRefreshCoordinator refreshCoordinator) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        using var retryRequest = await CloneAsync(request, cancellationToken).ConfigureAwait(false);
        AddAccessToken(request);
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.Unauthorized ||
            !await refreshCoordinator.RefreshAsync(cancellationToken).ConfigureAwait(false))
        {
            return response;
        }

        response.Dispose();
        AddAccessToken(retryRequest);
        return await base.SendAsync(retryRequest, cancellationToken).ConfigureAwait(false);
    }

    private void AddAccessToken(HttpRequestMessage request)
    {
        var token = session.AccessToken;
        request.Headers.Authorization = string.IsNullOrWhiteSpace(token)
            ? null
            : new AuthenticationHeaderValue("Bearer", token);
        if (!request.Headers.Contains("X-Correlation-ID"))
        {
            request.Headers.TryAddWithoutValidation("X-Correlation-ID", Guid.NewGuid().ToString("N"));
        }
    }

    private static async Task<HttpRequestMessage> CloneAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version,
            VersionPolicy = request.VersionPolicy,
        };
        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (request.Content is not null)
        {
            var bytes = await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            clone.Content = new ByteArrayContent(bytes);
            foreach (var header in request.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return clone;
    }
}

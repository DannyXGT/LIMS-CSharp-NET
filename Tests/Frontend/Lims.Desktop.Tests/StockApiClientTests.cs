using System.Net;
using System.Text;
using Lims.Contracts.ReferencePreparations;
using Lims.Desktop.Http;

namespace Lims.Desktop.Tests;

public sealed class StockApiClientTests
{
    [Fact]
    public async Task MalformedSuccessResponseDoesNotCrashAndPreservesRetryMessage()
    {
        using var handler = new ResponseHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://fixture.invalid/") };
        var api = new StockApiClient(new Factory(client));
        var response = await api.CreateAsync(new CreateStockRequest(Guid.NewGuid(), "Stock",
            new(Guid.NewGuid(), Guid.NewGuid(), 100, "mg/L", 1000, "mL"), new DateOnly(2026, 10, 6), null), CancellationToken.None);
        Assert.False(response.IsSuccess);
        Assert.Contains("Reintente esta misma solicitud", response.Error?.Message, StringComparison.Ordinal);
    }
    private sealed class Factory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
    private sealed class ResponseHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("unparseable success", Encoding.UTF8, "application/json") });
    }
}

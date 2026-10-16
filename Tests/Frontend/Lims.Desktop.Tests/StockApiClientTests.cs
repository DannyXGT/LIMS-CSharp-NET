using System.Net;
using System.Text;
using Lims.Contracts.ReferencePreparations;
using Lims.Desktop.Http;

namespace Lims.Desktop.Tests;

public sealed class StockApiClientTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadFiltersAreEncodedAndSentToThePaginatedEndpoint(bool intermediate)
    {
        using var handler = new FilterHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://fixture.invalid/") };
        var filter = new PreparationListFilter("Expired", "Low", "EPA & método");
        if (intermediate)
            Assert.True((await new IntermediateApiClient(new Factory(client)).ListAsync("lote & 1", 3, CancellationToken.None, filter)).IsSuccess);
        else
            Assert.True((await new StockApiClient(new Factory(client)).ListAsync("lote & 1", 3, CancellationToken.None, filter)).IsSuccess);
        var query = handler.RequestUri!.Query;
        Assert.Contains("page=3&pageSize=25", query, StringComparison.Ordinal);
        Assert.Contains("status=Expired&availability=Low", query, StringComparison.Ordinal);
        Assert.Contains("method=" + Uri.EscapeDataString(filter.Method!), query, StringComparison.Ordinal);
        Assert.Contains("search=" + Uri.EscapeDataString("lote & 1"), query, StringComparison.Ordinal);
    }

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
    private sealed class FilterHandler : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"items\":[],\"page\":3,\"pageSize\":25,\"totalCount\":0,\"filtersApplied\":true}", Encoding.UTF8, "application/json") });
        }
    }
}

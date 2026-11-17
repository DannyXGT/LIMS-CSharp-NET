using System.Net;
using System.Net.Http.Json;
using Lims.Contracts.Errors;
using Lims.Contracts.ReferencePreparations;
using Lims.Desktop.Http;

namespace Lims.Desktop.Tests;

public sealed class DilutionSolventCatalogTests
{
    private static readonly SolventOption Methanol = new(4, "Metanol", "MeOH", true, 0);

    [Theory]
    [InlineData("Intermedia", false)]
    [InlineData("Intermedia", true)]
    [InlineData("Curva", false)]
    [InlineData("Curva", true)]
    [InlineData("AQS", false)]
    [InlineData("AQS", true)]
    public async Task MissingCatalogUsesTheSamePersistentCatalogAsStock(string kind, bool empty)
    {
        var options = new IntermediateOptions([], [], empty ? [] : null, true);
        using var handler = new Handler(options);
        using var client = new HttpClient(handler) { BaseAddress = new("http://fixture.invalid/") };
        var result = await Options(kind, client);
        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.SupportsDilutionAdditives);
        Assert.Equal(Methanol, Assert.Single(result.Value.Solvents!));
        Assert.EndsWith("/stock/solvents?search=", handler.Paths[1]);
        Assert.Equal(2, handler.Paths.Count);
    }

    [Theory]
    [InlineData("Intermedia", true)]
    [InlineData("Curva", true)]
    [InlineData("AQS", true)]
    [InlineData("Intermedia", false)]
    [InlineData("Curva", false)]
    [InlineData("AQS", false)]
    public async Task ExistingCatalogAndLegacyCapabilitiesArePreserved(string kind, bool supported)
    {
        var options = new IntermediateOptions([], [], supported ? [Methanol] : null, supported);
        using var handler = new Handler(options);
        using var client = new HttpClient(handler) { BaseAddress = new("http://fixture.invalid/") };
        var result = await Options(kind, client);
        Assert.True(result.IsSuccess);
        Assert.Equal(supported, result.Value!.SupportsDilutionAdditives);
        Assert.Single(handler.Paths);
        if (supported) Assert.Equal(Methanol, Assert.Single(result.Value.Solvents!));
        else Assert.Null(result.Value.Solvents);
    }

    [Theory]
    [InlineData("Intermedia", HttpStatusCode.Forbidden)]
    [InlineData("Curva", HttpStatusCode.InternalServerError)]
    [InlineData("AQS", HttpStatusCode.Unauthorized)]
    public async Task CatalogFailuresRemainVisibleAndDoNotReturnAnApparentlyReadyForm(string kind, HttpStatusCode status)
    {
        using var handler = new Handler(new([], [], null, true), status);
        using var client = new HttpClient(handler) { BaseAddress = new("http://fixture.invalid/") };
        var result = await Options(kind, client);
        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal((int)status, result.StatusCode);
        Assert.Equal("Catálogo no disponible", result.Error!.Message);
    }

    private static Task<ApiCallResult<IntermediateOptions>> Options(string kind, HttpClient client) => kind == "Intermedia"
        ? new IntermediateApiClient(new Factory(client)).OptionsAsync(default)
        : new SolutionGroupApiClient(new Factory(client)).OptionsAsync(kind, default);

    private sealed class Factory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class Handler(IntermediateOptions options, HttpStatusCode catalogStatus = HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            Paths.Add(path);
            if (path.Contains("/stock/solvents", StringComparison.Ordinal))
                return Task.FromResult(catalogStatus == HttpStatusCode.OK
                    ? new HttpResponseMessage(catalogStatus) { Content = JsonContent.Create(new[] { Methanol }) }
                    : new HttpResponseMessage(catalogStatus) { Content = JsonContent.Create(new ApiError(ErrorCodes.UnexpectedError, "Catálogo no disponible", "")) });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(options) });
        }
    }
}

using System.Net;
using System.Net.Http.Json;
using Lims.Contracts.Errors;
using Lims.Desktop.Http;
using Lims.Desktop.Presentation;

namespace Lims.Desktop.Tests;

public sealed class SolutionGroupApiClientTests
{
    [Theory]
    [InlineData("Curva", "")]
    [InlineData("AQS", "")]
    [InlineData("Curva", "<html>Not found</html>")]
    [InlineData("AQS", "<html>Not found</html>")]
    public async Task MissingDeployedRoutesExplainWhyListingAndSavingAreUnavailable(string kind, string body)
    {
        using var handler = new Handler(() => new(HttpStatusCode.NotFound) { Content = new StringContent(body) });
        using var client = new HttpClient(handler) { BaseAddress = new("http://fixture.invalid/") };
        var api = new SolutionGroupApiClient(new Factory(client));
        var list = await api.ListAsync(kind, null, 1, default);
        var options = await api.OptionsAsync(kind, default);
        Assert.False(list.IsSuccess); Assert.Null(list.Value); Assert.Equal(404, list.StatusCode);
        Assert.False(options.IsSuccess); Assert.Null(options.Value);
        Assert.Contains("Actualice la API", ReferenceMaterialErrors.PreparationMessage(list.Error), StringComparison.Ordinal);
        Assert.Contains("consultar y guardar", ReferenceMaterialErrors.PreparationMessage(options.Error), StringComparison.Ordinal);
    }

    [Fact]
    public async Task StructuredMissingRecordIsPreservedAndDoesNotClaimApiIsAbsent()
    {
        var error = new ApiError(ErrorCodes.NotFound, "La preparación solicitada no existe.", "record-404");
        using var handler = new Handler(() => new(HttpStatusCode.NotFound) { Content = JsonContent.Create(error) });
        using var client = new HttpClient(handler) { BaseAddress = new("http://fixture.invalid/") };
        var response = await new SolutionGroupApiClient(new Factory(client)).GetAsync("Curva", Guid.NewGuid(), default);
        Assert.Equal(error, response.Error); Assert.False(response.IsSuccess);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task OtherFailuresAreNotMisreportedAsMissingRoutes(HttpStatusCode status)
    {
        using var handler = new Handler(() => new(status) { Content = new StringContent("") });
        using var client = new HttpClient(handler) { BaseAddress = new("http://fixture.invalid/") };
        var response = await new SolutionGroupApiClient(new Factory(client)).OptionsAsync("AQS", default);
        Assert.False(response.IsSuccess); Assert.Equal((int)status, response.StatusCode);
        Assert.DoesNotContain("Actualice la API", ReferenceMaterialErrors.PreparationMessage(response.Error), StringComparison.Ordinal);
    }

    private sealed class Factory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
    private sealed class Handler(Func<HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(response());
    }
}

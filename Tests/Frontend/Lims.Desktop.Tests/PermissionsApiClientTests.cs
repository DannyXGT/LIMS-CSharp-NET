using System.Net;
using System.Net.Http.Json;
using Lims.Contracts.Authentication;
using Lims.Contracts.Errors;
using Lims.Contracts.Permissions;
using Lims.Contracts.ReferenceMaterials;
using Lims.Desktop.Http;

namespace Lims.Desktop.Tests;

public sealed class PermissionsApiClientTests
{
    [Theory]
    [InlineData("")]
    [InlineData("<html>Not found</html>")]
    public async Task MissingPermissionsEndpointUsesFreshServerProfile(string errorBody)
    {
        using var handler = new ResponseHandler(path => path == "/api/auth/permissions"
            ? Error(HttpStatusCode.NotFound, errorBody)
            : new(HttpStatusCode.OK) { Content = JsonContent.Create(new UserProfile(1, "Admin", "admin@intertek.com", "Administrador", "Administraci\u00f3n", [])) });
        using var client = Client(handler);
        var result = await new PermissionsApiClient(new Factory(client)).EffectiveAsync(CancellationToken.None);
        Assert.True(result.IsSuccess); Assert.Equal("Administrador", result.Value!.Role);
        Assert.Collection(handler.Paths, path => Assert.Equal("/api/auth/permissions", path), path => Assert.Equal("/api/auth/me", path));
    }

    [Fact]
    public async Task LegacyProfileRestoresOnlyItsExistingPermissionsAndPreparationEquivalents()
    {
        using var handler = new ResponseHandler(path => path == "/api/auth/permissions"
            ? Error(HttpStatusCode.NotFound, "")
            : new(HttpStatusCode.OK) { Content = JsonContent.Create(new UserProfile(2, "Analyst", "analyst@intertek.com", "Analista", "Laboratorio Anal\u00edtico", [ReferenceMaterialPermissions.View])) });
        using var client = Client(handler);
        var result = await new PermissionsApiClient(new Factory(client)).EffectiveAsync(CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Contains(PermissionCatalog.StockView, result.Value!.Permissions);
        Assert.Contains(PermissionCatalog.IntermediateView, result.Value.Permissions);
        Assert.DoesNotContain(PermissionCatalog.StockCreate, result.Value.Permissions);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task AuthorizationAndServiceErrorsDoNotFallBackToLegacyPermissions(HttpStatusCode status)
    {
        using var handler = new ResponseHandler(_ => Error(status, ""));
        using var client = Client(handler);
        var result = await new PermissionsApiClient(new Factory(client)).EffectiveAsync(CancellationToken.None);
        Assert.False(result.IsSuccess); Assert.Equal((int)status, result.StatusCode); Assert.Single(handler.Paths);
    }

    [Fact]
    public async Task MissingEndpointDoesNotGrantAccessWhenCurrentProfileCannotBeValidated()
    {
        using var handler = new ResponseHandler(path => Error(path == "/api/auth/permissions" ? HttpStatusCode.NotFound : HttpStatusCode.Unauthorized, ""));
        using var client = Client(handler);
        var result = await new PermissionsApiClient(new Factory(client)).EffectiveAsync(CancellationToken.None);
        Assert.False(result.IsSuccess); Assert.Null(result.Value); Assert.Equal(401, result.StatusCode);
    }

    [Fact]
    public async Task MissingManagementCatalogShowsDeploymentMessageInsteadOfInvalidJson()
    {
        using var handler = new ResponseHandler(_ => Error(HttpStatusCode.NotFound, "<html>Not found</html>"));
        using var client = Client(handler);
        var result = await new PermissionsApiClient(new Factory(client)).CatalogAsync(CancellationToken.None);
        Assert.False(result.IsSuccess); Assert.Equal(404, result.StatusCode);
        Assert.Equal(ErrorCodes.NotFound, result.Error!.Code);
        Assert.Contains("actualizar el servicio", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CurrentServerPermissionsAreUsedWithoutLegacyExpansion()
    {
        using var handler = new ResponseHandler(_ => new(HttpStatusCode.OK) { Content = JsonContent.Create(new EffectivePermissions("Analista", [], true)) });
        using var client = Client(handler);
        var result = await new PermissionsApiClient(new Factory(client)).EffectiveAsync(CancellationToken.None);
        Assert.True(result.IsSuccess); Assert.Empty(result.Value!.Permissions); Assert.True(result.Value.IndividualEnforcement);
        Assert.Single(handler.Paths);
    }

    private static HttpClient Client(HttpMessageHandler handler) => new(handler) { BaseAddress = new("http://fixture.invalid/") };
    private static HttpResponseMessage Error(HttpStatusCode status, string body) => new(status) { Content = new StringContent(body) };
    private sealed class Factory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
    private sealed class ResponseHandler(Func<string, HttpResponseMessage> response) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            Paths.Add(path);
            return Task.FromResult(response(path));
        }
    }
}

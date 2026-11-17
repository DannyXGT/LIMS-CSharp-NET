using System.Net;
using System.Net.Http.Json;
using Lims.Contracts.Permissions;
using Lims.Desktop.Planning;

namespace Lims.Desktop.Tests;

public sealed class PlanningCompatibilityTests
{
    [Fact]
    public async Task ExistingApiKeepsAuthorizedLaboratoryAvailableWithoutAnotherLogin()
    {
        var paths=new List<string>();
        using var factory=new ClientFactory(request=>
        {
            paths.Add(request.RequestUri!.AbsolutePath);
            return request.RequestUri.AbsolutePath=="/api/workspaces"?new(HttpStatusCode.NotFound):new(HttpStatusCode.OK){Content=JsonContent.Create(new EffectivePermissions("Analista",[PermissionCatalog.StockView],true))};
        });
        var result=await new PlanningApiClient(factory).Workspaces(default);
        Assert.True(result.IsSuccess);Assert.True(result.Value!.Laboratory);Assert.False(result.Value.Planning);Assert.False(result.Value.Administration);Assert.NotNull(result.Value.Notice);
        Assert.Collection(paths,p=>Assert.Equal("/api/workspaces",p),p=>Assert.Equal("/api/auth/permissions",p));
    }
    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task AuthenticationAndServiceFailuresCannotEnableLegacyAccess(HttpStatusCode status)
    {
        var requests=0;using var factory=new ClientFactory(_=>{requests++;return new(status);});
        var result=await new PlanningApiClient(factory).Workspaces(default);
        Assert.False(result.IsSuccess);Assert.Null(result.Value);Assert.Equal(1,requests);
    }
    [Fact]
    public async Task LegacyApiDoesNotExposeSpacesWhenCurrentAccountHasNoPermissions()
    {
        using var factory=new ClientFactory(request=>request.RequestUri!.AbsolutePath=="/api/workspaces"?new(HttpStatusCode.NotFound):new(HttpStatusCode.OK){Content=JsonContent.Create(new EffectivePermissions("Usuario",[],true))});
        var result=await new PlanningApiClient(factory).Workspaces(default);
        Assert.True(result.IsSuccess);Assert.False(result.Value!.Laboratory);Assert.False(result.Value.Planning);Assert.False(result.Value.Administration);
    }
    private sealed class ClientFactory : IHttpClientFactory,IDisposable
    {
        private readonly HttpClient _client;
        public ClientFactory(Func<HttpRequestMessage,HttpResponseMessage> response)=>_client=new(new Handler(response)){BaseAddress=new Uri("https://localhost")};
        public HttpClient CreateClient(string name)=>_client;
        public void Dispose()=>_client.Dispose();
        private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> response):HttpMessageHandler
        {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)=>Task.FromResult(response(request));}
    }
}

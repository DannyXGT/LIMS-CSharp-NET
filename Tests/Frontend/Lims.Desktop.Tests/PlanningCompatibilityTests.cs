using System.Net;
using System.Net.Http.Json;
using Lims.Contracts.Permissions;
using Lims.Contracts.Planning;
using Lims.Desktop.Planning;

namespace Lims.Desktop.Tests;

public sealed class PlanningCompatibilityTests
{
    [Fact]
    public async Task ImportTimeoutDistinguishesAnUnconfirmedPreviewFromAnUncertainConfirmation()
    {
        using var factory=new ClientFactory(_=>throw new TaskCanceledException("Simulated server timeout"));
        var api=new PlanningApiClient(factory);
        var preview=await api.Preview("Summary.xlsx",[],"Report.xlsx",[],new("First",new(2026,11,26)),default);
        Assert.False(preview.IsSuccess);Assert.Contains("No se confirmó",preview.Error);Assert.DoesNotContain("historial",preview.Error);
        var confirm=await api.Confirm(Guid.NewGuid(),default);
        Assert.False(confirm.IsSuccess);Assert.Contains("historial",confirm.Error);Assert.Contains("comprobar si se guardó",confirm.Error);
        var history=await api.Batches(default);
        Assert.False(history.IsSuccess);Assert.Contains("consulta",history.Error);Assert.DoesNotContain("guardó",history.Error);
    }
    [Fact]
    public async Task ScheduleRequestsOmitAbsentRevisionAndEncodeSearchBeforeFirstPage()
    {
        var queries=new List<string>();
        using var factory=new ClientFactory(request=>
        {
            queries.Add(request.RequestUri!.Query);
            return new(HttpStatusCode.OK){Content=JsonContent.Create(new ReportSchedulePage([],0,1,100,42))};
        });
        var api=new PlanningApiClient(factory);
        Assert.True((await api.ReportSchedules(new(Search:"JOB & PH"),default)).IsSuccess);
        Assert.DoesNotContain("Revision",queries[0]);Assert.Contains("Search=JOB%20%26%20PH",queries[0]);
        await api.ReportSchedules(new(Page:2,Revision:42),default);Assert.Contains("Revision=42",queries[1]);
    }
    [Fact]
    public async Task OlderServerReportsMissingReportListWithoutPretendingItIsAnEmptyList()
    {
        using var factory=new ClientFactory(_=>new(HttpStatusCode.NotFound));
        var result=await new PlanningApiClient(factory).ReportList(new(),default);
        Assert.False(result.IsSuccess);Assert.Null(result.Value);Assert.Equal(404,result.StatusCode);
        Assert.Contains("servidor",result.Error);Assert.Contains("actualizar",result.Error);
    }
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

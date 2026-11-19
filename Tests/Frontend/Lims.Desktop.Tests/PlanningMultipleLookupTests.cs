using System.Net;
using System.Net.Http.Json;
using Lims.Contracts.Planning;
using Lims.Desktop.Planning;

namespace Lims.Desktop.Tests;

public sealed class PlanningMultipleLookupTests
{
    [Fact]
    public void ExcelListNormalizesOnlyInputAndKeepsFirstOccurrenceOrder()
    {
        var list=PlanningJobList.Parse(" guat01\r\nGUAT01;guat02, GUAT02\tguat03 ");
        Assert.Equal(["GUAT01","GUAT02","GUAT03"],list.Jobs);Assert.Equal(5,list.Entered);Assert.Equal(2,list.Duplicates);
        Assert.Throws<FormatException>(()=>PlanningJobList.Parse(string.Join(',',Enumerable.Range(0,501).Select(i=>"JOB"+i))));
    }
    [Fact]
    public async Task JobOnSecondServerPageIsFoundDespiteFiveHundredLegitimateExecutionsOfFirstJob()
    {
        int calls=0;
        using var factory=new Factory(request=>
        {
            calls++;Assert.Contains("SearchMode=Exact",request.RequestUri!.Query);Assert.Contains("PageSize=500",request.RequestUri.Query);
            var rows=calls==1?Enumerable.Range(0,500).Select(_=>Row("JOB-A")).ToArray():new[]{Row("JOB-B")};
            return new(HttpStatusCode.OK){Content=JsonContent.Create(new PlanningPage(rows,501,calls,500,new(2,501,0,0,501)))};
        });
        var result=await PlanningMultipleLookup.Find(new(factory),PlanningJobList.Parse("job-a\njob-b\nnot-found"),default);
        Assert.Equal(2,calls);Assert.Equal(["JOB-A","JOB-B"],result.Found);Assert.Equal(["NOT-FOUND"],result.Missing);Assert.Equal(501,result.Executions);
    }
    [Fact]
    public async Task FailedLaterPageCannotBeReportedAsMissingJobs()
    {
        int calls=0;using var factory=new Factory(_=>++calls==1?new(HttpStatusCode.OK){Content=JsonContent.Create(new PlanningPage([Row("JOB-A")],501,1,500))}:new(HttpStatusCode.ServiceUnavailable));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>PlanningMultipleLookup.Find(new(factory),PlanningJobList.Parse("JOB-A,JOB-B"),default));
    }
    private static PlanningRow Row(string job)=>new(Guid.NewGuid(),Guid.NewGuid(),job,null,null,"Synthetic execution",null,null,null,null,null,"Sin información",null,null,"ReportList",null,null,false);
    private sealed class Factory(Func<HttpRequestMessage,HttpResponseMessage> response):IHttpClientFactory,IDisposable
    {
        private readonly HttpClient _client=new(new Handler(response)){BaseAddress=new Uri("https://localhost")};
        public HttpClient CreateClient(string name)=>_client;
        public void Dispose()=>_client.Dispose();
        private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> response):HttpMessageHandler
        {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>Task.FromResult(response(request));}
    }
}

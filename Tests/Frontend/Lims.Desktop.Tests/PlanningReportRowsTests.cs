using Lims.Contracts.Planning;
using Lims.Desktop.Planning;

namespace Lims.Desktop.Tests;

public sealed class PlanningReportRowsTests
{
    [Fact]
    public void MarkHistoryIdentifiesTheExecutionEvenWhenPlanningNamesRepeat()
    {
        var id=Guid.NewGuid();var text=PlanningStatusText.Changes("{\"executionId\":\""+id+"\",\"before\":{\"digitizedBy\":null},\"after\":{\"digitizedBy\":\"Actor\"}}");
        Assert.Contains(id.ToString(),text);Assert.Contains("Verificado por",text);Assert.Contains("Actor",text);
        var keep=PlanningStatusText.Changes("{\"execution\":{\"executionId\":\""+id+"\",\"version\":3},\"keepInPlanning\":true}");
        Assert.Contains(id.ToString(),keep);Assert.Contains("Sí",keep);
    }
    private static ReportListItem Item()=>new(new(Guid.NewGuid(),Guid.NewGuid(),"JOB",null,null,"Technical PH","PH","CHEMICAL","Regular",null,null,"Pending",null,null,"ReportList","Buyer",null,false),0,false,null,"manual");
    [Fact]
    public async Task IncrementalLoadingHasNoOneThousandLimitAndCarriesTheFirstRevision()
    {
        var requests=new List<(int,long?)>();var source=Enumerable.Range(0,1203).Select(_=>Item()).ToArray();
        var rows=new ReportListRows((page,revision,_)=>
        {requests.Add((page,revision));return Task.FromResult(new ReportListPage(source.Skip((page-1)*500).Take(500).ToArray(),source.Length,page,500,42));},default);
        while(rows.HasMoreItems)await rows.LoadNext();
        Assert.Equal(1203,rows.Count);Assert.Equal(new[]{(1,(long?)null),(2,(long?)42),(3,(long?)42)},requests);
        Assert.Equal(1203,rows.Select(x=>x.Execution.ExecutionId).Distinct().Count());
    }
    [Fact]
    public async Task ChangedRevisionStopsContinuationWithoutMixingRows()
    {
        Exception? failure=null;
        var rows=new ReportListRows((page,revision,_)=>Task.FromResult(new ReportListPage([Item()],2,page,1,page)),default);
        rows.Updated+=error=>failure=error;await rows.LoadNext();await rows.LoadNext();
        Assert.Single(rows);Assert.False(rows.HasMoreItems);Assert.IsType<InvalidOperationException>(failure);
    }
    [Fact]
    public async Task CanceledOldSearchCannotPublishItsLateResponse()
    {
        using var cancellation=new CancellationTokenSource();
        var response=new TaskCompletionSource<ReportListPage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var rows=new ReportListRows((_,_,_)=>response.Task,cancellation.Token);
        var loading=rows.LoadNext();cancellation.Cancel();response.SetResult(new([Item()],1,1,500));await loading;
        Assert.Empty(rows);
    }
}

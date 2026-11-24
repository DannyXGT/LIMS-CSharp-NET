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
    [Fact]
    public async Task SavedCheckRemovesOnlyItsExecutionAndAdvancesRevisionWithoutReplacingTheCollection()
    {
        var a=Item();var b=Item();var rows=new ReportListRows((_,_,_)=>Task.FromResult(new ReportListPage([a,b],2,1,500,4)),default);
        await rows.LoadNext();var saved=a with{Version=1,Execution=a.Execution with{DigitizedAt=DateTimeOffset.UtcNow,TestedOn=new(2026,11,23)}};
        rows.ApplySaved(saved,5,"Pending");Assert.Same(b,Assert.Single(rows));Assert.Equal(1,rows.Total);Assert.Equal(5,rows.Revision);
        rows.ApplySaved(b with{KeepInPlanning=true},6,"Pending");Assert.True(Assert.Single(rows).KeepInPlanning);Assert.Equal(6,rows.Revision);
    }
    [Fact]
    public async Task SavingOneRowCannotHideAnExternalRevisionOrCatalogueChangeFromContinuation()
    {
        foreach(var (native,catalogue) in new[]{(12L,100L),(11L,101L)})
        {
            var item=Item();var rows=new ReportListRows((_,_,_)=>Task.FromResult(new ReportListPage([item],10,1,500,40,DataRevision:10,CatalogueRevision:100)),default);
            await rows.LoadNext();Assert.False(rows.ApplySaved(item,41,"Pending",native,catalogue));Assert.False(rows.HasMoreItems);
        }
        var own=Item();var valid=new ReportListRows((_,_,_)=>Task.FromResult(new ReportListPage([own],10,1,500,40,DataRevision:10,CatalogueRevision:100)),default);
        await valid.LoadNext();Assert.True(valid.ApplySaved(own,41,"Pending",11,100));Assert.True(valid.HasMoreItems);
    }
}

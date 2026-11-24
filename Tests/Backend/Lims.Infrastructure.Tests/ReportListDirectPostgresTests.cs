using Lims.Contracts.Planning;

namespace Lims.Infrastructure.Tests;

public sealed partial class PlanningPostgresTests
{
    [PlanningDatabaseFact]
    public async Task IndividualChecksShareFixedClockAndCloseOnlyWhenEveryExecutionIsDigitized()
    {
        await using var db=await TestDatabase.Create();var store=db.Store;
        await store.SaveEquivalenceAsync(new("PH","PH","Chemical",false,null,"Fixture"),1,default);
        await store.SaveEquivalenceAsync(new("Crocking","CROCKING","Physical",false,null,"Fixture"),1,default);
        var preview=await store.PreviewAsync(Input(Summary(),Report(test:"PH"),Report(3,"Crocking")),1,default);
        await store.ConfirmAsync(preview.Id,1,default);
        // Summary deliberately omits monetary metadata; resolve those fixture incidents only.
        await db.Sql("UPDATE planning.incidents SET state='Resolved'");
        var rows=(await store.ReportListAsync(new(),default)).Items;Assert.Equal(2,rows.Count);
        var at=new DateTimeOffset(2026,11,23,14,35,0,TimeSpan.FromHours(-6));
        var first=rows[0];var second=rows[1];
        var request=new ReportDigitization(Guid.NewGuid(),[new(first.Execution.ExecutionId!.Value,first.Version)],at,"CHK directo de fixture");
        Assert.Empty((await store.DigitizeReportAsync(request,1,false,default)).ReadyJobs);
        await store.DigitizeReportAsync(request,1,false,default); // Lost-response retry preserves the same operation.
        Assert.Empty((await store.ReportClosuresAsync(new(),default)).Items);
        var saved=Assert.Single((await store.ReportListAsync(new(View:"All",ExecutionId:first.Execution.ExecutionId),default)).Items);
        await store.KeepReportAsync(new(new(saved.Execution.ExecutionId!.Value,saved.Version),true,"Mantener en Muestreo"),1,default);
        saved=Assert.Single((await store.ReportListAsync(new(View:"All",ExecutionId:first.Execution.ExecutionId),default)).Items);
        Assert.True(saved.KeepInPlanning);Assert.Equal(at,saved.Execution.DigitizedAt);
        Assert.Single((await store.ReportListAsync(new(View:"Active",ExecutionId:first.Execution.ExecutionId),default)).Items);
        await store.DigitizeReportAsync(new(Guid.NewGuid(),[new(second.Execution.ExecutionId!.Value,second.Version)],at,"CHK directo de fixture"),1,false,default);
        var group=Assert.Single((await store.ReportSchedulesAsync(new(GroupByClock:true),default)).Items);
        Assert.Equal(2,group.Tests);Assert.Equal("14:35",group.TimeText);Assert.Equal("2026-11-23",group.DateText);
        Assert.Equal(2,group.SessionIds!.Count);
        var closure=Assert.Single((await store.ReportClosuresAsync(new(),default)).Items);
        Assert.Equal(2,closure.Digitized);Assert.Equal(at,closure.LastDigitizedAt);
        var detail=(await store.ReportScheduleAsync(group.Id,default,true))!;
        await store.ReviewReportScheduleAsync(new(group.Id,group.Version,detail.Members.Select(m=>new ReportExecutionVersion(m.Item.Execution.ExecutionId!.Value,m.Item.Version)).ToArray(),"Revisión del horario",detail.Sessions!.Select(s=>new ReportSessionVersion(s.Id,s.Version)).ToArray()),1,default);
        saved=Assert.Single((await store.ReportListAsync(new(View:"All",ExecutionId:first.Execution.ExecutionId),default)).Items);
        await store.KeepReportAsync(new(new(saved.Execution.ExecutionId!.Value,saved.Version),false,"Cambiar permanencia después de revisar",saved.KeepVersion),1,default);
        Assert.Equal("Chequeado",Assert.Single((await store.ReportSchedulesAsync(new(GroupByClock:true),default)).Items).State);
        await Assert.ThrowsAsync<Lims.Application.Planning.PlanningConflictException>(()=>store.KeepReportAsync(new(new(saved.Execution.ExecutionId!.Value,saved.Version),true,"Solicitud obsoleta",saved.KeepVersion),1,default));
        await store.CheckReportAsync(new([new(saved.Execution.ExecutionId!.Value,saved.Version)],false,"Desmarcado directo de fixture"),1,false,default);
        Assert.Empty((await store.ReportClosuresAsync(new(),default)).Items);
        Assert.Null(Assert.Single((await store.ReportSchedulesAsync(new(GroupByClock:true),default)).Items).ReviewedAt);
    }

    [PlanningDatabaseFact]
    public async Task ContinuationOffsetAfterRemovingCheckedRowDoesNotSkipTheNextPendingExecution()
    {
        await using var db=await TestDatabase.Create();var store=db.Store;
        await store.SaveEquivalenceAsync(new("PH","PH","Chemical",false,null,"Fixture"),1,default);
        var preview=await store.PreviewAsync(Input(Summary(),Report(test:"PH")),1,default);await store.ConfirmAsync(preview.Id,1,default);
        await db.Sql("INSERT INTO planning.executions(id,episode_id,original_test,test_key,equivalence,laboratory,origin) SELECT gen_random_uuid(),e.id,'Technical '||n,'TECHNICAL '||n,'PH','Chemical','ReportList' FROM planning.episodes e CROSS JOIN generate_series(1,9) n");
        var first=await store.ReportListAsync(new(PageSize:5,Sort:"ExecutionId"),default);
        var selected=first.Items[0];await store.CheckReportAsync(new([new(selected.Execution.ExecutionId!.Value,selected.Version)],true,"Fixture"),1,false,default);
        var current=await store.ReportListAsync(new(PageSize:1),default);
        Assert.NotNull(current.DataRevision);Assert.NotNull(current.CatalogueRevision);
        var next=await store.ReportListAsync(new(Page:2,PageSize:5,Sort:"ExecutionId",Offset:4,Revision:current.Revision),default);
        var ids=first.Items.Skip(1).Concat(next.Items).Select(i=>i.Execution.ExecutionId).ToArray();
        Assert.Equal(9,ids.Length);Assert.Equal(9,ids.Distinct().Count());Assert.DoesNotContain(selected.Execution.ExecutionId,ids);
    }
}

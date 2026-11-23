using Lims.Application.Planning;
using Lims.Contracts.Planning;

namespace Lims.Infrastructure.Tests;

public sealed partial class PlanningPostgresTests
{
    private static ReportScheduleReview ClockReview(ReportScheduleDetail d)=>new(d.Schedule.Id,d.Schedule.Version,d.Members.Select(m=>new ReportExecutionVersion(m.Item.Execution.ExecutionId!.Value,m.Item.Version)).ToArray(),"Revisión completa de fixture",d.Sessions!.Select(s=>new ReportSessionVersion(s.Id,s.Version)).ToArray());
    [PlanningDatabaseFact]
    public async Task ClockGroupCombinesConfirmationsPreservingIndependentMembersReviewersAndHistory()
    {
        await using var db=await TestDatabase.Create();var store=db.Store;
        await store.SaveEquivalenceAsync(new("A","PH","CHEMICAL",false,null,"Fixture"),1,default);await store.SaveEquivalenceAsync(new("B","PH","CHEMICAL",false,null,"Fixture"),1,default);
        var preview=await store.PreviewAsync(Input(Summary(),Report(test:"A"),Report(3,"B")),1,default);await store.ConfirmAsync(preview.Id,1,default);
        var rows=(await store.ReportListAsync(new(),default)).Items;
        var one=Digitization([rows[0]]);var two=Digitization([rows[1]]) with{OperationalAt=one.OperationalAt.AddSeconds(25)};
        await store.DigitizeReportAsync(one,1,false,default);await store.ReviewReportScheduleAsync(Review(await store.ReportScheduleAsync(one.SessionId,default)),1,default);
        await store.DigitizeReportAsync(two,1,false,default);
        Assert.Equal(2,(await store.ReportSchedulesAsync(new(),default)).Total);
        var group=Assert.Single((await store.ReportSchedulesAsync(new(GroupByClock:true),default)).Items);Assert.Equal(2,group.Tests);Assert.Equal(1,group.Jobs);Assert.Equal("Pendiente",group.State);
        var detail=await store.ReportScheduleAsync(group.Id,default,true);Assert.Equal(2,detail.Members.Count);Assert.Equal(2,detail.Sessions!.Count);
        var csv=System.Text.Encoding.UTF8.GetString(await store.ExportReviewedSchedulesAsync(new(GroupByClock:true),default));Assert.DoesNotContain("J1",csv);
        await db.Sql("INSERT INTO planning.actors(id,nombre) VALUES(2,'Revisor de horario')");
        await store.ReviewReportScheduleAsync(ClockReview(detail),2,default);
        var saved=await store.ReportScheduleAsync(group.Id,default,true);Assert.Equal("Chequeado",saved.Schedule.State);Assert.Equal(2,saved.History.Count(h=>h.Action=="ReportScheduleReviewed"));
        Assert.Contains(saved.Sessions!,s=>s.ReviewedBy=="Revisor de horario");Assert.Contains(saved.Sessions!,s=>s.ReviewedBy=="Actor sintético local");
        Assert.Equal(2,(await store.QueryAsync(new(State:"Digitado"),default)).Items.Count);
        csv=System.Text.Encoding.UTF8.GetString(await store.ExportReviewedSchedulesAsync(new(GroupByClock:true),default));Assert.Contains("Due Date",csv);Assert.Contains("2026-11-20",csv);Assert.Contains(rows[0].Execution.ExecutionId!.ToString()!,csv);Assert.Contains(rows[1].Execution.ExecutionId!.ToString()!,csv);
    }
    [PlanningDatabaseFact]
    public async Task ClockReviewRejectsNewMemberAndRollsBackAllSessionsOnStaleVersion()
    {
        await using var db=await TestDatabase.Create();var store=db.Store;
        foreach(var name in new[]{"A","B","C"})await store.SaveEquivalenceAsync(new(name,"PH","CHEMICAL",false,null,"Fixture"),1,default);
        var preview=await store.PreviewAsync(Input(Summary(),Report(test:"A"),Report(3,"B"),Report(4,"C")),1,default);await store.ConfirmAsync(preview.Id,1,default);
        var rows=(await store.ReportListAsync(new(),default)).Items;var one=Digitization([rows[0]]);await store.DigitizeReportAsync(one,1,false,default);
        var stale=ClockReview(await store.ReportScheduleAsync(one.SessionId,default,true));
        await store.DigitizeReportAsync(Digitization([rows[1]]),1,false,default);await Assert.ThrowsAsync<PlanningConflictException>(()=>store.ReviewReportScheduleAsync(stale,1,default));
        var current=ClockReview(await store.ReportScheduleAsync(one.SessionId,default,true));var invalid=current with{Executions=current.Executions.Select((e,i)=>i==1?e with{Version=e.Version+1}:e).ToArray()};
        await Assert.ThrowsAsync<PlanningConflictException>(()=>store.ReviewReportScheduleAsync(invalid,1,default));Assert.Equal(0L,await db.Scalar("SELECT count(*) FROM planning.report_sessions WHERE reviewed_at IS NOT NULL"));Assert.Equal(0L,await db.Scalar("SELECT count(*) FROM planning.history WHERE action='ReportScheduleReviewed'"));
    }
    [PlanningDatabaseFact]
    public async Task ReportListFiltersAndSortExposeSourceDatesAndPreserveUnknownState()
    {
        await using var db=await TestDatabase.Create();var store=db.Store;
        var preview=await store.PreviewAsync(Input(Summary(),Report(test:"Unknown original")),1,default);await store.ConfirmAsync(preview.Id,1,default);
        var result=await store.ReportListAsync(new(Sort:"InDate",ColumnFilters:"{\"InDate\":\"2026-11-16\",\"DueDate\":\"2026-11-20\",\"DigitizationState\":\"Sin información\"}"),default);
        var item=Assert.Single(result.Items);Assert.Equal(new DateOnly(2026,11,16),item.Execution.InDate);Assert.Equal(new DateOnly(2026,11,20),item.Execution.DueDate);Assert.Equal("Sin información",item.Execution.DigitizationState);
        Assert.Single((await store.ReportListAsync(new(Test:"Unknown original"),default)).Items);
        Assert.Empty((await store.ReportListAsync(new(ColumnFilters:"{\"DigitizationState\":\"Pendiente\"}"),default)).Items);
        await Assert.ThrowsAsync<PlanningValidationException>(()=>store.ReportListAsync(new(ColumnFilters:"{\"InDate\":\"bad\"}"),default));
    }
}

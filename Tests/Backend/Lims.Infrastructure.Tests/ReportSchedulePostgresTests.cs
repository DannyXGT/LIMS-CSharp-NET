using Lims.Application.Planning;
using Lims.Contracts.Planning;

namespace Lims.Infrastructure.Tests;

public sealed partial class PlanningPostgresTests
{
    private static ReportDigitization Digitization(IEnumerable<ReportListItem> rows,Guid? session=null)=>new(session??Guid.NewGuid(),rows.Select(x=>new ReportExecutionVersion(x.Execution.ExecutionId!.Value,x.Version)).ToArray(),new(2026,11,20,14,54,0,TimeSpan.FromHours(-6)),"Confirmación de fixture");
    private static ReportScheduleReview Review(ReportScheduleDetail detail)=>new(detail.Schedule.Id,detail.Schedule.Version,detail.Members.Select(m=>new ReportExecutionVersion(m.Item.Execution.ExecutionId!.Value,m.Item.Version)).ToArray(),"Revisión de fixture");
    [PlanningDatabaseFact]
    public async Task SessionsSeparateSameClockAndRetryAndReviewPersistWithConsultationAndHistory()
    {
        await using var db=await TestDatabase.Create();var store=db.Store;
        await store.SaveEquivalenceAsync(new("Technical A","PH","CHEMICAL",false,null,"Fixture"),1,default);
        await store.SaveEquivalenceAsync(new("Technical B","PH","CHEMICAL",false,null,"Fixture"),1,default);
        var p=await store.PreviewAsync(Input(Summary(),Report(test:"Technical A"),Report(3,"Technical B")),1,default);await store.ConfirmAsync(p.Id,1,default);
        var rows=(await store.ReportListAsync(new(),default)).Items;Assert.Equal(2,rows.Count);
        var request=Digitization([rows[0]]);await store.DigitizeReportAsync(request,1,false,default);await store.DigitizeReportAsync(request,1,false,default);
        Assert.Single((await store.ReportListAsync(new(),default)).Items);
        Assert.Equal(rows[1].Execution.ExecutionId,(await store.ReportListAsync(new(),default)).Items[0].Execution.ExecutionId);
        await store.DigitizeReportAsync(Digitization([rows[1]]),1,false,default);
        var schedules=await store.ReportSchedulesAsync(new(),default);Assert.Equal(2,schedules.Total);Assert.Single(schedules.Items.Select(x=>x.OperationalAt).Distinct());
        var d=await store.ReportScheduleAsync(request.SessionId,default);Assert.Single(d.Members);Assert.Single(d.History);Assert.Equal("Pendiente",d.Schedule.State);
        // A second server-derived actor reviews; the typist stays independent.
        await db.Sql("INSERT INTO planning.actors(id,nombre) VALUES(2,'Revisor de fixture')");
        await store.ReviewReportScheduleAsync(Review(d),2,default);
        var reread=await new Lims.Infrastructure.Planning.PostgresPlanningStore(db._database).ReportScheduleAsync(request.SessionId,default);
        Assert.Equal("Chequeado",reread.Schedule.State);Assert.Equal("Revisor de fixture",reread.Schedule.ReviewedBy);Assert.NotNull(reread.Schedule.ReviewedAt);
        Assert.Equal(2,reread.History.Count);var q=await store.QueryAsync(new(),default);var marked=q.Items.Single(x=>x.ExecutionId==rows[0].Execution.ExecutionId);
        Assert.Equal("Chequeado",marked.ScheduleReviewState);Assert.Equal("Revisor de fixture",marked.ScheduleReviewedBy);Assert.NotEqual(marked.DigitizedBy,marked.ScheduleReviewedBy);
        Assert.Single((await store.QueryAsync(new(ColumnFilters:"{\"ScheduleReviewState\":\"Chequeado\"}",Sort:"scheduleReviewedAt"),default)).Items);
        Assert.Contains("Revisor de fixture",System.Text.Encoding.UTF8.GetString(await store.ExportAsync(new(),default)));
        Assert.Contains(rows[0].Execution.OriginalTest!,System.Text.Encoding.UTF8.GetString(await store.ExportReviewedSchedulesAsync(new(),default)));
        await Assert.ThrowsAsync<PlanningConflictException>(()=>store.DigitizeReportAsync(request with{Reason="Otra solicitud"},1,false,default));
        Assert.Equal(2L,await db.Scalar("SELECT count(*) FROM planning.report_sessions"));
    }
    [PlanningDatabaseFact]
    public async Task GroupReviewRejectsIncompleteMembershipAndCorrectionInvalidatesWholeReviewedGroup()
    {
        await using var db=await TestDatabase.Create();var store=db.Store;
        await store.SaveEquivalenceAsync(new("PH","PH","CHEMICAL",false,null,"Fixture"),1,default);
        var p=await store.PreviewAsync(Input(Summary(),Report(test:"PH"),Report(3,"Another")),1,default);await store.ConfirmAsync(p.Id,1,default);
        await store.SaveEquivalenceAsync(new("Another","CROCKING","PHYSICAL",false,null,"Fixture"),1,default);
        var rows=(await store.ReportListAsync(new(),default)).Items;var request=Digitization(rows);await store.DigitizeReportAsync(request,1,false,default);
        var detail=await store.ReportScheduleAsync(request.SessionId,default);var review=Review(detail);
        await Assert.ThrowsAsync<PlanningConflictException>(()=>store.ReviewReportScheduleAsync(review with{Executions=review.Executions.Take(1).ToArray()},1,default));
        await Assert.ThrowsAsync<PlanningConflictException>(()=>store.ReviewReportScheduleAsync(review with{Executions=[review.Executions[0] with{Version=999},review.Executions[1]]},1,default));
        await store.ReviewReportScheduleAsync(review,1,default);
        var member=detail.Members[0].Item;await store.CheckReportAsync(new([new(member.Execution.ExecutionId!.Value,member.Version)],false,"Corrección de fixture"),1,false,default);
        var changed=await store.ReportScheduleAsync(request.SessionId,default);Assert.Equal("Revisar excepciones",changed.Schedule.State);Assert.Equal(1,changed.Schedule.Exceptions);
        Assert.Single(changed.Members,m=>m.Changed);Assert.NotNull(changed.Schedule.ReviewedAt);
        Assert.Empty((await store.ReportSchedulesAsync(new(State:"Reviewed"),default)).Items);
        Assert.DoesNotContain("Technical",System.Text.Encoding.UTF8.GetString(await store.ExportReviewedSchedulesAsync(new(),default)));
        await Assert.ThrowsAsync<PlanningConflictException>(()=>store.ReviewReportScheduleAsync(Review(changed),1,default));
        Assert.Empty((await store.ReportClosuresAsync(new(),default)).Items);
        var other=(await store.QueryAsync(new(),default)).Items.Single(x=>x.Complete);Assert.Equal("Revisar excepciones",other.ScheduleReviewState);
    }
    [PlanningDatabaseFact]
    public async Task AtomicDigitizationAndCompetingUsersHaveOneWinnerAndLegacyDataIsUntouched()
    {
        await using var db=await TestDatabase.Create();var store=db.Store;
        await store.SaveEquivalenceAsync(new("PH","PH","CHEMICAL",false,null,"Fixture"),1,default);
        var p=await store.PreviewAsync(Input(Summary(),Report(test:"PH"),Report(3,"Another")),1,default);await store.ConfirmAsync(p.Id,1,default);
        await store.SaveEquivalenceAsync(new("Another","CROCKING","PHYSICAL",false,null,"Fixture"),1,default);
        var rows=(await store.ReportListAsync(new(),default)).Items;var invalid=Digitization(rows);invalid=invalid with{Executions=[invalid.Executions[0],invalid.Executions[1] with{Version=999}]};
        await Assert.ThrowsAsync<PlanningConflictException>(()=>store.DigitizeReportAsync(invalid,1,false,default));
        Assert.Equal(0L,await db.Scalar("SELECT count(*) FROM planning.report_sessions"));Assert.Equal(2,(await store.ReportListAsync(new(),default)).Total);
        var start=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<Exception?> Save(){await start.Task;try{await store.DigitizeReportAsync(Digitization([rows[0]]),1,false,default);return null;}catch(Exception e){return e;}}
        var a=Save();var b=Save();start.SetResult();var result=await Task.WhenAll(a,b);Assert.Single(result,e=>e is null);Assert.IsType<PlanningConflictException>(Assert.Single(result,e=>e is not null));
        Assert.Equal(1L,await db.Scalar("SELECT count(*) FROM planning.report_sessions"));
        // Simulate an installation predating sessions, without converting its real block or clock.
        await db.Sql("UPDATE planning.executions SET digitized_at='2026-10-12T16:30:00Z',tested_on='2026-10-12',digitized_by='Usuario histórico' WHERE digitized_at IS NULL; INSERT INTO planning.report_checks(execution_id,block_number) SELECT id,4321 FROM planning.executions WHERE digitized_by='Usuario histórico' ON CONFLICT(execution_id) DO UPDATE SET block_number=4321");
        var legacy=(await store.ReportListAsync(new(View:"All"),default)).Items.Single(x=>x.Execution.DigitizedBy=="Usuario histórico");Assert.Equal(4321,legacy.BlockNumber);Assert.Equal("10:30:00",legacy.CheckedTimeText);Assert.Null(legacy.Execution.DigitizationSessionId);Assert.Equal(1,(await store.ReportSchedulesAsync(new(),default)).Total);
    }
    [PlanningDatabaseFact]
    public async Task ClosuresUseAllEpisodesAndUnresolvedSourceRowsRegardlessOfSearchSubset()
    {
        await using var db=await TestDatabase.Create();var store=db.Store;
        await store.SaveEquivalenceAsync(new("PH","PH","CHEMICAL",false,null,"Fixture"),1,default);
        var p=await store.PreviewAsync(Input(Summary(),Report(test:"PH")),1,default);await store.ConfirmAsync(p.Id,1,default);
        var rows=(await store.ReportListAsync(new(),default)).Items;await store.DigitizeReportAsync(Digitization(rows),1,false,default);
        // Existing fixture incidents can independently prevent closure; resolve only the fixture's issues.
        await db.Sql("UPDATE planning.incidents SET state='Resolved'");
        Assert.Single((await store.ReportClosuresAsync(new(Search:"J1"),default)).Items);
        await db.Sql("INSERT INTO planning.episodes(id,job,source_in_date) VALUES('11111111-1111-1111-1111-111111111111','J1','2026-11-19'); INSERT INTO planning.executions(id,episode_id,original_test,test_key,equivalence,laboratory,origin) VALUES(gen_random_uuid(),'11111111-1111-1111-1111-111111111111','PH','PH','PH','CHEMICAL','Manual')");
        Assert.Empty((await store.ReportClosuresAsync(new(Search:"J1"),default)).Items);
    }
}

using System.Text;
using Lims.Application.Planning;
using Lims.Contracts.Planning;
using Lims.Infrastructure.Planning;

namespace Lims.Infrastructure.Tests;

public sealed partial class PlanningPostgresTests
{
    private static async Task<ReportClosure> ReadyClosure(TestDatabase db)
    {
        await db.Store.SaveEquivalenceAsync(new("Technical A","PH","CHEMICAL",false,null,"Fixture"),1,default);
        await db.Store.SaveEquivalenceAsync(new("Technical B","PH","CHEMICAL",false,null,"Fixture"),1,default);
        var p=await db.Store.PreviewAsync(Input(Summary(),Report(test:"Technical A"),Report(3,"Technical B")),1,default);
        await db.Store.ConfirmAsync(p.Id,1,default);
        await db.Sql("UPDATE planning.incidents SET state='Resolved'");
        var rows=(await db.Store.ReportListAsync(new(),default)).Items;
        await db.Store.DigitizeReportAsync(Digitization(rows),1,false,default);
        return Assert.Single((await db.Store.ClosureWorkspaceAsync(new(),default)).Items);
    }
    private static ReportClosureFollowUp Follow(ReportClosure row,string state="Seen")=>new(Guid.NewGuid(),row.Job,row.Fingerprint,row.FollowUpVersion,state,"Responsable de fixture","Nota de fixture",new(2026,11,25),"Seguimiento de fixture");
    [PlanningDatabaseFact]
    public async Task ClosureWorkspacePreservesRepeatedExecutionsAndIndependentReviewAndSharesDossierAudit()
    {
        await using var db=await TestDatabase.Create();var row=await ReadyClosure(db);
        await db.Sql("UPDATE planning.episodes SET usd=190.40,service='Regular (After cut-off)'");
        row=Assert.Single((await db.Store.ClosureWorkspaceAsync(new(),default)).Items);
        Assert.Equal(190.40m,row.Usd);Assert.Equal("Regular (After cut-off)",row.Service);
        Assert.Equal(2,row.Required);Assert.Equal(0,row.Reviewed);Assert.Equal("PH",row.LastTest);Assert.Equal("CHEMICAL",row.Laboratories);
        var detail=await db.Store.ClosureDetailAsync(row.Job,row.Fingerprint,default);
        Assert.Equal(2,detail.Tests.Count);Assert.Equal(2,detail.Tests.Select(t=>t.ExecutionId).Distinct().Count());
        Assert.All(detail.Tests,t=>Assert.Equal("PH",t.PlanningTest));
        var saved=await db.Store.SaveClosureFollowUpAsync(Follow(row),1,default);
        Assert.Equal("Seen",saved.State);Assert.Equal(1,saved.FollowUpVersion);Assert.Equal("Actor sintético local",saved.UpdatedBy);
        Assert.Equal(0,saved.Reviewed);Assert.Equal(2,(await db.Store.ClosureDetailAsync(row.Job,row.Fingerprint,default)).Tests.Count);
        Assert.Contains((await db.Store.DossierAsync(row.EpisodeIds[0],default))!.History,h=>h.Action=="ReportClosureFollowUp"&&h.Details.Contains("Nota de fixture",StringComparison.Ordinal));
        var schedule=Assert.Single((await db.Store.ReportSchedulesAsync(new(),default)).Items);
        await db.Store.ReviewReportScheduleAsync(Review(await db.Store.ReportScheduleAsync(schedule.Id,default)),1,default);
        var reviewed=Assert.Single((await db.Store.ClosureWorkspaceAsync(new(),default)).Items);
        Assert.Equal(2,reviewed.Reviewed);Assert.Equal(saved.Fingerprint,reviewed.Fingerprint);Assert.Equal("Seen",reviewed.State);
        var restart=new PostgresPlanningStore(db._database);Assert.Equal(saved.Notes,Assert.Single((await restart.ClosureWorkspaceAsync(new(),default)).Items).Notes);
    }
    [PlanningDatabaseFact]
    public async Task AttendedClosureLeavesQueueButCorrectionAndRedigitizationCreateNewAttentionWithoutLosingHistory()
    {
        await using var db=await TestDatabase.Create();var row=await ReadyClosure(db);
        await db.Store.SaveClosureFollowUpAsync(Follow(row,"Archived"),1,default);
        Assert.Empty((await db.Store.ClosureWorkspaceAsync(new(),default)).Items);
        Assert.Equal("Archived",Assert.Single((await db.Store.ReportClosuresAsync(new(),default)).Items).State);
        var archived=Assert.Single((await db.Store.ClosureWorkspaceAsync(new(Scope:"History"),default)).Items);Assert.True(archived.Current);
        var item=(await db.Store.ReportListAsync(new(View:"All"),default)).Items[0];
        await db.Store.CheckReportAsync(new([new(item.Execution.ExecutionId!.Value,item.Version)],false,"Corrección de fixture"),1,false,default);
        Assert.Empty((await db.Store.ClosureWorkspaceAsync(new(),default)).Items);
        Assert.False(Assert.Single((await db.Store.ClosureWorkspaceAsync(new(Scope:"History"),default)).Items).Current);
        Assert.False((await db.Store.ClosureDetailAsync(row.Job,row.Fingerprint,default)).Closure.Current);
        await Assert.ThrowsAsync<PlanningConflictException>(()=>db.Store.SaveClosureFollowUpAsync(Follow(archived),1,default));
        await db.Store.DigitizeReportAsync(Digitization((await db.Store.ReportListAsync(new(),default)).Items),1,false,default);
        var ready=Assert.Single((await db.Store.ClosureWorkspaceAsync(new(),default)).Items);
        Assert.NotEqual(row.Fingerprint,ready.Fingerprint);Assert.Equal("Pending",ready.State);Assert.Equal(0,ready.FollowUpVersion);
        Assert.Single((await db.Store.ClosureWorkspaceAsync(new(Scope:"History"),default)).Items);
    }
    [PlanningDatabaseFact]
    public async Task ClosureFollowUpRetriesAreIdempotentAndCompetingUsersCannotOverwriteEachOther()
    {
        await using var db=await TestDatabase.Create();var row=await ReadyClosure(db);var request=Follow(row);
        var a=await db.Store.SaveClosureFollowUpAsync(request,1,default);var b=await db.Store.SaveClosureFollowUpAsync(request,1,default);
        Assert.Equal(a.FollowUpVersion,b.FollowUpVersion);Assert.Equal(a.UpdatedAt,b.UpdatedAt);Assert.Equal(a.Notes,b.Notes);Assert.Single((await db.Store.ClosureDetailAsync(row.Job,row.Fingerprint,default)).History);
        await Assert.ThrowsAsync<PlanningConflictException>(()=>db.Store.SaveClosureFollowUpAsync(request with{Notes="Otro texto"},1,default));
        await Assert.ThrowsAsync<PlanningConflictException>(()=>db.Store.SaveClosureFollowUpAsync(Follow(row),1,default));
        await db.Sql("INSERT INTO planning.actors(id,nombre) VALUES(2,'Segundo actor de fixture')");
        var start=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<Exception?> Save(int actor){await start.Task;try{await db.Store.SaveClosureFollowUpAsync(Follow(a) with{Responsible="Actor "+actor},actor,default);return null;}catch(Exception e){return e;}}
        var left=Save(1);var right=Save(2);start.SetResult();var outcomes=await Task.WhenAll(left,right);
        Assert.Single(outcomes,e=>e is null);Assert.IsType<PlanningConflictException>(Assert.Single(outcomes,e=>e is not null));
        Assert.Equal(2,(await db.Store.ClosureDetailAsync(row.Job,row.Fingerprint,default)).History.Count);
    }
    [PlanningDatabaseFact]
    public async Task ClosureFiltersAndFullExportUseSameScopeAndProtectSpreadsheetCells()
    {
        await using var db=await TestDatabase.Create();var row=await ReadyClosure(db);
        await db.Store.SaveClosureFollowUpAsync(Follow(row) with{Responsible="  =HYPERLINK(1)",Notes="Coma, comilla \" y\nsalto"},1,default);
        var query=new ReportClosureQuery(Search:"HYPERLINK",Laboratory:"chemical",From:new(2026,11,20),Until:new(2026,11,20),State:"Seen",Sort:"Due",PageSize:1);
        var page=await db.Store.ClosureWorkspaceAsync(query,default);Assert.Single(page.Items);
        Assert.Single(page.Facets);Assert.Equal("14:54",page.Facets[0].Time);
        Assert.Single((await db.Store.ClosureWorkspaceAsync(query with{Dates="2026-11-20",Times="14:54"},default)).Items);
        Assert.Empty((await db.Store.ClosureWorkspaceAsync(query with{Dates="None"},default)).Items);
        Assert.Empty((await db.Store.ClosureWorkspaceAsync(query with{Times="15:54"},default)).Items);
        await Assert.ThrowsAsync<PlanningValidationException>(()=>db.Store.ClosureWorkspaceAsync(query with{Times="25:00"},default));
        var csv=Encoding.UTF8.GetString(await db.Store.ExportClosuresAsync(query,default));Assert.Contains("'=HYPERLINK(1)",csv);Assert.Contains("Coma, comilla \"\" y\nsalto",csv);Assert.Contains("En seguimiento",csv);
        var testCsv=Encoding.UTF8.GetString(await db.Store.ExportClosuresAsync(query with{IncludeTests=true},default));
        Assert.Contains("Technical A",testCsv);Assert.Contains("Technical B",testCsv);Assert.Equal(4,testCsv.Split('\n').Length);
        Assert.Empty((await db.Store.ClosureWorkspaceAsync(query with{Until=new(2026,11,21),From=new(2026,11,21)},default)).Items);
        Assert.Empty((await db.Store.ClosureWorkspaceAsync(query with{Laboratory="PHYSICAL"},default)).Items);
        await Assert.ThrowsAsync<PlanningValidationException>(()=>db.Store.ClosureWorkspaceAsync(query with{Scope="Invalid"},default));
        await Assert.ThrowsAsync<PlanningValidationException>(()=>db.Store.ClosureWorkspaceAsync(query with{From=new(2026,11,25)},default));
        await Assert.ThrowsAsync<PlanningValidationException>(()=>db.Store.SaveClosureFollowUpAsync(Follow(row) with{Reason=""},1,default));
        await Assert.ThrowsAsync<PlanningConflictException>(()=>db.Store.ExportClosuresAsync(query with{Revision=page.Revision-1},default));
    }
    [PlanningDatabaseFact]
    public async Task ClosureHoldIncidentAndUnreconciledIdentityBlockStaleAttention()
    {
        await using var db=await TestDatabase.Create();var row=await ReadyClosure(db);
        await db.Sql("INSERT INTO planning.job_controls(job,on_hold,hold_reason,updated_by) VALUES('J1',true,'Retención fixture',1)");
        Assert.Empty((await db.Store.ClosureWorkspaceAsync(new(),default)).Items);
        await Assert.ThrowsAsync<PlanningConflictException>(()=>db.Store.SaveClosureFollowUpAsync(Follow(row),1,default));
        await db.Sql("UPDATE planning.job_controls SET on_hold=false; UPDATE planning.incidents SET state='Pending'");
        Assert.Empty((await db.Store.ClosureWorkspaceAsync(new(),default)).Items);
        await db.Sql("UPDATE planning.incidents SET state='Resolved'; UPDATE planning.source_rows SET execution_id=NULL WHERE file='Report List'");
        Assert.Empty((await db.Store.ClosureWorkspaceAsync(new(),default)).Items);
    }
    [PlanningDatabaseFact]
    public async Task ClosurePagingCountsAndExportIncludeAllMatchingJobsAndUseStableRevision()
    {
        await using var db=await TestDatabase.Create();await ReadyClosure(db);
        await db.Sql("""
            INSERT INTO planning.episodes(id,job,source_in_date,due_date,buyer)
              SELECT gen_random_uuid(),'J'||n,'2026-11-20'::date,'2026-11-26'::date,'Cliente fixture' FROM generate_series(2,4) n;
            INSERT INTO planning.executions(id,episode_id,original_test,test_key,equivalence,laboratory,origin,digitized_at,tested_on)
              SELECT gen_random_uuid(),id,'Technical','Technical','PH','CHEMICAL','Manual','2026-11-20T20:54:00Z','2026-11-20' FROM planning.episodes WHERE job<>'J1';
            """);
        var query=new ReportClosureQuery(Sort:"Job",PageSize:2);var first=await db.Store.ClosureWorkspaceAsync(query,default);
        Assert.Equal(4,first.Total);Assert.Collection(first.Items,c=>Assert.Equal("J1",c.Job),c=>Assert.Equal("J2",c.Job));
        var second=await db.Store.ClosureWorkspaceAsync(query with{Page=2,Revision=first.Revision},default);
        Assert.Collection(second.Items,c=>Assert.Equal("J3",c.Job),c=>Assert.Equal("J4",c.Job));
        var csv=Encoding.UTF8.GetString(await db.Store.ExportClosuresAsync(query,default));Assert.Contains("\"J4\"",csv);
        await db.Store.SaveClosureFollowUpAsync(Follow(first.Items[0],"Archived"),1,default);
        await Assert.ThrowsAsync<PlanningConflictException>(()=>db.Store.ClosureWorkspaceAsync(query with{Page=2,Revision=first.Revision},default));
        Assert.Equal(3,(await db.Store.ClosureWorkspaceAsync(new(),default)).Total);
        Assert.Single((await db.Store.ClosureWorkspaceAsync(new(Scope:"History"),default)).Items);
    }
}

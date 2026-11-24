using System.Text;
using Lims.Contracts.Planning;

namespace Lims.Infrastructure.Tests;

public sealed partial class PlanningPostgresTests
{
    [PlanningDatabaseFact]
    public async Task ReportListExcludesUnclassifiedAndRevisionJobsBeforePagingWithoutDeletingExecutions()
    {
        await using var db=await TestDatabase.Create();var store=db.Store;
        await store.SaveEquivalenceAsync(new("Technical A","PH","Chemical",false,null,"Clasificación de fixture"),1,default);
        await store.SaveEquivalenceAsync(new("Technical B","PH","Chemical",false,null,"Clasificación de fixture"),1,default);
        var preview=await store.PreviewAsync(Input(Summary(),Report(test:"Technical A"),Report(3,"Technical B"),Report(4,"Unclassified")),1,default);
        await store.ConfirmAsync(preview.Id,1,default);
        await db.Sql("""
            INSERT INTO planning.episodes(id,job,in_date,due_date)
              SELECT gen_random_uuid(),job,CURRENT_DATE,CURRENT_DATE FROM unnest(ARRAY['GUAT26009379-REV1','GUAT26009379-S','GUAT26009379-1','GUAT26009379rEv2']) job;
            INSERT INTO planning.executions(id,episode_id,original_test,test_key,equivalence,laboratory,origin)
              SELECT gen_random_uuid(),e.id,'Technical A','TECHNICAL A','PH','Chemical','ReportList'
              FROM planning.episodes e WHERE e.job<>'J1';
            UPDATE planning.executions x SET digitized_at=clock_timestamp(),tested_on=CURRENT_DATE
              FROM planning.episodes e WHERE e.id=x.episode_id AND e.job<>'J1';
            INSERT INTO planning.executions(id,episode_id,original_test,test_key,equivalence,laboratory,origin)
              SELECT gen_random_uuid(),e.id,label,upper(label),equivalence,'Chemical','ReportList'
              FROM planning.episodes e CROSS JOIN (VALUES('Blank classification','   '),('Placeholder classification','Sin equivalencia')) v(label,equivalence)
              WHERE e.job='J1';
            """);
        var ids=new HashSet<Guid>();
        for(var page=1;page<=2;page++)
        {
            var result=await store.ReportListAsync(new(Page:page,PageSize:1,Sort:"Job"),default);
            Assert.Equal(2,result.Total);var item=Assert.Single(result.Items);
            Assert.Equal("J1",item.Execution.Job);Assert.Equal("PH",item.Execution.PlanningTest);Assert.True(ids.Add(item.Execution.ExecutionId!.Value));
        }
        Assert.Empty((await store.ReportListAsync(new(Page:3,PageSize:1),default)).Items);
        Assert.Equal(0,(await store.ReportClosuresAsync(new(),default)).Total);
        foreach(var search in new[]{"GUAT26009379","GUAT26009379-REV1","GUAT26009379rEv2","Unclassified","Blank classification","Placeholder classification"})
            Assert.Equal(0,(await store.ReportListAsync(new(Search:search,View:"All"),default)).Total);
        var rows=(await store.ReportListAsync(new(),default)).Items;
        await store.DigitizeReportAsync(new(Guid.NewGuid(),rows.Select(r=>new ReportExecutionVersion(r.Execution.ExecutionId!.Value,r.Version)).ToArray(),new(2026,11,24,15,30,0,TimeSpan.FromHours(-6)),"Lote de fixture"),1,false,default);
        Assert.Equal(0,(await store.ReportListAsync(new(),default)).Total);
        foreach(var view in new[]{"Checked","Archived","All"})Assert.Equal(2,(await store.ReportListAsync(new(View:view),default)).Total);
        var dossier=(await store.DossierAsync(rows[0].Execution.EpisodeId,default))!;
        Assert.Equal(5,dossier.Tests.Count);Assert.Equal(2,dossier.Tests.Count(t=>t.Complete));
        Assert.Equal(9L,await db.Scalar("SELECT count(*) FROM planning.executions"));
        Assert.Single((await store.ReviewWorkspaceAsync(new(Category:"MissingEquivalence"),default)).Items);
    }

    [PlanningDatabaseFact]
    public async Task ConsultationExcludesHyphenAndRevJobsFromBothScopesCountsSearchAndExportButPreservesDossiers()
    {
        await using var db=await TestDatabase.Create();var store=db.Store;
        var jobs=new[]{"GUAT26011720","GUAT26009379-REV1","GUAT26009379-S","GUAT26009379-1","GUAT26009379rEv2"};
        var preview=await store.PreviewAsync(Input(jobs.Select((j,i)=>Summary() with{Job=j,Row=i+2}).ToArray()),1,default);await store.ConfirmAsync(preview.Id,1,default);
        await db.Sql("UPDATE planning.episodes SET due_date=CURRENT_DATE");
        var before=await db.Scalar("SELECT count(*) FROM planning.history");
        foreach(var scope in new[]{"History","Operational"})
        {
            var page=await store.QueryAsync(new(Scope:scope,PageSize:1),default);
            Assert.Equal(1,page.Total);Assert.Equal(1,page.Counts!.Jobs);Assert.Equal(jobs[0],Assert.Single(page.Items).Job);
            Assert.Empty((await store.QueryAsync(new(Scope:scope,Page:2,PageSize:1),default)).Items);
            var csv=Encoding.UTF8.GetString(await store.ExportAsync(new(Scope:scope),default));Assert.Contains(jobs[0],csv);
            foreach(var job in jobs.Skip(1))
            {
                Assert.Equal(0,(await store.QueryAsync(new(Search:job,SearchMode:"Exact",Scope:scope),default)).Total);
                Assert.DoesNotContain(job,csv);
            }
            Assert.Equal(1,(await store.QueryAsync(new(Search:string.Join(';',jobs),Scope:scope),default)).Total);
        }
        await using var c=await db._database.Source.OpenConnectionAsync();
        await using var cmd=new Npgsql.NpgsqlCommand("SELECT id FROM planning.episodes WHERE job='GUAT26009379-REV1'",c);
        var id=(Guid)(await cmd.ExecuteScalarAsync())!;var dossier=(await store.DossierAsync(id,default))!;
        Assert.Equal("GUAT26009379-REV1",dossier.Job);Assert.NotEmpty(dossier.History);
        Assert.Equal(5L,await db.Scalar("SELECT count(*) FROM planning.episodes"));Assert.Equal(before,await db.Scalar("SELECT count(*) FROM planning.history"));
    }
}

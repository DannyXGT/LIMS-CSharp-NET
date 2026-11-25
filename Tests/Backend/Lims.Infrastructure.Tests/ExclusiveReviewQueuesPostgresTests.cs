using System.Text;
using System.Text.Json;
using Lims.Application.Planning;
using Lims.Contracts.Planning;
using Lims.Infrastructure.Planning;

namespace Lims.Infrastructure.Tests;

public sealed partial class PlanningPostgresTests
{
    private static readonly string[] ExclusiveQueueNames=["RevisionJobs","SummaryWithoutReport","ReportWithoutSummary","MissingVendor","MissingParty","MissingEquivalence","ExecutionIdentity","MissingTests","IncompleteData","Admission","ImportError","Reviewed"];

    private static async Task<IReadOnlyList<PlanningReviewCase>> AssertExclusiveQueues(PostgresPlanningStore store,PlanningReviewQuery? scope=null)
    {
        scope??=new();var jobs=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);var keys=new HashSet<string>();var all=new List<PlanningReviewCase>();
        foreach(var queue in ExclusiveQueueNames)
        {
            var first=await store.ReviewWorkspaceAsync(scope with{Category=queue,Situation="All",Page=1,PageSize=2},default);
            Assert.Equal(first.Total,first.Counters.Single(c=>c.Category==queue).Total);
            for(var page=1;page<=Math.Max(1,(first.Total+1)/2);page++)
            {
                var result=page==1?first:await store.ReviewWorkspaceAsync(scope with{Category=queue,Situation="All",Page=page,PageSize=2},default);
                Assert.Equal(first.Total,result.Total);
                foreach(var item in result.Items)
                {
                    Assert.Equal(queue,item.QueueCategory);Assert.True(keys.Add(item.Key),"Un mismo caso aparece en dos bandejas o páginas");
                    if(item.Category!="ImportError")
                    {
                        if(jobs.TryGetValue(item.Job,out var previous))Assert.Equal(previous,queue);else jobs.Add(item.Job,queue);
                    }
                    all.Add(item);
                }
            }
        }
        return all;
    }

    [PlanningDatabaseFact]
    public async Task RevisionJobsHaveOneQueueEvenWithPartyIdentityAndEquivalenceProblemsAcrossFiltersAndReimport()
    {
        await using var db=await TestDatabase.Create();var store=db.Store;
        var names=new[]{"GUAT26009379-REV1","GUAT26011720-S","GUAT26011720-1","GUAT26011720rEv2","GUAT26011720"};
        var summaries=names.Select((j,i)=>Summary() with{Job=j,Row=i+2}).ToArray();
        var reports=names.Select((j,i)=>Report(i+2,"Unmapped "+i) with{Job=j}).ToArray();
        var input=Input([..summaries,..reports]);var preview=await store.PreviewAsync(input,1,default);await store.ConfirmAsync(preview.Id,1,default);
        var before=await db.Scalar("SELECT count(*) FROM planning.history");
        var all=await AssertExclusiveQueues(store);
        foreach(var name in names.Take(4))
        {
            var group=all.Where(c=>c.Job==name).ToArray();Assert.NotEmpty(group);Assert.All(group,c=>Assert.Equal("RevisionJobs",c.QueueCategory));
            Assert.Single(group,c=>c.Category=="RevisionJobs");Assert.Contains(group,c=>c.Category=="MissingEquivalence");Assert.Contains(group,c=>c.Category=="MissingParty");
        }
        Assert.All(all.Where(c=>c.Job==names[4]),c=>Assert.Equal("MissingParty",c.QueueCategory));
        await AssertExclusiveQueues(store,new(Scope:"Batch",BatchId:preview.Id));
        await AssertExclusiveQueues(store,new(Search:"Unmapped",ColumnFilters:"{\"State\":\"Pendiente\"}"));
        var revision=await store.ReviewWorkspaceAsync(new(Category:"RevisionJobs",Situation:"All",Search:names[0]),default);
        Assert.All(revision.Items,c=>Assert.Equal(names[0],c.Job));
        var csv=Encoding.UTF8.GetString(await store.ExportReviewsAsync(new(Category:"RevisionJobs",Situation:"All",Search:names[0]),default));
        Assert.Equal(revision.Total+1,csv.Split("\r\n",StringSplitOptions.RemoveEmptyEntries).Length);Assert.Contains("\"RevisionJobs\"",csv);
        foreach(var queue in ExclusiveQueueNames.Where(q=>q!="RevisionJobs"))
            Assert.Empty((await store.ReviewWorkspaceAsync(new(Category:queue,Situation:"All",SelectedKeys:JsonSerializer.Serialize(revision.Items.Select(r=>r.Key))),default)).Items);
        Assert.Equal(before,await db.Scalar("SELECT count(*) FROM planning.history"));
        Assert.Equal(5L,await db.Scalar("SELECT count(*) FROM planning.executions"));
        var header=revision.Items.Single(c=>c.Category=="RevisionJobs");
        await store.DecideReviewAsync(new(header.Key,header.Category,header.Version,"Held","Retener revisión de fixture"),1,default);
        var held=Assert.Single((await store.ReviewWorkspaceAsync(new(Category:"RevisionJobs",Situation:"Held",SelectedKeys:JsonSerializer.Serialize(new[]{header.Key})),default)).Items);
        Assert.NotNull(held.DecidedAt);Assert.NotNull(held.DecidedBy);
        await Assert.ThrowsAsync<PlanningConflictException>(()=>store.DecideReviewAsync(new(header.Key,header.Category,header.Version,"Excluded","Versión obsoleta de fixture"),1,default));
        var next=await store.PreviewAsync(input with{Fingerprint=Guid.NewGuid().ToString("N")},1,default);await store.ConfirmAsync(next.Id,1,default);
        var after=await AssertExclusiveQueues(store);
        Assert.Equal(held.Key,Assert.Single(after,c=>c.Key==held.Key).Key);Assert.Equal("Held",after.Single(c=>c.Key==held.Key).State);
        Assert.Equal(5L,await db.Scalar("SELECT count(*) FROM planning.executions"));
    }

    [PlanningDatabaseFact]
    public async Task JobMovesBetweenExclusiveQueuesAsProblemsAreResolvedAndSourceActionsKeepTheirIdentity()
    {
        await using var db=await TestDatabase.Create();var store=db.Store;
        var preview=await store.PreviewAsync(Input(Summary()),1,default);await store.ConfirmAsync(preview.Id,1,default);
        var rows=await AssertExclusiveQueues(store);Assert.All(rows,c=>Assert.Equal("SummaryWithoutReport",c.QueueCategory));
        var summary=Assert.Single(rows,c=>c.Category=="SummaryWithoutReport");
        await store.DecideReviewAsync(new(summary.Key,summary.Category,summary.Version,"Held","Esperar documentación de fixture"),1,default);
        rows=await AssertExclusiveQueues(store);Assert.All(rows,c=>Assert.Equal("MissingTests",c.QueueCategory));
        var retained=Assert.Single(rows,c=>c.Key==summary.Key);Assert.Equal("Held",retained.State);
        // This source case now belongs to MissingTests; its source category still identifies the action.
        await store.DecideReviewAsync(new(retained.Key,retained.Category,retained.Version,"Pending","Reabrir revisión de fixture"),1,default);
        rows=await AssertExclusiveQueues(store);Assert.All(rows,c=>Assert.Equal("SummaryWithoutReport",c.QueueCategory));
        var dossier=(await store.DossierAsync(summary.EpisodeId!.Value,default))!;
        await store.AddManualTestAsync(dossier.EpisodeId,new(dossier.Version,"PH","PH","Chemical","Asignación manual de fixture"),1,default);
        var pending=Assert.Single((await store.DossierAsync(dossier.EpisodeId,default))!.Incidents,c=>c.Kind=="SummaryWithoutReport");
        await store.DecideIncidentAsync(pending.Id,new(pending.Version,"Resolved","Prueba manual verificada en fixture"),1,default);
        rows=await AssertExclusiveQueues(store);Assert.All(rows,c=>Assert.Equal("MissingParty",c.QueueCategory));
        dossier=(await store.DossierAsync(dossier.EpisodeId,default))!;
        await store.EditWipAsync(dossier.EpisodeId,new(dossier.Version,dossier.Wip with{Vendor="Vendor verificado"},"Completar Vendor de fixture"),1,default);
        rows=await AssertExclusiveQueues(store);Assert.All(rows,c=>Assert.Equal("Reviewed",c.QueueCategory));
        Assert.Contains(rows,c=>c.Key==summary.Key&&c.State=="Resolved");
        Assert.Single((await store.DossierAsync(dossier.EpisodeId,default))!.Tests);
    }

    [PlanningDatabaseFact]
    public async Task ReportOnlyRevisionUsesItsActualSourceAndFileErrorsStayInTheirOwnQueue()
    {
        await using var db=await TestDatabase.Create();var store=db.Store;
        var preview=await store.PreviewAsync(Input(Report(test:"Source test") with{Job="GUAT26011720-S"}),1,default);await store.ConfirmAsync(preview.Id,1,default);
        await db.Sql("""
            UPDATE planning.batches SET preview=jsonb_set(preview,'{issues}',
              '[{"file":"report-revision.xlsx","blocking":true,"row":2,"message":"Fallo de archivo de fixture"}]'::jsonb);
            """);
        var rows=await AssertExclusiveQueues(store);
        var revision=Assert.Single(rows,c=>c.Category=="RevisionJobs");Assert.Equal("ReportList",revision.Origin);
        Assert.Equal("RevisionJobs",revision.QueueCategory);Assert.NotNull(revision.EpisodeId);
        var error=Assert.Single(rows,c=>c.Category=="ImportError");Assert.Equal("ImportError",error.QueueCategory);
        Assert.Contains((await store.ReviewWorkspaceAsync(new(Category:"RevisionJobs",Origin:"ReportList"),default)).Items,c=>c.Key==revision.Key);
        Assert.Single((await store.DossierAsync(revision.EpisodeId!.Value,default))!.Tests);
    }

    [PlanningDatabaseFact]
    public async Task RepeatedExecutionsAndUnlinkedSourceRowsKeepIndividualIdentityInsideTheirOnlyQueue()
    {
        await using var db=await TestDatabase.Create();var store=db.Store;
        var input=Input(Summary() with{Job="GUAT26011720-REV1"},Report(test:"Repeated") with{Job="GUAT26011720-REV1"},Report(3,"Repeated") with{Job="GUAT26011720-REV1"});
        var preview=await store.PreviewAsync(input,1,default);Assert.Equal(2,preview.AmbiguousRows);await store.ConfirmAsync(preview.Id,1,default);
        var cases=await AssertExclusiveQueues(store);
        var identities=cases.Where(c=>c.Category=="ExecutionIdentity").ToArray();Assert.Equal(2,identities.Length);
        Assert.Equal(2,identities.Select(c=>c.Key).Distinct().Count());Assert.All(cases,c=>Assert.Equal("RevisionJobs",c.QueueCategory));
        var episode=cases.First(c=>c.EpisodeId is not null).EpisodeId!.Value;
        foreach(var source in new[]{2,3})
        {
            var dossier=(await store.DossierAsync(episode,default))!;
            await store.ResolveSourceAsync(new(preview.Id,source,episode,null,true,dossier.Version,"Dos pruebas físicas verificadas en fixture"),1,default);
        }
        var saved=(await store.DossierAsync(episode,default))!;Assert.Equal(2,saved.Tests.Count);Assert.Equal(2,saved.Tests.Select(t=>t.ExecutionId).Distinct().Count());
        await AssertExclusiveQueues(store);
    }
}

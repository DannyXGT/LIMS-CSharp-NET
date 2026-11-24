using Lims.Application.Planning;
using Lims.Contracts.Planning;
using Lims.Infrastructure.Planning;
using Npgsql;

namespace Lims.Infrastructure.Tests;

public sealed partial class PlanningPostgresTests
{
    [PlanningDatabaseFact]
    public async Task OperationalClockIsPreservedWhileAuditAndBlockUseServerSaveTime()
    {
        await using var db=await TestDatabase.Create();var store=db.Store;
        await store.SaveEquivalenceAsync(new("PH","PH","CHEMICAL",false,null,"Fixture"),1,default);
        await store.SaveEquivalenceAsync(new("Another","CROCKING","PHYSICAL",false,null,"Fixture"),1,default);
        var p=await store.PreviewAsync(Input(Summary(),Report(test:"PH"),Report(3,"Another")),1,default);await store.ConfirmAsync(p.Id,1,default);
        var rows=(await store.ReportListAsync(new(),default)).Items;
        var at=new DateTimeOffset(2026,10,12,14,35,0,TimeSpan.FromHours(-6));
        foreach(var item in rows)await store.CheckReportAsync(new([new(item.Execution.ExecutionId!.Value,item.Version)],true,"Fecha operativa de fixture",CheckedAt:at),1,false,default);
        var checkedRows=(await store.ReportListAsync(new(View:"Checked"),default)).Items;
        Assert.All(checkedRows,item=>{Assert.Equal(at,item.Execution.DigitizedAt);Assert.Equal("2026-10-12",item.CheckedDateText);Assert.Equal("14:35:00",item.CheckedTimeText);});
        Assert.Single(checkedRows.Select(item=>item.BlockNumber).Distinct());
        Assert.Equal(2L,await db.Scalar("SELECT count(*) FROM planning.history WHERE action='ReportMarked' AND at>now()-interval '1 minute'"));
        Assert.Equal(1L,await db.Scalar("SELECT count(*) FROM planning.report_blocks WHERE last_checked_at>now()-interval '1 minute'"));
    }
    [PlanningDatabaseFact]
    public async Task SimultaneousMarksOfSameVersionHaveOneWinnerAndOneAudit()
    {
        await using var db=await TestDatabase.Create();var store=db.Store;
        await store.SaveEquivalenceAsync(new("PH","PH","CHEMICAL",false,null,"Fixture"),1,default);
        var p=await store.PreviewAsync(Input(Summary(),Report(test:"PH")),1,default);await store.ConfirmAsync(p.Id,1,default);
        var item=Assert.Single((await store.ReportListAsync(new(),default)).Items);
        var start=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<Exception?> Mark(){await start.Task;try{await store.CheckReportAsync(new([new(item.Execution.ExecutionId!.Value,item.Version)],true,"Fixture simultáneo"),1,false,default);return null;}catch(Exception ex){return ex;}}
        var a=Mark();var b=Mark();start.SetResult();var results=await Task.WhenAll(a,b);
        Assert.Single(results,x=>x is null);Assert.IsType<PlanningConflictException>(Assert.Single(results,x=>x is not null));
        Assert.Equal(1L,await db.Scalar("SELECT count(*) FROM planning.history WHERE action='ReportMarked'"));
        Assert.Single((await store.ReportListAsync(new(View:"Checked"),default)).Items);
    }

    [PlanningDatabaseFact]
    public async Task ThousandsOfRowsContinuePastOneThousandWithServerSortFiltersAndRevisionGuard()
    {
        await using var db=await TestDatabase.Create();var store=db.Store;
        var p=await store.PreviewAsync(Input(Summary()),1,default);await store.ConfirmAsync(p.Id,1,default);
        await db.Sql("""
            INSERT INTO planning.executions(id,episode_id,original_test,test_key,equivalence,laboratory,origin)
            SELECT gen_random_uuid(),e.id,'Technical '||n,'TECHNICAL '||n,'PH','CHEMICAL','ReportList'
            FROM planning.episodes e CROSS JOIN generate_series(1,2503) n;
            """);
        var ids=new HashSet<Guid>();long? revision=null;
        for(int page=1;page<=6;page++)
        {
            var result=await store.ReportListAsync(new(Page:page,PageSize:500,Sort:"Job",Revision:revision),default);
            revision=result.Revision;Assert.Equal(2503,result.Total);
            foreach(var item in result.Items)Assert.True(ids.Add(item.Execution.ExecutionId!.Value));
        }
        Assert.Equal(2503,ids.Count);
        Assert.Equal(2503,(await store.ReportListAsync(new(Laboratory:"chem",Test:"PH",Buyer:"buy"),default)).Total);
        Assert.Equal(0,(await store.ReportListAsync(new(VerifiedBy:"unknown"),default)).Total);
        await Assert.ThrowsAsync<PlanningValidationException>(()=>store.ReportListAsync(new(Sort:"e.job; DELETE"),default));
        var selected=(await store.ReportListAsync(new(PageSize:1),default)).Items[0];
        await store.CheckReportAsync(new([new(selected.Execution.ExecutionId!.Value,selected.Version)],true,"Fixture revisión"),1,false,default);
        await Assert.ThrowsAsync<PlanningConflictException>(()=>store.ReportListAsync(new(Page:2,Revision:revision),default));
        var marked=Assert.Single((await store.ReportListAsync(new(View:"Checked",VerifiedBy:"Actor",Block:1,CheckedDate:DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-6)).DateTime),Sort:"Time",Descending:true),default)).Items);
        Assert.Equal(selected.Execution.ExecutionId,marked.Execution.ExecutionId);
    }

    [PlanningDatabaseFact]
    public async Task TwoPhExecutionsAndCrockingKeepIdentityMarksAndReviewedDocumentLinks()
    {
        await using var db=await TestDatabase.Create();var store=db.Store;
        await store.SaveEquivalenceAsync(new("PH","PH","CHEMICAL",false,null,"Catálogo de fixture"),1,default);
        await store.SaveEquivalenceAsync(new("CROCKING","CROCKING","PHYSICAL",false,null,"Catálogo de fixture"),1,default);
        var input=Input(Summary() with{Job="GUAT-PRUEBA-001"},Report(test:"PH") with{Job="GUAT-PRUEBA-001"},Report(3,"PH") with{Job="GUAT-PRUEBA-001"},Report(4,"CROCKING") with{Job="GUAT-PRUEBA-001"});
        var preview=await store.PreviewAsync(input,1,default);Assert.Equal(2,preview.AmbiguousRows);await store.ConfirmAsync(preview.Id,1,default);
        var episode=Assert.Single((await store.QueryAsync(new(),default)).Items).EpisodeId;
        foreach(var source in new[]{2,3})
        {var d=(await store.DossierAsync(episode,default))!;await store.ResolveSourceAsync(new(preview.Id,source,episode,null,true,d.Version,"Fixture: dos ejecuciones físicas verificadas"),1,default);}
        var rows=(await store.ReportListAsync(new(),default)).Items;Assert.Equal(3,rows.Count);
        var ph=rows.Where(x=>x.Execution.PlanningTest=="PH").ToArray();Assert.Equal(2,ph.Length);
        await store.CheckReportAsync(new([new(ph[0].Execution.ExecutionId!.Value,ph[0].Version)],true,"Fixture: marcar solo PH A"),1,false,default);
        var d1=(await store.DossierAsync(episode,default))!;Assert.Single(d1.Tests,t=>t.Complete);Assert.False(d1.Tests.Single(t=>t.ExecutionId==ph[1].Execution.ExecutionId).Complete);
        var ids=d1.Tests.Select(t=>t.ExecutionId).Order().ToArray();
        var later=await store.PreviewAsync(input with{Fingerprint=Guid.NewGuid().ToString("N"),Cut=new("Last",new(2026,11,16))},1,default);
        Assert.Equal(0,later.NewTests);Assert.DoesNotContain(later.Reviews,r=>r.Category=="ExecutionIdentity");await store.ConfirmAsync(later.Id,1,default);
        var d2=(await store.DossierAsync(episode,default))!;Assert.Equal(ids,d2.Tests.Select(t=>t.ExecutionId).Order());Assert.Single(d2.Tests,t=>t.Complete);
        Assert.True((await store.ConfirmAsync(later.Id,1,default)).AlreadyApplied);
        Assert.Equal(1L,await db.Scalar("SELECT count(*) FROM planning.history WHERE action='ReportMarked'"));
        var reordered=await store.PreviewAsync(input with{Fingerprint=Guid.NewGuid().ToString("N"),Report=[7,8]},1,default);
        Assert.Equal(2,reordered.AmbiguousRows);Assert.Equal(0,reordered.NewTests);
    }

    [PlanningDatabaseFact]
    public async Task ApprovedNamesStayCurrentWithoutConsolidatingSourceExecutions()
    {
        await using var db=await TestDatabase.Create();var store=db.Store;
        await store.SaveEquivalenceAsync(new("Technical A","PH","CHEMICAL",false,null,"Fixture aprobado A"),1,default);
        await store.SaveEquivalenceAsync(new("Technical B","PH","CHEMICAL",false,null,"Fixture aprobado B"),1,default);
        var p=await store.PreviewAsync(Input(Summary(),Report(test:"Technical A"),Report(3,"Technical B"),Report(4,"Unknown")),1,default);await store.ConfirmAsync(p.Id,1,default);
        var rows=(await store.QueryAsync(new(),default)).Items;Assert.Equal(3,rows.Count);Assert.Equal(2,rows.Count(r=>r.PlanningTest=="PH"));Assert.Single(rows,r=>r.PlanningTest=="Sin equivalencia");
        Assert.Single((await store.ReviewWorkspaceAsync(new(Category:"MissingEquivalence"),default)).Items);
        await store.SaveEquivalenceAsync(new("Technical A","PH vigente","CHEMICAL",false,0,"Cambio aprobado"),1,default);
        var updated=Assert.Single((await store.QueryAsync(new(ColumnFilters:"{\"PlanningTest\":\"vigente\"}"),default)).Items);Assert.Equal("Technical A",updated.OriginalTest);Assert.Equal("PH vigente",updated.PlanningTest);
        var csv=System.Text.Encoding.UTF8.GetString(await store.ExportAsync(new(),default));Assert.Contains("PH vigente",csv);Assert.Contains("Technical A",csv);
        await store.SaveEquivalenceAsync(new("Technical A",null,null,true,1,"NO PROGRAMAR explícito"),1,default);
        var no=Assert.Single((await store.QueryAsync(new(State:"NoProgramar"),default)).Items);Assert.Equal("NO PROGRAMAR",no.PlanningTest);
        Assert.Equal(3L,await db.Scalar("SELECT count(*) FROM planning.executions"));
        Assert.Contains((await store.DossierAsync(no.EpisodeId,default))!.History,h=>h.Action=="EquivalenceApplied");
    }

    [PlanningDatabaseFact]
    public async Task ReportBlocksKeepArchiveCorrectionsLastPendingAndAtomicVersions()
    {
        await using var db=await TestDatabase.Create();var store=db.Store;
        await store.SaveEquivalenceAsync(new("PH","PH","CHEMICAL",false,null,"Fixture"),1,default);
        var p=await store.PreviewAsync(Input(Summary(),Report(test:"PH"),Report(3,"Another")),1,default);await store.ConfirmAsync(p.Id,1,default);
        await store.SaveEquivalenceAsync(new("Another","CROCKING","PHYSICAL",false,null,"Fixture"),1,default);
        var items=(await store.ReportListAsync(new(),default)).Items;var first=items[0];var second=items[1];
        await Assert.ThrowsAsync<PlanningConflictException>(()=>store.CheckReportAsync(new([new(first.Execution.ExecutionId!.Value,first.Version),new(second.Execution.ExecutionId!.Value,second.Version+99)],true,"Fixture atomicidad"),1,false,default));
        Assert.Empty((await store.ReportListAsync(new(View:"Checked"),default)).Items);
        await store.KeepReportAsync(new(new(first.Execution.ExecutionId!.Value,first.Version),true,"Fixture permanencia"),1,default);
        first=(await store.ReportListAsync(new(),default)).Items.Single(x=>x.Execution.ExecutionId==first.Execution.ExecutionId);
        var a=await store.CheckReportAsync(new([new(first.Execution.ExecutionId!.Value,first.Version)],true,"Fixture check A"),1,false,default);Assert.Empty(a.ReadyJobs);
        var b=await store.CheckReportAsync(new([new(second.Execution.ExecutionId!.Value,second.Version)],true,"Fixture check B"),1,false,default);Assert.Equal("J1",Assert.Single(b.ReadyJobs));
        var checkedRows=(await store.ReportListAsync(new(View:"Checked"),default)).Items;Assert.Single(checkedRows.Select(x=>x.BlockNumber).Distinct());
        Assert.Single((await store.ReportListAsync(new(View:"Archived"),default)).Items);Assert.Single((await store.ReportListAsync(new(View:"Active"),default)).Items);
        var correction=checkedRows.Single(x=>x.Execution.ExecutionId==first.Execution.ExecutionId);
        await store.CheckReportAsync(new([new(correction.Execution.ExecutionId!.Value,correction.Version)],false,"Fixture corrección"),1,false,default);
        Assert.Single((await store.ReportListAsync(new(),default)).Items);Assert.Equal(1L,await db.Scalar("SELECT count(*) FROM planning.report_close_alerts WHERE reopened_at IS NOT NULL"));
        Assert.Contains((await store.DossierAsync(first.Execution.EpisodeId,default))!.History,h=>h.Action=="ReportCorrected");
        await db.Sql("UPDATE planning.report_blocks SET last_checked_at=now()-interval '16 minutes'");
        var pending=Assert.Single((await store.ReportListAsync(new(),default)).Items);
        await store.CheckReportAsync(new([new(pending.Execution.ExecutionId!.Value,pending.Version)],true,"Fixture después de 15 minutos"),1,false,default);
        Assert.Equal(2L,await db.Scalar("SELECT count(*) FROM planning.report_blocks"));
    }

    [PlanningDatabaseFact]
    public async Task ScannerChecksVerifiedTrackingAndNeverChoosesBetweenRepeatedExecutions()
    {
        await using var db=await TestDatabase.Create();var store=db.Store;
        await store.SaveEquivalenceAsync(new("Technical AZO","AZO","ANALYTICAL",false,null,"Fixture"),1,default);
        var p=await store.PreviewAsync(Input(Summary(),Report(test:"Technical AZO")),1,default);await store.ConfirmAsync(p.Id,1,default);
        var row=Assert.Single((await store.ReportListAsync(new(),default)).Items);
        await Assert.ThrowsAsync<PlanningValidationException>(()=>store.CheckReportAsync(new([new(row.Execution.ExecutionId!.Value,row.Version)],true,"Manual no permitido"),1,false,default));
        await Assert.ThrowsAsync<PlanningValidationException>(()=>store.ScanReportAsync(new("LIMS1-AR-1-XX","Checksum falso"),1,default));
        var acc=(17*31+'1')%1296;const string digits="0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";var code=$"LIMS1-AR-1-{digits[acc/36]}{digits[acc%36]}";
        await Assert.ThrowsAsync<PlanningValidationException>(()=>store.ScanReportAsync(new(code,"No hay tracking"),1,default));
        await db.Sql("CREATE TABLE public.tabla_programacion_analitica(id bigint PRIMARY KEY,job text,prueba text); INSERT INTO public.tabla_programacion_analitica VALUES(1,'J1','AZO')");
        var fixedAt=new DateTimeOffset(2026,11,23,8,45,0,TimeSpan.FromHours(-6));
        await store.ScanReportAsync(new(code,"Escaneo fixture",fixedAt),1,default);await store.ScanReportAsync(new(code,"Reintento fixture",fixedAt),1,default);
        Assert.Equal(fixedAt,Assert.Single((await store.ReportListAsync(new(View:"Checked"),default)).Items).Execution.DigitizedAt);Assert.Equal(1L,await db.Scalar("SELECT count(*) FROM planning.history WHERE action='ReportMarked'"));
        Assert.Equal(1L,await db.Scalar("SELECT count(*) FROM planning.report_scan_links"));
    }

    [PlanningDatabaseFact]
    public async Task MarkedManualExecutionSurvivesNewSourceTestAndSummaryOnlyWip()
    {
        await using var db=await TestDatabase.Create();var store=db.Store;
        var p=await store.PreviewAsync(Input(Summary(),Summary() with{Job="SUMMARY-ONLY",Row=3}),1,default);await store.ConfirmAsync(p.Id,1,default);
        var episode=(await store.QueryAsync(new(Search:"J1",SearchMode:"Exact"),default)).Items.Single().EpisodeId;
        var d=(await store.DossierAsync(episode,default))!;
        await store.AddManualTestAsync(episode,new(d.Version,"PH","PH","CHEMICAL","Fixture manual"),1,default);
        var manual=Assert.Single((await store.ReportListAsync(new(),default)).Items);
        await store.CheckReportAsync(new([new(manual.Execution.ExecutionId!.Value,manual.Version)],true,"Fixture manual marcada"),1,false,default);
        var next=await store.PreviewAsync(Input(Summary(due:22),Report(test:"CROCKING")),1,default);Assert.Equal(1,next.NewTests);await store.ConfirmAsync(next.Id,1,default);
        d=(await store.DossierAsync(episode,default))!;Assert.Equal(2,d.Tests.Count);Assert.True(d.Tests.Single(t=>t.ExecutionId==manual.Execution.ExecutionId).Complete);
        Assert.Equal(1L,await db.Scalar("SELECT count(*) FROM planning.report_close_alerts WHERE reopened_at IS NOT NULL"));
        var only=Assert.Single((await store.QueryAsync(new(Search:"SUMMARY-ONLY"),default)).Items);Assert.Null(only.ExecutionId);Assert.NotNull(only.DueDate);
    }
}

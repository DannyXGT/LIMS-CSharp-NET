using Lims.Contracts.Planning;

namespace Lims.Infrastructure.Tests;

public sealed partial class PlanningPostgresTests
{
    [PlanningDatabaseFact]
    public async Task AutomaticScannerMarksOnlyTheJobAndTestIdentifiedByTheReactBarcode()
    {
        await using var db=await TestDatabase.Create();var store=db.Store;
        await store.SaveEquivalenceAsync(new("Technical AZO","AZO","Analytical",false,null,"Fixture"),1,default);
        var preview=await store.PreviewAsync(Input(Summary(),Summary() with{Row=3,Job="J2"},Report(test:"Technical AZO"),Report(4,"Technical AZO") with{Job="J2"}),1,default);
        await store.ConfirmAsync(preview.Id,1,default);
        await db.Sql("CREATE TABLE public.tabla_programacion_analitica(id bigint PRIMARY KEY,job text,prueba text); INSERT INTO public.tabla_programacion_analitica VALUES(1,'J2','AZO')");
        var at=new DateTimeOffset(2026,11,24,11,32,0,TimeSpan.FromHours(-6));
        var missing=await Assert.ThrowsAsync<Lims.Application.Planning.PlanningValidationException>(()=>store.ScanReportAsync(new("AR10TC","Reporte sin registro",at),1,default));
        Assert.Contains("AR #10",missing.Message,StringComparison.Ordinal);Assert.Contains("React",missing.Message,StringComparison.Ordinal);
        Assert.Empty((await store.ReportListAsync(new(View:"Checked"),default)).Items);
        await store.ScanReportAsync(new("AR1G0","Lectura automática",at),1,default);
        await store.ScanReportAsync(new("AR1G0","Relectura automática",at.AddMinutes(1)),1,default);
        var marked=Assert.Single((await store.ReportListAsync(new(View:"Checked"),default)).Items);
        Assert.Equal("J2",marked.Execution.Job);Assert.Equal("AZO",marked.Execution.PlanningTest);Assert.Equal(at,marked.Execution.DigitizedAt);
        Assert.Equal("J1",Assert.Single((await store.ReportListAsync(new(View:"Pending"),default)).Items).Execution.Job);
        Assert.Equal(1L,await db.Scalar("SELECT count(*) FROM planning.history WHERE action='ReportMarked'"));
    }
    [PlanningDatabaseFact]
    public async Task FixedTimeBatchKeepsRepeatedTestsAndDifferentJobsAndWaitsForTheRemainingTestToClose()
    {
        await using var db=await TestDatabase.Create();var store=db.Store;
        await store.SaveEquivalenceAsync(new("Technical PH A","PH","Chemical",false,null,"Fixture"),1,default);
        await store.SaveEquivalenceAsync(new("Technical PH B","PH","Chemical",false,null,"Fixture"),1,default);
        await store.SaveEquivalenceAsync(new("Crocking","CROCKING","Physical",false,null,"Fixture"),1,default);
        var preview=await store.PreviewAsync(Input(Summary(),Summary() with{Row=3,Job="J2"},Report(test:"Technical PH A"),Report(3,"Technical PH B"),Report(4,"Crocking"),Report(5,"Technical PH A") with{Job="J2"}),1,default);
        await store.ConfirmAsync(preview.Id,1,default);await db.Sql("UPDATE planning.incidents SET state='Resolved'");
        var pending=(await store.ReportListAsync(new(),default)).Items;Assert.Equal(4,pending.Count);
        var selected=pending.Where(x=>x.Execution.PlanningTest=="PH").ToArray();Assert.Equal(3,selected.Length);
        var at=new DateTimeOffset(2026,11,24,9,20,0,TimeSpan.FromHours(-6));
        var batch=new ReportDigitization(Guid.NewGuid(),selected.Select(x=>new ReportExecutionVersion(x.Execution.ExecutionId!.Value,x.Version)).ToArray(),at,"Lote revisado de fixture");
        var result=await store.DigitizeReportAsync(batch,1,false,default);Assert.Equal("J2",Assert.Single(result.ReadyJobs));
        await store.DigitizeReportAsync(batch,1,false,default);
        var detail=await store.ReportScheduleAsync(batch.SessionId,default);Assert.Equal(3,detail.Members.Count);Assert.Equal(3,detail.Members.Select(m=>m.Item.Execution.ExecutionId).Distinct().Count());
        Assert.All(detail.Members,m=>Assert.Equal(at,m.Item.Execution.DigitizedAt));Assert.Equal(1L,await db.Scalar("SELECT count(*) FROM planning.report_sessions"));
        Assert.Equal("J2",Assert.Single((await store.ReportClosuresAsync(new(),default)).Items).Job);
        var remaining=Assert.Single((await store.ReportListAsync(new(),default)).Items);Assert.Equal("CROCKING",remaining.Execution.PlanningTest);
        var final=await store.DigitizeReportAsync(new(Guid.NewGuid(),[new(remaining.Execution.ExecutionId!.Value,remaining.Version)],at,"Última prueba de fixture"),1,false,default);
        Assert.Equal("J1",Assert.Single(final.ReadyJobs));Assert.Equal(2,(await store.ReportClosuresAsync(new(),default)).Total);
    }
    [PlanningDatabaseFact]
    public async Task ReactCompactBarcodeResolvesTechnicalTrackingNameAndRejectsAmbiguousIdentity()
    {
        await using var db=await TestDatabase.Create();var store=db.Store;
        await store.SaveEquivalenceAsync(new("Technical AZO","AZO","Analytical",false,null,"Fixture"),1,default);
        var preview=await store.PreviewAsync(Input(Summary(),Report(test:"Technical AZO")),1,default);await store.ConfirmAsync(preview.Id,1,default);
        await db.Sql("CREATE TABLE public.tabla_programacion_analitica(id bigint PRIMARY KEY,job text,prueba text); INSERT INTO public.tabla_programacion_analitica VALUES(1,' j1 ','Technical AZO'),(10,'J1','AZO')");
        var at=new DateTimeOffset(2026,11,24,8,45,0,TimeSpan.FromHours(-6));
        Assert.True((await store.ReportListAsync(new(),default)).Capabilities!.ScannerAvailable);
        await store.ScanReportAsync(new("\u0002]A0*AR1G0*\r","Formato emitido por React",at),1,default);
        Assert.Equal(at,Assert.Single((await store.ReportListAsync(new(View:"Checked"),default)).Items).Execution.DigitizedAt);
        await db.Sql("INSERT INTO planning.executions(id,episode_id,original_test,test_key,equivalence,laboratory,origin) SELECT gen_random_uuid(),episode_id,original_test,test_key,equivalence,laboratory,origin FROM planning.executions LIMIT 1");
        await Assert.ThrowsAsync<Lims.Application.Planning.PlanningConflictException>(()=>store.ScanReportAsync(new("AR10TC","Tracking ambiguo",at),1,default));
        Assert.Equal(1L,await db.Scalar("SELECT count(*) FROM planning.report_scan_links"));
        Assert.Single((await store.ReportListAsync(new(View:"Pending"),default)).Items);
    }

    [PlanningDatabaseFact]
    public async Task ConfiguredReactTrackingTableIsReadBeforeDefaultTables()
    {
        await using var db=await TestDatabase.Create();
        await db.Store.SaveEquivalenceAsync(new("Technical AZO","AZO","Analytical",false,null,"Fixture"),1,default);
        var preview=await db.Store.PreviewAsync(Input(Summary(),Report(test:"Technical AZO")),1,default);await db.Store.ConfirmAsync(preview.Id,1,default);
        await db.Sql("CREATE TABLE public.react_tracking(id bigint PRIMARY KEY,job text,prueba text); INSERT INTO public.react_tracking VALUES(1,'J1','AZO'); CREATE TABLE public.tabla_programacion_analitica(id bigint PRIMARY KEY,job text,prueba text); INSERT INTO public.tabla_programacion_analitica VALUES(1,'OTHER','AZO')");
        await using var source=new Lims.Infrastructure.Planning.PlanningDatabase(db.ConnectionString,"public.react_tracking");
        var store=new Lims.Infrastructure.Planning.PostgresPlanningStore(source);
        Assert.Equal("public.react_tracking",(await store.ReportListAsync(new(),default)).Capabilities!.ScannerSource);
        await store.ScanReportAsync(new("AR1G0","Tracking configurado de React"),1,default);
        Assert.Single((await store.ReportListAsync(new(View:"Checked"),default)).Items);
    }
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

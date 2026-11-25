using Lims.Application.Planning;
using Lims.Contracts.Planning;
using Lims.Infrastructure.Planning;

namespace Lims.Infrastructure.Tests;

public sealed partial class PlanningPostgresTests
{
    private const string ReactFixture="""
        CREATE TABLE public.planificacion_activa(id bigserial PRIMARY KEY,job_prueba text UNIQUE NOT NULL,
          jobs text,report_no text,prueba text,nombre_prueba_plan text,test_name_tecnico text,laboratorio text,lab text,
          fecha_ingreso date,reg_date date,due_date date,delivery_date date,cliente text,buyer_name text,buyer_excel2 text,vendor text,
          servicio text,service_type text,tipo_prioridad text,usd numeric,usd_p text,pi_total_amt numeric,pi_currency text,
          sample_status text,status_tips text,close_date date,comentarios text,horario text,fecha_prueba date,
          prueba_completa boolean NOT NULL DEFAULT false,reporte_completo boolean NOT NULL DEFAULT false,
          created_at timestamptz DEFAULT now(),updated_at timestamptz DEFAULT now());
        CREATE TABLE public.planificacion_archivada(id bigserial PRIMARY KEY,job_prueba text UNIQUE NOT NULL,archived_reason text,updated_at timestamptz DEFAULT now());
        CREATE TABLE public.planificacion_test_map(id bigserial PRIMARY KEY,nombre_tecnico text UNIQUE NOT NULL,
          nombre_planificacion text NOT NULL,lab text,activo boolean NOT NULL DEFAULT true,test_name_tecnico text,nombre_prueba_plan text,
          nombre_tecnico_norm text GENERATED ALWAYS AS(upper(trim(nombre_tecnico))) STORED,planificacion_nombre_id bigint,
          created_at timestamptz DEFAULT now(),updated_at timestamptz DEFAULT now());
        CREATE TABLE public.planificacion_no_programar(tecnico_norm text PRIMARY KEY,updated_at timestamptz DEFAULT now());
        """;
    private static PlanningImportInput ReactInput(params PlanningSourceRow[] rows)=>Input(rows) with{Cut=new("Noon",new(2026,11,16),"HtmlReact")};
    [PlanningDatabaseFact]
    public async Task ReportListReadsCanonicalReactPlanningNameForExistingExecutionsAndUsesItForDigitization()
    {
        await using var db=await TestDatabase.Create();await db.Sql(ReactFixture);
        await db.Sql("CREATE TABLE public.planificacion_nombre(id bigint PRIMARY KEY,nombre_planificacion text);INSERT INTO public.planificacion_nombre VALUES(146,'FORMALDEHYDE');INSERT INTO public.planificacion_test_map(nombre_tecnico,nombre_planificacion,lab,planificacion_nombre_id) VALUES('Formaldehyde Content','Old map alias','Chemical',146)");
        var p=await db.Store.PreviewAsync(Input(Summary(),Report(test:"Formaldehyde Content"),Report(3,"Unknown technical")),1,default);await db.Store.ConfirmAsync(p.Id,1,default);
        Assert.Equal(2L,await db.Scalar("SELECT count(*) FROM planning.executions WHERE equivalence IS NULL"));
        var page=await db.Store.ReportListAsync(new(Test:"FORMALDEHYDE",Sort:"Test"),default);
        var item=Assert.Single(page.Items);var id=item.Execution.ExecutionId!.Value;
        Assert.Equal("FORMALDEHYDE",item.Execution.PlanningTest);Assert.Equal("Chemical",item.Execution.Laboratory);Assert.Equal("Formaldehyde Content",item.Execution.OriginalTest);
        Assert.Equal("FORMALDEHYDE",Assert.Single(await db.Store.EquivalencesAsync(default)).Equivalence);
        Assert.Equal(2L,await db.Scalar("SELECT count(*) FROM planning.executions WHERE equivalence IS NULL"));
        Assert.Empty((await db.Store.ReportListAsync(new(Test:"Unknown technical"),default)).Items);
        Assert.Single(await AssertExclusiveQueues(db.Store,new(Search:"Unknown technical")),r=>r.Category=="MissingEquivalence"&&r.State=="Pending");
        await db.Store.CheckReportAsync(new([new(id,item.Version)],true,"Digitacion con nombre oficial React"),1,false,default);
        Assert.Equal("FORMALDEHYDE",await db.Scalar("SELECT equivalence FROM planning.executions WHERE original_test='Formaldehyde Content'"));
        Assert.Equal(1L,await db.Scalar("SELECT count(*) FROM planning.history WHERE action='ClassificationFromReact'"));
        var beforeRename=(await db.Store.ReportListAsync(new(View:"All"),default)).Revision;
        await db.Sql("UPDATE public.planificacion_nombre SET nombre_planificacion='Changed catalogue name'");
        await Assert.ThrowsAsync<PlanningConflictException>(()=>db.Store.ReportListAsync(new(Revision:beforeRename),default));
        var saved=Assert.Single((await db.Store.ReportListAsync(new(View:"Checked"),default)).Items);
        Assert.Equal(id,saved.Execution.ExecutionId);Assert.Equal("FORMALDEHYDE",saved.Execution.PlanningTest);
    }
    [PlanningDatabaseFact]
    public async Task ReportListKeepsManualAndSupplementClassificationAndHonorsCurrentReactNoProgram()
    {
        await using var db=await TestDatabase.Create();await db.Sql(ReactFixture);
        await db.Sql("INSERT INTO public.planificacion_test_map(nombre_tecnico,nombre_planificacion,lab) VALUES('Technical source','Canonical name','Chemical')");
        var p=await db.Store.PreviewAsync(Input(Summary(),Report(test:"Technical source")),1,default);await db.Store.ConfirmAsync(p.Id,1,default);
        await db.Sql("UPDATE planning.executions SET origin='Manual',equivalence='Manual decision',laboratory='Physical'");
        Assert.Equal("Manual decision",Assert.Single((await db.Store.ReportListAsync(new(),default)).Items).Execution.PlanningTest);
        await db.Sql("UPDATE planning.executions SET origin='ReportList',execution_type='ReactSide2:v1'");
        Assert.Equal("Manual decision",Assert.Single((await db.Store.ReportListAsync(new(),default)).Items).Execution.PlanningTest);
        await db.Sql("UPDATE planning.executions SET execution_type=NULL;UPDATE public.planificacion_test_map SET activo=false");
        Assert.Empty((await db.Store.ReportListAsync(new(),default)).Items);
        Assert.Equal("NO PROGRAMAR",Assert.Single((await db.Store.ReportListAsync(new(View:"All"),default)).Items).Execution.PlanningTest);
    }
    [PlanningDatabaseFact]
    public async Task HtmlReactLinkDoesNotBlockLegacyRemovalAndPreservesTheIndependentHistory()
    {
        await using var db=await TestDatabase.Create();await db.Sql(ReactFixture+ReactPlanningSchema.Sql);
        await db.Sql("INSERT INTO public.planificacion_test_map(nombre_tecnico,nombre_planificacion,lab) VALUES('AZO','AZO','Chemical')");
        var p=await db.Store.PreviewAsync(ReactInput(Summary(),Report()),1,default);await db.Store.ConfirmAsync(p.Id,1,default);
        var id=await db.Scalar("SELECT id FROM planning.executions");
        await db.Sql("DELETE FROM public.planificacion_activa");
        Assert.Equal(id,await db.Scalar("SELECT id FROM planning.executions"));
        Assert.Equal(true,await db.Scalar("SELECT react_row_id IS NULL FROM planning.react_execution_links"));
        Assert.Contains(await AssertExclusiveQueues(db.Store),r=>r.Category=="Admission"&&r.Reason!.Contains("retirada",StringComparison.Ordinal));
        p=await db.Store.PreviewAsync(ReactInput(Summary(),Report()),1,default);Assert.Equal("ReactIdentity",Assert.Single(p.Admissions!).State);
    }
    [PlanningDatabaseFact]
    public async Task HtmlReactTableReviewHonorsNoProgramAndDoesNotResurrectItsDerivedProjection()
    {
        await using var db=await TestDatabase.Create();await db.Sql(ReactFixture+ReactPlanningSchema.Sql);
        await db.Sql("INSERT INTO public.planificacion_test_map(nombre_tecnico,nombre_planificacion,lab) VALUES('AZO','RF','Analytical')");
        var summary=Summary() with{Wip=Summary().Wip! with{Buyer="Disney"}};
        var first=await db.Store.PreviewAsync(ReactInput(summary,Report()),1,default);await db.Store.ConfirmAsync(first.Id,1,default);
        var mapping=Assert.Single(await db.Store.EquivalencesAsync(default));
        await db.Store.SaveEquivalenceAsync(new("AZO",null,null,true,mapping.Version,"Decision explicita NO PROGRAMAR"),1,default);
        var table=await db.Store.PreviewReactTablesAsync(1,new(2026,11,16),default);await db.Store.ConfirmAsync(table.Id,1,default);
        Assert.Equal("NoProgram",await db.Scalar("SELECT disposition FROM planning.react_execution_links l JOIN planning.executions x ON x.id=l.execution_id WHERE x.original_test='AZO'"));
        Assert.Equal("RuleRetired",await db.Scalar("SELECT disposition FROM planning.react_execution_links l JOIN planning.executions x ON x.id=l.execution_id WHERE x.execution_type='DisneyRfSccp:v1'"));
    }
    [PlanningDatabaseFact]
    public async Task HtmlReactSupplementUpdatesDeclaredLaboratoryAndKeepsProtectionOnTableReview()
    {
        await using var db=await TestDatabase.Create();await db.Sql(ReactFixture+ReactPlanningSchema.Sql);
        var source=new PlanningSourceRow("Side2","Original",2,"J2-REV1","Manual test",new(null,null,null,new(2026,11,16),new(2026,11,20),null,null,null),"{}",SourceLaboratory:"Physical");
        var first=Input(source) with{Cut=new("Noon",new(2026,11,16),"HtmlReactSide2")};
        var p=await db.Store.PreviewAsync(first,1,default);await db.Store.ConfirmAsync(p.Id,1,default);
        var next=Input(source with{SourceLaboratory="Chemical"}) with{Cut=first.Cut};
        p=await db.Store.PreviewAsync(next,1,default);await db.Store.ConfirmAsync(p.Id,1,default);
        Assert.Equal("Chemical",await db.Scalar("SELECT laboratory FROM planning.executions"));
        Assert.Equal("Chemical",await db.Scalar("SELECT laboratorio FROM public.planificacion_activa"));
        Assert.Equal(true,await db.Scalar("SELECT bool_and(details->'before'->>'Laboratory'='Physical' AND details->'after'->>'Laboratory'='Chemical') FROM planning.history WHERE action='ClassificationSourceObserved'"));
        p=await db.Store.PreviewReactTablesAsync(1,new(2026,11,16),default);
        Assert.Equal("Active",Assert.Single(p.Admissions!).State);
    }
    [PlanningDatabaseFact]
    public async Task HtmlReactToxicClosureRequiresDerivedTotalAndChromiumWithoutRequiringRetiredComponents()
    {
        await using var db=await TestDatabase.Create();await db.Sql(ReactFixture+ReactPlanningSchema.Sql);
        await db.Sql("INSERT INTO public.planificacion_test_map(nombre_tecnico,nombre_planificacion,lab) VALUES('CADMIUM / LEAD','CADMIUM / LEAD','Analytical'),('TOTAL MERCURY','TOTAL MERCURY','Analytical'),('CHROMIUM VI','CHROMIUM VI','Analytical')");
        var summary=Summary() with{Wip=Summary().Wip! with{Vendor="Vendor verificado"}};
        var p=await db.Store.PreviewAsync(ReactInput(summary,Report(test:"CADMIUM / LEAD"),Report(3,"TOTAL MERCURY"),Report(4,"CHROMIUM VI")),1,default);await db.Store.ConfirmAsync(p.Id,1,default);
        await db.Sql("UPDATE planning.executions x SET digitized_at='2026-11-16T16:00:00Z',tested_on='2026-11-16' FROM planning.react_execution_links l WHERE l.execution_id=x.id AND l.disposition='Active'");
        var closure=Assert.Single((await db.Store.ReportClosuresAsync(new(),default)).Items);
        Assert.Equal(2,closure.Required);Assert.Equal(2,closure.Digitized);
        Assert.Equal(4L,await db.Scalar("SELECT count(*) FROM planning.executions"));
    }
    [PlanningDatabaseFact]
    public async Task HtmlReactSnapshotCannotOverwriteSummaryAuthorityWithConflictingLegacyValues()
    {
        await using var db=await TestDatabase.Create();await db.Sql(ReactFixture+ReactPlanningSchema.Sql);
        await db.Sql("INSERT INTO public.planificacion_test_map(nombre_tecnico,nombre_planificacion,lab) VALUES('AZO','AZO','Chemical')");
        var first=await db.Store.PreviewAsync(ReactInput(Summary(),Report()),1,default);await db.Store.ConfirmAsync(first.Id,1,default);
        await db.Sql("UPDATE public.planificacion_activa SET buyer_excel2='Conflicting source'");
        var preview=await db.Store.PreviewReactTablesAsync(1,new(2026,11,16),default);
        Assert.Contains(preview.Reviews,r=>r.Category=="SourceConflict");await db.Store.ConfirmAsync(preview.Id,1,default);
        Assert.Equal("Buyer",await db.Scalar("SELECT buyer FROM planning.episodes"));
        Assert.Equal("Conflicting source",await db.Scalar("SELECT buyer_excel2 FROM public.planificacion_activa"));
    }
    [PlanningDatabaseFact]
    public async Task HtmlReactManualCompletionUsesCurrentReactCatalogueAndSharedOperationalTable()
    {
        await using var db=await TestDatabase.Create();await db.Sql(ReactFixture+ReactPlanningSchema.Sql);
        await db.Sql("INSERT INTO public.planificacion_test_map(nombre_tecnico,nombre_planificacion,lab) VALUES('AZO','AZO','Chemical')");
        var p=await db.Store.PreviewAsync(ReactInput(Summary()),1,default);await db.Store.ConfirmAsync(p.Id,1,default);
        var row=Assert.Single((await db.Store.QueryAsync(new(),default)).Items);var dossier=(await db.Store.DossierAsync(row.EpisodeId,default))!;
        var map=Assert.Single(await db.Store.EquivalencesAsync(default));
        await db.Store.AddManualTestAsync(row.EpisodeId,new(dossier.Version,"AZO","AZO","Chemical","Completar pruebas verificadas",map.Version),1,default);
        Assert.Equal(1L,await db.Scalar("SELECT count(*) FROM public.planificacion_activa"));
        Assert.Equal("Active",await db.Scalar("SELECT disposition FROM planning.react_execution_links"));
        Assert.Equal("Manual",await db.Scalar("SELECT origin FROM planning.executions"));
    }
    [PlanningDatabaseFact]
    public async Task HtmlReactGroupedProjectionNeverMergesIndependentExecutionUuids()
    {
        await using var db=await TestDatabase.Create();await db.Sql(ReactFixture+ReactPlanningSchema.Sql);
        await db.Sql("INSERT INTO public.planificacion_test_map(nombre_tecnico,nombre_planificacion,lab) VALUES('Technical A','PH','Chemical'),('Technical B','PH','Chemical')");
        var p=await db.Store.PreviewAsync(ReactInput(Summary(),Report(test:"Technical A"),Report(3,"Technical B")),1,default);
        Assert.Equal(2,p.NewTests);Assert.All(p.Admissions!,a=>Assert.Equal("Active",a.State));await db.Store.ConfirmAsync(p.Id,1,default);
        Assert.Equal(2L,await db.Scalar("SELECT count(DISTINCT execution_id) FROM planning.react_execution_links"));
        Assert.Equal(1L,await db.Scalar("SELECT count(DISTINCT react_row_id) FROM planning.react_execution_links"));
        Assert.Equal(1L,await db.Scalar("SELECT count(*) FROM public.planificacion_activa"));
        Assert.Equal(2,(await db.Store.QueryAsync(new(),default)).Items.Select(r=>r.ExecutionId).Distinct().Count());
    }
    [PlanningDatabaseFact]
    public async Task HtmlReactExistingTableRowsCanBeReviewedAndLinkedWithoutChangingTheirValues()
    {
        await using var db=await TestDatabase.Create();await db.Sql(ReactFixture+ReactPlanningSchema.Sql);
        await db.Sql("INSERT INTO public.planificacion_activa(job_prueba,jobs,prueba,nombre_prueba_plan,laboratorio,fecha_ingreso,due_date,horario,prueba_completa,cliente,vendor) VALUES('J5 - PH','J5','PH','PH','Chemical','2026-11-16','2026-11-20','14:00',true,'Buyer verificado','Vendor verificado')");
        var before=await db.Scalar("SELECT to_jsonb(a)::text FROM public.planificacion_activa a");
        var preview=await db.Store.PreviewReactTablesAsync(1,new(2026,11,16),default);
        Assert.Equal("ReactTable",preview.Cut.Policy);Assert.Equal("Active",Assert.Single(preview.Admissions!).State);
        var inspection=(await db.Store.InspectImportAsync(preview.Id,default))!;
        Assert.DoesNotContain(inspection.Items,r=>r.Category is "SummaryWithoutReport" or "ReportWithoutSummary");
        Assert.Equal(0L,await db.Scalar("SELECT count(*) FROM planning.executions"));
        await db.Store.ConfirmAsync(preview.Id,1,default);
        Assert.Equal(before,await db.Scalar("SELECT to_jsonb(a)::text FROM public.planificacion_activa a"));
        Assert.Equal(1L,await db.Scalar("SELECT count(*) FROM planning.react_execution_links WHERE react_row_id IS NOT NULL"));
        Assert.Equal("React Active",await db.Scalar("SELECT file FROM planning.source_rows"));
        Assert.Equal(0L,await db.Scalar("SELECT count(*) FROM planning.incidents WHERE kind IN ('SummaryWithoutReport','ReportWithoutSummary') AND state='Pending'"));
        Assert.Equal(1,(await db.Store.QueryAsync(new(),default)).Total);
        Assert.Equal("Applied",(await db.Store.PreviewReactTablesAsync(1,new(2026,11,16),default)).Status);
    }
    [PlanningDatabaseFact]
    public async Task HtmlReactDerivedSccpKeepsItsUuidAndSourceLineageAcrossReimport()
    {
        await using var db=await TestDatabase.Create();await db.Sql(ReactFixture+ReactPlanningSchema.Sql);
        await db.Sql("INSERT INTO public.planificacion_test_map(nombre_tecnico,nombre_planificacion,lab) VALUES('AZO','RF','Analytical')");
        var source=Summary() with{Wip=Summary().Wip! with{Buyer="Disney Consumer Products"}};
        var p=await db.Store.PreviewAsync(ReactInput(source,Report()),1,default);Assert.Equal(2,p.NewTests);await db.Store.ConfirmAsync(p.Id,1,default);
        var derived=await db.Scalar("SELECT id FROM planning.executions WHERE execution_type='DisneyRfSccp:v1'");
        var next=await db.Store.PreviewAsync(ReactInput(source,Report()),1,default);Assert.Equal(0,next.NewTests);await db.Store.ConfirmAsync(next.Id,1,default);
        Assert.Equal(derived,await db.Scalar("SELECT id FROM planning.executions WHERE execution_type='DisneyRfSccp:v1'"));
        Assert.Equal(2L,await db.Scalar("SELECT count(*) FROM public.planificacion_activa"));
        Assert.Equal(true,await db.Scalar("SELECT bool_and(details->>'parentExecutionId' IS NOT NULL) FROM planning.history WHERE action='ImportAdmission' AND details->>'rule'='DisneyRfSccp:v1'"));
    }
    [PlanningDatabaseFact]
    public async Task HtmlReactReimportUpdatesSourceButPreservesDigitizationNotesAndManualWip()
    {
        await using var db=await TestDatabase.Create();await db.Sql(ReactFixture+ReactPlanningSchema.Sql);
        await db.Sql("INSERT INTO public.planificacion_test_map(nombre_tecnico,nombre_planificacion,lab) VALUES('AZO','AZO','Chemical')");
        var first=await db.Store.PreviewAsync(ReactInput(Summary(),Report()),1,default);await db.Store.ConfirmAsync(first.Id,1,default);
        await db.Sql("UPDATE planning.executions SET digitized_at='2026-11-16T16:00:00Z',tested_on='2026-11-16',digitized_by='Verificado',observation='Conservar'; UPDATE public.planificacion_activa SET horario='10:00',prueba_completa=true");
        var row=Assert.Single((await db.Store.QueryAsync(new(),default)).Items);var dossier=(await db.Store.DossierAsync(row.EpisodeId,default))!;
        await db.Store.EditWipAsync(row.EpisodeId,new(dossier.Version,dossier.Wip with{Vendor="Manual verificado"},"Corrección manual verificable"),1,default);
        Assert.Equal("Manual verificado",await db.Scalar("SELECT vendor FROM public.planificacion_activa"));
        var changed=Summary() with{Wip=Summary().Wip! with{Buyer="Buyer actualizado",Vendor="Vendor del archivo"}};
        var next=await db.Store.PreviewAsync(ReactInput(changed,Report()),1,default);await db.Store.ConfirmAsync(next.Id,1,default);
        Assert.Equal("Manual verificado",await db.Scalar("SELECT vendor FROM public.planificacion_activa"));
        Assert.Equal("Buyer actualizado",await db.Scalar("SELECT cliente FROM public.planificacion_activa"));
        Assert.Equal("10:00",await db.Scalar("SELECT horario FROM public.planificacion_activa"));
        Assert.Equal(true,await db.Scalar("SELECT prueba_completa FROM public.planificacion_activa"));
        var final=Assert.Single((await db.Store.QueryAsync(new(),default)).Items);Assert.Equal(row.ExecutionId,final.ExecutionId);
        Assert.Equal("Verificado",final.DigitizedBy);Assert.Equal("Conservar",final.Observation);
    }
    [PlanningDatabaseFact]
    public async Task HtmlReactConflictingLegacyIdentityIsReviewedWithoutOverwritingOrMerging()
    {
        await using var db=await TestDatabase.Create();await db.Sql(ReactFixture+ReactPlanningSchema.Sql);
        await db.Sql("INSERT INTO public.planificacion_test_map(nombre_tecnico,nombre_planificacion,lab) VALUES('AZO','AZO','Chemical'); INSERT INTO public.planificacion_activa(job_prueba,test_name_tecnico,fecha_ingreso,horario) VALUES('J1 - AZO','AZO','2025-01-01','Preservar')");
        var p=await db.Store.PreviewAsync(ReactInput(Summary(),Report()),1,default);
        Assert.Equal("ReactIdentity",Assert.Single(p.Admissions!).State);await db.Store.ConfirmAsync(p.Id,1,default);
        Assert.Equal(1L,await db.Scalar("SELECT count(*) FROM public.planificacion_activa"));
        Assert.Equal("Preservar",await db.Scalar("SELECT horario FROM public.planificacion_activa"));
        Assert.Equal(true,await db.Scalar("SELECT react_row_id IS NULL FROM planning.react_execution_links"));
    }
    [PlanningDatabaseFact]
    public async Task HtmlReactCatalogueRenameKeepsTheSameOperationalRowAndNoProgramRemovesAdmission()
    {
        await using var db=await TestDatabase.Create();await db.Sql(ReactFixture+ReactPlanningSchema.Sql);
        await db.Sql("INSERT INTO public.planificacion_test_map(nombre_tecnico,nombre_planificacion,lab) VALUES('AZO','AZO','Chemical')");
        var p=await db.Store.PreviewAsync(ReactInput(Summary(),Report()),1,default);await db.Store.ConfirmAsync(p.Id,1,default);
        var before=await db.Scalar("SELECT id FROM public.planificacion_activa");var map=Assert.Single(await db.Store.EquivalencesAsync(default));
        await db.Store.SaveEquivalenceAsync(new("AZO","Renamed","Chemical",false,map.Version,"Cambio verificado"),1,default);
        Assert.Equal(before,await db.Scalar("SELECT id FROM public.planificacion_activa"));Assert.Equal("Renamed",await db.Scalar("SELECT prueba FROM public.planificacion_activa"));
        map=Assert.Single(await db.Store.EquivalencesAsync(default));
        await db.Store.SaveEquivalenceAsync(new("AZO",null,null,true,map.Version,"NO PROGRAMAR verificado"),1,default);
        Assert.Equal("NoProgram",await db.Scalar("SELECT disposition FROM planning.react_execution_links"));
        Assert.Equal(1L,await db.Scalar("SELECT count(*) FROM planning.executions"));
    }
    [PlanningDatabaseFact]
    public async Task HtmlReactSupplementKeepsSourceLaboratoryAndPinsDatesWithoutInventingBuyer()
    {
        await using var db=await TestDatabase.Create();await db.Sql(ReactFixture+ReactPlanningSchema.Sql);
        var source=new PlanningSourceRow("Side2","Original",2,"J2-REV1","Manual test",new(null,null,null,new(2026,11,16),new(2026,11,20),null,null,null),"{\"Laboratorio\":\"Physical\"}",SourceLaboratory:"Physical");
        var input=Input(source) with{Cut=new("Noon",new(2026,11,16),"HtmlReactSide2")};
        var p=await db.Store.PreviewAsync(input,1,default);Assert.Equal("Active",Assert.Single(p.Admissions!).State);
        await db.Store.ConfirmAsync(p.Id,1,default);
        Assert.Equal(true,await db.Scalar("SELECT manual_pinned FROM public.planificacion_activa"));
        Assert.Equal("Physical",await db.Scalar("SELECT laboratorio FROM public.planificacion_activa"));
        Assert.Equal(true,await db.Scalar("SELECT buyer_name IS NULL AND vendor IS NULL AND usd IS NULL FROM public.planificacion_activa"));
        Assert.Equal("Manual",await db.Scalar("SELECT origin FROM planning.executions"));
        Assert.Equal("Side2",await db.Scalar("SELECT file FROM planning.source_rows"));
        Assert.Equal(true,await db.Scalar("SELECT 'DueDate'=ANY(manual_fields) FROM planning.episodes"));
    }
    [PlanningDatabaseFact]
    public async Task HtmlReactRequiresAnExplicitInstallationAndNeverCreatesTablesDuringImport()
    {
        await using var db=await TestDatabase.Create();
        await Assert.ThrowsAsync<PlanningValidationException>(()=>db.Store.PreviewAsync(ReactInput(Summary(),Report()),1,default));
        Assert.Equal(true,await db.Scalar("SELECT to_regclass('planning.react_execution_links') IS NULL"));
        Assert.Equal(0L,await db.Scalar("SELECT count(*) FROM planning.batches"));
    }
    [PlanningDatabaseFact]
    public async Task ReactCatalogueIsAuthorityAndConfirmationWritesOriginalTablesIdempotently()
    {
        await using var db=await TestDatabase.Create();await db.Sql(ReactFixture+ReactPlanningSchema.Sql);
        await db.Sql("INSERT INTO public.planificacion_test_map(nombre_tecnico,nombre_planificacion,lab) VALUES('AZO','AZO React','Chemical')");
        var input=ReactInput(Summary(),Report());var p=await db.Store.PreviewAsync(input,1,default);
        Assert.Equal("Active",Assert.Single(p.Admissions!).State);await db.Store.ConfirmAsync(p.Id,1,default);
        Assert.Equal("AZO React",await db.Scalar("SELECT prueba FROM public.planificacion_activa"));
        Assert.Equal(1L,await db.Scalar("SELECT count(*) FROM planning.react_execution_links WHERE disposition='Active' AND react_row_id IS NOT NULL"));
        Assert.Equal(2L,await db.Scalar("SELECT count(*) FROM planning.source_rows"));
        Assert.True((await db.Store.ConfirmAsync(p.Id,1,default)).AlreadyApplied);
        Assert.Equal(1L,await db.Scalar("SELECT count(*) FROM public.planificacion_activa"));
        Assert.Contains(await AssertExclusiveQueues(db.Store),r=>r.Category=="Admission"&&r.State=="Resolved");
    }
    [PlanningDatabaseFact]
    public async Task PendingEquivalenceUsesReactQueueAndAssignmentReprocessesWithoutAnotherImport()
    {
        await using var db=await TestDatabase.Create();await db.Sql(ReactFixture+ReactPlanningSchema.Sql);
        var p=await db.Store.PreviewAsync(ReactInput(Summary(),Report()),1,default);await db.Store.ConfirmAsync(p.Id,1,default);
        Assert.Equal(0L,await db.Scalar("SELECT count(*) FROM public.planificacion_activa"));
        Assert.Equal(1L,await db.Scalar("SELECT count(*) FROM public.planificacion_test_map_pendiente"));
        await db.Store.SaveEquivalenceAsync(new("AZO","AZO","Chemical",false,null,"Clasificación verificada en prueba aislada"),1,default);
        Assert.Equal(1L,await db.Scalar("SELECT count(*) FROM public.planificacion_activa"));
        Assert.Equal(0L,await db.Scalar("SELECT count(*) FROM public.planificacion_test_map_pendiente"));
        Assert.Equal(1L,await db.Scalar("SELECT count(*) FROM planning.executions"));
        Assert.Equal("Active",await db.Scalar("SELECT disposition FROM planning.react_execution_links"));
    }
    [PlanningDatabaseFact]
    public async Task ReactChangesInvalidatePreviewEvenWithoutNativeRevisionChanges()
    {
        await using var db=await TestDatabase.Create();await db.Sql(ReactFixture+ReactPlanningSchema.Sql);
        await db.Sql("INSERT INTO public.planificacion_test_map(nombre_tecnico,nombre_planificacion,lab) VALUES('AZO','AZO','Chemical')");
        var p=await db.Store.PreviewAsync(ReactInput(Summary(),Report()),1,default);
        await db.Sql("UPDATE public.planificacion_test_map SET nombre_planificacion='Changed by React'");
        await Assert.ThrowsAsync<PlanningConflictException>(()=>db.Store.ConfirmAsync(p.Id,1,default));
        Assert.Equal(0L,await db.Scalar("SELECT count(*) FROM planning.executions"));
        Assert.Equal(0L,await db.Scalar("SELECT count(*) FROM public.planificacion_activa"));
    }
    [PlanningDatabaseFact]
    public async Task HtmlClosedAndRevisionSourcesRemainHistoricalWithoutEnteringReactActive()
    {
        await using var db=await TestDatabase.Create();await db.Sql(ReactFixture+ReactPlanningSchema.Sql);
        await db.Sql("INSERT INTO public.planificacion_test_map(nombre_tecnico,nombre_planificacion,lab) VALUES('AZO','AZO','Chemical')");
        var p=await db.Store.PreviewAsync(ReactInput(Summary() with{Official=new(CloseDate:new(2026,11,17))},Report()),1,default);
        Assert.Equal("Closed",Assert.Single(p.Admissions!).State);await db.Store.ConfirmAsync(p.Id,1,default);
        Assert.Equal(0L,await db.Scalar("SELECT count(*) FROM public.planificacion_activa"));
        Assert.Single((await db.Store.QueryAsync(new(Scope:"History"),default)).Items);
        Assert.Contains(await AssertExclusiveQueues(db.Store),r=>r.Category=="Admission"&&r.Reason=="Histórico cerrado");
    }
}

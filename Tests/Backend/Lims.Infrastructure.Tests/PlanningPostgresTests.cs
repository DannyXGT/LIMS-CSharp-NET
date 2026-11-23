using Lims.Application.Planning;
using Lims.Contracts.Planning;
using Lims.Infrastructure.Planning;
using Npgsql;

namespace Lims.Infrastructure.Tests;

public sealed class PlanningDatabaseFactAttribute : FactAttribute
{
    public PlanningDatabaseFactAttribute(){if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("LIMS_PLANNING_TEST_CONNECTION")))Skip="Requiere PostgreSQL local aislado; ejecutar scripts/test-planning.ps1.";}
}
public sealed class RealPlanningDatabaseFactAttribute : FactAttribute
{
    public RealPlanningDatabaseFactAttribute()
    {
        if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("LIMS_PLANNING_TEST_CONNECTION"))||!File.Exists(Path.Combine((Environment.GetEnvironmentVariable("USERPROFILE")??Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),"Downloads","a0_-_pro-invoice_summary_17Nov2026045852237.xlsx")))Skip="Requiere los XLSX reales autorizados y PostgreSQL CI local aislado.";
    }
}
public sealed partial class PlanningPostgresTests
{
    [PlanningDatabaseFact]
    public async Task IdentityUpgradePreservesExistingBatchEpisodeExecutionAndHistory()
    {
        await using var db=await TestDatabase.Create(false);
        var preview=await db.Store.PreviewAsync(Input(Summary(),Report()),1,default);
        await db.Store.ConfirmAsync(preview.Id,1,default);
        var row=Assert.Single((await db.Store.QueryAsync(new(),default)).Items);
        var before=await db.Scalar("SELECT count(*) FROM planning.history");
        await db.Sql(PlanningIdentitySchema.Sql+PlanningIdentitySchema.AccessSql);
        var dossier=(await db.Store.DossierAsync(row.EpisodeId,default))!;
        Assert.Equal(row.ExecutionId,Assert.Single(dossier.Tests).ExecutionId);
        Assert.Equal(preview.Id,Assert.Single(await db.Store.BatchesAsync(default)).Id);
        Assert.All(dossier.History,h=>Assert.Equal("Actor sintético local",h.ActorName));
        Assert.Equal(before,await db.Scalar("SELECT count(*) FROM planning.history"));
        Assert.Equal(0L,await db.Scalar("SELECT count(*) FROM pg_constraint WHERE connamespace='planning'::regnamespace AND confrelid='public.usuarios'::regclass"));
    }

    [PlanningDatabaseFact]
    public async Task SeparateIdentityDatabasePreservesImportNamesHistoryAndHostPermissions()
    {
        await using var host=await TestDatabase.Create();
        await using var target=await TestDatabase.Create();
        await target.Sql("DELETE FROM planning.actors; DROP TABLE public.planning_access_history; DROP TABLE public.usuarios_permisos; DROP TABLE public.usuarios; DROP TABLE public.departamentos;");
        await using var identities=new PlanningIdentityDatabase(host.ConnectionString);
        var store=new PostgresPlanningStore(target._database,identities);
        await identities.RegisterActorAsync(target._database,1,default);
        var preview=await store.PreviewAsync(Input(Summary()),1,default);
        await store.ConfirmAsync(preview.Id,1,default);
        var episode=Assert.Single((await store.QueryAsync(new(),default)).Items).EpisodeId;
        var dossier=(await store.DossierAsync(episode,default))!;
        Assert.NotEmpty(dossier.History);
        Assert.All(dossier.History,h=>Assert.Equal("Actor sintético local",h.ActorName));
        Assert.Equal("Actor sintético local",Assert.Single(await store.BatchesAsync(default)).ActorName);
        Assert.NotNull(await store.InspectImportAsync(preview.Id,default));
        var review=Assert.Single((await store.ReviewWorkspaceAsync(new(Category:"SummaryWithoutReport"),default)).Items);
        await store.DecideReviewAsync(new(review.Key,review.Category,review.Version,"Held","Retener en prueba separada"),1,default);
        Assert.Equal("Actor sintético local",Assert.Single((await store.ReviewWorkspaceAsync(new(Category:"SummaryWithoutReport"),default)).Items).DecidedBy);
        await host.Sql("INSERT INTO public.usuarios_permisos VALUES(1,'stock.view')");
        await store.SaveAccessAsync(1,new([PlanningPermissions.View],"Permiso real en host de prueba"),1,default);
        Assert.Contains(PlanningPermissions.View,Assert.Single(await store.AccessUsersAsync(default)).Permissions);
        Assert.Equal(1L,await host.Scalar("SELECT count(*) FROM public.planning_access_history WHERE actor_id=1 AND user_id=1"));
        Assert.Equal(1L,await host.Scalar("SELECT count(*) FROM public.usuarios_permisos WHERE permiso_key='stock.view'"));
        Assert.Equal(0L,await host.Scalar("SELECT count(*) FROM planning.batches"));
        Assert.Equal(true,await target.Scalar("SELECT to_regclass('public.usuarios') IS NULL"));
        Assert.Equal(true,await target.Scalar("SELECT to_regclass('public.usuarios_permisos') IS NULL"));
    }

    [PlanningDatabaseFact]
    public async Task SeparateIdentityRejectsUnknownOrInactiveActorWithoutRegisteringIt()
    {
        await using var host=await TestDatabase.Create();await using var target=await TestDatabase.Create();
        await using var identities=new PlanningIdentityDatabase(host.ConnectionString);
        await Assert.ThrowsAsync<PlanningValidationException>(()=>identities.RegisterActorAsync(target._database,999,default));
        await host.Sql("UPDATE public.usuarios SET activo=false WHERE id=1");
        await target.Sql("DELETE FROM planning.actors");
        await Assert.ThrowsAsync<PlanningValidationException>(()=>identities.RegisterActorAsync(target._database,1,default));
        Assert.Equal(0L,await target.Scalar("SELECT count(*) FROM planning.actors"));
    }

    [PlanningDatabaseFact]
    public async Task HostPermissionAuditFailureRollsBackGrantWithoutPlanningDependency()
    {
        await using var host=await TestDatabase.Create();await using var target=await TestDatabase.Create();
        await using var identities=new PlanningIdentityDatabase(host.ConnectionString);
        var store=new PostgresPlanningStore(target._database,identities);
        await host.Sql("INSERT INTO public.usuarios_permisos VALUES(1,'stock.view'); ALTER TABLE public.planning_access_history ADD CONSTRAINT reject_fixture CHECK(false)");
        await Assert.ThrowsAsync<PostgresException>(()=>store.SaveAccessAsync(1,new([PlanningPermissions.View],"Fallo de auditoría de prueba"),1,default));
        Assert.Equal(1L,await host.Scalar("SELECT count(*) FROM public.usuarios_permisos WHERE permiso_key='stock.view'"));
        Assert.Equal(0L,await host.Scalar("SELECT count(*) FROM public.usuarios_permisos WHERE permiso_key LIKE 'planning.%'"));
        Assert.Equal(0L,await host.Scalar("SELECT count(*) FROM public.planning_access_history"));
    }

    [PlanningDatabaseFact]
    public async Task ManualResolutionSurvivesReimportAndLaterSourceLinkWithoutLosingDigitization()
    {
        await using var db=await TestDatabase.Create();var store=db.Store;
        var initial=await store.PreviewAsync(Input(Summary()),1,default);await store.ConfirmAsync(initial.Id,1,default);
        var row=Assert.Single((await store.QueryAsync(new(),default)).Items);var dossier=(await store.DossierAsync(row.EpisodeId,default))!;
        Assert.Null(row.ExecutionId);Assert.Null(dossier.Wip.Usd);
        var pending=Assert.Single(dossier.Incidents,i=>i.Kind=="MissingTests");await store.DecideIncidentAsync(pending.Id,new(pending.Version,"Held","Esperar verificación"),1,default);
        await store.AddManualTestAsync(row.EpisodeId,new(dossier.Version,"AZO","AZO manual","Químico","Solicitud verificada"),1,default);
        dossier=(await store.DossierAsync(row.EpisodeId,default))!;Assert.Equal("Resolved",Assert.Single(dossier.Incidents,i=>i.Kind=="MissingTests").State);Assert.Equal("Pending",Assert.Single(dossier.Incidents,i=>i.Kind=="MissingVendor").State);
        var execution=Assert.Single(dossier.Tests).ExecutionId;
        await db.Sql("UPDATE planning.executions SET digitized_at='2026-11-16T12:00:00Z',digitized_by='fixture digitador',tested_on='2026-11-15' WHERE id='"+execution+"'");
        var later=await store.PreviewAsync(Input(Summary(due:21)),1,default);Assert.DoesNotContain(later.Reviews,r=>r.Category=="SummaryWithoutReport");await store.ConfirmAsync(later.Id,1,default);
        Assert.DoesNotContain(await store.ReviewsAsync(default),r=>r.Category=="MissingTests");
        var matching=await store.PreviewAsync(Input(Summary(due:22),Report()),1,default);Assert.Equal(1,matching.LinkedManualTests);Assert.Equal(0,matching.NewTests);await store.ConfirmAsync(matching.Id,1,default);
        dossier=(await store.DossierAsync(row.EpisodeId,default))!;var preserved=Assert.Single(dossier.Tests);Assert.Equal(execution,preserved.ExecutionId);Assert.Equal("Manual",preserved.Origin);Assert.Equal("fixture digitador",preserved.DigitizedBy);Assert.NotNull(preserved.DigitizedAt);Assert.Equal("AZO manual",preserved.Equivalence);
        Assert.Contains(dossier.History,h=>h.Action=="ManualTestAdded");Assert.Contains(dossier.History,h=>h.Action=="ManualSourceLinked");Assert.Equal(4,dossier.Sources.Count);
    }
    [PlanningDatabaseFact]
    public async Task RepeatedPreviewAndConfirmAreIdempotent()
    {
        await using var db=await TestDatabase.Create();var input=Input(Summary(),Report());var preview=await db.Store.PreviewAsync(input,1,default);
        var repeated=await db.Store.PreviewAsync(input,1,default);Assert.Equal(preview.Id,repeated.Id);
        await db.Store.ConfirmAsync(preview.Id,1,default);Assert.True((await db.Store.ConfirmAsync(preview.Id,1,default)).AlreadyApplied);
        Assert.Equal("Applied",(await db.Store.PreviewAsync(input,1,default)).Status);Assert.Single((await db.Store.QueryAsync(new(),default)).Items);
        Assert.Equal(1L,await db.Scalar("SELECT count(*) FROM planning.executions"));Assert.Equal(2L,await db.Scalar("SELECT count(*) FROM planning.source_rows"));
    }
    [PlanningDatabaseFact]
    public async Task WipOverrideIsAuditedAndConcurrentPreviewCannotOverwriteIt()
    {
        await using var db=await TestDatabase.Create();var initial=await db.Store.PreviewAsync(Input(Summary()),1,default);await db.Store.ConfirmAsync(initial.Id,1,default);
        var episode=Assert.Single((await db.Store.QueryAsync(new(),default)).Items).EpisodeId;var dossier=(await db.Store.DossierAsync(episode,default))!;
        var pending=await db.Store.PreviewAsync(Input(Summary(due:22)),1,default);
        await db.Store.EditWipAsync(episode,new(dossier.Version,dossier.Wip with{Vendor="Vendor verificado"},"Orden fuente verificada"),1,default);
        await Assert.ThrowsAsync<PlanningConflictException>(()=>db.Store.ConfirmAsync(pending.Id,1,default));
        await Assert.ThrowsAsync<PlanningConflictException>(()=>db.Store.EditWipAsync(episode,new(dossier.Version,dossier.Wip,"Intento obsoleto"),1,default));
        var refreshed=await db.Store.PreviewAsync(Input(Summary(due:23)),1,default);await db.Store.ConfirmAsync(refreshed.Id,1,default);
        var saved=(await db.Store.DossierAsync(episode,default))!;Assert.Equal("Vendor verificado",saved.Wip.Vendor);Assert.Equal(new DateOnly(2026,11,23),saved.Wip.DueDate);Assert.Equal("Resolved",Assert.Single(saved.Incidents,i=>i.Kind=="MissingVendor").State);
    }
    [PlanningDatabaseFact]
    public async Task RowFailureRollsBackWholeConfirmation()
    {
        await using var db=await TestDatabase.Create();var same=Summary();var preview=await db.Store.PreviewAsync(Input(same,same),1,default);
        await Assert.ThrowsAsync<PostgresException>(()=>db.Store.ConfirmAsync(preview.Id,1,default));Assert.Empty((await db.Store.QueryAsync(new(),default)).Items);
        Assert.Equal("Preview",(await db.Store.BatchPreviewAsync(preview.Id,default))!.Status);
    }
    [PlanningDatabaseFact]
    public async Task RepeatedNamesNeedExplicitSourceResolutionAndKeepSeparateExecutionIds()
    {
        await using var db=await TestDatabase.Create();var preview=await db.Store.PreviewAsync(Input(Summary(),Report(),Report(3)),1,default);Assert.Equal(0,preview.NewTests);await db.Store.ConfirmAsync(preview.Id,1,default);
        var episode=Assert.Single((await db.Store.QueryAsync(new(),default)).Items).EpisodeId;
        foreach(var row in new[]{2,3})
        {var dossier=(await db.Store.DossierAsync(episode,default))!;await db.Store.ResolveSourceAsync(new(preview.Id,row,episode,null,true,dossier.Version,"Dos ejecuciones verificadas"),1,default);}
        var tests=(await db.Store.DossierAsync(episode,default))!.Tests;Assert.Equal(2,tests.Count);Assert.Equal(2,tests.Select(t=>t.ExecutionId).Distinct().Count());Assert.DoesNotContain(await db.Store.ReviewsAsync(default),r=>r.Category=="ExecutionIdentity");
    }
    [PlanningDatabaseFact]
    public async Task NoProgramKeepsRecordsAndHistoryAndAccessDoesNotChangeLaboratoryGrants()
    {
        await using var db=await TestDatabase.Create();var preview=await db.Store.PreviewAsync(Input(Summary(),Report()),1,default);await db.Store.ConfirmAsync(preview.Id,1,default);
        await db.Store.SaveEquivalenceAsync(new("AZO",null,null,true,null,"Decisión verificada"),1,default);
        var row=Assert.Single((await db.Store.QueryAsync(new(State:"NoProgramar"),default)).Items);Assert.True(row.NoProgram);Assert.Equal("Sin información",row.DigitizationState);
        await db.Sql("INSERT INTO public.usuarios_permisos VALUES(1,'stock.view')");
        await db.Store.SaveAccessAsync(1,new([PlanningPermissions.View],"Acceso de prueba"),1,default);await db.Store.SaveAccessAsync(1,new([],"Revocar planificación"),1,default);
        Assert.Equal(1L,await db.Scalar("SELECT count(*) FROM public.usuarios_permisos WHERE permiso_key='stock.view'"));
        Assert.Equal(0L,await db.Scalar("SELECT count(*) FROM public.usuarios_permisos WHERE permiso_key LIKE 'planning.%'"));
    }
    [PlanningDatabaseFact]
    public async Task ExportUsesFiltersAndProtectsSpreadsheetFormulaCells()
    {
        await using var db=await TestDatabase.Create();var preview=await db.Store.PreviewAsync(Input(Summary(),Report(test:"=1+1")),1,default);await db.Store.ConfirmAsync(preview.Id,1,default);
        var bytes=await db.Store.ExportAsync(new(Search:"J1"),default);var csv=System.Text.Encoding.UTF8.GetString(bytes);Assert.Contains("'=1+1",csv,StringComparison.Ordinal);Assert.Single((await db.Store.QueryAsync(new(Search:"J1;OTHER"),default)).Items);
    }
    [PlanningDatabaseFact]
    public async Task NotesBatchIsAuditedAtomicAndSurvivesReimportWithoutEditingImportedFields()
    {
        await using var db=await TestDatabase.Create();var reports=Enumerable.Range(2,20).Select(i=>Report(i,"Prueba sintética "+i)).ToArray();
        var first=await db.Store.PreviewAsync(Input([Summary(),..reports]),1,default);await db.Store.ConfirmAsync(first.Id,1,default);
        var before=(await db.Store.QueryAsync(new(PageSize:100),default)).Items.OrderBy(r=>r.ExecutionId).ToArray();
        var edits=before.Take(10).Select((r,i)=>new PlanningNoteCell(r.ExecutionId!.Value,r.NoteVersion,"Responsible","Asignación de prueba "+i)).ToArray();
        Assert.Equal(10,(await db.Store.EditNotesAsync(new(edits,"Aceptación sintética de pegado"),1,default)).Count);
        var saved=(await db.Store.QueryAsync(new(PageSize:100),default)).Items.OrderBy(r=>r.ExecutionId).ToArray();
        Assert.Equal(10,saved.Count(r=>r.Responsible is not null));Assert.Equal(before.Select(r=>r.OriginalTest),saved.Select(r=>r.OriginalTest));
        Assert.All(saved,r=>{Assert.Null(r.DigitizedAt);Assert.Null(r.DigitizedBy);Assert.Null(r.TestedOn);});
        await Assert.ThrowsAsync<PlanningValidationException>(()=>db.Store.EditNotesAsync(new([new(before[10].ExecutionId!.Value,0,"DueDate","2040-01-01")],"Intento protegido"),1,default));
        await Assert.ThrowsAsync<PlanningConflictException>(()=>db.Store.EditNotesAsync(new([new(saved[19].ExecutionId!.Value,0,"Observation","Debe revertirse"),new(saved[0].ExecutionId!.Value,0,"Responsible","Obsoleto")],"Lote obsoleto"),1,default));
        Assert.Equal(0L,await db.Scalar("SELECT count(*) FROM planning.executions WHERE observation IS NOT NULL"));
        var later=await db.Store.PreviewAsync(Input([Summary(due:22),..reports]),1,default);await db.Store.ConfirmAsync(later.Id,1,default);
        Assert.Equal(10,(await db.Store.QueryAsync(new(PageSize:100),default)).Items.Count(r=>r.Responsible is not null));
        var history=(await db.Store.DossierAsync(before[0].EpisodeId,default))!.History.Where(h=>h.Action=="ConsultationNotesEdited").ToArray();
        Assert.Equal(10,history.Length);Assert.All(history,h=>{Assert.Equal("Actor sintético local",h.ActorName);Assert.Contains("before",h.Details);Assert.Contains("after",h.Details);});
    }
    [PlanningDatabaseFact]
    public async Task ConcurrentCellEditsHaveOneWinnerAndNoSilentOverwrite()
    {
        await using var db=await TestDatabase.Create();var preview=await db.Store.PreviewAsync(Input(Summary(),Report()),1,default);await db.Store.ConfirmAsync(preview.Id,1,default);
        var row=Assert.Single((await db.Store.QueryAsync(new(),default)).Items);
        async Task<bool> Save(string value){try{await db.Store.EditNotesAsync(new([new(row.ExecutionId!.Value,0,"Observation",value)],"Cambio concurrente"),1,default);return true;}catch(PlanningConflictException){return false;}}
        var outcomes=await Task.WhenAll(Save("Uno\r\nSegunda línea"),Save("Dos\r\nSegunda línea"));Assert.Single(outcomes,x=>x);
        var saved=Assert.Single((await db.Store.QueryAsync(new(),default)).Items);Assert.Equal(1,saved.NoteVersion);Assert.DoesNotContain('\r',saved.Observation!);Assert.Contains('\n',saved.Observation!);
    }
    [PlanningDatabaseFact]
    public async Task SearchDatesEquivalenceAndSortingApplyToAllPagesAndCounts()
    {
        await using var db=await TestDatabase.Create();var preview=await db.Store.PreviewAsync(Input([Summary(),..Enumerable.Range(2,25).Select(i=>Report(i,"Prueba sintética "+i))]),1,default);await db.Store.ConfirmAsync(preview.Id,1,default);
        await db.Store.SaveEquivalenceAsync(new("Prueba sintética 26","Equivalente específico","Físico",false,null,"Clasificación verificada sintética"),1,default);
        var first=await db.Store.QueryAsync(new(Search:"j1",SearchMode:"Exact",PageSize:5),default);Assert.Equal(25,first.Total);Assert.Equal(25,first.Counts!.Executions);Assert.Equal(25,first.Counts.Unknown);
        Assert.Equal(5,(await db.Store.QueryAsync(new(Search:"J",SearchMode:"Prefix",Page:5,PageSize:5),default)).Items.Count);
        Assert.Empty((await db.Store.QueryAsync(new(Search:"J",SearchMode:"Exact"),default)).Items);
        Assert.Single((await db.Store.QueryAsync(new(Test:"Equivalente específico"),default)).Items);
        Assert.Empty((await db.Store.QueryAsync(new(InFrom:new(2026,11,17)),default)).Items);
        Assert.Equal(25,(await db.Store.QueryAsync(new(Search:"j1;OTHER",Until:new(2026,11,21)),default)).Total);
        var sorted=await db.Store.QueryAsync(new(Sort:"test",Descending:true,PageSize:5),default);
        Assert.Equal(first.Total,sorted.Total);Assert.All(sorted.Items,r=>Assert.Equal("Sin equivalencia",r.PlanningTest));
    }
    [PlanningDatabaseFact]
    public async Task HtmlRulesRequireBothFieldsIncludeTodayAndKeepUnknownIndependentOfHold()
    {
        await using var db=await TestDatabase.Create();var store=db.Store;
        var preview=await store.PreviewAsync(Input([Summary(),..Enumerable.Range(2,4).Select(i=>Report(i,"Prueba "+i))]),1,default);await store.ConfirmAsync(preview.Id,1,default);
        var today=DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow,"Central America Standard Time").DateTime);
        await db.Sql($"UPDATE planning.episodes SET due_date='{today:yyyy-MM-dd}'; UPDATE planning.executions SET no_program=true WHERE original_test='Prueba 2'; UPDATE planning.executions SET digitized_at='2026-11-17T14:30:00Z' WHERE original_test IN ('Prueba 3','Prueba 5'); UPDATE planning.executions SET tested_on='{today:yyyy-MM-dd}' WHERE original_test IN ('Prueba 4','Prueba 5');");
        var page=await store.QueryAsync(new(PageSize:1),default);Assert.Equal(4,page.Total);Assert.Equal(4,page.Counts!.Records);Assert.Equal(3,page.Counts.Pending);Assert.Equal(3,page.Counts.Overdue);Assert.Equal(1,page.Counts.Unknown);Assert.Equal(1,page.Counts.Digitized);
        Assert.Equal(3,(await store.QueryAsync(new(State:"Pending"),default)).Total);Assert.Equal(3,(await store.QueryAsync(new(Overdue:true),default)).Total);
        var episode=page.Items[0].EpisodeId;
        var held=await store.SaveControlAsync(episode,new(0,true,"  oeko   dr ","Retención de prueba aislada"),1,default);Assert.Equal("OEKO DR",held.Label);
        var tagged=await store.QueryAsync(new(Search:"oeko",Tagged:true,OnHold:true,PageSize:1),default);
        Assert.Equal(4,tagged.Total);Assert.Equal(4,tagged.Counts!.OnHoldTests);Assert.Equal(1,tagged.Counts.OnHoldJobs);Assert.Equal(1,tagged.Counts.TaggedJobs);Assert.Equal(0,tagged.Counts.Pending);Assert.Equal(0,tagged.Counts.Overdue);Assert.Equal(1,tagged.Counts.Unknown);
        Assert.True(Assert.Single(tagged.Items).OnHold);Assert.Equal(held,(await store.DossierAsync(episode,default))!.Control);
        await Assert.ThrowsAsync<PlanningConflictException>(()=>store.SaveControlAsync(episode,new(0,false,null,"Versión obsoleta"),1,default));
        var row=tagged.Items[0];await Assert.ThrowsAsync<PlanningValidationException>(()=>store.EditNotesAsync(new([new(row.ExecutionId!.Value,row.NoteVersion,"Observation","No debe guardarse")],"Intento retenido"),1,default));
        var released=await store.SaveControlAsync(episode,new(held.Version,false,held.Label,"Liberación de prueba aislada"),1,default);
        Assert.Null(released.HoldReason);Assert.Equal(3,(await store.QueryAsync(new(),default)).Counts!.Pending);
        Assert.Equal(2,(await store.DossierAsync(episode,default))!.History.Count(h=>h.Action=="JobControlChanged"));
        Assert.All((await store.QueryAsync(new(),default)).Items,r=>Assert.StartsWith("Prueba ",r.OriginalTest));
    }
    [PlanningDatabaseFact]
    public async Task ScopeAndColumnCalendarsFilterTheWholeServerSetAndExportTheSameRows()
    {
        await using var db=await TestDatabase.Create();var store=db.Store;
        var preview=await store.PreviewAsync(Input([Summary(),..Enumerable.Range(2,25).Select(i=>Report(i,"Prueba "+i))]),1,default);await store.ConfirmAsync(preview.Id,1,default);
        var today=DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow,"Central America Standard Time").DateTime);
        await db.Sql($"UPDATE planning.episodes SET due_date='{today.AddDays(-21):yyyy-MM-dd}'");
        Assert.Empty((await store.QueryAsync(new(Scope:"Operational"),default)).Items);
        var history=await store.QueryAsync(new(Scope:"History",PageSize:5),default);Assert.Equal(25,history.Total);Assert.Equal(25,history.Counts!.Pending);Assert.Equal("History",history.Scope!.Mode);Assert.Null(history.Scope.DueFrom);
        await db.Sql($"UPDATE planning.episodes SET due_date='{today.AddDays(-20):yyyy-MM-dd}'; UPDATE planning.executions SET tested_on='{today:yyyy-MM-dd}' WHERE original_test IN ('Prueba 2','Prueba 3');");
        var active=await store.QueryAsync(new(Scope:"Operational",PageSize:5),default);Assert.Equal(25,active.Total);Assert.Equal(today.AddDays(-20),active.Scope!.DueFrom);
        var filters=System.Text.Json.JsonSerializer.Serialize(new Dictionary<string,string>{{"TestedOn",today.ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture)},{"OriginalTest","Prueba"}});
        var query=new PlanningQuery(Scope:"Operational",ColumnFilters:filters,PageSize:1);
        var filtered=await store.QueryAsync(query,default);Assert.Single(filtered.Items);Assert.Equal(2,filtered.Total);Assert.Equal(2,filtered.Counts!.Pending);Assert.Equal(2,filtered.Counts.Overdue);Assert.Equal(0,filtered.Counts.Unknown);
        Assert.Single((await store.QueryAsync(query with{Page=2},default)).Items);
        var csv=System.Text.Encoding.UTF8.GetString(await store.ExportAsync(query,default));Assert.Equal(4,csv.Split('\n').Length);Assert.Contains("Operational",csv,StringComparison.Ordinal);Assert.Contains("Equivalencia",csv,StringComparison.Ordinal);
        await Assert.ThrowsAsync<PlanningValidationException>(()=>store.QueryAsync(new(ColumnFilters:"{\"TestedOn\":\"no-es-fecha\"}"),default));
        await Assert.ThrowsAsync<PlanningValidationException>(()=>store.QueryAsync(new(ColumnFilters:"{\"job);DROP TABLE planning.episodes;--\":\"x\"}"),default));
        await Assert.ThrowsAsync<PlanningValidationException>(()=>store.QueryAsync(new(Scope:"inventado"),default));
        Assert.Empty((await store.QueryAsync(new(ColumnFilters:"{\"Job\":\"' OR 1=1--\"}"),default)).Items);
    }
    [PlanningDatabaseFact]
    public async Task TimeAndSourceFiltersUseTheDisplayedValuesWithoutChangingImportedIdentity()
    {
        await using var db=await TestDatabase.Create();var store=db.Store;
        var preview=await store.PreviewAsync(Input(Summary(),Report(2,"Antes"),Report(3,"Después")),1,default);await store.ConfirmAsync(preview.Id,1,default);
        await db.Sql("UPDATE planning.executions SET digitized_at=CASE WHEN original_test='Antes' THEN '2026-11-17T05:00:00Z'::timestamptz ELSE '2026-11-17T07:00:00Z'::timestamptz END,tested_on='2026-11-16',digitized_by='Usuario de fixture';");
        var sorted=await store.QueryAsync(new(Sort:"digitizedAt"),default);Assert.Equal("Después",sorted.Items[0].OriginalTest);
        var filters="{\"DigitizedAt\":\"23:00\",\"Origin\":\"Report List\",\"DigitizedBy\":\"Usuario\",\"DigitizationState\":\"Digitado\"}";
        var row=Assert.Single((await store.QueryAsync(new(ColumnFilters:filters),default)).Items);Assert.Equal("Antes",row.OriginalTest);Assert.True(row.Complete);
        Assert.Equal(0,(await store.QueryAsync(new(),default)).Counts!.Pending);
    }
    [PlanningDatabaseFact]
    public async Task OfficialStateChangesArePreviewedAuditedFilteredAndIndependentOfDigitization()
    {
        await using var db=await TestDatabase.Create();var store=db.Store;
        var first=await store.PreviewAsync(Input(Summary() with{Official=new("Registered",null,null,1,"Active")},Report()),1,default);
        await store.ConfirmAsync(first.Id,1,default);
        var row=Assert.Single((await store.QueryAsync(new(),default)).Items);Assert.Equal("Registered",row.JobState);Assert.Equal("Sin información",row.DigitizationState);
        var next=await store.PreviewAsync(Input(Summary() with{Official=new("Report Closed","Official job",new(2026,11,17),1,"Hold Payment")},Report()),1,default);
        Assert.Contains(next.Changes,c=>c.Entity=="OfficialState"&&c.Before.Contains("Registered")&&c.After.Contains("Report Closed"));
        Assert.Equal("Registered",Assert.Single((await store.QueryAsync(new(),default)).Items).JobState);
        await store.ConfirmAsync(next.Id,1,default);
        var selected=await store.QueryAsync(new(ColumnFilters:"{\"JobState\":\"Report Closed\"}",Sort:"jobState"),default);row=Assert.Single(selected.Items);
        Assert.Equal(1,selected.Counts!.Pending);Assert.Equal("Official job",row.OfficialJobStatus);
        var dossier=(await store.DossierAsync(row.EpisodeId,default))!;Assert.Equal(new DateOnly(2026,11,17),dossier.Official!.CloseDate);Assert.Equal("Hold Payment",dossier.Official.CustomerStatus);
        Assert.Contains(dossier.History,h=>h.Details.Contains("Registered")&&h.Details.Contains("Report Closed"));
        Assert.Contains("Report Closed",System.Text.Encoding.UTF8.GetString(await store.ExportAsync(new(),default)));
    }
    [PlanningDatabaseFact]
    public async Task ReportOnlyEpisodeIsVisibleThenSummaryEnrichesSameExecutionWithoutDuplicates()
    {
        await using var db=await TestDatabase.Create();var store=db.Store;
        var preview=await store.PreviewAsync(Input(Report()),1,default);await store.ConfirmAsync(preview.Id,1,default);
        var row=Assert.Single((await store.QueryAsync(new(),default)).Items);Assert.Null(row.InDate);Assert.Equal("No informado",row.JobState);Assert.NotNull(row.ExecutionId);
        preview=await store.PreviewAsync(Input(Summary() with{Official=new("Registered")},Report()),1,default);Assert.Equal(0,preview.NewJobs);Assert.Equal(0,preview.NewTests);
        await store.ConfirmAsync(preview.Id,1,default);var enriched=Assert.Single((await store.QueryAsync(new(),default)).Items);
        Assert.Equal(row.EpisodeId,enriched.EpisodeId);Assert.Equal(row.ExecutionId,enriched.ExecutionId);Assert.NotNull(enriched.InDate);Assert.Equal("Registered",enriched.JobState);
    }
    [PlanningDatabaseFact]
    public async Task SummaryTrayRequiresExplicitManualResolutionAndPreservesItAcrossPartialLoads()
    {
        await using var db=await TestDatabase.Create();var store=db.Store;
        var preview=await store.PreviewAsync(Input(Summary()),1,default);await store.ConfirmAsync(preview.Id,1,default);
        var row=Assert.Single((await store.QueryAsync(new(),default)).Items);var dossier=(await store.DossierAsync(row.EpisodeId,default))!;
        var incident=Assert.Single(dossier.Incidents,i=>i.Kind=="SummaryWithoutReport");
        await Assert.ThrowsAsync<PlanningValidationException>(()=>store.DecideIncidentAsync(incident.Id,new(incident.Version,"Resolved","Sin pruebas"),1,default));
        await store.DecideIncidentAsync(incident.Id,new(incident.Version,"Held","Revisión autorizada"),1,default);
        await store.AddManualTestAsync(row.EpisodeId,new(dossier.Version,"AZO","AZO","Químico","Prueba verificada"),1,default);
        dossier=(await store.DossierAsync(row.EpisodeId,default))!;incident=Assert.Single(dossier.Incidents,i=>i.Kind=="SummaryWithoutReport");Assert.Equal("Held",incident.State);
        await store.DecideIncidentAsync(incident.Id,new(incident.Version,"Resolved","Pruebas completas verificadas por operador"),1,default);
        preview=await store.PreviewAsync(Input(Summary(due:22)),1,default);await store.ConfirmAsync(preview.Id,1,default);
        dossier=(await store.DossierAsync(row.EpisodeId,default))!;Assert.Equal("Resolved",Assert.Single(dossier.Incidents,i=>i.Kind=="SummaryWithoutReport").State);
        Assert.DoesNotContain(await store.ReviewsAsync(default),r=>r.Category=="SummaryWithoutReport");
        var original=Assert.Single(dossier.Tests).ExecutionId;
        preview=await store.PreviewAsync(Input(Summary(),Report()),1,default);Assert.Equal(1,preview.LinkedManualTests);await store.ConfirmAsync(preview.Id,1,default);
        Assert.Equal(original,Assert.Single((await store.DossierAsync(row.EpisodeId,default))!.Tests).ExecutionId);
    }
    [RealPlanningDatabaseFact]
    public async Task RealTipsRowsPreserveManualIdentityAndExplicitSummaryResolutionAcrossBothActualFiles()
    {
        var directory=Path.Combine((Environment.GetEnvironmentVariable("USERPROFILE")??Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),"Downloads");
        var summary=await File.ReadAllBytesAsync(Path.Combine(directory,"a0_-_pro-invoice_summary_17Nov2026045852237.xlsx"));
        var report=await File.ReadAllBytesAsync(Path.Combine(directory,"a0_-_delay_report_list_based_on_due_date_17Nov2026045945888.xlsx"));
        var input=PlanningImportReader.Read("real-summary.xlsx",summary,"real-report.xlsx",report,new("First",new(2026,11,17)));
        Assert.DoesNotContain(input.Issues,i=>i.Blocking);
        const string job="GUAT25038091-REV3";
        var source=Assert.Single(input.Rows,r=>r.File=="Summary"&&r.Job==job);
        var actualTests=input.Rows.Where(r=>r.File=="Report List"&&r.Job==job).ToArray();Assert.Equal(24,actualTests.Length);
        var actual=Assert.Single(actualTests,r=>r.Test=="Appearance After Laundering");
        await using var db=await TestDatabase.Create();var store=db.Store;
        // Real source rows in an explicitly partial CI fixture; decisions below are test operator actions, never TIPS claims.
        var partial=input with{Rows=[source],Fingerprint=PlanningImportReader.Hash(System.Text.Encoding.UTF8.GetBytes("real-rows-ci-summary-only"))};
        var preview=await store.PreviewAsync(partial,1,default);await store.ConfirmAsync(preview.Id,1,default);
        var row=Assert.Single((await store.QueryAsync(new(Search:job),default)).Items);var dossier=(await store.DossierAsync(row.EpisodeId,default))!;
        Assert.Equal(source.Official!.SampleStatus,row.JobState);Assert.Equal(source.Wip!.Vendor,dossier.Wip.Vendor);
        await store.AddManualTestAsync(row.EpisodeId,new(dossier.Version,actual.Test!,"DIMENSIONAL","Physical","CI local: clasificación real restaurada; decisión manual de prueba"),1,default);
        dossier=(await store.DossierAsync(row.EpisodeId,default))!;var manual=Assert.Single(dossier.Tests).ExecutionId;
        var pending=Assert.Single(dossier.Incidents,i=>i.Kind=="SummaryWithoutReport");Assert.Equal("Pending",pending.State);
        await store.DecideIncidentAsync(pending.Id,new(pending.Version,"Resolved","CI local: confirmación explícita de asignación manual"),1,default);
        var complete=input with{Rows=[source,..actualTests],Fingerprint=PlanningImportReader.Hash(System.Text.Encoding.UTF8.GetBytes("real-rows-ci-both"))};
        preview=await store.PreviewAsync(complete,1,default);Assert.Equal(1,preview.LinkedManualTests);Assert.Equal(23,preview.NewTests);await store.ConfirmAsync(preview.Id,1,default);
        dossier=(await store.DossierAsync(row.EpisodeId,default))!;Assert.Equal(24,dossier.Tests.Count);Assert.Contains(dossier.Tests,t=>t.ExecutionId==manual&&t.Origin=="Manual");
        Assert.Equal("Resolved",Assert.Single(dossier.Incidents,i=>i.Kind=="SummaryWithoutReport").State);
        Assert.True((await store.ConfirmAsync(preview.Id,1,default)).AlreadyApplied);
        await store.AddManualTestAsync(row.EpisodeId,new(dossier.Version,actual.Test!,"DIMENSIONAL","Physical","CI local: repetición autorizada de una prueba real"),1,default);
        preview=await store.PreviewAsync(complete with{Fingerprint=Guid.NewGuid().ToString("N")},1,default);
        Assert.DoesNotContain(preview.Reviews,r=>r.Category=="ExecutionIdentity"&&r.OriginalTest==actual.Test);await store.ConfirmAsync(preview.Id,1,default);
        Assert.Equal(25,(await store.DossierAsync(row.EpisodeId,default))!.Tests.Count);
        var reviewPage=await store.ReviewPageAsync("ExecutionIdentity",job,1,1,default);Assert.Equal(0,reviewPage.Total);Assert.Empty(reviewPage.Items);
    }
    [RealPlanningDatabaseFact]
    public async Task RealReviewWorkspaceFiltersDecisionsCatalogueAndExportRemainConsistentAcrossReimport()
    {
        var directory=Path.Combine(Environment.GetEnvironmentVariable("USERPROFILE")??Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Downloads");
        var input=PlanningImportReader.Read("a0_-_pro-invoice_summary_17Nov2026045852237.xlsx",await File.ReadAllBytesAsync(Path.Combine(directory,"a0_-_pro-invoice_summary_17Nov2026045852237.xlsx")),
            "a0_-_delay_report_list_based_on_due_date_17Nov2026045945888.xlsx",await File.ReadAllBytesAsync(Path.Combine(directory,"a0_-_delay_report_list_based_on_due_date_17Nov2026045945888.xlsx")),new("First",new(2026,11,17)));
        const string solo="GUAT25002016-REV4-REV5-RE",both="GUAT25038091-REV3";
        // Unmodified actual rows, explicitly partial CI scope. Manual decisions are local operator actions.
        input=input with{Rows=input.Rows.Where(r=>r.Job is solo or both or "GUAT26011849" or "GUAT26010723").ToArray(),Fingerprint=Guid.NewGuid().ToString("N")};
        await using var db=await TestDatabase.Create();var store=db.Store;
        var preview=await store.PreviewAsync(input,1,default);await store.ConfirmAsync(preview.Id,1,default);
        var query=new PlanningReviewQuery(Search:solo,PageSize:1);
        var page=await store.ReviewWorkspaceAsync(query,default);var row=Assert.Single(page.Items);Assert.Equal(1,page.Total);Assert.Equal(input.Rows.Single(r=>r.Job==solo&&r.File=="Summary").Official!.SampleStatus,row.OfficialState);
        Assert.NotNull(row.Buyer);Assert.NotNull(row.InDate);Assert.NotNull(row.IncidentId);
        Assert.Single((await store.ReviewWorkspaceAsync(query with{ColumnFilters="{\"State\":\"Pendiente\"}"},default)).Items);
        Assert.Single((await store.ReviewWorkspaceAsync(new(Category:"MissingVendor",Search:"GUAT26011849",ColumnFilters:"{\"Vendor\":\"No informado\"}"),default)).Items);
        var counts=page.Counters;Assert.Contains(counts,c=>c.Category=="SummaryWithoutReport"&&c.Pending>0);Assert.Contains(counts,c=>c.Category=="Admission"&&c.Total==0);Assert.Equal(11,counts.Count);
        var filtered=await store.ReviewWorkspaceAsync(query with{ColumnFilters="{\"Job\":\"DOES_NOT_EXIST\"}"},default);Assert.Empty(filtered.Items);Assert.All(filtered.Counters,c=>Assert.Equal(0,c.Total));
        var dates=await store.ReviewWorkspaceAsync(query with{ColumnFilters="{\"InDateFrom\":\"2040-01-01\"}"},default);Assert.Equal(0,dates.Total);
        await Assert.ThrowsAsync<PlanningValidationException>(()=>store.ReviewWorkspaceAsync(query with{Sort="job; DROP TABLE planning.episodes"},default));
        await Assert.ThrowsAsync<PlanningValidationException>(()=>store.ReviewWorkspaceAsync(query with{ColumnFilters="{\"untrusted\":\"x\"}"},default));
        await store.DecideIncidentAsync(row.IncidentId!.Value,new(row.Version,"Held","Validación local con registros reales: revisión de asignación"),1,default);
        row=Assert.Single((await store.ReviewWorkspaceAsync(query with{Situation="Held"},default)).Items);Assert.NotNull(row.DecidedAt);Assert.NotNull(row.DecidedBy);
        var dossier=(await store.DossierAsync(row.EpisodeId!.Value,default))!;
        const string original="Appearance After Laundering";
        await store.SaveEquivalenceAsync(new(original,"DIMENSIONAL","Physical",false,null,"Clasificación real restaurada: validación local"),1,default);
        var mapping=Assert.Single(await store.EquivalencesAsync(default));
        await Assert.ThrowsAsync<PlanningConflictException>(()=>store.AddManualTestAsync(dossier.EpisodeId,new(dossier.Version,original,"DIMENSIONAL","Physical","Validación de catálogo obsoleto",mapping.Version+1),1,default));
        Assert.Empty((await store.DossierAsync(dossier.EpisodeId,default))!.Tests);
        await store.AddManualTestAsync(dossier.EpisodeId,new(dossier.Version,original,"DIMENSIONAL","Physical","Asignación manual de validación local, no declaración oficial de TIPS",mapping.Version),1,default);
        await store.DecideIncidentAsync(row.IncidentId!.Value,new(row.Version,"Resolved","Completado y verificado en la base CI local"),1,default);
        Assert.Empty((await store.ReviewWorkspaceAsync(query,default)).Items);
        var resolved=Assert.Single((await store.ReviewWorkspaceAsync(query with{Situation="Resolved"},default)).Items);
        var export=System.Text.Encoding.UTF8.GetString(await store.ExportReviewsAsync(query with{Situation="Resolved"},default));Assert.Contains(solo,export);Assert.Contains("Resuelto",export);
        var second=await store.PreviewAsync(input with{Cut=new("Last",new(2026,11,17)),Fingerprint=Guid.NewGuid().ToString("N")},1,default);await store.ConfirmAsync(second.Id,1,default);
        Assert.Empty((await store.ReviewWorkspaceAsync(query,default)).Items);Assert.Single((await store.DossierAsync(dossier.EpisodeId,default))!.Tests);
        resolved=Assert.Single((await store.ReviewWorkspaceAsync(query with{Situation="Resolved"},default)).Items);
        await Assert.ThrowsAsync<PlanningConflictException>(()=>store.DecideIncidentAsync(resolved.IncidentId!.Value,new(resolved.Version-1,"Pending","Intento concurrente"),1,default));
        await store.DecideIncidentAsync(resolved.IncidentId!.Value,new(resolved.Version,"Pending","Reapertura explícita auditada"),1,default);
        Assert.Single((await store.ReviewWorkspaceAsync(query,default)).Items);
        Assert.True((await store.ConfirmAsync(second.Id,1,default)).AlreadyApplied);
        Assert.Contains((await store.DossierAsync(dossier.EpisodeId,default))!.History,h=>h.Action=="IncidentDecision"&&h.Details.Contains("Resolved",StringComparison.Ordinal));
    }
    [RealPlanningDatabaseFact]
    public async Task ActualFileInspectionAndBatchScopesPreserveResolvedManualCasesAndExportBeyondPage()
    {
        var dir=Path.Combine(Environment.GetEnvironmentVariable("USERPROFILE")!,"Downloads");
        var input=PlanningImportReader.Read("summary.xlsx",await File.ReadAllBytesAsync(Path.Combine(dir,"a0_-_pro-invoice_summary_17Nov2026045852237.xlsx")),
            "report.xlsx",await File.ReadAllBytesAsync(Path.Combine(dir,"a0_-_delay_report_list_based_on_due_date_17Nov2026045945888.xlsx")),new("First",new(2026,11,18)));
        const string solo="GUAT25002016-REV4-REV5-RE",both="GUAT25038091-REV3",missing="GUAT26011849";
        input=input with{Rows=input.Rows.Where(r=>r.Job is solo or both or missing or "GUAT26010723").ToArray(),Fingerprint=Guid.NewGuid().ToString("N")};
        await using var db=await TestDatabase.Create();var store=db.Store;
        var preview=await store.PreviewAsync(input,1,default);var inspection=(await store.InspectImportAsync(preview.Id,default))!;
        Assert.Equal(input.Rows.Count,inspection.ProcessedRows);Assert.Equal(preview.NewTests,inspection.NewCombinations);Assert.Equal(0,inspection.ExistingTests);
        Assert.Contains(inspection.Items,r=>r.Category=="SummaryWithoutReport"&&r.Job==solo&&r.Buyer is not null);
        Assert.Contains(inspection.Items,r=>r.Category=="ReportWithoutSummary"&&r.Job=="GUAT26010723");
        Assert.Contains(inspection.Items,r=>r.Category=="MissingParty"&&r.Job==missing&&r.Explanation=="Falta Vendor");
        Assert.Contains(inspection.Items,r=>r.Category=="PartyComplemented"&&r.Job==both&&r.Vendor is not null);
        Assert.All(inspection.Items.Where(r=>r.Category=="MissingEquivalence"),r=>Assert.True(r.RelatedJobs>0));
        await store.ConfirmAsync(preview.Id,1,default);
        var query=new PlanningReviewQuery(Category:"SummaryWithoutReport",Scope:"Batch",BatchId:preview.Id,PageSize:1);
        var scoped=await store.ReviewWorkspaceAsync(query,default);Assert.True(scoped.Total>0);Assert.Single(scoped.Items);
        Assert.Empty((await store.ReviewWorkspaceAsync(query with{BatchId=Guid.NewGuid()},default)).Items);
        Assert.Empty((await store.ReviewWorkspaceAsync(query with{ImportedFrom=new(2040,1,1)},default)).Items);
        var csv=System.Text.Encoding.UTF8.GetString(await store.ExportReviewsAsync(query,default));Assert.Equal(scoped.Total+1,csv.Split("\r\n",StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.True((await store.QueryAsync(new(BatchId:preview.Id,Scope:"History"),default)).Total>0);
        Assert.Equal(0,(await store.QueryAsync(new(BatchId:Guid.NewGuid(),Scope:"History"),default)).Total);
        var unclassified=Assert.Single((await store.ReviewWorkspaceAsync(new(Category:"MissingEquivalence",Search:both,PageSize:1),default)).Items);
        await store.DecideReviewAsync(new(unclassified.Key,unclassified.Category,unclassified.Version,"Held","Decisión explícita local de revisión"),1,default);
        var held=Assert.Single((await store.ReviewWorkspaceAsync(new(Category:"MissingEquivalence",Situation:"Held",SelectedKeys:System.Text.Json.JsonSerializer.Serialize(new[]{unclassified.Key})),default)).Items);
        Assert.NotNull(held.DecidedBy);Assert.NotNull(held.DecidedAt);
        await Assert.ThrowsAsync<PlanningConflictException>(()=>store.DecideReviewAsync(new(held.Key,held.Category,held.Version-1,"Excluded","Versión obsoleta"),1,default));
        await store.DecideReviewAsync(new(held.Key,held.Category,held.Version,"Excluded","Exclusión justificada para validar persistencia local"),1,default);
        var excluded=Assert.Single((await store.ReviewWorkspaceAsync(new(Category:"MissingEquivalence",Situation:"Excluded",SelectedKeys:System.Text.Json.JsonSerializer.Serialize(new[]{held.Key})),default)).Items);
        Assert.Equal("Excluded",excluded.State);
        var next=await store.PreviewAsync(input with{Cut=new("Last",new(2026,11,18)),Fingerprint=Guid.NewGuid().ToString("N")},1,default);await store.ConfirmAsync(next.Id,1,default);
        Assert.Single((await store.ReviewWorkspaceAsync(new(Category:"MissingEquivalence",Situation:"Excluded",SelectedKeys:System.Text.Json.JsonSerializer.Serialize(new[]{held.Key})),default)).Items);
        Assert.True((await store.ConfirmAsync(next.Id,1,default)).AlreadyApplied);
        var final=(await store.InspectImportAsync(next.Id,default))!;Assert.Equal(0,final.Preview.NewJobs);Assert.Equal(0,final.Preview.NewTests);Assert.NotNull(final.AppliedAt);
        Assert.True(final.ExistingTests>0);Assert.NotEmpty(final.Laboratories);
    }
    [PlanningDatabaseFact]
    public async Task UnnamedSourceRemainsDistinctFromUnmappedTestAndRetainedPartyDecisionIsVersioned()
    {
        await using var db=await TestDatabase.Create();var store=db.Store;
        var summary=Summary() with{Wip=Summary().Wip! with{Buyer=null,Vendor="Vendor verificado"}};
        var input=Input(summary,Report() with{Test=null});
        var preview=await store.PreviewAsync(input,1,default);Assert.DoesNotContain(preview.Changes,c=>c.Entity=="Test");
        await store.ConfirmAsync(preview.Id,1,default);
        Assert.Contains((await store.ReviewWorkspaceAsync(new(Category:"IncompleteData"),default)).Items,r=>r.Reason!.Contains("Test_Name",StringComparison.Ordinal));
        Assert.Empty((await store.ReviewWorkspaceAsync(new(Category:"MissingEquivalence"),default)).Items);
        var party=Assert.Single((await store.ReviewWorkspaceAsync(new(Category:"MissingParty"),default)).Items);Assert.Equal("Falta Buyer",party.Reason);
        await store.DecideReviewAsync(new(party.Key,party.Category,party.Version,"Held","Buyer pendiente de verificación documental"),1,default);
        var held=Assert.Single((await store.ReviewWorkspaceAsync(new(Category:"MissingParty",Situation:"Held"),default)).Items);Assert.NotNull(held.DecidedAt);
        var dossier=(await store.DossierAsync(party.EpisodeId!.Value,default))!;
        await store.EditWipAsync(dossier.EpisodeId,new(dossier.Version,dossier.Wip with{Buyer="Buyer verificado"},"Documento verificado para prueba aislada"),1,default);
        Assert.Empty((await store.ReviewWorkspaceAsync(new(Category:"MissingParty"),default)).Items);
    }
    [RealPlanningDatabaseFact]
    public async Task ActualNextDayJobCountsOneEmergencyAndPreservesItsTwoOriginalExecutions()
    {
        var dir=Path.Combine(Environment.GetEnvironmentVariable("USERPROFILE")!,"Downloads");
        var input=PlanningImportReader.Read("summary.xlsx",await File.ReadAllBytesAsync(Path.Combine(dir,"a0_-_pro-invoice_summary_17Nov2026045852237.xlsx")),
            "report.xlsx",await File.ReadAllBytesAsync(Path.Combine(dir,"a0_-_delay_report_list_based_on_due_date_17Nov2026045945888.xlsx")),new("First",new(2026,11,18)));
        // Exact original rows in a disposable CI database, no synthetic job or test names.
        input=input with{Rows=input.Rows.Where(r=>r.Job=="GUAT26011508-REV1").ToArray(),Fingerprint=Guid.NewGuid().ToString("N")};
        Assert.Equal("Next Day",Assert.Single(input.Rows,r=>r.File=="Summary").Wip!.Service);Assert.Equal(2,input.Rows.Count(r=>r.File=="Report List"));
        await using var db=await TestDatabase.Create();var store=db.Store;var p=await store.PreviewAsync(input,1,default);
        var inspection=(await store.InspectImportAsync(p.Id,default))!;Assert.Equal(1,p.NewJobs);Assert.Equal(2,p.NewTests);Assert.Equal(1,inspection.NewEmergencies);
        Assert.Equal(2,inspection.Items.Count(r=>r.Category=="Emergency"));Assert.All(inspection.Items.Where(r=>r.Category=="Emergency"),r=>Assert.Equal("Next Day",r.Service));
        await store.ConfirmAsync(p.Id,1,default);Assert.Equal(2,(await store.QueryAsync(new(Search:"GUAT26011508-REV1"),default)).Total);
        Assert.True((await store.ConfirmAsync(p.Id,1,default)).AlreadyApplied);
    }
    [PlanningDatabaseFact]
    public async Task ExplicitLegacyNoProgramLaboratoryRemainsOriginalButCannotBeAssignedAsManualLaboratory()
    {
        await using var db=await TestDatabase.Create();var store=db.Store;
        await store.SaveEquivalenceAsync(new("Original heredado","Nombre heredado","NO PROGRAMAR",false,null,"Clasificación original explícita del HTML"),1,default);
        var mapping=Assert.Single(await store.EquivalencesAsync(default));Assert.True(mapping.NoProgram);Assert.Equal("Nombre heredado",mapping.Equivalence);Assert.Equal("NO PROGRAMAR",mapping.Laboratory);
        var preview=await store.PreviewAsync(Input(Summary(),Report(test:"Original heredado")),1,default);await store.ConfirmAsync(preview.Id,1,default);
        var row=Assert.Single((await store.QueryAsync(new(State:"NoProgramar"),default)).Items);Assert.True(row.NoProgram);Assert.Equal("Original heredado",row.OriginalTest);
        var inspection=(await store.InspectImportAsync(preview.Id,default))!;Assert.Equal(1,Assert.Single(inspection.Laboratories).NoProgram);
        var dossier=(await store.DossierAsync(row.EpisodeId,default))!;
        await Assert.ThrowsAsync<PlanningValidationException>(()=>store.AddManualTestAsync(row.EpisodeId,new(dossier.Version,mapping.OriginalTest,mapping.Equivalence!,mapping.Laboratory!,"No es una prueba programable"),1,default));
    }
    private static PlanningSourceRow Summary(int due=20)=>new("Summary","S",2,"J1",null,new("Buyer",null,"Regular",new(2026,11,16),new(2026,11,due),null,null,null),"{}");
    private static PlanningSourceRow Report(int row=2,string test="AZO")=>new("Report List","R",row,"J1",test,null,"{}");
    private static PlanningImportInput Input(params PlanningSourceRow[] rows)=>new(new("Noon",new(2026,11,16)),[1],[2],"summary.xlsx","report.xlsx",Guid.NewGuid().ToString("N"),[],rows,[]);
    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly string _admin;
        private readonly string _name;
        internal readonly PlanningDatabase _database;
        public string ConnectionString { get; }
        public PostgresPlanningStore Store{get;}
        private TestDatabase(string admin,string name,PlanningDatabase database,string connectionString){_admin=admin;_name=name;_database=database;ConnectionString=connectionString;Store=new(database);}
        public static async Task<TestDatabase> Create(bool migrateIdentity=true)
        {
            var cs=new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("LIMS_PLANNING_TEST_CONNECTION"));
            if(cs.Host!="127.0.0.1"||cs.Port!=55441||cs.Database!="lims_planning_dev")throw new InvalidOperationException("Se rechaza cualquier servidor/base distinta de la copia local autorizada.");
            cs.Database="postgres";var admin=cs.ConnectionString;var name="lims_planning_ci_"+Guid.NewGuid().ToString("N");
            await using(var c=new NpgsqlConnection(admin)){await c.OpenAsync();await using var cmd=new NpgsqlCommand("CREATE DATABASE "+name,c);await cmd.ExecuteNonQueryAsync();}
            cs.Database=name;var database=new PlanningDatabase(cs.ConnectionString);var fixture=new TestDatabase(admin,name,database,cs.ConnectionString);
            await fixture.Sql("CREATE TABLE public.departamentos(id integer PRIMARY KEY,nombre text);CREATE TABLE public.usuarios(id integer PRIMARY KEY,nombre text,id_departamento integer,activo boolean);CREATE TABLE public.usuarios_permisos(id_usuario integer,permiso_key text,PRIMARY KEY(id_usuario,permiso_key));INSERT INTO public.usuarios VALUES(1,'Actor sintético local',NULL,true);"+PlanningSchema.Sql+PlanningNotesSchema.Sql+PlanningControlsSchema.Sql+PlanningOfficialSchema.Sql+PlanningReviewSchema.Sql);
            if(migrateIdentity)
            {
                await fixture.Sql(PlanningIdentitySchema.Sql+PlanningIdentitySchema.AccessSql+ReportListSchema.Sql+ReportScheduleSchema.Sql);
                await using var identity = new PlanningIdentityDatabase(cs.ConnectionString);
                await identity.RegisterActorAsync(database,1,default);
            }
            return fixture;
        }
        public async Task Sql(string sql){await using var c=await _database.Source.OpenConnectionAsync();await using var cmd=new NpgsqlCommand(sql,c);await cmd.ExecuteNonQueryAsync();}
        public async Task<object?> Scalar(string sql){await using var c=await _database.Source.OpenConnectionAsync();await using var cmd=new NpgsqlCommand(sql,c);return await cmd.ExecuteScalarAsync();}
        public async ValueTask DisposeAsync()
        {
            await _database.DisposeAsync();
            if(!_name.StartsWith("lims_planning_ci_",StringComparison.Ordinal)||_name.Length!=49)throw new InvalidOperationException("Nombre CI inesperado; no se elimina.");
            await using var c=new NpgsqlConnection(_admin);await c.OpenAsync();await using var cmd=new NpgsqlCommand("DROP DATABASE "+_name,c);await cmd.ExecuteNonQueryAsync();
        }
    }
}

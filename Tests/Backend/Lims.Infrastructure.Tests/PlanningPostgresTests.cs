using Lims.Application.Planning;
using Lims.Contracts.Planning;
using Lims.Infrastructure.Planning;
using Npgsql;

namespace Lims.Infrastructure.Tests;

public sealed class PlanningDatabaseFactAttribute : FactAttribute
{
    public PlanningDatabaseFactAttribute(){if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("LIMS_PLANNING_TEST_CONNECTION")))Skip="Requiere PostgreSQL local aislado; ejecutar scripts/test-planning.ps1.";}
}
public sealed class PlanningPostgresTests
{
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
        var row=Assert.Single((await db.Store.QueryAsync(new(State:"NoProgramar"),default)).Items);Assert.Equal("NoProgramar",row.DigitizationState);
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
        Assert.Equal(first.Total,sorted.Total);Assert.NotEqual(first.Items[0].OriginalTest,sorted.Items[0].OriginalTest);
    }
    private static PlanningSourceRow Summary(int due=20)=>new("Summary","S",2,"J1",null,new("Buyer",null,"Regular",new(2026,11,16),new(2026,11,due),null,null,null),"{}");
    private static PlanningSourceRow Report(int row=2,string test="AZO")=>new("Report List","R",row,"J1",test,null,"{}");
    private static PlanningImportInput Input(params PlanningSourceRow[] rows)=>new(new("Noon",new(2026,11,16)),[1],[2],"summary.xlsx","report.xlsx",Guid.NewGuid().ToString("N"),[],rows,[]);
    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly string _admin;
        private readonly string _name;
        private readonly PlanningDatabase _database;
        public PostgresPlanningStore Store{get;}
        private TestDatabase(string admin,string name,PlanningDatabase database){_admin=admin;_name=name;_database=database;Store=new(database);}
        public static async Task<TestDatabase> Create()
        {
            var cs=new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("LIMS_PLANNING_TEST_CONNECTION"));
            if(cs.Host!="127.0.0.1"||cs.Port!=55441||cs.Database!="lims_planning_dev")throw new InvalidOperationException("Se rechaza cualquier servidor/base distinta de la copia local autorizada.");
            cs.Database="postgres";var admin=cs.ConnectionString;var name="lims_planning_ci_"+Guid.NewGuid().ToString("N");
            await using(var c=new NpgsqlConnection(admin)){await c.OpenAsync();await using var cmd=new NpgsqlCommand("CREATE DATABASE "+name,c);await cmd.ExecuteNonQueryAsync();}
            cs.Database=name;var database=new PlanningDatabase(cs.ConnectionString);var fixture=new TestDatabase(admin,name,database);
            await fixture.Sql("CREATE TABLE public.departamentos(id integer PRIMARY KEY,nombre text);CREATE TABLE public.usuarios(id integer PRIMARY KEY,nombre text,id_departamento integer,activo boolean);CREATE TABLE public.usuarios_permisos(id_usuario integer,permiso_key text,PRIMARY KEY(id_usuario,permiso_key));INSERT INTO public.usuarios VALUES(1,'Actor sintético local',NULL,true);"+PlanningSchema.Sql+PlanningNotesSchema.Sql);
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

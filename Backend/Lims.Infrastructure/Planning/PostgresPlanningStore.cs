using System.Data;
using System.Globalization;
using System.Text.Json;
using Lims.Application.Planning;
using Lims.Contracts.Planning;
using Npgsql;
using NpgsqlTypes;

namespace Lims.Infrastructure.Planning;

public sealed partial class PostgresPlanningStore(PlanningDatabase database, PlanningIdentityDatabase? identities = null) : IPlanningStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);
    private static T Read<T>(string value) => JsonSerializer.Deserialize<T>(value, Json) ?? throw new InvalidDataException("Documento de Planificación inválido.");
    private static NpgsqlCommand Command(NpgsqlConnection connection, NpgsqlTransaction? transaction, string sql, params (string Name, object? Value)[] args)
    {
        var command = new NpgsqlCommand(sql, connection, transaction);
        foreach (var (name, value) in args)
        {
            var parameter = new NpgsqlParameter { ParameterName = name, Value = value ?? DBNull.Value };
            if (value is null) parameter.NpgsqlDbType = NpgsqlDbType.Text;
            command.Parameters.Add(parameter);
        }
        return command;
    }
    private static async Task Execute(NpgsqlConnection connection, NpgsqlTransaction? tx, string sql, CancellationToken ct, params (string Name, object? Value)[] args)
    { await using var cmd = Command(connection, tx, sql, args); await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false); }
    private static async Task<long> Revision(NpgsqlConnection connection, NpgsqlTransaction tx, bool locked, CancellationToken ct)
    {
        await using var cmd = Command(connection, tx, "SELECT value FROM planning.revision WHERE singleton" + (locked ? " FOR UPDATE" : ""));
        return (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) ?? throw new InvalidDataException("Falta revisión."));
    }
    private static Task Bump(NpgsqlConnection c, NpgsqlTransaction tx, CancellationToken ct) => Execute(c, tx, "UPDATE planning.revision SET value=value+1 WHERE singleton", ct);
    private static Task Audit(NpgsqlConnection c, NpgsqlTransaction tx, Guid? episode, string action, int actor, string reason, object details, CancellationToken ct) =>
        Execute(c, tx, "INSERT INTO planning.history(id,episode_id,action,actor_id,reason,details) VALUES(@id,@episode::uuid,@action,@actor,@reason,@details::jsonb)", ct,
            ("id", Guid.NewGuid()), ("episode", episode), ("action", action), ("actor", actor), ("reason", reason), ("details", Serialize(details)));
    private static string Reason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 1000) throw new PlanningValidationException("Escriba un motivo de 1 a 1000 caracteres.");
        return reason.Trim();
    }
    private static string Required(string? value, int max, string field)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > max || value.Any(char.IsControl)) throw new PlanningValidationException($"{field} requerido, sin caracteres de control y hasta {max} caracteres.");
        return value.Trim();
    }
    private static async Task<List<StoredEpisode>> Episodes(NpgsqlConnection c, NpgsqlTransaction? tx, string[] jobs, CancellationToken ct)
    {
        await using var cmd = Command(c, tx, "SELECT id,job,buyer,vendor,service,in_date,due_date,amount,currency,usd,version,manual_fields,source_in_date,official_fields::text,EXISTS(SELECT 1 FROM planning.source_rows evidence WHERE evidence.episode_id=e.id AND evidence.file='Summary') FROM planning.episodes e WHERE job=ANY(@jobs)", ("jobs", jobs));
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var result = new List<StoredEpisode>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false)) result.Add(new(reader.GetGuid(0), reader.GetString(1), Wip(reader, 2), reader.GetInt64(10), reader.GetFieldValue<string[]>(11), NullableDate(reader, 12),Read<PlanningOfficialState>(reader.GetString(13)),reader.GetBoolean(14)));
        return result;
    }
    private static WipValues Wip(NpgsqlDataReader r, int o) => new(NullableString(r,o), NullableString(r,o+1), NullableString(r,o+2), NullableDate(r,o+3), NullableDate(r,o+4), r.IsDBNull(o+5)?null:r.GetDecimal(o+5), NullableString(r,o+6), r.IsDBNull(o+7)?null:r.GetDecimal(o+7));
    private static string? NullableString(NpgsqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
    private static DateOnly? NullableDate(NpgsqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetFieldValue<DateOnly>(i);
    private static async Task<List<StoredExecution>> Executions(NpgsqlConnection c, NpgsqlTransaction? tx, Guid[] episodes, CancellationToken ct)
    {
        await using var cmd = Command(c,tx,"SELECT id,episode_id,original_test,equivalence,laboratory,origin,execution_type,no_program FROM planning.executions WHERE episode_id=ANY(@episodes)",("episodes",episodes));
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false); var result=new List<StoredExecution>();
        while(await reader.ReadAsync(ct).ConfigureAwait(false)) result.Add(new(reader.GetGuid(0),reader.GetGuid(1),reader.GetString(2),NullableString(reader,3),NullableString(reader,4),reader.GetString(5),NullableString(reader,6),reader.GetBoolean(7)));
        await reader.DisposeAsync().ConfigureAwait(false);
        var derived=result.Where(x=>x.ExecutionType is "DisneyRfSccp:v1" or "ReactToxicElements:v1").Select(x=>x.Id).ToArray();
        if(derived.Length>0)
        {
            var backed=new HashSet<Guid>();
            await using var source=Command(c,tx,"SELECT DISTINCT execution_id FROM planning.source_rows WHERE execution_id=ANY(@ids) AND file='Report List'",("ids",derived));
            await using var evidence=await source.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while(await evidence.ReadAsync(ct).ConfigureAwait(false))backed.Add(evidence.GetGuid(0));
            for(var i=0;i<result.Count;i++)if(backed.Contains(result[i].Id))result[i]=result[i] with{HasReportSource=true};
        }
        return result;
    }
    public async Task<IReadOnlyList<PlanningEquivalence>> EquivalencesAsync(CancellationToken ct)
    { await using var c=await database.Source.OpenConnectionAsync(ct).ConfigureAwait(false); return await HasReactCatalogue(c,null,ct).ConfigureAwait(false)?await ReactMappings(c,null,ct).ConfigureAwait(false):await Mappings(c,null,ct).ConfigureAwait(false); }
    private static async Task<List<PlanningEquivalence>> Mappings(NpgsqlConnection c,NpgsqlTransaction? tx,CancellationToken ct)
    {
        // Legacy HTML also stores the explicit NO PROGRAMAR decision in the laboratory field. Preserve its original values.
        await using var cmd=Command(c,tx,"SELECT original_test,equivalence,laboratory,(no_program OR upper(coalesce(trim(laboratory),''))='NO PROGRAMAR'),version FROM planning.equivalences ORDER BY original_test");
        await using var r=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false); var result=new List<PlanningEquivalence>();
        while(await r.ReadAsync(ct).ConfigureAwait(false)) result.Add(new(r.GetString(0),NullableString(r,1),NullableString(r,2),!r.IsDBNull(3)&&r.GetBoolean(3),r.GetInt64(4)));
        return result;
    }
    public async Task<PlanningImportPreview> PreviewAsync(PlanningImportInput input, int actor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        await using var c=await database.Source.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var tx=await c.BeginTransactionAsync(IsolationLevel.ReadCommitted,ct).ConfigureAwait(false);
        // The revision row serializes previews and mutations, including simultaneous imports of the same bytes.
        var revision=await Revision(c,tx,true,ct).ConfigureAwait(false);
        string? reactToken=null;
        if(input.Cut.Policy is "HtmlReact" or "HtmlReactSide2" or "ReactTable")
        {
            await RequireReactSchema(c,tx,ct).ConfigureAwait(false);reactToken=await ReadReactStateToken(c,tx,ct).ConfigureAwait(false);
            if(input.SourceSnapshotToken is not null&&input.SourceSnapshotToken!=reactToken)throw new PlanningConflictException("Las tablas de React cambiaron al preparar la revisión. Actualice la instantánea.");
        }
        await using(var find=Command(c,tx,"SELECT preview::text,status FROM planning.batches WHERE fingerprint=@hash AND (status='Applied' OR (actor_id=@actor AND base_revision=@revision AND (@react::text IS NULL OR plan->>'reactStateToken'=@react)))",("hash",input.Fingerprint),("actor",actor),("revision",revision),("react",reactToken)))
        await using(var r=await find.ExecuteReaderAsync(ct).ConfigureAwait(false))
            if(await r.ReadAsync(ct).ConfigureAwait(false)) return Read<PlanningImportPreview>(r.GetString(0)) with { Status=r.GetString(1) };
        var jobs=input.Rows.Select(r=>r.Job).Distinct(StringComparer.Ordinal).ToArray();
        var episodes=await Episodes(c,tx,jobs,ct).ConfigureAwait(false);
        var executions=await Executions(c,tx,episodes.Select(e=>e.Id).ToArray(),ct).ConfigureAwait(false);
        var links=new List<StoredSourceIdentity>();
        await using(var cmd=Command(c,tx,"SELECT DISTINCT b.report_sha256,s.row_number,s.sheet,s.execution_id FROM planning.source_rows s JOIN planning.batches b ON b.id=s.batch_id WHERE b.status='Applied' AND b.report_sha256=@hash AND s.file IN ('Report List','Side2','React Active') AND s.execution_id IS NOT NULL",("hash",PlanningImportReader.Hash(input.Report))))
        await using(var r=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
            while(await r.ReadAsync(ct).ConfigureAwait(false))links.Add(new(r.GetString(0),r.GetInt32(1),r.GetString(2),r.GetGuid(3)));
        var maps=input.Cut.Policy=="ReactTable"?ReactSnapshotMappings(input,await ReactMappings(c,tx,ct).ConfigureAwait(false)):input.Cut.Policy=="HtmlReactSide2"?PlanningImportReader.SupplementMappings(input):input.Cut.Policy=="HtmlReact"?await ReactMappings(c,tx,ct).ConfigureAwait(false):await Mappings(c,tx,ct).ConfigureAwait(false);
        var plan=PlanningReconciler.Plan(input,episodes,executions,maps,links);
        if(input.Cut.Policy is "HtmlReact" or "HtmlReactSide2" or "ReactTable")plan=(await PrepareReactPlan(c,tx,plan,ct).ConfigureAwait(false)) with{ReactStateToken=reactToken};
        var id=Guid.NewGuid();
        var newIds=plan.Episodes.Where(e=>e.IsNew).Select(e=>e.Id).ToHashSet();
        var changedIds=plan.Changes.Where(c=>c.Entity is "WIP" or "OfficialState" or "Classification").Select(c=>c.EpisodeId).ToHashSet();
        var preview=new PlanningImportPreview(id,"Preview",revision,input.Cut,input.Files,plan.Changes,plan.Reviews,plan.Issues,DateTimeOffset.UtcNow,
            plan.Episodes.Count(e=>e.IsNew),plan.Executions.Count(e=>e.Action=="Create"),plan.Changes.Where(e=>e.Entity is "WIP" or "OfficialState" or "Classification" && e.Action=="UpdateSource" && !newIds.Contains(e.EpisodeId??Guid.Empty)).Select(e=>e.EpisodeId).Distinct().Count(),plan.Executions.Count(e=>e.Action=="LinkManual"),
            plan.Episodes.Count(e=>!e.IsNew&&!changedIds.Contains(e.Id)),
            plan.Reviews.Count(r=>r.Category=="ExecutionIdentity"),plan.Reviews.Count(r=>r.Category is not ("BothFiles" or "ManualManaged")),plan.Admissions);
        // An unapplied stale preview can be superseded only by its creator; original files and audit remain in history.
        await using(var owner=Command(c,tx,"SELECT actor_id FROM planning.batches WHERE fingerprint=@hash",("hash",input.Fingerprint)))
        {
            var previous=await owner.ExecuteScalarAsync(ct).ConfigureAwait(false);
            if(previous is int creator && creator!=actor) throw new PlanningConflictException("Este archivo tiene una revisión pendiente de otro usuario.");
        }
        await Execute(c,tx,"""
            INSERT INTO planning.batches(id,fingerprint,status,actor_id,base_revision,cut_window,day,summary_name,report_name,summary_sha256,report_sha256,summary_data,report_data,input,plan,preview)
            VALUES(@id,@hash,'Preview',@actor,@revision,@window,@day,@summary,@report,@sha,@rsha,@sbytes,@rbytes,@input::jsonb,@plan::jsonb,@preview::jsonb)
            ON CONFLICT(fingerprint) DO UPDATE SET base_revision=EXCLUDED.base_revision,plan=EXCLUDED.plan,preview=EXCLUDED.preview,created_at=now()
            RETURNING id
            """,ct,("id",id),("hash",input.Fingerprint),("actor",actor),("revision",revision),("window",input.Cut.Window),("day",input.Cut.Day),
            ("summary",input.SummaryName),("report",input.ReportName),("sha",PlanningImportReader.Hash(input.Summary)),("rsha",PlanningImportReader.Hash(input.Report)),
            ("sbytes",input.Summary),("rbytes",input.Report),("input",Serialize(input with { Summary=[],Report=[] })),("plan",Serialize(plan)),("preview",Serialize(preview))).ConfigureAwait(false);
        // Preserve the identifier when a stale preview is recalculated.
        await using(var identity=Command(c,tx,"SELECT id FROM planning.batches WHERE fingerprint=@hash",("hash",input.Fingerprint)))
            preview=preview with { Id=(Guid)(await identity.ExecuteScalarAsync(ct).ConfigureAwait(false))! };
        await Execute(c,tx,"UPDATE planning.batches SET preview=@preview::jsonb WHERE id=@id",ct,("preview",Serialize(preview)),("id",preview.Id)).ConfigureAwait(false);
        await Audit(c,tx,null,"ImportPreview",actor,"Vista previa sin cambios operativos",new { preview.Id,input.Fingerprint,revision },ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false); return preview;
    }
    public async Task<PlanningImportResult> ConfirmAsync(Guid id,int actor,CancellationToken ct)
    {
        await using var c=await database.Source.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var tx=await c.BeginTransactionAsync(ct).ConfigureAwait(false);
        var revision=await Revision(c,tx,true,ct).ConfigureAwait(false);
        PlanningImportPreview preview; PlanningImportInput input; PlanningPlan plan;
        await using(var cmd=Command(c,tx,"SELECT actor_id,status,preview::text,input::text,plan::text FROM planning.batches WHERE id=@id FOR UPDATE",("id",id)))
        await using(var r=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            if(!await r.ReadAsync(ct).ConfigureAwait(false)) throw new PlanningValidationException("No existe el lote.");
            if(r.GetInt32(0)!=actor) throw new PlanningValidationException("Solo el creador del lote puede confirmar su revisión.");
            preview=Read<PlanningImportPreview>(r.GetString(2)); input=Read<PlanningImportInput>(r.GetString(3)); plan=Read<PlanningPlan>(r.GetString(4));
            if(r.GetString(1)=="Applied") return Outcome(preview,true);
        }
        if(preview.BaseRevision!=revision) throw new PlanningConflictException("La información cambió después de la revisión. Valide nuevamente los archivos.");
        if(preview.CreatedAt < DateTimeOffset.UtcNow.AddHours(-24)) throw new PlanningConflictException("La vista previa venció. Valide nuevamente los archivos.");
        if(plan.Issues.Any(i=>i.Blocking)) throw new PlanningValidationException("Corrija los errores estructurales antes de confirmar.");
        if(input.Cut.Policy is "HtmlReact" or "HtmlReactSide2" or "ReactTable")
        {
            await RequireReactSchema(c,tx,ct).ConfigureAwait(false);
            await Execute(c,tx,"LOCK TABLE public.planificacion_activa,public.planificacion_archivada,public.planificacion_test_map IN SHARE ROW EXCLUSIVE MODE",ct).ConfigureAwait(false);
            if(plan.ReactStateToken!=await ReadReactStateToken(c,tx,ct).ConfigureAwait(false))throw new PlanningConflictException("Las tablas de React cambiaron después de la revisión. Valide de nuevo antes de confirmar.");
        }
        var audit=new List<ImportAudit>();
        if(input.Cut.Policy=="HtmlReactSide2")await Execute(c,tx,"UPDATE planning.episodes SET manual_fields=array_remove(array_remove(manual_fields,'InDate'),'DueDate') WHERE id=ANY(@ids)",ct,("ids",plan.Episodes.Select(e=>e.Id).ToArray())).ConfigureAwait(false);
        await CopyEpisodes(c,tx,id,plan.Episodes,ct).ConfigureAwait(false);
        var changesByEpisode=plan.Changes.ToLookup(c=>c.EpisodeId);
        foreach(var episode in plan.Episodes)
        {
            audit.Add(new(episode.Id,episode.IsNew?"WipImported":"WipSourceObserved",new { BatchId=id,episode.Source,episode.Official,Changes=changesByEpisode[episode.Id].ToArray() }));
        }
        foreach(var change in plan.Changes.Where(c=>c.Entity=="OfficialState"&&c.EpisodeId is not null))
            audit.Add(new(change.EpisodeId!.Value,"OfficialStateImported",new { BatchId=id,Before=JsonSerializer.Deserialize<JsonElement>(change.Before),After=JsonSerializer.Deserialize<JsonElement>(change.After) }));
        foreach(var change in plan.Changes.Where(c=>c.Entity=="Classification"&&c.EpisodeId is not null))
            audit.Add(new(change.EpisodeId!.Value,"ClassificationSourceObserved",new{BatchId=id,change.OriginalTest,Before=JsonSerializer.Deserialize<JsonElement>(change.Before),After=JsonSerializer.Deserialize<JsonElement>(change.After),input.Cut.Policy}));
        await CopyExecutions(c,plan.Executions,ct).ConfigureAwait(false);
        if(input.Cut.Policy is "HtmlReact" or "HtmlReactSide2" or "ReactTable")
            foreach(var execution in plan.Executions.Where(e=>e.Action=="Observe"&&(e.Origin=="ReportList"||e.Rule=="ReactSide2:v1")))
                await Execute(c,tx,"""
                    UPDATE planning.executions SET equivalence=@eq,laboratory=@lab,no_program=@no,execution_type=coalesce(@rule,execution_type),version=version+1
                    WHERE id=@id AND (equivalence,laboratory,no_program,execution_type) IS DISTINCT FROM (@eq,@lab,@no,coalesce(@rule,execution_type))
                    """,ct,("id",execution.Id),("eq",execution.Equivalence),("lab",execution.Laboratory),("no",execution.NoProgram),("rule",execution.Rule)).ConfigureAwait(false);
        foreach(var execution in plan.Executions)
        {
            if(execution.Action!="Observe") audit.Add(new(execution.EpisodeId,execution.Action=="LinkManual"?"ManualSourceLinked":"TestImported",new { BatchId=id,execution.Id,execution.OriginalTest }));
        }
        await CopySources(c,id,input,plan,ct).ConfigureAwait(false);
        if(input.Cut.Policy is "HtmlReact" or "HtmlReactSide2" or "ReactTable")await ApplyReactPlan(c,tx,id,plan,actor,ct).ConfigureAwait(false);
        await ReopenReportAlerts(c,tx,ct).ConfigureAwait(false);
        await RefreshSummaryTray(c,tx,plan.Episodes.Select(e=>e.Id).Concat(plan.Executions.Select(e=>e.EpisodeId)).Distinct().ToArray(),ct).ConfigureAwait(false);
        await ReconcileImportIncidents(c,tx,plan.Episodes.Select(e=>e.Id).Concat(plan.Executions.Select(e=>e.EpisodeId)).Distinct().ToArray(),audit,ct).ConfigureAwait(false);
        await CopyImportAudit(c,actor,audit,ct).ConfigureAwait(false);
        await Execute(c,tx,"UPDATE planning.batches SET status='Applied',applied_at=now() WHERE id=@id",ct,("id",id)).ConfigureAwait(false);
        await Audit(c,tx,null,"ImportApplied",actor,"Confirmación explícita de importación",new { BatchId=id,preview.NewJobs,preview.NewTests,preview.LinkedManualTests },ct).ConfigureAwait(false);
        await Bump(c,tx,ct).ConfigureAwait(false); await tx.CommitAsync(ct).ConfigureAwait(false); return Outcome(preview,false);
    }
    private static PlanningImportResult Outcome(PlanningImportPreview p,bool repeated) => new(p.Id,"Applied",p.NewJobs,p.NewTests,p.UpdatedJobs,p.LinkedManualTests,repeated);
    private static Task InsertExecution(NpgsqlConnection c,NpgsqlTransaction tx,Guid id,Guid episode,string test,string? equivalence,string? lab,bool noProgram,string origin,CancellationToken ct) => Execute(c,tx,
        "INSERT INTO planning.executions(id,episode_id,original_test,test_key,equivalence,laboratory,no_program,origin) VALUES(@id,@episode,@test,@key,@equivalence,@lab,@no,@origin)",ct,
        ("id",id),("episode",episode),("test",test),("key",PlanningReconciler.TestKey(test)),("equivalence",equivalence),("lab",lab),("no",noProgram),("origin",origin));
    private static async Task RefreshIncidents(NpgsqlConnection c,NpgsqlTransaction tx,Guid episode,int actor,CancellationToken ct)
    {
        await using var cmd=Command(c,tx,"SELECT coalesce(trim(vendor),'')='',NOT EXISTS(SELECT 1 FROM planning.executions x WHERE x.episode_id=e.id) FROM planning.episodes e WHERE id=@id",("id",episode));
        bool missingVendor; bool missingTests;
        await using(var r=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false)) { if(!await r.ReadAsync(ct).ConfigureAwait(false)) throw new PlanningValidationException("No existe el episodio."); missingVendor=r.GetBoolean(0);missingTests=r.GetBoolean(1); }
        foreach(var (kind,pending) in new[]{("MissingVendor",missingVendor),("MissingTests",missingTests)})
        {
            await Execute(c,tx,"""
                INSERT INTO planning.incidents(id,episode_id,kind,state,reason) VALUES(@id,@episode,@kind,@state,'Conciliación de datos persistidos')
                ON CONFLICT(episode_id,kind,execution_id) DO UPDATE SET
                state=CASE WHEN @pending AND planning.incidents.state IN ('Held','Excluded') THEN planning.incidents.state ELSE @state END,
                version=planning.incidents.version+1
                """,ct,("id",Guid.NewGuid()),("episode",episode),("kind",kind),("state",pending?"Pending":"Resolved"),("pending",pending)).ConfigureAwait(false);
        }
        await Audit(c,tx,episode,"IncidentsReconciled",actor,"Vendor y asignación de pruebas se evalúan por separado",new { missingVendor,missingTests },ct).ConfigureAwait(false);
    }
    public async Task<IReadOnlyList<PlanningBatch>> BatchesAsync(CancellationToken ct)
    {
        await using var c=await database.Source.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd=Command(c,null,"""
            SELECT b.id,b.status,b.summary_name,b.report_name,b.cut_window,b.day,b.created_at,b.applied_at,b.actor_id,u.nombre,
              coalesce((preview->>'newJobs')::int,0),coalesce((preview->>'newTests')::int,0),coalesce((preview->>'updatedJobs')::int,0),
              (SELECT count(DISTINCT ch->>'episodeId')::int FROM jsonb_array_elements(coalesce(preview->'changes','[]'::jsonb)) ch WHERE ch->>'entity'='WIP' AND ch->>'action'='UpdateSource'),
              coalesce((preview->>'pendingReview')::int,0)
            FROM planning.batches b LEFT JOIN planning.actors u ON u.id=b.actor_id ORDER BY b.created_at DESC,b.id LIMIT 200
            """);
        await using var r=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false); var items=new List<PlanningBatch>();
        while(await r.ReadAsync(ct).ConfigureAwait(false)) items.Add(new(r.GetGuid(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetFieldValue<DateOnly>(5),r.GetFieldValue<DateTimeOffset>(6),r.IsDBNull(7)?null:r.GetFieldValue<DateTimeOffset>(7),r.GetInt32(8),NullableString(r,9),r.GetInt32(10),r.GetInt32(11),r.GetInt32(12),r.GetInt32(13),r.GetInt32(14)));
        return items;
    }
}

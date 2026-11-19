using Lims.Application.Planning;
using Lims.Contracts.Planning;
using Npgsql;
using NpgsqlTypes;

namespace Lims.Infrastructure.Planning;

public sealed partial class PostgresPlanningStore
{
    private sealed record ImportAudit(Guid EpisodeId,string Action,object Details);
    private static async Task CopyEpisodes(NpgsqlConnection c,NpgsqlTransaction tx,Guid batch,IReadOnlyList<ProposedEpisode> episodes,CancellationToken ct)
    {
        await Execute(c,tx,"CREATE TEMP TABLE planning_import_episodes (id uuid,job text,buyer text,vendor text,service text,in_date date,due_date date,amount numeric,currency text,usd numeric,source_batch_id uuid,source_row integer,official_fields jsonb) ON COMMIT DROP",ct).ConfigureAwait(false);
        await using(var writer=await c.BeginBinaryImportAsync("COPY planning_import_episodes FROM STDIN (FORMAT BINARY)",ct).ConfigureAwait(false))
        {
            foreach(var e in episodes)
            {
                var w=e.Source;await writer.StartRowAsync(ct).ConfigureAwait(false);
                await Write(writer,e.Id,NpgsqlDbType.Uuid,ct).ConfigureAwait(false);await Write(writer,e.Job,NpgsqlDbType.Text,ct).ConfigureAwait(false);
                await Write(writer,w.Buyer,NpgsqlDbType.Text,ct).ConfigureAwait(false);await Write(writer,w.Vendor,NpgsqlDbType.Text,ct).ConfigureAwait(false);await Write(writer,w.Service,NpgsqlDbType.Text,ct).ConfigureAwait(false);
                await Write(writer,w.InDate,NpgsqlDbType.Date,ct).ConfigureAwait(false);await Write(writer,w.DueDate,NpgsqlDbType.Date,ct).ConfigureAwait(false);
                await Write(writer,w.Amount,NpgsqlDbType.Numeric,ct).ConfigureAwait(false);await Write(writer,w.Currency,NpgsqlDbType.Text,ct).ConfigureAwait(false);await Write(writer,w.Usd,NpgsqlDbType.Numeric,ct).ConfigureAwait(false);
                await Write(writer,e.HasSummary?batch:null,NpgsqlDbType.Uuid,ct).ConfigureAwait(false);await Write(writer,e.HasSummary?e.SourceRow:null,NpgsqlDbType.Integer,ct).ConfigureAwait(false);
                await Write(writer,Serialize(e.Official??new()),NpgsqlDbType.Jsonb,ct).ConfigureAwait(false);
            }
            await writer.CompleteAsync(ct).ConfigureAwait(false);
        }
        await Execute(c,tx,"""
                INSERT INTO planning.episodes(id,job,source_in_date,buyer,vendor,service,in_date,due_date,amount,currency,usd,source_batch_id,source_row,official_fields)
                SELECT id,job,in_date,buyer,vendor,service,in_date,due_date,amount,currency,usd,source_batch_id,source_row,official_fields FROM planning_import_episodes WHERE true
                ON CONFLICT(id) DO UPDATE SET
                source_in_date=coalesce(planning.episodes.source_in_date,EXCLUDED.source_in_date),
                official_fields=EXCLUDED.official_fields,
                buyer=CASE WHEN 'Buyer'=ANY(planning.episodes.manual_fields) THEN planning.episodes.buyer ELSE EXCLUDED.buyer END,
                vendor=CASE WHEN 'Vendor'=ANY(planning.episodes.manual_fields) THEN planning.episodes.vendor ELSE EXCLUDED.vendor END,
                service=CASE WHEN 'Service'=ANY(planning.episodes.manual_fields) THEN planning.episodes.service ELSE EXCLUDED.service END,
                in_date=CASE WHEN 'InDate'=ANY(planning.episodes.manual_fields) THEN planning.episodes.in_date ELSE EXCLUDED.in_date END,
                due_date=CASE WHEN 'DueDate'=ANY(planning.episodes.manual_fields) THEN planning.episodes.due_date ELSE EXCLUDED.due_date END,
                amount=CASE WHEN 'Amount'=ANY(planning.episodes.manual_fields) THEN planning.episodes.amount ELSE EXCLUDED.amount END,
                currency=CASE WHEN 'Currency'=ANY(planning.episodes.manual_fields) THEN planning.episodes.currency ELSE EXCLUDED.currency END,
                usd=CASE WHEN 'Usd'=ANY(planning.episodes.manual_fields) THEN planning.episodes.usd ELSE EXCLUDED.usd END,
                source_batch_id=EXCLUDED.source_batch_id,source_row=EXCLUDED.source_row,version=planning.episodes.version+1
                """,ct).ConfigureAwait(false);
    }
    private static async Task CopyExecutions(NpgsqlConnection c,IReadOnlyList<ProposedExecution> executions,CancellationToken ct)
    {
        await using var writer=await c.BeginBinaryImportAsync("COPY planning.executions(id,episode_id,original_test,test_key,equivalence,laboratory,no_program,origin) FROM STDIN (FORMAT BINARY)",ct).ConfigureAwait(false);
        foreach(var e in executions.Where(e=>e.Action=="Create"))
        {
            await writer.StartRowAsync(ct).ConfigureAwait(false);
            await Write(writer,e.Id,NpgsqlDbType.Uuid,ct).ConfigureAwait(false);await Write(writer,e.EpisodeId,NpgsqlDbType.Uuid,ct).ConfigureAwait(false);
            await Write(writer,e.OriginalTest,NpgsqlDbType.Text,ct).ConfigureAwait(false);await Write(writer,PlanningReconciler.TestKey(e.OriginalTest),NpgsqlDbType.Text,ct).ConfigureAwait(false);
            await Write(writer,e.Equivalence,NpgsqlDbType.Text,ct).ConfigureAwait(false);await Write(writer,e.Laboratory,NpgsqlDbType.Text,ct).ConfigureAwait(false);
            await Write(writer,e.NoProgram,NpgsqlDbType.Boolean,ct).ConfigureAwait(false);await Write(writer,"ReportList",NpgsqlDbType.Text,ct).ConfigureAwait(false);
        }
        await writer.CompleteAsync(ct).ConfigureAwait(false);
    }
    private static async Task CopySources(NpgsqlConnection c,Guid batch,PlanningImportInput input,PlanningPlan plan,CancellationToken ct)
    {
        var executionRows=plan.Executions.SelectMany(e=>e.SourceRows.Select(row=>(row,e))).ToDictionary(p=>p.row,p=>p.e);
        var summaryEpisodes=plan.Episodes.ToDictionary(e=>(e.Job,e.Source.InDate),e=>e.Id);
        await using var writer=await c.BeginBinaryImportAsync("COPY planning.source_rows(batch_id,file,row_number,sheet,job,original_test,fields,wip,episode_id,execution_id) FROM STDIN (FORMAT BINARY)",ct).ConfigureAwait(false);
        foreach(var row in input.Rows)
        {
            executionRows.TryGetValue(row.Row,out var execution);if(row.File!="Report List")execution=null;
            Guid? episode=row.File=="Summary"?(summaryEpisodes.TryGetValue((row.Job,row.Wip!.InDate),out var id)?id:null):execution?.EpisodeId;
            if(episode is null&&row.File=="Report List"){var candidates=plan.Episodes.Where(e=>e.Job==row.Job).Take(2).ToArray();if(candidates.Length==1)episode=candidates[0].Id;}
            await writer.StartRowAsync(ct).ConfigureAwait(false);
            await Write(writer,batch,NpgsqlDbType.Uuid,ct).ConfigureAwait(false);await Write(writer,row.File,NpgsqlDbType.Text,ct).ConfigureAwait(false);
            await Write(writer,row.Row,NpgsqlDbType.Integer,ct).ConfigureAwait(false);await Write(writer,row.Sheet,NpgsqlDbType.Text,ct).ConfigureAwait(false);
            await Write(writer,row.Job,NpgsqlDbType.Text,ct).ConfigureAwait(false);await Write(writer,row.Test,NpgsqlDbType.Text,ct).ConfigureAwait(false);
            await Write(writer,row.Fields,NpgsqlDbType.Jsonb,ct).ConfigureAwait(false);await Write(writer,row.Wip is null?null:Serialize(row.Wip),NpgsqlDbType.Jsonb,ct).ConfigureAwait(false);
            await Write(writer,episode,NpgsqlDbType.Uuid,ct).ConfigureAwait(false);await Write(writer,execution?.Id,NpgsqlDbType.Uuid,ct).ConfigureAwait(false);
        }
        await writer.CompleteAsync(ct).ConfigureAwait(false);
    }
    private static async Task CopyImportAudit(NpgsqlConnection c,int actor,IReadOnlyList<ImportAudit> records,CancellationToken ct)
    {
        await using var writer=await c.BeginBinaryImportAsync("COPY planning.history(id,episode_id,action,actor_id,reason,details) FROM STDIN (FORMAT BINARY)",ct).ConfigureAwait(false);
        foreach(var record in records)
        {
            await writer.StartRowAsync(ct).ConfigureAwait(false);
            await Write(writer,Guid.NewGuid(),NpgsqlDbType.Uuid,ct).ConfigureAwait(false);await Write(writer,record.EpisodeId,NpgsqlDbType.Uuid,ct).ConfigureAwait(false);
            await Write(writer,record.Action,NpgsqlDbType.Text,ct).ConfigureAwait(false);await Write(writer,actor,NpgsqlDbType.Integer,ct).ConfigureAwait(false);
            await Write(writer,"Confirmación explícita de importación",NpgsqlDbType.Text,ct).ConfigureAwait(false);await Write(writer,Serialize(record.Details),NpgsqlDbType.Jsonb,ct).ConfigureAwait(false);
        }
        await writer.CompleteAsync(ct).ConfigureAwait(false);
    }
    private static async Task Write(NpgsqlBinaryImporter writer,object? value,NpgsqlDbType type,CancellationToken ct)
    {if(value is null)await writer.WriteNullAsync(ct).ConfigureAwait(false);else await writer.WriteAsync(value,type,ct).ConfigureAwait(false);}
    private static async Task ReconcileImportIncidents(NpgsqlConnection c,NpgsqlTransaction tx,Guid[] episodes,List<ImportAudit> audit,CancellationToken ct)
    {
        await using var cmd=Command(c,tx,"""
            INSERT INTO planning.incidents(id,episode_id,kind,state,reason)
            SELECT gen_random_uuid(),e.id,states.kind,CASE WHEN states.pending THEN 'Pending' ELSE 'Resolved' END,'Conciliación de datos persistidos'
            FROM planning.episodes e CROSS JOIN LATERAL (VALUES
              ('MissingVendor',coalesce(trim(e.vendor),'')=''),
              ('MissingTests',NOT EXISTS(SELECT 1 FROM planning.executions x WHERE x.episode_id=e.id))) AS states(kind,pending)
            WHERE e.id=ANY(@episodes)
            ON CONFLICT(episode_id,kind,execution_id) DO UPDATE SET
              state=CASE WHEN EXCLUDED.state='Pending' AND planning.incidents.state IN ('Held','Excluded') THEN planning.incidents.state ELSE EXCLUDED.state END,
              version=planning.incidents.version+1
            RETURNING episode_id,kind,state
            """,("episodes",episodes));
        await using var r=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while(await r.ReadAsync(ct).ConfigureAwait(false))audit.Add(new(r.GetGuid(0),"IncidentsReconciled",new{Kind=r.GetString(1),State=r.GetString(2)}));
    }
    private static Task RefreshSummaryTray(NpgsqlConnection c,NpgsqlTransaction tx,Guid[] episodes,CancellationToken ct)=>Execute(c,tx,"""
        INSERT INTO planning.incidents(id,episode_id,kind,state,reason)
        SELECT gen_random_uuid(),e.id,'SummaryWithoutReport','Pending','Summary disponible; sin pruebas vinculadas a Report List'
        FROM planning.episodes e WHERE e.id=ANY(@episodes) AND EXISTS(
          SELECT 1 FROM planning.source_rows s WHERE s.episode_id=e.id AND s.file='Summary')
        ON CONFLICT(episode_id,kind,execution_id) DO UPDATE SET
          state=CASE WHEN planning.incidents.manual_resolved AND planning.incidents.state<>'Excluded' THEN 'Resolved' ELSE planning.incidents.state END,
          version=planning.incidents.version+1;
        UPDATE planning.incidents i SET state='Resolved',version=version+1
        WHERE i.episode_id=ANY(@episodes) AND i.kind='SummaryWithoutReport' AND EXISTS(
          SELECT 1 FROM planning.source_rows s WHERE s.episode_id=i.episode_id AND s.file='Report List' AND s.execution_id IS NOT NULL);
        INSERT INTO planning.incidents(id,episode_id,kind,state,reason)
        SELECT gen_random_uuid(),e.id,'ReportWithoutSummary','Pending','Report List conservado; cobertura de Summary insuficiente'
        FROM planning.episodes e WHERE e.id=ANY(@episodes) AND NOT EXISTS(
          SELECT 1 FROM planning.source_rows s WHERE s.episode_id=e.id AND s.file='Summary')
        ON CONFLICT(episode_id,kind,execution_id) DO NOTHING;
        UPDATE planning.incidents i SET state='Resolved',version=version+1
        WHERE i.episode_id=ANY(@episodes) AND i.kind='ReportWithoutSummary' AND EXISTS(
          SELECT 1 FROM planning.source_rows s WHERE s.episode_id=i.episode_id AND s.file='Summary');
        """,ct,("episodes",episodes));
}

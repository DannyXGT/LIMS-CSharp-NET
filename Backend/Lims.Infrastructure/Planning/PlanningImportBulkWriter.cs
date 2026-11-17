using Lims.Application.Planning;
using Lims.Contracts.Planning;
using Npgsql;
using NpgsqlTypes;

namespace Lims.Infrastructure.Planning;

public sealed partial class PostgresPlanningStore
{
    private sealed record ImportAudit(Guid EpisodeId,string Action,object Details);
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
              state=CASE WHEN EXCLUDED.state='Pending' AND planning.incidents.state='Held' THEN 'Held' ELSE EXCLUDED.state END,
              version=planning.incidents.version+1
            RETURNING episode_id,kind,state
            """,("episodes",episodes));
        await using var r=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while(await r.ReadAsync(ct).ConfigureAwait(false))audit.Add(new(r.GetGuid(0),"IncidentsReconciled",new{Kind=r.GetString(1),State=r.GetString(2)}));
    }
}

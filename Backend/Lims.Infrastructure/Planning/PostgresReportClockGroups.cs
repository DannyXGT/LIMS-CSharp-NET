using System.Data;
using Lims.Application.Planning;
using Lims.Contracts.Planning;
using Npgsql;

namespace Lims.Infrastructure.Planning;

public sealed partial class PostgresPlanningStore
{
    // Group the operational minute for review; immutable confirmation sessions remain separate underneath.
    private const string ClockGroups="""
        WITH clocks AS (
          SELECT (array_agg(g.id ORDER BY g.saved_at,g.id))[1] AS id,
            date_trunc('minute',g.operational_at) AS operational_at,max(g.saved_at) AS saved_at,
            string_agg(DISTINCT a.nombre,', ' ORDER BY a.nombre) AS digitized_by,
            sum(g.tests)::int AS tests,sum(g.version)::bigint AS version,
            CASE WHEN sum(g.exceptions)>0 THEN 'Revisar excepciones' WHEN bool_and(g.reviewed_at IS NOT NULL) THEN 'Chequeado' ELSE 'Pendiente' END AS state,
            CASE WHEN bool_and(g.reviewed_at IS NOT NULL) AND sum(g.exceptions)=0 THEN string_agg(DISTINCT reviewer.nombre,', ' ORDER BY reviewer.nombre) END AS reviewed_by,
            CASE WHEN bool_and(g.reviewed_at IS NOT NULL) AND sum(g.exceptions)=0 THEN max(g.reviewed_at) END AS reviewed_at,
            sum(g.exceptions)::int AS exceptions,array_agg(g.id ORDER BY g.saved_at,g.id) AS session_ids,
            bool_or(@search='' OR strpos(lower(concat_ws(' ',a.nombre,g.operational_at AT TIME ZONE 'America/Guatemala')),lower(@search))>0
              OR EXISTS(SELECT 1 FROM planning.report_session_members m JOIN planning.executions x ON x.id=m.execution_id JOIN planning.episodes e ON e.id=x.episode_id
                WHERE m.session_id=g.id AND strpos(lower(concat_ws(' ',e.job,x.equivalence,x.original_test,x.laboratory)),lower(@search))>0)) AS matches
          FROM planning.report_schedule_status g JOIN planning.actors a ON a.id=g.actor_id LEFT JOIN planning.actors reviewer ON reviewer.id=g.reviewed_by
          GROUP BY date_trunc('minute',g.operational_at)
        )
        """;
    private const string ClockColumns="id,operational_at,saved_at,digitized_by,tests,(SELECT count(DISTINCT e.job)::int FROM planning.report_session_members m JOIN planning.executions x ON x.id=m.execution_id JOIN planning.episodes e ON e.id=x.episode_id WHERE m.session_id=ANY(clocks.session_ids)),version,state,reviewed_by,reviewed_at,exceptions,session_ids";
    private static ReportSchedule Clock(NpgsqlDataReader r)=>Schedule(r) with{SessionIds=r.GetFieldValue<Guid[]>(11)};
    private async Task<ReportSchedulePage> ReportClockGroupsAsync(ReportScheduleQuery query,CancellationToken ct)
    {
        ValidateScheduleQuery(query);var page=Math.Clamp(query.Page,1,1000000);var size=Math.Clamp(query.PageSize,1,200);
        await using var c=await database.Source.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var tx=await c.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct).ConfigureAwait(false);
        var revision=await Revision(c,tx,false,ct).ConfigureAwait(false);if(query.Revision is {} expected&&revision!=expected)throw new PlanningConflictException("Los horarios cambiaron. Actualice para continuar.");
        const string where=" FROM clocks WHERE matches AND (@state='All' OR (@state='Pending' AND state='Pendiente') OR (@state='Reviewed' AND state='Chequeado') OR (@state='Exceptions' AND exceptions>0))";
        var args=new (string,object?)[]{("search",query.Search?.Trim()??""),("state",query.State),("take",size),("skip",(page-1)*size)};
        int total;await using(var cmd=Command(c,tx,ClockGroups+" SELECT count(*)::int"+where,args))total=(int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
        var rows=new List<ReportSchedule>();await using(var cmd=Command(c,tx,ClockGroups+" SELECT "+ClockColumns+where+" ORDER BY operational_at DESC,id LIMIT @take OFFSET @skip",args))
        await using(var r=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))while(await r.ReadAsync(ct).ConfigureAwait(false))rows.Add(Clock(r));
        await tx.CommitAsync(ct).ConfigureAwait(false);return new(rows,total,page,size,revision);
    }
    private static async Task<ReportSchedule> ReadClockGroup(NpgsqlConnection c,NpgsqlTransaction tx,Guid id,CancellationToken ct)
    {
        await using var cmd=Command(c,tx,ClockGroups+" SELECT "+ClockColumns+" FROM clocks WHERE @id=ANY(session_ids)",("id",id),("search",""));
        await using var r=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);return await r.ReadAsync(ct).ConfigureAwait(false)?Clock(r):throw new PlanningValidationException("No existe el horario.");
    }
    private async Task<ReportSchedule> ReviewReportClockAsync(ReportScheduleReview request,int actor,CancellationToken ct)
    {
        var reason=Reason(request.Reason);
        if(request.ClockSessions is not {Count:>0 and <=1000} versions||request.Executions is not {Count:>0 and <=50000} executions||versions.Select(s=>s.SessionId).Distinct().Count()!=versions.Count||executions.Select(e=>e.ExecutionId).Distinct().Count()!=executions.Count)
            throw new PlanningValidationException("Revise las sesiones y ejecuciones completas del horario.");
        await using var c=await database.Source.OpenConnectionAsync(ct).ConfigureAwait(false);await using var tx=await c.BeginTransactionAsync(ct).ConfigureAwait(false);await Revision(c,tx,true,ct).ConfigureAwait(false);
        var clock=await ReadClockGroup(c,tx,request.SessionId,ct).ConfigureAwait(false);
        if(clock.Version!=request.Version||clock.Exceptions>0||clock.ReviewedAt is not null||clock.Tests!=executions.Count||!clock.SessionIds!.Order().SequenceEqual(versions.Select(s=>s.SessionId).Order()))throw new PlanningConflictException("Cambió el horario o recibió otra digitación. Actualice y revise todas sus pruebas.");
        var expected=executions.ToDictionary(x=>x.ExecutionId,x=>x.Version);
        foreach(var version in versions.OrderBy(s=>s.SessionId))
        {
            var session=await ReadSchedule(c,tx,version.SessionId,ct).ConfigureAwait(false);
            if(session.Version!=version.Version)throw new PlanningConflictException("Cambió una confirmación del horario. Vuelva a abrir el detalle.");
            var members=new List<ReportExecutionVersion>();await using(var cmd=Command(c,tx,"SELECT x.id,x.version FROM planning.report_session_members m JOIN planning.executions x ON x.id=m.execution_id WHERE m.session_id=@id",("id",session.Id)))
            await using(var r=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))while(await r.ReadAsync(ct).ConfigureAwait(false))
            {var id=r.GetGuid(0);if(!expected.TryGetValue(id,out var v)||v!=r.GetInt64(1))throw new PlanningConflictException("Faltan pruebas del horario o cambiaron sus versiones.");members.Add(new(id,v));}
            // Validate reviewed members as well; their original reviewer and audit event remain untouched.
            if(session.ReviewedAt is null)await ApplyScheduleReview(c,tx,new(session.Id,session.Version,members,reason),actor,reason,ct).ConfigureAwait(false);
        }
        await Bump(c,tx,ct).ConfigureAwait(false);var result=await ReadClockGroup(c,tx,request.SessionId,ct).ConfigureAwait(false);await tx.CommitAsync(ct).ConfigureAwait(false);return result;
    }
}

using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Lims.Application.Planning;
using Lims.Contracts.Planning;
using Npgsql;

namespace Lims.Infrastructure.Planning;

public sealed partial class PostgresPlanningStore
{
    private const string ScheduleState="CASE WHEN g.exceptions>0 THEN 'Revisar excepciones' WHEN g.reviewed_at IS NOT NULL THEN 'Chequeado' ELSE 'Pendiente' END";
    private const string ScheduleColumns="g.id,g.operational_at,g.saved_at,a.nombre,g.tests,g.jobs,g.version,"+ScheduleState+",reviewer.nombre,g.reviewed_at,g.exceptions";
    private const string ScheduleFrom=" FROM planning.report_schedule_status g JOIN planning.actors a ON a.id=g.actor_id LEFT JOIN planning.actors reviewer ON reviewer.id=g.reviewed_by ";
    private static ReportSchedule Schedule(NpgsqlDataReader r)=>new(r.GetGuid(0),r.GetFieldValue<DateTimeOffset>(1),r.GetFieldValue<DateTimeOffset>(2),r.GetString(3),r.GetInt32(4),r.GetInt32(5),r.GetInt64(6),r.GetString(7),NullableString(r,8),r.IsDBNull(9)?null:r.GetFieldValue<DateTimeOffset>(9),r.GetInt32(10));
    private static void ValidateScheduleQuery(ReportScheduleQuery query)
    {
        if(query.Search?.Length>20000||query.State is not ("All" or "Pending" or "Reviewed" or "Exceptions"))throw new PlanningValidationException("Filtro de horarios inválido.");
    }
    private const string ScheduleWhere="""
        WHERE (@search='' OR strpos(lower(concat_ws(' ',a.nombre,g.operational_at AT TIME ZONE 'America/Guatemala')),lower(@search))>0
          OR EXISTS(SELECT 1 FROM planning.report_session_members m JOIN planning.executions x ON x.id=m.execution_id
            JOIN planning.episodes e ON e.id=x.episode_id WHERE m.session_id=g.id
              AND strpos(lower(concat_ws(' ',e.job,x.equivalence,x.laboratory)),lower(@search))>0))
        AND (@state='All' OR (@state='Pending' AND g.reviewed_at IS NULL AND g.exceptions=0)
          OR (@state='Reviewed' AND g.reviewed_at IS NOT NULL AND g.exceptions=0) OR (@state='Exceptions' AND g.exceptions>0))
        """;
    public async Task<ReportListResult> DigitizeReportAsync(ReportDigitization request,int actor,bool administrator,CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);var reason=Reason(request.Reason);
        if(request.SessionId==Guid.Empty||request.Executions is null||request.Executions.Count is <1 or >500||request.Executions.Select(x=>x.ExecutionId).Distinct().Count()!=request.Executions.Count)
            throw new PlanningValidationException("Seleccione de 1 a 500 ejecuciones distintas y una sesión válida.");
        if(request.AdminOverride&&!administrator)throw new PlanningValidationException("La excepción de modo requiere un administrador.");
        if(request.OperationalAt.Offset!=TimeSpan.FromHours(-6)||request.OperationalAt.Year<2000||request.OperationalAt.Year>2100)
            throw new PlanningValidationException("Revise fecha y hora operativas de Guatemala (UTC−6).");
        var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Serialize(request with{Executions=request.Executions.OrderBy(x=>x.ExecutionId).ToArray(),Reason=reason}))));
        await using var c=await database.Source.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var tx=await c.BeginTransactionAsync(ct).ConfigureAwait(false);await Revision(c,tx,true,ct).ConfigureAwait(false);
        await using(var cmd=Command(c,tx,"SELECT actor_id,request_hash FROM planning.report_sessions WHERE id=@id",("id",request.SessionId)))
        await using(var r=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            if(await r.ReadAsync(ct).ConfigureAwait(false))
            {
                if(r.GetInt32(0)!=actor||r.GetString(1)!=hash)throw new PlanningConflictException("La sesión ya existe con otra solicitud.");
                return new(request.Executions.Select(x=>x.ExecutionId).ToArray(),[]);
            }
        }
        // A new operation may never silently consume an already digitized selection.
        await using(var cmd=Command(c,tx,"SELECT EXISTS(SELECT 1 FROM planning.executions WHERE id=ANY(@ids) AND digitized_at IS NOT NULL AND tested_on IS NOT NULL)",("ids",request.Executions.Select(x=>x.ExecutionId).ToArray())))
            if(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is true)throw new PlanningConflictException("La selección contiene ejecuciones digitadas. Actualice y revise la selección.");
        var result=await ApplyReportChecks(c,tx,new(request.Executions,true,reason,request.AdminOverride,request.OperationalAt),actor,false,reason,ct,request.SessionId,hash).ConfigureAwait(false);
        await Bump(c,tx,ct).ConfigureAwait(false);await tx.CommitAsync(ct).ConfigureAwait(false);return result;
    }
    public async Task<ReportSchedulePage> ReportSchedulesAsync(ReportScheduleQuery query,CancellationToken ct)
    {
        if(query.GroupByClock)return await ReportClockGroupsAsync(query,ct).ConfigureAwait(false);
        ValidateScheduleQuery(query);var page=Math.Clamp(query.Page,1,1000000);var size=Math.Clamp(query.PageSize,1,200);
        await using var c=await database.Source.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var tx=await c.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct).ConfigureAwait(false);
        var revision=await Revision(c,tx,false,ct).ConfigureAwait(false);
        if(query.Revision is {} expected&&expected!=revision)throw new PlanningConflictException("Los horarios cambiaron. Actualice para continuar.");
        var args=new (string,object?)[]{("search",query.Search?.Trim()??""),("state",query.State),("take",size),("skip",(page-1)*size)};
        int total;await using(var cmd=Command(c,tx,"SELECT count(*)::int"+ScheduleFrom+ScheduleWhere,args))total=(int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
        var items=new List<ReportSchedule>();
        await using(var cmd=Command(c,tx,"SELECT "+ScheduleColumns+ScheduleFrom+ScheduleWhere+" ORDER BY g.operational_at DESC,g.id LIMIT @take OFFSET @skip",args))
        await using(var r=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))while(await r.ReadAsync(ct).ConfigureAwait(false))items.Add(Schedule(r));
        await tx.CommitAsync(ct).ConfigureAwait(false);return new(items,total,page,size,revision);
    }
    private static async Task<ReportSchedule> ReadSchedule(NpgsqlConnection c,NpgsqlTransaction tx,Guid id,CancellationToken ct)
    {
        await using var cmd=Command(c,tx,"SELECT "+ScheduleColumns+ScheduleFrom+" WHERE g.id=@id",("id",id));
        await using var r=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await r.ReadAsync(ct).ConfigureAwait(false)?Schedule(r):throw new PlanningValidationException("No existe el horario.");
    }
    public async Task<ReportScheduleDetail> ReportScheduleAsync(Guid id,CancellationToken ct,bool groupByClock=false)
    {
        await using var c=await database.Source.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var tx=await c.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct).ConfigureAwait(false);
        var schedule=groupByClock?await ReadClockGroup(c,tx,id,ct).ConfigureAwait(false):await ReadSchedule(c,tx,id,ct).ConfigureAwait(false);var members=new List<ReportScheduleMember>();
        var ids=schedule.SessionIds?.ToArray()??[id];var sessions=new List<ReportSchedule>();
        foreach(var session in ids)sessions.Add(await ReadSchedule(c,tx,session,ct).ConfigureAwait(false));
        await using(var cmd=Command(c,tx,"SELECT "+RowColumns+",x.version,coalesce(rc.keep_in_planning,false),rc.block_number,m.digitization_version,m.session_id FROM planning.report_session_members m JOIN planning.executions x ON x.id=m.execution_id JOIN planning.episodes e ON e.id=x.episode_id LEFT JOIN planning.job_controls jc ON jc.job=upper(trim(e.job)) LEFT JOIN planning.report_checks rc ON rc.execution_id=x.id WHERE m.session_id=ANY(@ids) ORDER BY e.job,x.equivalence,x.id,m.session_id",("ids",ids)))
        await using(var r=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))while(await r.ReadAsync(ct).ConfigureAwait(false))
        {
            var row=Row(r);var version=r.GetInt64(29);var saved=r.GetInt64(32);var session=sessions.Single(s=>s.Id==r.GetGuid(33));
            row=row with{DigitizationSessionId=session.Id,ScheduleReviewState=session.State,ScheduleReviewedBy=session.ReviewedBy,ScheduleReviewedAt=session.ReviewedAt};
            members.Add(new(new(row,version,r.GetBoolean(30),r.IsDBNull(31)?null:r.GetInt64(31),ReportListRules.CheckMode(row.Equivalence,row.Laboratory)),saved,
                version!=saved||row.DigitizedAt!=session.OperationalAt||row.TestedOn!=DateOnly.FromDateTime(session.OperationalAt.ToOffset(TimeSpan.FromHours(-6)).DateTime)));
        }
        var history=new List<PlanningHistory>();
        await using(var cmd=Command(c,tx,"SELECT h.id,h.action,h.actor_id,h.at,h.reason,h.details::text,a.nombre FROM planning.history h JOIN planning.actors a ON a.id=h.actor_id WHERE h.details->>'sessionId'=ANY(@ids) ORDER BY h.at DESC,h.id LIMIT 1000",("ids",ids.Select(i=>i.ToString()).ToArray())))
        await using(var r=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))while(await r.ReadAsync(ct).ConfigureAwait(false))history.Add(new(r.GetGuid(0),r.GetString(1),r.GetInt32(2),r.GetFieldValue<DateTimeOffset>(3),r.GetString(4),r.GetString(5),r.GetString(6)));
        await tx.CommitAsync(ct).ConfigureAwait(false);return new(schedule,members,history,groupByClock?sessions:null);
    }
    public async Task<ReportSchedule> ReviewReportScheduleAsync(ReportScheduleReview request,int actor,CancellationToken ct)
    {
        if(request.ClockSessions is not null)return await ReviewReportClockAsync(request,actor,ct).ConfigureAwait(false);
        ArgumentNullException.ThrowIfNull(request);var reason=Reason(request.Reason);
        if(request.Executions is null||request.Executions.Count is <1 or >500||request.Executions.Select(x=>x.ExecutionId).Distinct().Count()!=request.Executions.Count)throw new PlanningValidationException("Revise todas las ejecuciones del horario.");
        await using var c=await database.Source.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var tx=await c.BeginTransactionAsync(ct).ConfigureAwait(false);await Revision(c,tx,true,ct).ConfigureAwait(false);
        await ApplyScheduleReview(c,tx,request,actor,reason,ct).ConfigureAwait(false);
        await Bump(c,tx,ct).ConfigureAwait(false);var result=await ReadSchedule(c,tx,request.SessionId,ct).ConfigureAwait(false);await tx.CommitAsync(ct).ConfigureAwait(false);return result;
    }
    private static async Task ApplyScheduleReview(NpgsqlConnection c,NpgsqlTransaction tx,ReportScheduleReview request,int actor,string reason,CancellationToken ct)
    {
        var schedule=await ReadSchedule(c,tx,request.SessionId,ct).ConfigureAwait(false);
        if(schedule.Version!=request.Version||schedule.ReviewedAt is not null||schedule.Exceptions>0||schedule.Tests!=request.Executions.Count)throw new PlanningConflictException("El horario cambió o ya fue chequeado. Revise sus excepciones y vuelva a abrirlo.");
        var expected=request.Executions.ToDictionary(x=>x.ExecutionId,x=>x.Version);var episodes=new HashSet<Guid>();
        await using(var cmd=Command(c,tx,"SELECT x.id,x.version,x.episode_id FROM planning.report_session_members m JOIN planning.executions x ON x.id=m.execution_id WHERE m.session_id=@id ORDER BY x.id FOR UPDATE OF x",("id",request.SessionId)))
        await using(var r=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))while(await r.ReadAsync(ct).ConfigureAwait(false))
        {if(!expected.TryGetValue(r.GetGuid(0),out var version)||version!=r.GetInt64(1))throw new PlanningConflictException("Cambió una ejecución del horario. Vuelva a revisar el detalle.");episodes.Add(r.GetGuid(2));}
        await Execute(c,tx,"UPDATE planning.report_sessions SET reviewed_by=@actor,reviewed_at=clock_timestamp(),version=version+1 WHERE id=@id",ct,("actor",actor),("id",request.SessionId)).ConfigureAwait(false);
        foreach(var episode in episodes)await Audit(c,tx,episode,"ReportScheduleReviewed",actor,reason,new{request.SessionId,request.Executions,request.Version},ct).ConfigureAwait(false);
    }
    private static async Task EnrichScheduleReviews(NpgsqlConnection c,NpgsqlTransaction tx,List<PlanningRow> rows,CancellationToken ct)
    {
        // Consultation remains available during an additive rollout before schedules are installed.
        await using(var cmd=Command(c,tx,"SELECT to_regclass('planning.report_schedule_status') IS NOT NULL"))if(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is not true)return;
        var ids=rows.Where(x=>x.ExecutionId is not null).Select(x=>x.ExecutionId!.Value).ToArray();if(ids.Length==0)return;
        var review=new Dictionary<Guid,(string State,string? By,DateTimeOffset? At,Guid Session)>();
        await using(var cmd=Command(c,tx,"SELECT DISTINCT ON(m.execution_id) m.execution_id,"+ScheduleState+",reviewer.nombre,g.reviewed_at,g.id"+ScheduleFrom+" JOIN planning.report_session_members m ON m.session_id=g.id JOIN planning.executions x ON x.id=m.execution_id WHERE m.execution_id=ANY(@ids) AND x.version=m.digitization_version AND x.digitized_at=g.operational_at ORDER BY m.execution_id,g.saved_at DESC,g.id",("ids",ids)))
        await using(var r=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))while(await r.ReadAsync(ct).ConfigureAwait(false))review[r.GetGuid(0)]=(r.GetString(1),NullableString(r,2),r.IsDBNull(3)?null:r.GetFieldValue<DateTimeOffset>(3),r.GetGuid(4));
        for(int i=0;i<rows.Count;i++)if(rows[i].ExecutionId is {} id&&review.TryGetValue(id,out var v))rows[i]=rows[i] with{ScheduleReviewState=v.State,ScheduleReviewedBy=v.By,ScheduleReviewedAt=v.At,DigitizationSessionId=v.Session};
    }
    public async Task<ReportClosurePage> ReportClosuresAsync(ReportScheduleQuery query,CancellationToken ct)
    {
        ValidateScheduleQuery(query);var page=Math.Clamp(query.Page,1,1000000);var size=Math.Clamp(query.PageSize,1,200);
        const string closure="""
            WITH complete AS (
              SELECT e.job,count(*) FILTER(WHERE NOT (x.no_program OR upper(coalesce(trim(x.laboratory),''))='NO PROGRAMAR'))::int AS required,
                count(*) FILTER(WHERE NOT (x.no_program OR upper(coalesce(trim(x.laboratory),''))='NO PROGRAMAR') AND x.digitized_at IS NOT NULL AND x.tested_on IS NOT NULL)::int AS digitized,
                max(x.digitized_at) FILTER(WHERE NOT (x.no_program OR upper(coalesce(trim(x.laboratory),''))='NO PROGRAMAR')) AS last_at,min(e.due_date) AS due
              FROM planning.episodes e LEFT JOIN planning.executions x ON x.episode_id=e.id
              /*ADMISSION*/
              GROUP BY e.job HAVING count(x.id)>0 AND bool_and(x.id IS NOT NULL)
            ) SELECT c.* FROM complete c WHERE c.required>0 AND c.required=c.digitized
              AND (@search='' OR strpos(lower(c.job),lower(@search))>0)
              AND NOT EXISTS(SELECT 1 FROM planning.job_controls jc WHERE jc.job=upper(trim(c.job)) AND jc.on_hold)
              AND NOT EXISTS(SELECT 1 FROM planning.source_rows s WHERE s.job=c.job AND s.file IN ('Report List','Side2','React Active') AND s.execution_id IS NULL)
              AND NOT EXISTS(SELECT 1 FROM planning.episodes e JOIN planning.incidents i ON i.episode_id=e.id WHERE e.job=c.job AND i.state NOT IN ('Resolved','Excluded'))
              AND NOT EXISTS(SELECT 1 FROM planning.episodes e JOIN planning.executions x ON x.episode_id=e.id WHERE e.job=c.job AND NOT x.no_program AND (nullif(trim(x.equivalence),'') IS NULL OR nullif(trim(x.laboratory),'') IS NULL))
              /*ADMISSION_BLOCK*/
            """;
        await using var c=await database.Source.OpenConnectionAsync(ct).ConfigureAwait(false);await using var tx=await c.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct).ConfigureAwait(false);
        var integrated=await HasReactLinks(c,tx,ct).ConfigureAwait(false);
        var closureSql=closure.Replace("/*ADMISSION*/",integrated?"WHERE "+ReactOperationalPredicate:"",StringComparison.Ordinal)
            .Replace("/*ADMISSION_BLOCK*/",integrated?"""
                AND NOT EXISTS(SELECT 1 FROM planning.react_execution_links admission JOIN planning.executions x ON x.id=admission.execution_id
                  JOIN planning.episodes e ON e.id=x.episode_id WHERE e.job=c.job
                    AND admission.disposition IN ('MissingEquivalence','InvalidLaboratory','IncompleteData','ReactIdentity','Revision','ReportWithoutSummary'))
                """:"",StringComparison.Ordinal);
        var revision=await Revision(c,tx,false,ct).ConfigureAwait(false);if(query.Revision is {} expected&&expected!=revision)throw new PlanningConflictException("Los JOBs cambiaron. Actualice Cierres.");
        var args=new (string,object?)[]{("search",query.Search?.Trim()??""),("take",size),("skip",(page-1)*size)};
        int total;await using(var cmd=Command(c,tx,"SELECT count(*)::int FROM ("+closureSql+") results",args))total=(int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
        var items=new List<ReportClosure>();await using(var cmd=Command(c,tx,closureSql+" ORDER BY c.last_at DESC,c.job LIMIT @take OFFSET @skip",args))
        await using(var r=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))while(await r.ReadAsync(ct).ConfigureAwait(false))items.Add(new(r.GetString(0),r.GetInt32(1),r.GetInt32(2),r.GetFieldValue<DateTimeOffset>(3),NullableDate(r,4)));
        await tx.CommitAsync(ct).ConfigureAwait(false);return new(items,total,page,size,revision,"Completos entre todas las ejecuciones persistidas del JOB. Cobertura externa TIPS no certificada; no constituye cierre oficial.");
    }
    public async Task<byte[]> ExportReviewedSchedulesAsync(ReportScheduleQuery query,CancellationToken ct)
    {
        // One snapshot for selection, membership and validity. Never export a reviewed subset of an invalid group.
        ValidateScheduleQuery(query);
        await using var c=await database.Source.OpenConnectionAsync(ct).ConfigureAwait(false);await using var tx=await c.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct).ConfigureAwait(false);
        var csv=new StringBuilder("\uFEFFSesión,JOB,Prueba,Prueba original,Laboratorio,Ejecución,Fecha y hora,Digitado por,Chequeado por,Fecha de chequeo,Fecha de ingreso,Due Date,Cliente\r\n");
        static string Cell(string? s){s??="";if(s.Length>0&&"=+-@\t\r".Contains(s[0]))s="'"+s;return "\""+s.Replace("\"","\"\"",StringComparison.Ordinal)+"\"";}
        var wholeClock=query.GroupByClock?" AND NOT EXISTS(SELECT 1 FROM planning.report_schedule_status other WHERE date_trunc('minute',other.operational_at)=date_trunc('minute',g.operational_at) AND (other.exceptions>0 OR other.reviewed_at IS NULL))":"";
        await using var cmd=Command(c,tx,"SELECT g.id,e.job,x.equivalence,x.original_test,x.laboratory,x.id,g.operational_at,a.nombre,reviewer.nombre,g.reviewed_at,e.in_date,e.due_date,e.buyer"+ScheduleFrom+" JOIN planning.report_session_members m ON m.session_id=g.id JOIN planning.executions x ON x.id=m.execution_id JOIN planning.episodes e ON e.id=x.episode_id "+ScheduleWhere+" AND g.exceptions=0 AND g.reviewed_at IS NOT NULL"+wholeClock+" ORDER BY g.operational_at DESC,g.id,e.job,x.id LIMIT 50001",("search",query.Search?.Trim()??""),("state","Reviewed"));
        await using(var r=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {int n=0;while(await r.ReadAsync(ct).ConfigureAwait(false)){if(++n>50000)throw new PlanningValidationException("Ajuste el filtro: máximo 50000 pruebas por exportación.");csv.AppendLine(string.Join(',',Enumerable.Range(0,13).Select(i=>Cell(r.IsDBNull(i)?null:i is 10 or 11?r.GetFieldValue<DateOnly>(i).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture):r.GetValue(i) is DateTime dt?dt.ToUniversalTime().ToString("O",CultureInfo.InvariantCulture):Convert.ToString(r.GetValue(i),CultureInfo.InvariantCulture)))));}}
        await tx.CommitAsync(ct).ConfigureAwait(false);return Encoding.UTF8.GetBytes(csv.ToString());
    }
}

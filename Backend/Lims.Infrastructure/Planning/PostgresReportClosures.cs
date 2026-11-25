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
    private const string ClosureCoverage="Todas las ejecuciones requeridas del JOB tienen fecha y hora. El seguimiento es interno de LIMS; el estado oficial se consulta en el expediente.";
    // Shared by the original endpoint, the workspace, export and the write-time eligibility check.
    private const string ClosureCandidates="""
        WITH valid_reviews AS MATERIALIZED (
          SELECT DISTINCT m.execution_id FROM planning.report_session_members m
          JOIN planning.report_schedule_status g ON g.id=m.session_id JOIN planning.executions x ON x.id=m.execution_id
          WHERE m.digitization_version=x.version AND g.operational_at=x.digitized_at
            AND x.tested_on=(g.operational_at AT TIME ZONE 'America/Guatemala')::date AND g.exceptions=0 AND g.reviewed_at IS NOT NULL
        ),complete AS (
          SELECT e.job,
            count(*) FILTER(WHERE NOT (x.no_program OR upper(coalesce(trim(x.laboratory),''))='NO PROGRAMAR'))::int AS required,
            count(*) FILTER(WHERE NOT (x.no_program OR upper(coalesce(trim(x.laboratory),''))='NO PROGRAMAR') AND x.digitized_at IS NOT NULL AND x.tested_on IS NOT NULL)::int AS digitized,
            max(x.digitized_at) FILTER(WHERE NOT (x.no_program OR upper(coalesce(trim(x.laboratory),''))='NO PROGRAMAR')) AS last_at,
            min(e.due_date) AS due,string_agg(DISTINCT e.buyer,', ' ORDER BY e.buyer) AS buyer,
            (array_agg(e.usd ORDER BY e.source_in_date DESC,e.id DESC))[1] AS usd,
            (array_agg(e.service ORDER BY e.source_in_date DESC,e.id DESC))[1] AS service,
            string_agg(DISTINCT x.laboratory,', ' ORDER BY x.laboratory) FILTER(WHERE NOT x.no_program AND upper(trim(x.laboratory))<>'NO PROGRAMAR') AS labs,
            string_agg(DISTINCT x.digitized_by,', ' ORDER BY x.digitized_by) FILTER(WHERE NOT x.no_program AND upper(trim(x.laboratory))<>'NO PROGRAMAR') AS actors,
            (array_agg(x.equivalence ORDER BY x.digitized_at DESC NULLS LAST,x.id) FILTER(WHERE NOT x.no_program AND upper(trim(x.laboratory))<>'NO PROGRAMAR'))[1] AS last_test,
            array_agg(DISTINCT e.id ORDER BY e.id) AS episodes,
            md5(string_agg(jsonb_build_array(x.id,x.version,extract(epoch FROM x.digitized_at),x.tested_on,x.equivalence,x.laboratory,x.no_program,x.original_test,x.execution_type)::text,',' ORDER BY x.id)) AS fingerprint,
            count(*) FILTER(WHERE NOT x.no_program AND upper(trim(x.laboratory))<>'NO PROGRAMAR' AND vr.execution_id IS NOT NULL)::int AS reviewed
          FROM planning.episodes e LEFT JOIN planning.executions x ON x.episode_id=e.id LEFT JOIN valid_reviews vr ON vr.execution_id=x.id
          /*ADMISSION*/
          GROUP BY e.job HAVING count(x.id)>0 AND bool_and(x.id IS NOT NULL)
        ) SELECT c.*,jsonb_build_object('job',c.job,'required',c.required,'digitized',c.digitized,'lastDigitizedAt',c.last_at,'dueDate',c.due,
            'buyer',c.buyer,'laboratories',coalesce(c.labs,''),'digitizedBy',coalesce(c.actors,''),'lastTest',coalesce(c.last_test,''),
            'episodeIds',c.episodes,'fingerprint',c.fingerprint,'reviewed',c.reviewed,'usd',c.usd,'service',c.service) AS data
          FROM complete c WHERE c.required>0 AND c.required=c.digitized
          AND NOT EXISTS(SELECT 1 FROM planning.job_controls jc WHERE jc.job=upper(trim(c.job)) AND jc.on_hold)
          AND NOT EXISTS(SELECT 1 FROM planning.source_rows s WHERE s.job=c.job AND s.file IN ('Report List','Side2','React Active') AND s.execution_id IS NULL)
          AND NOT EXISTS(SELECT 1 FROM planning.episodes e JOIN planning.incidents i ON i.episode_id=e.id WHERE e.job=c.job AND i.state NOT IN ('Resolved','Excluded'))
          AND NOT EXISTS(SELECT 1 FROM planning.episodes e JOIN planning.executions x ON x.episode_id=e.id WHERE e.job=c.job AND NOT x.no_program
            AND (nullif(trim(x.equivalence),'') IS NULL OR upper(trim(x.equivalence))='SIN EQUIVALENCIA' OR nullif(trim(x.laboratory),'') IS NULL))
          /*ADMISSION_BLOCK*/
        """;
    private static async Task<string> ClosureCandidatesSql(NpgsqlConnection c,NpgsqlTransaction tx,CancellationToken ct)
    {
        var integrated=await HasReactLinks(c,tx,ct).ConfigureAwait(false);
        return ClosureCandidates.Replace("/*ADMISSION*/","WHERE "+VisiblePlanningJobSql+(integrated?" AND "+ReactOperationalPredicate:""),StringComparison.Ordinal)
            .Replace("/*ADMISSION_BLOCK*/",integrated?"""
                AND NOT EXISTS(SELECT 1 FROM planning.react_execution_links admission JOIN planning.executions x ON x.id=admission.execution_id
                  JOIN planning.episodes e ON e.id=x.episode_id WHERE e.job=c.job
                    AND admission.disposition IN ('MissingEquivalence','InvalidLaboratory','IncompleteData','ReactIdentity','Revision','ReportWithoutSummary'))
                """:"",StringComparison.Ordinal);
    }
    private static void ValidateClosureQuery(ReportClosureQuery q)
    {
        if(q.Search?.Length>20000||q.Laboratory?.Length>200||q.Scope is not ("Current" or "History" or "All")||q.State is not ("All" or "Pending" or "Seen" or "Archived")||q.Sort is not ("Latest" or "Due" or "Job" or "FollowUp")||q.From>q.Until)
            throw new PlanningValidationException("Revise los filtros de Cierres.");
        if(q.Dates?.Length>12000||q.Times?.Length>9000||!ValidClosureSelection(q.Dates,true)||!ValidClosureSelection(q.Times,false))throw new PlanningValidationException("Revise las fechas y horas seleccionadas.");
    }
    private static bool ValidClosureSelection(string? value,bool dates)=>string.IsNullOrEmpty(value)||value=="None"||value.Split(',').All(s=>dates?DateOnly.TryParseExact(s,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out _):TimeOnly.TryParseExact(s,"HH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out _));
    private const string FollowUpData="""
        jsonb_build_object('state',coalesce(f.state,'Pending'),'responsible',f.responsible,'notes',f.notes,'followUpOn',f.follow_up_on,
          'followUpVersion',coalesce(f.version,0),'updatedBy',a.nombre,'updatedAt',f.updated_at,'current',c.job IS NOT NULL)
        """;
    private static async Task<string> ClosureWorkspaceSql(NpgsqlConnection c,NpgsqlTransaction tx,ReportClosureQuery q,CancellationToken ct)
    {
        var candidates=await ClosureCandidatesSql(c,tx,ct).ConfigureAwait(false);
        var entries=q.Scope=="History"?$"""
            SELECT f.job,f.fingerprint,f.snapshot->>'laboratories' AS labs,
              (f.snapshot->>'lastDigitizedAt')::timestamptz AS last_at,(f.snapshot->>'dueDate')::date AS due,
              f.follow_up_on,f.state,f.snapshot||{FollowUpData} AS data
            FROM planning.report_closure_followups f LEFT JOIN ready c ON c.job=f.job AND c.fingerprint=f.fingerprint
            JOIN planning.actors a ON a.id=f.updated_by
            WHERE strpos(f.job,'-')=0 AND strpos(upper(f.job),'REV')=0
            """:$"""
            SELECT c.job,c.fingerprint,c.labs,c.last_at,c.due,f.follow_up_on,coalesce(f.state,'Pending') AS state,c.data||{FollowUpData} AS data
            FROM ready c LEFT JOIN planning.report_closure_followups f ON f.job=c.job AND f.fingerprint=c.fingerprint
            LEFT JOIN planning.actors a ON a.id=f.updated_by
            WHERE {(q.Scope=="Current"?"coalesce(f.state,'Pending')<>'Archived'":"true")}
            """;
        return $"""
            WITH ready AS ({candidates}),entries AS ({entries})
            SELECT * FROM entries WHERE (@search='' OR strpos(lower(concat_ws(' ',job,labs,data->>'buyer',data->>'lastTest',data->>'service',data->>'digitizedBy',data->>'responsible',data->>'notes')),lower(@search))>0)
              AND (@lab='' OR strpos(lower(labs),lower(@lab))>0) AND (@state='All' OR state=@state)
              AND (@from='' OR (last_at AT TIME ZONE 'America/Guatemala')::date>=nullif(@from,'')::date)
              AND (@until='' OR (last_at AT TIME ZONE 'America/Guatemala')::date<=nullif(@until,'')::date)
              AND (@dates='' OR to_char(last_at AT TIME ZONE 'America/Guatemala','YYYY-MM-DD')=ANY(string_to_array(@dates,',')))
              AND (@times='' OR to_char(last_at AT TIME ZONE 'America/Guatemala','HH24:MI')=ANY(string_to_array(@times,',')))
            """;
    }
    private static (string,object?)[] ClosureArgs(ReportClosureQuery q)=>[("search",q.Search?.Trim()??""),("lab",q.Laboratory?.Trim()??""),("state",q.State),
        ("from",q.From?.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)??""),("until",q.Until?.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)??""),("dates",q.Dates??""),("times",q.Times??"")];
    private static string ClosureOrder(ReportClosureQuery q)=>q.Sort switch{"Due"=>"due ASC NULLS LAST,last_at DESC,job,fingerprint","Job"=>"job,fingerprint","FollowUp"=>"follow_up_on ASC NULLS LAST,last_at DESC,job,fingerprint",_=>"last_at DESC,job,fingerprint"};
    public async Task<ReportClosurePage> ClosureWorkspaceAsync(ReportClosureQuery query,CancellationToken ct)
    {
        ValidateClosureQuery(query);var page=Math.Clamp(query.Page,1,1000000);var size=Math.Clamp(query.PageSize,1,200);
        await using var c=await database.Source.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var tx=await c.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct).ConfigureAwait(false);
        var revision=await Revision(c,tx,false,ct).ConfigureAwait(false);
        if(query.Revision is {} expected&&expected!=revision)throw new PlanningConflictException("Cierres cambió. Actualice para continuar.");
        var sql=await ClosureWorkspaceSql(c,tx,query,ct).ConfigureAwait(false);var args=ClosureArgs(query);
        int total;await using(var cmd=Command(c,tx,"SELECT count(*)::int FROM ("+sql+") results",args))total=(int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
        var facets=new List<ReportClosureFacet>();
        if(page==1)
        {
            var facetQuery=query with{Dates=null,Times=null,From=null,Until=null};var facetSql=await ClosureWorkspaceSql(c,tx,facetQuery,ct).ConfigureAwait(false);
            await using var cmd=Command(c,tx,"SELECT (last_at AT TIME ZONE 'America/Guatemala')::date,to_char(last_at AT TIME ZONE 'America/Guatemala','HH24:MI'),count(*)::int FROM ("+facetSql+") results GROUP BY 1,2 ORDER BY 1 DESC,2 DESC",ClosureArgs(facetQuery));
            await using var r=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);while(await r.ReadAsync(ct).ConfigureAwait(false))facets.Add(new(r.GetFieldValue<DateOnly>(0),r.GetString(1),r.GetInt32(2)));
        }
        var items=new List<ReportClosure>();await using(var cmd=Command(c,tx,"SELECT (data||jsonb_build_object('groupCount',count(*) OVER(PARTITION BY date_trunc('minute',last_at))))::text FROM ("+sql+") results ORDER BY "+ClosureOrder(query)+" LIMIT @take OFFSET @skip",[..args,("take",size),("skip",(page-1)*size)]))
        await using(var r=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))while(await r.ReadAsync(ct).ConfigureAwait(false))items.Add(Read<ReportClosure>(r.GetString(0)));
        await tx.CommitAsync(ct).ConfigureAwait(false);return new(items,total,page,size,revision,ClosureCoverage){Facets=facets};
    }
    private static async Task<ReportClosure?> CurrentClosure(NpgsqlConnection c,NpgsqlTransaction tx,string job,string fingerprint,CancellationToken ct)
    {
        var sql=await ClosureWorkspaceSql(c,tx,new(Scope:"All"),ct).ConfigureAwait(false);
        await using var cmd=Command(c,tx,"SELECT data::text FROM ("+sql+") results WHERE job=@job AND fingerprint=@fingerprint",[..ClosureArgs(new()),("job",job),("fingerprint",fingerprint)]);
        var result=await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);return result is string json?Read<ReportClosure>(json):null;
    }
    public async Task<ReportClosureDetail> ClosureDetailAsync(string job,string fingerprint,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(job)||job.Length>200||fingerprint.Length!=32)throw new PlanningValidationException("Seleccione un cierre válido.");
        await using var c=await database.Source.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var tx=await c.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct).ConfigureAwait(false);
        var closure=await CurrentClosure(c,tx,job,fingerprint,ct).ConfigureAwait(false);
        if(closure is null)
        {
            var sql=await ClosureWorkspaceSql(c,tx,new(Scope:"History"),ct).ConfigureAwait(false);
            await using var cmd=Command(c,tx,"SELECT data::text FROM ("+sql+") results WHERE job=@job AND fingerprint=@fingerprint",[..ClosureArgs(new()),("job",job),("fingerprint",fingerprint)]);
            closure=await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is string json?Read<ReportClosure>(json):throw new PlanningConflictException("El JOB cambió. Actualice Cierres.");
        }
        var rows=new List<PlanningRow>();
        await using(var cmd=Command(c,tx,"SELECT "+RowColumns+" FROM planning.executions x JOIN planning.episodes e ON e.id=x.episode_id LEFT JOIN planning.job_controls jc ON jc.job=upper(trim(e.job)) WHERE e.job=@job ORDER BY x.digitized_at NULLS FIRST,x.equivalence,x.id",("job",job)))
        await using(var r=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))while(await r.ReadAsync(ct).ConfigureAwait(false))rows.Add(Row(r));
        await EnrichScheduleReviews(c,tx,rows,ct).ConfigureAwait(false);
        var history=new List<PlanningHistory>();
        await using(var cmd=Command(c,tx,"SELECT h.id,h.action,h.actor_id,h.at,h.reason,h.details::text,a.nombre FROM planning.history h JOIN planning.actors a ON a.id=h.actor_id WHERE h.action='ReportClosureFollowUp' AND h.details->>'job'=@job ORDER BY h.at DESC,h.id LIMIT 200",("job",job)))
        await using(var r=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))while(await r.ReadAsync(ct).ConfigureAwait(false))history.Add(new(r.GetGuid(0),r.GetString(1),r.GetInt32(2),r.GetFieldValue<DateTimeOffset>(3),r.GetString(4),r.GetString(5),r.GetString(6)));
        await tx.CommitAsync(ct).ConfigureAwait(false);return new(closure,rows,history);
    }
    public async Task<ReportClosure> SaveClosureFollowUpAsync(ReportClosureFollowUp request,int actor,CancellationToken ct)
    {
        var reason=Reason(request.Reason);
        if(request.RequestId==Guid.Empty||string.IsNullOrWhiteSpace(request.Job)||request.Job.Length>200||request.Fingerprint?.Length!=32||request.Version<0||request.State is not ("Pending" or "Seen" or "Archived")||request.Responsible?.Length>200||request.Notes?.Length>2000)
            throw new PlanningValidationException("Revise estado, responsable y notas del seguimiento.");
        var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Serialize(request))));
        await using var c=await database.Source.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var tx=await c.BeginTransactionAsync(ct).ConfigureAwait(false);await Revision(c,tx,true,ct).ConfigureAwait(false);
        await using(var cmd=Command(c,tx,"SELECT actor_id,request_hash,result::text FROM planning.report_closure_requests WHERE id=@id",("id",request.RequestId)))
        await using(var r=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))if(await r.ReadAsync(ct).ConfigureAwait(false))
        {if(r.GetInt32(0)!=actor||r.GetString(1)!=hash)throw new PlanningConflictException("La solicitud ya se utilizó para otro seguimiento.");return Read<ReportClosure>(r.GetString(2));}
        var before=await CurrentClosure(c,tx,request.Job,request.Fingerprint,ct).ConfigureAwait(false)
            ??throw new PlanningConflictException("El JOB ya no corresponde a este cierre. Actualice y revise sus pruebas.");
        if(before.FollowUpVersion!=request.Version)throw new PlanningConflictException("Otro usuario modificó el seguimiento. Vuelva a abrir el JOB.");
        await Execute(c,tx,"""
            INSERT INTO planning.report_closure_followups(job,fingerprint,snapshot,state,responsible,notes,follow_up_on,updated_by)
            VALUES(@job,@fingerprint,@snapshot::jsonb,@state,@responsible,@notes,nullif(@follow,'')::date,@actor)
            ON CONFLICT(job,fingerprint) DO UPDATE SET state=EXCLUDED.state,responsible=EXCLUDED.responsible,notes=EXCLUDED.notes,
              follow_up_on=EXCLUDED.follow_up_on,updated_by=EXCLUDED.updated_by,updated_at=clock_timestamp(),version=report_closure_followups.version+1,
              snapshot=EXCLUDED.snapshot
            """,ct,("job",request.Job),("fingerprint",request.Fingerprint),("snapshot",Serialize(before)),("state",request.State),
            ("responsible",request.Responsible?.Trim()),("notes",request.Notes?.Trim()),("follow",request.FollowUpOn?.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)??""),("actor",actor)).ConfigureAwait(false);
        var saved=(await CurrentClosure(c,tx,request.Job,request.Fingerprint,ct).ConfigureAwait(false))!;
        // Audit each episode so Consulta and the closure detail share the same evidence.
        foreach(var episode in saved.EpisodeIds)await Audit(c,tx,episode,"ReportClosureFollowUp",actor,reason,new{job=request.Job,request.RequestId,before,after=saved},ct).ConfigureAwait(false);
        await Execute(c,tx,"INSERT INTO planning.report_closure_requests(id,actor_id,request_hash,result) VALUES(@id,@actor,@hash,@result::jsonb)",ct,("id",request.RequestId),("actor",actor),("hash",hash),("result",Serialize(saved))).ConfigureAwait(false);
        await Bump(c,tx,ct).ConfigureAwait(false);await tx.CommitAsync(ct).ConfigureAwait(false);return saved;
    }
    public async Task<byte[]> ExportClosuresAsync(ReportClosureQuery query,CancellationToken ct)
    {
        ValidateClosureQuery(query);await using var c=await database.Source.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var tx=await c.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct).ConfigureAwait(false);
        var revision=await Revision(c,tx,false,ct).ConfigureAwait(false);
        if(query.Revision is {} expected&&expected!=revision)throw new PlanningConflictException("Cierres cambió. Actualice antes de exportar.");
        var sql=await ClosureWorkspaceSql(c,tx,query,ct).ConfigureAwait(false);
        if(query.IncludeTests)
        {
            if(query.Scope=="History")throw new PlanningValidationException("La exportación por prueba corresponde a cierres vigentes. Use la bandeja Por atender.");
            var tests=new StringBuilder("\uFEFFJOB,Cliente,Prueba,Prueba original,Laboratorio,Ejecución,Fecha de prueba,Fecha y hora de digitación,Digitado por\r\n");
            await using(var cmd=Command(c,tx,"SELECT e.job,e.buyer,x.equivalence,x.original_test,x.laboratory,x.id,x.tested_on,x.digitized_at,x.digitized_by FROM ("+sql+") results JOIN planning.episodes e ON e.job=results.job JOIN planning.executions x ON x.episode_id=e.id WHERE NOT x.no_program AND upper(trim(x.laboratory))<>'NO PROGRAMAR' ORDER BY results.last_at DESC,e.job,x.digitized_at,x.id",ClosureArgs(query)))
            await using(var r=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))while(await r.ReadAsync(ct).ConfigureAwait(false))
                tests.AppendLine(string.Join(',',new[]{r.GetString(0),NullableString(r,1),NullableString(r,2),r.GetString(3),NullableString(r,4),r.GetGuid(5).ToString(),NullableDate(r,6)?.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),r.GetFieldValue<DateTimeOffset>(7).ToOffset(TimeSpan.FromHours(-6)).ToString("yyyy-MM-dd HH:mm:ss",CultureInfo.InvariantCulture),NullableString(r,8)}.Select(ClosureCsvCell)));
            await tx.CommitAsync(ct).ConfigureAwait(false);return Encoding.UTF8.GetBytes(tests.ToString());
        }
        var csv=new StringBuilder("\uFEFFJOB,Buyer,Prueba,USD,Service,Fecha,Hora,Laboratorios,Pruebas requeridas,Digitadas,Pruebas con horario chequeado,Due Date,Estado,Responsable,Seguimiento,Notas,Actualizado por,Vigencia\r\n");
        await using(var cmd=Command(c,tx,"SELECT data::text FROM ("+sql+") results ORDER BY "+ClosureOrder(query),ClosureArgs(query)))
        await using(var r=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))while(await r.ReadAsync(ct).ConfigureAwait(false))
        {var row=Read<ReportClosure>(r.GetString(0));var local=row.LastDigitizedAt.ToOffset(TimeSpan.FromHours(-6));csv.AppendLine(string.Join(',',new[]{row.Job,row.Buyer,row.LastTest,row.Usd?.ToString(CultureInfo.InvariantCulture),row.Service,local.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),local.ToString("HH:mm",CultureInfo.InvariantCulture),row.Laboratories,row.Required.ToString(CultureInfo.InvariantCulture),row.Digitized.ToString(CultureInfo.InvariantCulture),row.Reviewed.ToString(CultureInfo.InvariantCulture),row.DueDate?.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),row.StateText,row.Responsible,row.FollowUpOn?.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),row.Notes,row.UpdatedBy,row.Current?"Vigente":"Cambió: revisar"}.Select(ClosureCsvCell)));}
        await tx.CommitAsync(ct).ConfigureAwait(false);return Encoding.UTF8.GetBytes(csv.ToString());
    }
    private static string ClosureCsvCell(string? value){value??="";if(value.TrimStart().Length>0&&"=+-@".Contains(value.TrimStart()[0]))value="'"+value;return "\""+value.Replace("\"","\"\"",StringComparison.Ordinal)+"\"";}
}

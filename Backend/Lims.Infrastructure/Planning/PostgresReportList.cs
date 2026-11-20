using System.Data;
using Lims.Application.Planning;
using Lims.Contracts.Planning;
using Npgsql;

namespace Lims.Infrastructure.Planning;

public sealed partial class PostgresPlanningStore
{
    public async Task<ReportListPage> ReportListAsync(ReportListQuery query,CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        if(query.View is not ("Pending" or "Checked" or "Active" or "Archived" or "All"))throw new PlanningValidationException("Vista de Report List inválida.");
        var search=(query.Search??"").Trim();
        var jobs=search.Split([';',',','\r','\n','\t'],StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries);
        if(search.Length>20000||jobs.Length>500||new[]{query.Test,query.Laboratory,query.Service,query.Buyer,query.VerifiedBy}.Any(s=>s?.Length>512))throw new PlanningValidationException("Filtros demasiado largos; máximo 500 JOBs.");
        var order=query.Sort switch
        {
            "Default"=>"CASE WHEN @view='Checked' THEN x.digitized_at END DESC NULLS LAST,e.due_date NULLS LAST,e.job,x.equivalence",
            "Job"=>"e.job","Test"=>"x.equivalence","Laboratory"=>"x.laboratory","Buyer"=>"e.buyer","Service"=>"e.service",
            "Block"=>"rc.block_number","Keep"=>"coalesce(rc.keep_in_planning,false)","VerifiedBy"=>"x.digitized_by",
            "Date"=>"(x.digitized_at AT TIME ZONE 'America/Guatemala')::date","Time"=>"(x.digitized_at AT TIME ZONE 'America/Guatemala')::time",
            "Checked"=>"(x.digitized_at IS NOT NULL AND x.tested_on IS NOT NULL)",
            _=>throw new PlanningValidationException("Orden de Report List inválido.")
        };
        if(query.Sort!="Default")order+=(query.Descending?" DESC":" ASC")+" NULLS LAST";
        var page=Math.Clamp(query.Page,1,1000000);var size=Math.Clamp(query.PageSize,1,500);
        const string where="""
            FROM planning.episodes e JOIN planning.executions x ON x.episode_id=e.id
            LEFT JOIN planning.job_controls jc ON jc.job=upper(trim(e.job))
            LEFT JOIN planning.report_checks rc ON rc.execution_id=x.id
            WHERE (@search='' OR CASE WHEN @multiple THEN upper(e.job)=ANY(@jobs)
              ELSE strpos(lower(concat_ws(' ',e.job,x.equivalence,x.original_test,e.buyer,x.digitized_by)),lower(@search))>0 END)
            AND (@test='' OR strpos(lower(coalesce(x.equivalence,'')),lower(@test))>0)
            AND (@lab='' OR strpos(lower(coalesce(x.laboratory,'')),lower(@lab))>0)
            AND (@service='' OR strpos(lower(coalesce(e.service,'')),lower(@service))>0)
            AND (@buyer='' OR strpos(lower(coalesce(e.buyer,'')),lower(@buyer))>0)
            AND (@verified='' OR strpos(lower(coalesce(x.digitized_by,'')),lower(@verified))>0)
            AND (@block::bigint IS NULL OR rc.block_number=@block::bigint)
            AND (@date::date IS NULL OR (x.digitized_at AT TIME ZONE 'America/Guatemala')::date=@date::date)
            AND (@view='All' OR (@view='Pending' AND (x.digitized_at IS NULL OR x.tested_on IS NULL) AND NOT (x.no_program OR upper(coalesce(trim(x.laboratory),''))='NO PROGRAMAR'))
              OR (@view='Checked' AND x.digitized_at IS NOT NULL AND x.tested_on IS NOT NULL)
              OR (@view='Archived' AND x.digitized_at IS NOT NULL AND x.tested_on IS NOT NULL AND NOT coalesce(rc.keep_in_planning,false))
              OR (@view='Active' AND NOT (x.no_program OR upper(coalesce(trim(x.laboratory),''))='NO PROGRAMAR') AND (x.digitized_at IS NULL OR x.tested_on IS NULL OR coalesce(rc.keep_in_planning,false))))
            """;
        var args=new (string,object?)[]{("search",search),("multiple",jobs.Length>1),("jobs",jobs.Select(j=>j.ToUpperInvariant()).ToArray()),("view",query.View),
            ("test",query.Test??""),("lab",query.Laboratory??""),("service",query.Service??""),("buyer",query.Buyer??""),("verified",query.VerifiedBy??""),("block",query.Block),("date",query.CheckedDate),("take",size),("skip",(page-1)*size)};
        await using var c=await database.Source.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var tx=await c.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct).ConfigureAwait(false);
        var revision=await Revision(c,tx,false,ct).ConfigureAwait(false);
        if(query.Revision is {} expected&&expected!=revision)throw new PlanningConflictException("Los datos cambiaron durante el desplazamiento. Actualice Report List para continuar.");
        int count;
        await using(var cmd=Command(c,tx,"SELECT count(*)::int "+where,args))count=(int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
        var items=new List<ReportListItem>();
        await using(var cmd=Command(c,tx,"SELECT "+RowColumns+",x.version,coalesce(rc.keep_in_planning,false),rc.block_number "+where+
            " ORDER BY "+order+",x.id LIMIT @take OFFSET @skip",args))
        await using(var r=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
            while(await r.ReadAsync(ct).ConfigureAwait(false))
            {var row=Row(r);items.Add(new(row,r.GetInt64(29),r.GetBoolean(30),r.IsDBNull(31)?null:r.GetInt64(31),ReportListRules.CheckMode(row.Equivalence,row.Laboratory)));}
        string? scannerSource=null;
        await using(var cmd=Command(c,tx,"""
            SELECT table_schema||'.'||table_name FROM information_schema.columns
            WHERE table_schema='public' AND table_name IN ('tabla_programacion_analitica','tabla_programacion')
              AND column_name IN ('id','job','prueba')
            GROUP BY table_schema,table_name HAVING count(*)=3
              AND has_table_privilege(table_schema||'.'||table_name,'SELECT')
            ORDER BY table_name LIMIT 1
            """))scannerSource=await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
        if(scannerSource is null)
        {
            await using var cmd=Command(c,tx,"SELECT EXISTS(SELECT 1 FROM planning.report_scan_links)");
            if((bool)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!)scannerSource="planning.report_scan_links";
        }
        await tx.CommitAsync(ct).ConfigureAwait(false);return new(items,count,page,size,revision,new(scannerSource is not null,scannerSource));
    }

    public async Task<ReportListResult> CheckReportAsync(ReportListCheck request,int actor,bool administrator,CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);var reason=Reason(request.Reason);
        if(request.Executions is null||request.Executions.Count is <1 or >500||request.Executions.Select(e=>e.ExecutionId).Distinct().Count()!=request.Executions.Count)
            throw new PlanningValidationException("Seleccione de 1 a 500 ejecuciones distintas.");
        if(request.AdminOverride&&!administrator)throw new PlanningValidationException("La excepción de modo requiere un administrador.");
        await using var c=await database.Source.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var tx=await c.BeginTransactionAsync(ct).ConfigureAwait(false);
        await Revision(c,tx,true,ct).ConfigureAwait(false);
        var result=await ApplyReportChecks(c,tx,request,actor,false,reason,ct).ConfigureAwait(false);
        await Bump(c,tx,ct).ConfigureAwait(false);await tx.CommitAsync(ct).ConfigureAwait(false);return result;
    }

    private static async Task<ReportListResult> ApplyReportChecks(NpgsqlConnection c,NpgsqlTransaction tx,ReportListCheck request,int actor,bool scanner,string reason,CancellationToken ct)
    {
        var ready=new HashSet<string>(StringComparer.Ordinal);
        foreach(var selected in request.Executions.OrderBy(e=>e.ExecutionId))
        {
            Guid episode;string job;string? eq,lab,by;bool complete,no,hold;long version;DateTimeOffset? at;DateOnly? tested;
            await using(var cmd=Command(c,tx,"""
                SELECT e.id,e.job,x.equivalence,x.laboratory,x.digitized_at,x.tested_on,x.digitized_by,x.version,
                  (x.no_program OR upper(coalesce(trim(x.laboratory),''))='NO PROGRAMAR'),coalesce(jc.on_hold,false)
                FROM planning.executions x JOIN planning.episodes e ON e.id=x.episode_id
                LEFT JOIN planning.job_controls jc ON jc.job=upper(trim(e.job)) WHERE x.id=@id FOR UPDATE OF x
                """,("id",selected.ExecutionId)))
            await using(var r=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
            {
                if(!await r.ReadAsync(ct).ConfigureAwait(false))throw new PlanningValidationException("No existe la ejecución seleccionada.");
                episode=r.GetGuid(0);job=r.GetString(1);eq=NullableString(r,2);lab=NullableString(r,3);
                at=r.IsDBNull(4)?null:r.GetFieldValue<DateTimeOffset>(4);tested=NullableDate(r,5);by=NullableString(r,6);version=r.GetInt64(7);no=r.GetBoolean(8);hold=r.GetBoolean(9);complete=at is not null&&tested is not null;
            }
            if(version!=selected.Version)throw new PlanningConflictException("La ejecución cambió. Actualice Report List antes de marcar.");
            if(request.Checked&&complete||!request.Checked&&at is null&&tested is null)continue;
            if(request.Checked&&(no||string.IsNullOrWhiteSpace(eq)||hold))throw new PlanningValidationException("Revise la equivalencia, NO PROGRAMAR o la retención del JOB antes de marcar.");
            var mode=ReportListRules.CheckMode(eq,lab);
            if(!request.AdminOverride&&((scanner&&mode!="scanner")||(!scanner&&mode!="manual")))throw new PlanningValidationException("Esta prueba requiere "+mode+". Use el flujo correspondiente.");
            long? block=null;DateTimeOffset? stamp=null;string? actorName=null;
            if(request.Checked)
            {
                await using(var cmd=Command(c,tx,"SELECT nombre FROM planning.actors WHERE id=@actor",("actor",actor)))
                    actorName=(string?)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))??throw new PlanningValidationException("Actor no registrado.");
                DateTimeOffset savedAt;
                await using(var cmd=Command(c,tx,"SELECT clock_timestamp()"))savedAt=new DateTimeOffset((DateTime)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!);
                stamp=request.CheckedAt?.ToUniversalTime()??savedAt;
                // Revision lock serializes all writers. Only the most recent actor can reuse the block.
                await using(var cmd=Command(c,tx,"SELECT number FROM planning.report_blocks WHERE actor_id=@actor AND last_checked_at>=@at-interval '15 minutes' AND number=(SELECT max(number) FROM planning.report_blocks)",("actor",actor),("at",savedAt)))
                    block=await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) as long?;
                if(block is null)
                {await using var cmd=Command(c,tx,"INSERT INTO planning.report_blocks(actor_id,last_checked_at) VALUES(@actor,@at) RETURNING number",("actor",actor),("at",savedAt));block=(long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;}
                else await Execute(c,tx,"UPDATE planning.report_blocks SET last_checked_at=@at WHERE number=@block",ct,("at",savedAt),("block",block)).ConfigureAwait(false);
            }
            var testDate=stamp is null?(DateOnly?)null:DateOnly.FromDateTime(stamp.Value.ToOffset(TimeSpan.FromHours(-6)).DateTime);
            await Execute(c,tx,"UPDATE planning.executions SET digitized_at=@at::timestamptz,tested_on=@date::date,digitized_by=@name,version=version+1 WHERE id=@id",ct,
                ("at",stamp),("date",testDate),("name",actorName),("id",selected.ExecutionId)).ConfigureAwait(false);
            await Execute(c,tx,"""
                INSERT INTO planning.report_checks(execution_id,actor_id,checked_at,block_number) VALUES(@id,@actor::int,@at::timestamptz,@block::bigint)
                ON CONFLICT(execution_id) DO UPDATE SET actor_id=EXCLUDED.actor_id,checked_at=EXCLUDED.checked_at,block_number=EXCLUDED.block_number
                """,ct,("id",selected.ExecutionId),("actor",request.Checked?actor:null),("at",stamp),("block",block)).ConfigureAwait(false);
            await Audit(c,tx,episode,request.Checked?"ReportMarked":"ReportCorrected",actor,reason,
                new {ExecutionId=selected.ExecutionId,Before=new{DigitizedAt=at,TestedOn=tested,DigitizedBy=by},After=new{DigitizedAt=stamp,TestedOn=testDate,DigitizedBy=actorName},BlockNumber=block,Source=scanner?"scanner":"manual",request.AdminOverride},ct).ConfigureAwait(false);
            if(!request.Checked)
                await Execute(c,tx,"UPDATE planning.report_close_alerts a SET reopened_at=clock_timestamp() FROM planning.episodes e WHERE a.episode_id=e.id AND e.job=@job AND a.reopened_at IS NULL",ct,("job",job)).ConfigureAwait(false);
            else
            {
                await using var pending=Command(c,tx,"""
                    SELECT NOT EXISTS(SELECT 1 FROM planning.executions x JOIN planning.episodes e ON e.id=x.episode_id
                      WHERE e.job=@job AND NOT (x.no_program OR upper(coalesce(trim(x.laboratory),''))='NO PROGRAMAR') AND (x.digitized_at IS NULL OR x.tested_on IS NULL))
                    AND NOT EXISTS(SELECT 1 FROM planning.source_rows s WHERE s.job=@job AND s.file='Report List' AND s.execution_id IS NULL)
                    """,("job",job));
                if(await pending.ExecuteScalarAsync(ct).ConfigureAwait(false) is true)
                {
                    ready.Add(job);
                    await Execute(c,tx,"""
                        INSERT INTO planning.report_close_alerts(episode_id,execution_id,actor_id,block_number) VALUES(@episode,@id,@actor,@block::bigint)
                        ON CONFLICT(episode_id) DO UPDATE SET execution_id=EXCLUDED.execution_id,actor_id=EXCLUDED.actor_id,block_number=EXCLUDED.block_number,marked_at=clock_timestamp(),reopened_at=NULL
                        """,ct,("episode",episode),("id",selected.ExecutionId),("actor",actor),("block",block)).ConfigureAwait(false);
                    await Audit(c,tx,episode,"ReportLastPendingCompleted",actor,reason,new {Job=job,ExecutionId=selected.ExecutionId,BlockNumber=block},ct).ConfigureAwait(false);
                }
            }
        }
        return new(request.Executions.Select(e=>e.ExecutionId).ToArray(),ready.ToArray());
    }

    private static async Task ReopenReportAlerts(NpgsqlConnection c,NpgsqlTransaction tx,CancellationToken ct)
    {
        // Existing import installations can finish their identity upgrade before enabling Report List.
        await using(var available=Command(c,tx,"SELECT to_regclass('planning.report_close_alerts') IS NOT NULL"))
            if(await available.ExecuteScalarAsync(ct).ConfigureAwait(false) is not true)return;
        await Execute(c,tx,"""
        UPDATE planning.report_close_alerts a SET reopened_at=clock_timestamp()
        FROM planning.episodes target WHERE target.id=a.episode_id AND a.reopened_at IS NULL
          AND (EXISTS(SELECT 1 FROM planning.executions x JOIN planning.episodes e ON e.id=x.episode_id WHERE e.job=target.job
              AND NOT (x.no_program OR upper(coalesce(trim(x.laboratory),''))='NO PROGRAMAR') AND (x.digitized_at IS NULL OR x.tested_on IS NULL))
            OR EXISTS(SELECT 1 FROM planning.source_rows s WHERE s.job=target.job AND s.file='Report List' AND s.execution_id IS NULL))
        """,ct).ConfigureAwait(false);
    }

    public async Task KeepReportAsync(ReportListKeep request,int actor,CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);var reason=Reason(request.Reason);
        if(request.Execution is null)throw new PlanningValidationException("Seleccione una ejecución.");
        await using var c=await database.Source.OpenConnectionAsync(ct).ConfigureAwait(false);await using var tx=await c.BeginTransactionAsync(ct).ConfigureAwait(false);
        await Revision(c,tx,true,ct).ConfigureAwait(false);Guid episode;
        await using(var cmd=Command(c,tx,"UPDATE planning.executions SET version=version+1 WHERE id=@id AND version=@version RETURNING episode_id",("id",request.Execution.ExecutionId),("version",request.Execution.Version)))
            episode=(Guid)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false)??throw new PlanningConflictException("La ejecución cambió o no existe."));
        await Execute(c,tx,"INSERT INTO planning.report_checks(execution_id,keep_in_planning) VALUES(@id,@keep) ON CONFLICT(execution_id) DO UPDATE SET keep_in_planning=EXCLUDED.keep_in_planning",ct,("id",request.Execution.ExecutionId),("keep",request.KeepInPlanning)).ConfigureAwait(false);
        await Audit(c,tx,episode,"ReportKeepChanged",actor,reason,request,ct).ConfigureAwait(false);
        await Bump(c,tx,ct).ConfigureAwait(false);await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task<ReportListResult> ScanReportAsync(ReportListScan request,int actor,CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);var reason=Reason(request.Reason);
        if(request.Barcode?.Length>200)throw new PlanningValidationException("Código demasiado largo.");
        var id=ReportListRules.BarcodeId(request.Barcode??"")??throw new PlanningValidationException("Código AR o checksum inválido.");
        await using var c=await database.Source.OpenConnectionAsync(ct).ConfigureAwait(false);await using var tx=await c.BeginTransactionAsync(ct).ConfigureAwait(false);
        await Revision(c,tx,true,ct).ConfigureAwait(false);
        Guid? execution=null;
        await using(var cmd=Command(c,tx,"SELECT execution_id FROM planning.report_scan_links WHERE tracking_id=@id",("id",id)))execution=await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) as Guid?;
        if(execution is null)
        {
            // Read verified legacy tracking shapes only in the configured Planning database.
            // A barcode never falls back to JOB + test when several executions match.
            foreach(var table in new[]{"tabla_programacion_analitica","tabla_programacion"})
            {
                await using(var cmd=Command(c,tx,"SELECT count(*)=3 FROM information_schema.columns WHERE table_schema='public' AND table_name=@table AND column_name IN ('id','job','prueba')",("table",table)))
                    if(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is not true)continue;
                var candidates=new List<Guid>();
                await using(var cmd=Command(c,tx,$"SELECT x.id FROM public.{table} t JOIN planning.episodes e ON upper(trim(e.job))=upper(trim(t.job)) JOIN planning.executions x ON x.episode_id=e.id AND upper(trim(x.equivalence))=upper(trim(t.prueba)) WHERE t.id=@id",("id",id)))
                await using(var r=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))while(await r.ReadAsync(ct).ConfigureAwait(false))candidates.Add(r.GetGuid(0));
                if(candidates.Count>1)throw new PlanningConflictException("El código corresponde a varias ejecuciones; concilie su identidad antes de escanear.");
                if(candidates.Count==0)continue;
                execution=candidates[0];
                await Execute(c,tx,"INSERT INTO planning.report_scan_links(tracking_id,execution_id,actor_id,reason) VALUES(@tracking,@execution,@actor,@reason)",ct,("tracking",id),("execution",execution),("actor",actor),("reason",reason)).ConfigureAwait(false);break;
            }
        }
        if(execution is null)throw new PlanningValidationException("El código AR no tiene una identidad de tracking verificada en esta base. No se marcará ninguna ejecución.");
        long version;
        await using(var cmd=Command(c,tx,"SELECT version FROM planning.executions WHERE id=@id",("id",execution)))version=(long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
        var result=await ApplyReportChecks(c,tx,new([new(execution.Value,version)],true,reason,CheckedAt:request.CheckedAt),actor,true,reason,ct).ConfigureAwait(false);
        await Bump(c,tx,ct).ConfigureAwait(false);await tx.CommitAsync(ct).ConfigureAwait(false);return result;
    }
}

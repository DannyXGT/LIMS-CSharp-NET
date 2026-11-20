using System.Text.Json;
using Lims.Contracts.Planning;
using Npgsql;

namespace Lims.Infrastructure.Planning;

public sealed partial class PostgresPlanningStore
{
    private const string RowColumns = """
        e.id,x.id,e.job,e.in_date,e.due_date,x.original_test,x.equivalence,x.laboratory,e.service,
        x.digitized_at,x.tested_on,CASE WHEN x.digitized_at IS NOT NULL AND x.tested_on IS NOT NULL THEN 'Digitado' WHEN x.digitized_at IS NOT NULL OR x.tested_on IS NOT NULL THEN 'Información incompleta' ELSE 'Sin información' END,
        x.digitized_by,x.execution_type,coalesce(x.origin,'Summary'),e.buyer,e.vendor,
        (EXISTS(SELECT 1 FROM planning.incidents i WHERE i.episode_id=e.id AND i.state<>'Resolved') OR (x.id IS NOT NULL AND x.equivalence IS NULL AND NOT x.no_program)),
        x.responsible,x.observation,coalesce(x.note_version,0),coalesce(jc.on_hold,false),jc.label,jc.hold_reason,coalesce(jc.version,0),e.usd,(coalesce(x.no_program,false) OR upper(coalesce(trim(x.laboratory),''))='NO PROGRAMAR'),e.official_fields->>'sampleStatus',e.official_fields->>'jobStatus'
        """;
    private static PlanningRow Row(NpgsqlDataReader r) => new(r.GetGuid(0),r.IsDBNull(1)?null:r.GetGuid(1),r.GetString(2),NullableDate(r,3),NullableDate(r,4),
        NullableString(r,5),NullableString(r,6),NullableString(r,7),NullableString(r,8),r.IsDBNull(9)?null:r.GetFieldValue<DateTimeOffset>(9),NullableDate(r,10),
        r.GetString(11),NullableString(r,12),NullableString(r,13),r.GetString(14),NullableString(r,15),NullableString(r,16),r.GetBoolean(17),NullableString(r,18),NullableString(r,19),r.GetInt64(20),r.GetBoolean(21),NullableString(r,22),NullableString(r,23),r.GetInt64(24),r.IsDBNull(25)?null:r.GetDecimal(25),r.GetBoolean(26),NullableString(r,27),NullableString(r,28));
    public Task<PlanningPage> QueryAsync(PlanningQuery query,CancellationToken ct) => QueryPage(query,500,ct);
    private async Task<PlanningPage> QueryPage(PlanningQuery query,int maximum,CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var size=Math.Clamp(query.PageSize,1,maximum);var page=Math.Clamp(query.Page,1,1000000);
        var sort=query.Sort switch { "jobState"=>OfficialStateSql, "inDate"=>"e.in_date", "dueDate"=>"e.due_date", "test"=>PlanningTestSql, "originalTest"=>"x.original_test", "equivalence"=>"x.equivalence", "laboratory"=>"x.laboratory", "service"=>"e.service", "digitizedAt"=>"(x.digitized_at AT TIME ZONE 'America/Guatemala')::time", "testedOn"=>"x.tested_on", "digitizedBy"=>"x.digitized_by", "state"=>DigitizationSql, "responsible"=>"x.responsible", "observation"=>"x.observation", "executionType"=>"x.execution_type", "origin"=>"x.origin","buyer"=>"e.buyer","vendor"=>"e.vendor","usd"=>"e.usd","onHold"=>"coalesce(jc.on_hold,false)","jobLabel"=>"jc.label","noProgram"=>"x.no_program", _=>"e.job" };
        if(query.SearchMode is not ("Contains" or "Exact" or "Prefix"))throw new Application.Planning.PlanningValidationException("Modo de búsqueda inválido.");
        if(query.Scope is not ("History" or "Operational"))throw new Application.Planning.PlanningValidationException("Seleccione período operativo o histórico completo.");
        var today=DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow,"Central America Standard Time").DateTime);
        DateOnly? dueFrom=query.Scope=="Operational"?today.AddDays(-20):null;
        var columnConditions=ColumnConditions(query.ColumnFilters);
        if(query.InFrom>query.InUntil || query.From>query.Until)throw new Application.Planning.PlanningValidationException("El inicio del rango no puede ser posterior al fin.");
        var search=(query.Search??"").Trim();
        var multiple=search.Split([';',',','\r','\n','\t'],StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
        if(search.Length>20000 || multiple.Length>500) throw new Application.Planning.PlanningValidationException("Máximo 500 JOBs por búsqueda múltiple.");
        if(multiple.Length==1)search=multiple[0];
        var where="""
            FROM planning.episodes e LEFT JOIN planning.executions x ON x.episode_id=e.id
            LEFT JOIN planning.job_controls jc ON jc.job=upper(trim(e.job))
            WHERE (@scopefrom::date IS NULL OR e.due_date>=@scopefrom::date)
            AND (@batch::uuid IS NULL OR EXISTS(SELECT 1 FROM planning.source_rows bs WHERE bs.batch_id=@batch::uuid AND bs.episode_id=e.id AND (x.id IS NULL OR bs.execution_id=x.id OR (x.origin='Manual' AND bs.file='Summary'))))
            AND (@hold::boolean IS NULL OR coalesce(jc.on_hold,false)=@hold::boolean)
            AND (@tagged::boolean IS NULL OR (nullif(trim(jc.label),'') IS NOT NULL)=@tagged::boolean)
            AND (@label='' OR strpos(lower(coalesce(jc.label,'')),lower(@label))>0)
            AND (@search='' OR CASE WHEN @multiple THEN upper(e.job)=ANY(@jobs) WHEN @mode='Exact' THEN lower(e.job)=lower(@search) WHEN @mode='Prefix' THEN starts_with(lower(e.job),lower(@search)) ELSE strpos(lower(e.job||' '||coalesce(jc.label,'')),lower(@search))>0 END)
            AND (@test='' OR strpos(lower(coalesce(x.original_test,'')),lower(@test))>0 OR strpos(lower(coalesce(x.equivalence,'')),lower(@test))>0)
            AND (@equivalence='' OR strpos(lower(coalesce(x.equivalence,'')),lower(@equivalence))>0)
            AND (@lab='' OR strpos(lower(coalesce(x.laboratory,'')),lower(@lab))>0)
            AND (@service='' OR strpos(lower(coalesce(e.service,'')),lower(@service))>0)
            AND (@buyer='' OR strpos(lower(coalesce(e.buyer,'')),lower(@buyer))>0)
            AND (@vendor='' OR strpos(lower(coalesce(e.vendor,'')),lower(@vendor))>0)
            AND (@from::date IS NULL OR e.due_date>=@from::date) AND (@until::date IS NULL OR e.due_date<=@until::date)
            AND (@infrom::date IS NULL OR e.in_date>=@infrom::date) AND (@inuntil::date IS NULL OR e.in_date<=@inuntil::date)
            AND (NOT @overdue OR (e.due_date<=@today::date AND x.id IS NOT NULL AND NOT coalesce(jc.on_hold,false) AND (x.digitized_at IS NULL OR x.tested_on IS NULL)))
            AND (@state='' OR (@state='Digitado' AND x.digitized_at IS NOT NULL AND x.tested_on IS NOT NULL) OR (@state='Pending' AND x.id IS NOT NULL AND NOT coalesce(jc.on_hold,false) AND (x.digitized_at IS NULL OR x.tested_on IS NULL))
              OR (@state='Unknown' AND x.id IS NOT NULL AND x.digitized_at IS NULL AND x.tested_on IS NULL)
              OR (@state='Partial' AND ((x.digitized_at IS NULL)<>(x.tested_on IS NULL)))
              OR (@state='NoProgramar' AND (x.no_program OR upper(coalesce(trim(x.laboratory),''))='NO PROGRAMAR')) OR (@state='Exceptions' AND (EXISTS(SELECT 1 FROM planning.incidents i WHERE i.episode_id=e.id AND i.state<>'Resolved') OR (x.id IS NOT NULL AND x.equivalence IS NULL AND NOT x.no_program))))
            AND (@incident='' OR (@incident='MissingEquivalence' AND x.id IS NOT NULL AND x.equivalence IS NULL AND NOT x.no_program) OR EXISTS(SELECT 1 FROM planning.incidents i WHERE i.episode_id=e.id AND i.kind=@incident AND i.state<>'Resolved'))
            """+columnConditions.Sql;
        var args=new (string,object?)[]{("batch",query.BatchId),("search",search),("multiple",multiple.Length>1),("jobs",multiple.Select(j=>j.ToUpperInvariant()).ToArray()),("test",query.Test??""),("equivalence",query.Equivalence??""),
            ("lab",query.Laboratory??""),("service",query.Service??""),("buyer",query.Buyer??""),("vendor",query.Vendor??""),("from",query.From),("until",query.Until),("state",query.State??""),("incident",query.Incident??""),
            ("mode",query.SearchMode),("infrom",query.InFrom),("inuntil",query.InUntil),("overdue",query.Overdue),("today",today),
            ("scopefrom",dueFrom),("hold",query.OnHold),("tagged",query.Tagged),("label",query.Label??"")}.Concat(columnConditions.Args).ToArray();
        await using var c=await database.Source.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var tx=await c.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead,ct).ConfigureAwait(false);
        int total;PlanningCounts counts;PlanningScopeInfo scope;
        await using(var count=Command(c,tx,"""
            SELECT count(*)::int,count(DISTINCT upper(trim(e.job)))::int,count(x.id)::int,
              count(x.id) FILTER(WHERE x.digitized_at IS NOT NULL AND x.tested_on IS NOT NULL)::int,
              count(x.id) FILTER(WHERE NOT coalesce(jc.on_hold,false) AND (x.digitized_at IS NULL OR x.tested_on IS NULL))::int,
              count(x.id) FILTER(WHERE x.digitized_at IS NULL AND x.tested_on IS NULL)::int,
              count(x.id) FILTER(WHERE NOT coalesce(jc.on_hold,false) AND (x.digitized_at IS NULL OR x.tested_on IS NULL) AND e.due_date<=@today::date)::int,
              count(x.id) FILTER(WHERE jc.on_hold)::int,count(DISTINCT jc.job) FILTER(WHERE jc.on_hold)::int,
              count(DISTINCT jc.job) FILTER(WHERE nullif(trim(jc.label),'') IS NOT NULL)::int,
              count(*) FILTER(WHERE x.id IS NULL)::int,count(*) FILTER(WHERE e.due_date IS NULL)::int
            """+" "+where,args))
        await using(var reader=await count.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            await reader.ReadAsync(ct).ConfigureAwait(false);total=reader.GetInt32(0);
            counts=new(reader.GetInt32(1),reader.GetInt32(2),reader.GetInt32(3),reader.GetInt32(4),reader.GetInt32(5),total,reader.GetInt32(6),reader.GetInt32(7),reader.GetInt32(8),reader.GetInt32(9));
            scope=new(query.Scope,dueFrom,today,reader.GetInt32(10),reader.GetInt32(11),counts.Unknown,
                "Cobertura parcial: los conteos usan los datos persistidos disponibles. Las listas heredadas de On Hold/etiquetas y la cobertura completa de digitación no están verificadas. On Hold y etiquetas muestran registros del nuevo LIMS.");
        }
        var paging=args.Concat(new (string,object?)[]{("size",size),("offset",(page-1)*size)}).ToArray();
        await using var cmd=Command(c,tx,"SELECT "+RowColumns+" "+where+" ORDER BY "+sort+(query.Descending?" DESC":" ASC")+" NULLS LAST,e.id,x.id LIMIT @size OFFSET @offset",paging);
        var rows=new List<PlanningRow>();
        await using(var r=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false)) while(await r.ReadAsync(ct).ConfigureAwait(false)) rows.Add(Row(r));
        await tx.CommitAsync(ct).ConfigureAwait(false);return new(rows,total,page,size,counts,scope);
    }
    public async Task<byte[]> ExportAsync(PlanningQuery query,CancellationToken ct)
    {
        var page=await QueryPage(query with {Page=1,PageSize=50001},50001,ct).ConfigureAwait(false);
        if(page.Total>50000)throw new Application.Planning.PlanningValidationException("La exportación admite hasta 50000 filas. Ajuste los filtros antes de exportar.");
        static string Cell(object? value)
        {
            var text=value switch {DateOnly date=>date.ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture),DateTimeOffset at=>at.ToString("O",System.Globalization.CultureInfo.InvariantCulture),decimal number=>number.ToString(System.Globalization.CultureInfo.InvariantCulture),_=>value?.ToString()??""};
            if(text.Length>0&&"=+-@\t\r".Contains(text[0],StringComparison.Ordinal))text="'"+text;
            return "\""+text.Replace("\"","\"\"",StringComparison.Ordinal)+"\"";
        }
        var csv=new System.Text.StringBuilder("\uFEFFFecha Ingreso,Due Date,Estado,JOB,Prueba,Nombre t?cnico original,Equivalencia,Laboratorio,Hora,Fecha Prueba,Estado de digitación,Digitado por,Servicio,Buyer,Vendor,USD,On Hold,Etiqueta,NO PROGRAMAR,Responsable,Observación,Tipo ejecución,Origen,Episodio,Ejecución,Alcance\r\n");
        foreach(var row in page.Items)csv.AppendLine(string.Join(',',new object?[]{row.InDate,row.DueDate,row.JobState,row.Job,row.PlanningTest,row.OriginalTest,row.Equivalence,row.Laboratory,row.DigitizedAt?.ToOffset(TimeSpan.FromHours(-6)).ToString("HH:mm:ss",System.Globalization.CultureInfo.InvariantCulture),row.TestedOn,row.DigitizationState,row.DigitizedBy,row.Service,row.Buyer,row.Vendor,row.Usd,row.OnHold,row.JobLabel,row.NoProgram,row.Responsible,row.Observation,row.ExecutionType,row.Origin,row.EpisodeId,row.ExecutionId,page.Scope?.Mode}.Select(Cell)));
        return System.Text.Encoding.UTF8.GetBytes(csv.ToString());
    }
    public async Task<JobDossier?> DossierAsync(Guid episodeId,CancellationToken ct)
    {
        await using var c=await database.Source.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var tx=await c.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead,ct).ConfigureAwait(false);
        string job;WipValues wip;long version;PlanningOfficialState official;
        await using(var cmd=Command(c,tx,"SELECT job,buyer,vendor,service,in_date,due_date,amount,currency,usd,version,official_fields::text FROM planning.episodes WHERE id=@id",("id",episodeId)))
        await using(var r=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {if(!await r.ReadAsync(ct).ConfigureAwait(false))return null;job=r.GetString(0);wip=Wip(r,1);version=r.GetInt64(9);official=Read<PlanningOfficialState>(r.GetString(10));}
        var rows=new List<PlanningRow>();
        await using(var cmd=Command(c,tx,"SELECT "+RowColumns+" FROM planning.episodes e JOIN planning.executions x ON x.episode_id=e.id LEFT JOIN planning.job_controls jc ON jc.job=upper(trim(e.job)) WHERE e.id=@id ORDER BY x.original_test,x.id",("id",episodeId)))
        await using(var r=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false)) while(await r.ReadAsync(ct).ConfigureAwait(false))rows.Add(Row(r));
        var incidents=new List<PlanningIncident>();
        await using(var cmd=Command(c,tx,"SELECT id,kind,state,reason,version FROM planning.incidents WHERE episode_id=@id ORDER BY kind",("id",episodeId)))
        await using(var r=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))while(await r.ReadAsync(ct).ConfigureAwait(false))incidents.Add(new(r.GetGuid(0),episodeId,r.GetString(1),r.GetString(2),NullableString(r,3),r.GetInt64(4)));
        var history=new List<PlanningHistory>();
        await using(var cmd=Command(c,tx,"SELECT h.id,h.action,h.actor_id,h.at,h.reason,h.details::text,u.nombre FROM planning.history h JOIN planning.actors u ON u.id=h.actor_id WHERE episode_id=@id ORDER BY at DESC,h.id LIMIT 1000",("id",episodeId)))
        await using(var r=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))while(await r.ReadAsync(ct).ConfigureAwait(false))history.Add(new(r.GetGuid(0),r.GetString(1),r.GetInt32(2),r.GetFieldValue<DateTimeOffset>(3),r.GetString(4),r.GetString(5),NullableString(r,6)));
        var sources=new List<PlanningSource>();
        await using(var cmd=Command(c,tx,"SELECT s.batch_id,s.file,s.sheet,s.row_number,CASE WHEN s.file='Summary' THEN b.summary_sha256 ELSE b.report_sha256 END,b.applied_at,s.fields::text FROM planning.source_rows s JOIN planning.batches b ON b.id=s.batch_id WHERE s.episode_id=@id ORDER BY b.applied_at DESC,s.file,s.row_number LIMIT 2000",("id",episodeId)))
        await using(var r=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))while(await r.ReadAsync(ct).ConfigureAwait(false))sources.Add(new(r.GetGuid(0),r.GetString(1),r.GetString(2),r.GetInt32(3),r.GetString(4),r.GetFieldValue<DateTimeOffset>(5),r.GetString(6)));
        var control=await Control(c,tx,job,ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);return new(episodeId,job,wip,version,rows,incidents,history,sources,control,official);
    }
    public async Task<IReadOnlyList<ImportReview>> ReviewsAsync(CancellationToken ct,string? category=null)=>(await ReviewPageAsync(category,null,1,2000,ct).ConfigureAwait(false)).Items;
    public async Task<PlanningReviewPage> ReviewPageAsync(string? category,string? search,int page,int pageSize,CancellationToken ct)
    {
        if(category?.Length>100||search?.Length>200)throw new Application.Planning.PlanningValidationException("Filtro de bandeja demasiado largo.");
        page=Math.Clamp(page,1,1000000);pageSize=Math.Clamp(pageSize,1,2000);
        await using var c=await database.Source.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var tx=await c.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead,ct).ConfigureAwait(false);
        const string rowsSql="""
            SELECT * FROM (SELECT i.kind,e.job,e.id,NULL::text AS original_test,i.state||': '||coalesce(i.reason,'') AS description,0 AS source_row,NULL::uuid AS batch_id
            FROM planning.incidents i JOIN planning.episodes e ON e.id=i.episode_id WHERE i.state<>'Resolved'
            UNION ALL SELECT 'MissingEquivalence',e.job,e.id,x.original_test,'Asignar equivalencia o NO PROGRAMAR',0,NULL::uuid
            FROM planning.executions x JOIN planning.episodes e ON e.id=x.episode_id WHERE x.equivalence IS NULL AND NOT x.no_program
            UNION ALL SELECT 'ManualManaged',e.job,e.id,x.original_test,'Prueba manual persistida; conserva origen e historial',0,NULL::uuid
            FROM planning.executions x JOIN planning.episodes e ON e.id=x.episode_id WHERE x.origin='Manual'
            UNION ALL SELECT 'ExecutionIdentity',s.job,s.episode_id,s.original_test,'Fila sin vincular; lote '||s.batch_id::text,s.row_number,s.batch_id
            FROM planning.source_rows s WHERE s.file='Report List' AND s.execution_id IS NULL) reviews
            WHERE (@category='' OR kind=@category)
            AND (@search='' OR strpos(lower(job),lower(@search))>0 OR strpos(lower(coalesce(original_test,'')),lower(@search))>0)
            """;
        var args=new (string,object?)[]{("category",category??""),("search",search?.Trim()??""),("skip",(page-1)*pageSize),("take",pageSize)};
        int total;await using(var count=Command(c,tx,"SELECT count(*)::int FROM ("+rowsSql+") all_reviews",args))total=(int)(await count.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
        await using var cmd=Command(c,tx,rowsSql+" ORDER BY CASE WHEN kind='SummaryWithoutReport' THEN 0 ELSE 1 END,kind,job,id,source_row,batch_id LIMIT @take OFFSET @skip",args);
        var items=new List<ImportReview>();
        await using(var r=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
            while(await r.ReadAsync(ct).ConfigureAwait(false))items.Add(new(r.GetString(0),r.GetString(1),r.IsDBNull(2)?null:r.GetGuid(2),NullableString(r,3),r.GetString(4),r.GetInt32(5),r.IsDBNull(6)?null:r.GetGuid(6)));
        await tx.CommitAsync(ct).ConfigureAwait(false);return new(items,total,page,pageSize);
    }
    public async Task<PlanningImportPreview?> BatchPreviewAsync(Guid id,CancellationToken ct)
    {
        await using var c=await database.Source.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd=Command(c,null,"SELECT preview::text,status FROM planning.batches WHERE id=@id",("id",id));
        await using var r=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await r.ReadAsync(ct).ConfigureAwait(false)?Read<PlanningImportPreview>(r.GetString(0)) with { Status=r.GetString(1) }:null;
    }
}

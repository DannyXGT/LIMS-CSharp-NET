using System.Text.Json;
using Lims.Contracts.Planning;
using Npgsql;

namespace Lims.Infrastructure.Planning;

public sealed partial class PostgresPlanningStore
{
    private const string RowColumns = """
        e.id,x.id,e.job,e.in_date,e.due_date,x.original_test,x.equivalence,x.laboratory,e.service,
        x.digitized_at,x.tested_on,CASE WHEN x.no_program THEN 'NoProgramar' WHEN x.digitized_at IS NOT NULL THEN 'Digitado' WHEN x.tested_on IS NOT NULL THEN 'Pendiente' ELSE 'Sin información' END,
        x.digitized_by,x.execution_type,coalesce(x.origin,'Summary'),e.buyer,e.vendor,
        (EXISTS(SELECT 1 FROM planning.incidents i WHERE i.episode_id=e.id AND i.state<>'Resolved') OR (x.id IS NOT NULL AND x.equivalence IS NULL AND NOT x.no_program)),
        x.responsible,x.observation,coalesce(x.note_version,0)
        """;
    private static PlanningRow Row(NpgsqlDataReader r) => new(r.GetGuid(0),r.IsDBNull(1)?null:r.GetGuid(1),r.GetString(2),NullableDate(r,3),NullableDate(r,4),
        NullableString(r,5),NullableString(r,6),NullableString(r,7),NullableString(r,8),r.IsDBNull(9)?null:r.GetFieldValue<DateTimeOffset>(9),NullableDate(r,10),
        r.GetString(11),NullableString(r,12),NullableString(r,13),r.GetString(14),NullableString(r,15),NullableString(r,16),r.GetBoolean(17),NullableString(r,18),NullableString(r,19),r.GetInt64(20));
    public Task<PlanningPage> QueryAsync(PlanningQuery query,CancellationToken ct) => QueryPage(query,500,ct);
    private async Task<PlanningPage> QueryPage(PlanningQuery query,int maximum,CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var size=Math.Clamp(query.PageSize,1,maximum);var page=Math.Clamp(query.Page,1,1000000);
        var sort=query.Sort switch { "inDate"=>"e.in_date", "dueDate"=>"e.due_date", "test"=>"x.original_test", "equivalence"=>"x.equivalence", "laboratory"=>"x.laboratory", "service"=>"e.service", "digitizedAt"=>"x.digitized_at", "testedOn"=>"x.tested_on", "digitizedBy"=>"x.digitized_by", "state"=>"x.digitized_at", "responsible"=>"x.responsible", "observation"=>"x.observation", "executionType"=>"x.execution_type", "origin"=>"x.origin", _=>"e.job" };
        if(query.SearchMode is not ("Contains" or "Exact" or "Prefix"))throw new Application.Planning.PlanningValidationException("Modo de búsqueda inválido.");
        if(query.InFrom>query.InUntil || query.From>query.Until)throw new Application.Planning.PlanningValidationException("El inicio del rango no puede ser posterior al fin.");
        var search=(query.Search??"").Trim();
        var multiple=search.Split([';',',','\r','\n','\t'],StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
        if(search.Length>20000 || multiple.Length>500) throw new Application.Planning.PlanningValidationException("Máximo 500 JOBs por búsqueda múltiple.");
        if(multiple.Length==1)search=multiple[0];
        var where="""
            FROM planning.episodes e LEFT JOIN planning.executions x ON x.episode_id=e.id
            WHERE (@search='' OR CASE WHEN @multiple THEN upper(e.job)=ANY(@jobs) WHEN @mode='Exact' THEN lower(e.job)=lower(@search) WHEN @mode='Prefix' THEN starts_with(lower(e.job),lower(@search)) ELSE strpos(lower(e.job),lower(@search))>0 END)
            AND (@test='' OR strpos(lower(coalesce(x.original_test,'')),lower(@test))>0 OR strpos(lower(coalesce(x.equivalence,'')),lower(@test))>0)
            AND (@equivalence='' OR strpos(lower(coalesce(x.equivalence,'')),lower(@equivalence))>0)
            AND (@lab='' OR strpos(lower(coalesce(x.laboratory,'')),lower(@lab))>0)
            AND (@service='' OR strpos(lower(coalesce(e.service,'')),lower(@service))>0)
            AND (@buyer='' OR strpos(lower(coalesce(e.buyer,'')),lower(@buyer))>0)
            AND (@vendor='' OR strpos(lower(coalesce(e.vendor,'')),lower(@vendor))>0)
            AND (@from::date IS NULL OR e.due_date>=@from::date) AND (@until::date IS NULL OR e.due_date<=@until::date)
            AND (@infrom::date IS NULL OR e.in_date>=@infrom::date) AND (@inuntil::date IS NULL OR e.in_date<=@inuntil::date)
            AND (NOT @overdue OR (e.due_date<@today::date AND x.digitized_at IS NULL AND NOT coalesce(x.no_program,false)))
            AND (@state='' OR (@state='Digitado' AND x.digitized_at IS NOT NULL) OR (@state='Pending' AND x.digitized_at IS NULL AND NOT coalesce(x.no_program,false))
              OR (@state='Unknown' AND x.digitized_at IS NULL AND x.tested_on IS NULL AND NOT coalesce(x.no_program,false))
              OR (@state='NoProgramar' AND x.no_program) OR (@state='Exceptions' AND (EXISTS(SELECT 1 FROM planning.incidents i WHERE i.episode_id=e.id AND i.state<>'Resolved') OR (x.id IS NOT NULL AND x.equivalence IS NULL AND NOT x.no_program))))
            AND (@incident='' OR (@incident='MissingEquivalence' AND x.id IS NOT NULL AND x.equivalence IS NULL AND NOT x.no_program) OR EXISTS(SELECT 1 FROM planning.incidents i WHERE i.episode_id=e.id AND i.kind=@incident AND i.state<>'Resolved'))
            """;
        var args=new (string,object?)[]{("search",search),("multiple",multiple.Length>1),("jobs",multiple.Select(j=>j.ToUpperInvariant()).ToArray()),("test",query.Test??""),("equivalence",query.Equivalence??""),
            ("lab",query.Laboratory??""),("service",query.Service??""),("buyer",query.Buyer??""),("vendor",query.Vendor??""),("from",query.From),("until",query.Until),("state",query.State??""),("incident",query.Incident??""),
            ("mode",query.SearchMode),("infrom",query.InFrom),("inuntil",query.InUntil),("overdue",query.Overdue),("today",DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow,"Central America Standard Time").DateTime))};
        await using var c=await database.Source.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var tx=await c.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead,ct).ConfigureAwait(false);
        int total;PlanningCounts counts;
        await using(var count=Command(c,tx,"SELECT count(*)::int,count(DISTINCT e.job)::int,count(x.id)::int,count(*) FILTER(WHERE x.digitized_at IS NOT NULL)::int,count(*) FILTER(WHERE x.digitized_at IS NULL AND x.tested_on IS NOT NULL AND NOT x.no_program)::int,count(*) FILTER(WHERE x.id IS NOT NULL AND x.digitized_at IS NULL AND x.tested_on IS NULL AND NOT x.no_program)::int "+where,args))
        await using(var reader=await count.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {await reader.ReadAsync(ct).ConfigureAwait(false);total=reader.GetInt32(0);counts=new(reader.GetInt32(1),reader.GetInt32(2),reader.GetInt32(3),reader.GetInt32(4),reader.GetInt32(5));}
        var paging=args.Concat(new (string,object?)[]{("size",size),("offset",(page-1)*size)}).ToArray();
        await using var cmd=Command(c,tx,"SELECT "+RowColumns+" "+where+" ORDER BY "+sort+(query.Descending?" DESC":" ASC")+" NULLS LAST,e.id,x.id LIMIT @size OFFSET @offset",paging);
        var rows=new List<PlanningRow>();
        await using(var r=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false)) while(await r.ReadAsync(ct).ConfigureAwait(false)) rows.Add(Row(r));
        await tx.CommitAsync(ct).ConfigureAwait(false);return new(rows,total,page,size,counts);
    }
    public async Task<byte[]> ExportAsync(PlanningQuery query,CancellationToken ct)
    {
        var page=await QueryPage(query with {Page=1,PageSize=50001},50001,ct).ConfigureAwait(false);
        if(page.Total>50000)throw new Application.Planning.PlanningValidationException("La exportación admite hasta 50000 filas. Ajuste los filtros antes de exportar.");
        static string Cell(object? value)
        {
            var text=value switch {DateOnly date=>date.ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture),DateTimeOffset at=>at.ToString("O",System.Globalization.CultureInfo.InvariantCulture),_=>value?.ToString()??""};
            if(text.Length>0&&"=+-@\t\r".Contains(text[0],StringComparison.Ordinal))text="'"+text;
            return "\""+text.Replace("\"","\"\"",StringComparison.Ordinal)+"\"";
        }
        var csv=new System.Text.StringBuilder("\uFEFFJOB,In Date,Due Date,Prueba original,Equivalencia,Laboratorio,Servicio,Digitación,Fecha prueba,Estado,Digitado por,Tipo ejecución,Origen,Buyer,Vendor,Episodio,Ejecución,Responsable,Observación\r\n");
        foreach(var row in page.Items)csv.AppendLine(string.Join(',',new object?[]{row.Job,row.InDate,row.DueDate,row.OriginalTest,row.Equivalence,row.Laboratory,row.Service,row.DigitizedAt,row.TestedOn,row.DigitizationState,row.DigitizedBy,row.ExecutionType,row.Origin,row.Buyer,row.Vendor,row.EpisodeId,row.ExecutionId,row.Responsible,row.Observation}.Select(Cell)));
        return System.Text.Encoding.UTF8.GetBytes(csv.ToString());
    }
    public async Task<JobDossier?> DossierAsync(Guid episodeId,CancellationToken ct)
    {
        await using var c=await database.Source.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var tx=await c.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead,ct).ConfigureAwait(false);
        string job;WipValues wip;long version;
        await using(var cmd=Command(c,tx,"SELECT job,buyer,vendor,service,in_date,due_date,amount,currency,usd,version FROM planning.episodes WHERE id=@id",("id",episodeId)))
        await using(var r=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {if(!await r.ReadAsync(ct).ConfigureAwait(false))return null;job=r.GetString(0);wip=Wip(r,1);version=r.GetInt64(9);}
        var rows=new List<PlanningRow>();
        await using(var cmd=Command(c,tx,"SELECT "+RowColumns+" FROM planning.episodes e JOIN planning.executions x ON x.episode_id=e.id WHERE e.id=@id ORDER BY x.original_test,x.id",("id",episodeId)))
        await using(var r=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false)) while(await r.ReadAsync(ct).ConfigureAwait(false))rows.Add(Row(r));
        var incidents=new List<PlanningIncident>();
        await using(var cmd=Command(c,tx,"SELECT id,kind,state,reason,version FROM planning.incidents WHERE episode_id=@id ORDER BY kind",("id",episodeId)))
        await using(var r=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))while(await r.ReadAsync(ct).ConfigureAwait(false))incidents.Add(new(r.GetGuid(0),episodeId,r.GetString(1),r.GetString(2),NullableString(r,3),r.GetInt64(4)));
        var history=new List<PlanningHistory>();
        await using(var cmd=Command(c,tx,"SELECT h.id,h.action,h.actor_id,h.at,h.reason,h.details::text,u.nombre FROM planning.history h JOIN public.usuarios u ON u.id=h.actor_id WHERE episode_id=@id ORDER BY at DESC,h.id LIMIT 1000",("id",episodeId)))
        await using(var r=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))while(await r.ReadAsync(ct).ConfigureAwait(false))history.Add(new(r.GetGuid(0),r.GetString(1),r.GetInt32(2),r.GetFieldValue<DateTimeOffset>(3),r.GetString(4),r.GetString(5),NullableString(r,6)));
        var sources=new List<PlanningSource>();
        await using(var cmd=Command(c,tx,"SELECT s.batch_id,s.file,s.sheet,s.row_number,CASE WHEN s.file='Summary' THEN b.summary_sha256 ELSE b.report_sha256 END,b.applied_at,s.fields::text FROM planning.source_rows s JOIN planning.batches b ON b.id=s.batch_id WHERE s.episode_id=@id ORDER BY b.applied_at DESC,s.file,s.row_number LIMIT 2000",("id",episodeId)))
        await using(var r=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))while(await r.ReadAsync(ct).ConfigureAwait(false))sources.Add(new(r.GetGuid(0),r.GetString(1),r.GetString(2),r.GetInt32(3),r.GetString(4),r.GetFieldValue<DateTimeOffset>(5),r.GetString(6)));
        await tx.CommitAsync(ct).ConfigureAwait(false);return new(episodeId,job,wip,version,rows,incidents,history,sources);
    }
    public async Task<IReadOnlyList<ImportReview>> ReviewsAsync(CancellationToken ct)
    {
        await using var c=await database.Source.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd=Command(c,null,"""
            SELECT i.kind,e.job,e.id,NULL::text,i.state||': '||coalesce(i.reason,''),0,NULL::uuid
            FROM planning.incidents i JOIN planning.episodes e ON e.id=i.episode_id WHERE i.state<>'Resolved'
            UNION ALL SELECT 'MissingEquivalence',e.job,e.id,x.original_test,'Asignar equivalencia o NO PROGRAMAR',0,NULL::uuid
            FROM planning.executions x JOIN planning.episodes e ON e.id=x.episode_id WHERE x.equivalence IS NULL AND NOT x.no_program
            UNION ALL SELECT 'ManualManaged',e.job,e.id,x.original_test,'Prueba manual persistida; conserva origen e historial',0,NULL::uuid
            FROM planning.executions x JOIN planning.episodes e ON e.id=x.episode_id WHERE x.origin='Manual'
            UNION ALL SELECT 'ExecutionIdentity',s.job,s.episode_id,s.original_test,'Fila sin vincular; lote '||s.batch_id::text,s.row_number,s.batch_id
            FROM planning.source_rows s WHERE s.file='Report List' AND s.execution_id IS NULL
            ORDER BY 1,2 LIMIT 2000
            """);
        var items=new List<ImportReview>();await using var r=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while(await r.ReadAsync(ct).ConfigureAwait(false))items.Add(new(r.GetString(0),r.GetString(1),r.IsDBNull(2)?null:r.GetGuid(2),NullableString(r,3),r.GetString(4),r.GetInt32(5),r.IsDBNull(6)?null:r.GetGuid(6)));
        return items;
    }
    public async Task<PlanningImportPreview?> BatchPreviewAsync(Guid id,CancellationToken ct)
    {
        await using var c=await database.Source.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd=Command(c,null,"SELECT preview::text,status FROM planning.batches WHERE id=@id",("id",id));
        await using var r=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await r.ReadAsync(ct).ConfigureAwait(false)?Read<PlanningImportPreview>(r.GetString(0)) with { Status=r.GetString(1) }:null;
    }
}

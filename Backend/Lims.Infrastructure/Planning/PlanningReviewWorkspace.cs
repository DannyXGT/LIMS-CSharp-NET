using System.Globalization;
using System.Text;
using System.Text.Json;
using Lims.Application.Planning;
using Lims.Contracts.Planning;

namespace Lims.Infrastructure.Planning;

public sealed partial class PostgresPlanningStore
{
    private static readonly string[] ReviewCategories=["SummaryWithoutReport","ReportWithoutSummary","MissingVendor","MissingEquivalence","ExecutionIdentity","ImportError","MissingParty","MissingTests","IncompleteData","Reviewed"];
    private const string IntervenedReviewSql="""
        category NOT IN ('MissingVendor','ImportError') AND state IN ('Held','Resolved','Excluded') AND
        (decision_actor IS NOT NULL OR EXISTS(SELECT 1 FROM planning.history ih WHERE ih.episode_id=enriched.episode_id AND
          ((ih.action='IncidentDecision' AND ih.details->>'incidentId'=enriched.incident_id::text)
           OR (ih.action='ManualTestAdded' AND category IN ('SummaryWithoutReport','MissingTests'))
           OR (ih.action='WipEdited' AND category IN ('MissingParty','IncompleteData'))
           OR (ih.action='EquivalenceApplied' AND category='MissingEquivalence' AND lower(ih.details->>'originalTest')=lower(enriched.original_test)))))
        """;
    // A case belongs to the persistent local history, not to the current import preview.
    // Source identity uses the original file hash + sheet + row, never JOB + test name.
    private const string ReviewCasesSql="""
        WITH source_candidates AS (
          SELECT b.report_sha256,s.sheet,s.row_number FROM planning.source_rows s JOIN planning.batches b ON b.id=s.batch_id
          WHERE s.file='Report List' AND nullif(trim(s.original_test),'') IS NOT NULL AND b.status='Applied'
          GROUP BY b.report_sha256,s.sheet,s.row_number HAVING bool_or(s.execution_id IS NULL)
        ), latest_source AS (
          SELECT DISTINCT ON(b.report_sha256,s.sheet,s.row_number) s.*
          FROM planning.source_rows s JOIN planning.batches b ON b.id=s.batch_id
          JOIN source_candidates k ON k.report_sha256=b.report_sha256 AND k.sheet=s.sheet AND k.row_number=s.row_number
          WHERE s.file='Report List' AND b.status='Applied'
          ORDER BY b.report_sha256,s.sheet,s.row_number,b.applied_at DESC,b.id DESC
        ), cases AS (
          SELECT i.id::text AS key,i.kind AS category,i.episode_id,i.id AS incident_id,NULL::uuid AS execution_id,
            e.job,i.state,i.reason,i.version,NULL::text AS original_test,NULL::text AS equivalence,NULL::text AS laboratory,
            e.source_batch_id AS batch_id,coalesce(e.source_row,0) AS source_row
          FROM planning.incidents i JOIN planning.episodes e ON e.id=i.episode_id
          WHERE i.kind IN ('SummaryWithoutReport','ReportWithoutSummary','MissingVendor')
          UNION ALL
          SELECT x.id::text,'MissingEquivalence',x.episode_id,NULL::uuid,x.id,e.job,
            CASE WHEN x.equivalence IS NULL AND NOT x.no_program THEN 'Pending' ELSE 'Resolved' END,
            CASE WHEN (x.no_program OR upper(coalesce(trim(x.laboratory),''))='NO PROGRAMAR') THEN 'NO PROGRAMAR validado' ELSE NULL END,x.version,x.original_test,x.equivalence,x.laboratory,e.source_batch_id,coalesce(e.source_row,0)
          FROM planning.executions x JOIN planning.episodes e ON e.id=x.episode_id
          UNION ALL
          SELECT s.batch_id::text||':'||s.sheet||':'||s.row_number::text,'ExecutionIdentity',s.episode_id,NULL::uuid,s.execution_id,s.job,
            CASE WHEN s.execution_id IS NULL THEN 'Pending' ELSE 'Resolved' END,
            'Verificar identidad de la fila original',0,s.original_test,NULL,NULL,s.batch_id,s.row_number FROM latest_source s
          UNION ALL
          SELECT b.id::text||':'||issue.ordinality::text,'ImportError',NULL::uuid,NULL::uuid,NULL::uuid,
            coalesce(issue.value->>'file','Archivo'),'Pending',issue.value->>'message',0,NULL,NULL,NULL,b.id,
            coalesce((issue.value->>'row')::int,0)
          FROM planning.batches b CROSS JOIN LATERAL jsonb_array_elements(coalesce(b.preview->'issues','[]'::jsonb)) WITH ORDINALITY issue(value,ordinality)
          WHERE coalesce((issue.value->>'blocking')::boolean,false)
          UNION ALL
          SELECT CASE WHEN nullif(trim(e.vendor),'') IS NOT NULL AND nullif(trim(e.buyer),'') IS NULL THEN 'buyer:'||e.id::text ELSE coalesce(i.id::text,'buyer:'||e.id::text) END,'MissingParty',e.id,
            CASE WHEN nullif(trim(e.vendor),'') IS NOT NULL AND nullif(trim(e.buyer),'') IS NULL THEN NULL::uuid ELSE i.id END,NULL::uuid,e.job,
            CASE WHEN nullif(trim(e.vendor),'') IS NOT NULL AND nullif(trim(e.buyer),'') IS NULL THEN 'Pending' ELSE coalesce(i.state,'Pending') END,
            CASE WHEN nullif(trim(e.buyer),'') IS NULL AND nullif(trim(e.vendor),'') IS NULL THEN 'Faltan Buyer y Vendor'
                 WHEN nullif(trim(e.buyer),'') IS NULL THEN 'Falta Buyer' WHEN nullif(trim(e.vendor),'') IS NULL THEN 'Falta Vendor'
                 ELSE 'Datos Buyer y Vendor disponibles' END,
            coalesce(i.version,e.version),NULL,NULL,NULL,e.source_batch_id,coalesce(e.source_row,0)
          FROM planning.episodes e LEFT JOIN planning.incidents i ON i.episode_id=e.id AND i.kind='MissingVendor'
          WHERE i.id IS NOT NULL OR nullif(trim(e.buyer),'') IS NULL
          UNION ALL
          SELECT i.id::text,'MissingTests',e.id,i.id,NULL::uuid,e.job,i.state,i.reason,i.version,NULL,NULL,NULL,e.source_batch_id,coalesce(e.source_row,0)
          FROM planning.incidents i JOIN planning.episodes e ON e.id=i.episode_id WHERE i.kind='MissingTests'
          UNION ALL
          SELECT 'dates:'||e.id::text,'IncompleteData',e.id,NULL::uuid,NULL::uuid,e.job,
            CASE WHEN e.in_date IS NULL OR e.due_date IS NULL THEN 'Pending' ELSE 'Resolved' END,
            concat_ws(' · ',CASE WHEN e.in_date IS NULL THEN 'Fecha de ingreso no informada' END,CASE WHEN e.due_date IS NULL THEN 'Due Date no informado' END),
            e.version,NULL,NULL,NULL,e.source_batch_id,coalesce(e.source_row,0)
          FROM planning.episodes e WHERE e.in_date IS NULL OR e.due_date IS NULL OR EXISTS(SELECT 1 FROM planning.review_decisions d WHERE d.case_key='dates:'||e.id::text)
          UNION ALL
          SELECT 'unnamed:'||b.report_sha256||':'||s.sheet||':'||s.row_number::text,'IncompleteData',s.episode_id,NULL::uuid,NULL::uuid,s.job,'Pending',
            'Test_Name no informado en fuente; distinto de equivalencia pendiente',0,NULL,NULL,NULL,s.batch_id,s.row_number
          FROM planning.source_rows s JOIN planning.batches b ON b.id=s.batch_id
          WHERE s.file='Report List' AND nullif(trim(s.original_test),'') IS NULL AND b.status='Applied'
            AND NOT EXISTS(SELECT 1 FROM planning.source_rows newer JOIN planning.batches nb ON nb.id=newer.batch_id
              WHERE nb.report_sha256=b.report_sha256 AND newer.sheet=s.sheet AND newer.row_number=s.row_number AND newer.file='Report List' AND nb.applied_at>b.applied_at)
        ), enriched AS (
          SELECT r.key,r.category,r.episode_id,r.incident_id,r.execution_id,r.job,
            CASE WHEN r.state='Resolved' THEN 'Resolved' ELSE coalesce(d.state,r.state) END AS state,
            coalesce(d.reason,r.reason) AS reason,coalesce(d.version,r.version) AS version,r.original_test,r.equivalence,r.laboratory,r.batch_id,r.source_row,
            e.in_date,e.due_date,coalesce(e.official_fields->>'sampleStatus',e.official_fields->>'jobStatus') AS official_state,
            e.buyer,e.vendor,e.service,e.usd,coalesce(x.origin,CASE WHEN r.category='ReportWithoutSummary' THEN 'ReportList' ELSE 'Summary' END) AS origin,
            b.created_at AS imported_at,d.actor_id AS decision_actor,d.decided_at
          FROM cases r LEFT JOIN planning.episodes e ON e.id=r.episode_id LEFT JOIN planning.executions x ON x.id=r.execution_id
          LEFT JOIN planning.batches b ON b.id=r.batch_id LEFT JOIN planning.review_decisions d ON d.case_key=r.key
        )
        """;
    private static readonly Dictionary<string,string> ReviewFields=new(StringComparer.Ordinal)
    {
        ["Job"]="job",["InDate"]="in_date",["DueDate"]="due_date",["OfficialState"]="official_state",
        ["Buyer"]="buyer",["Vendor"]="vendor",["Service"]="service",["Usd"]="usd",["State"]="state",
        ["OriginalTest"]="original_test",["Equivalence"]="equivalence",["Laboratory"]="laboratory",["Reason"]="reason",["Origin"]="origin",["ImportedAt"]="imported_at"
    };
    public Task<PlanningReviewWorkspace> ReviewWorkspaceAsync(PlanningReviewQuery query,CancellationToken ct)=>ReviewWorkspacePage(query,2000,ct);
    private async Task<PlanningReviewWorkspace> ReviewWorkspacePage(PlanningReviewQuery query,int maximum,CancellationToken ct)
    {
        if(!ReviewCategories.Contains(query.Category,StringComparer.Ordinal)||query.Situation is not ("Open" or "All" or "Pending" or "Held" or "Resolved" or "Excluded")||query.Scope is not ("History" or "Batch"))
            throw new PlanningValidationException("Seleccione una bandeja y situación válidas.");
        if(query.Scope=="Batch"&&query.BatchId is null)throw new PlanningValidationException("Seleccione el lote de revisión.");
        if(query.ImportedFrom>query.ImportedUntil)throw new PlanningValidationException("Rango de importación inválido.");
        if(query.Search?.Length>2000||query.ColumnFilters?.Length>6000)throw new PlanningValidationException("Filtros demasiado largos.");
        var sort=ReviewFields.GetValueOrDefault(query.Sort)??(query.Sort=="job"?"job":throw new PlanningValidationException("Orden de bandeja inválido."));
        var page=Math.Clamp(query.Page,1,1000000);var size=Math.Clamp(query.PageSize,1,maximum);
        var args=new List<(string,object?)>{("category",query.Category),("situation",query.Situation),("search",query.Search?.Trim()??""),("skip",(page-1)*size),("take",size)};
        var filters=new StringBuilder();
        var jobs=(query.Search??"").Split([';',',','\r','\n','\t'],StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
        if(jobs.Length>500)throw new PlanningValidationException("Máximo 500 JOBs por búsqueda.");
        args.AddRange([("jobs",jobs.Select(j=>j.ToUpperInvariant()).ToArray()),("multiple",jobs.Length>1),("scope",query.Scope),("batch",query.BatchId),
            ("importfrom",query.ImportedFrom),("importuntil",query.ImportedUntil),("test",query.Test??""),("lab",query.Laboratory??""),("origin",query.Origin??"")]);
        string[] keys=[];
        if(query.SelectedKeys is not null){try{keys=JsonSerializer.Deserialize<string[]>(query.SelectedKeys)??[];}catch(JsonException){throw new PlanningValidationException("Selección inválida.");}if(keys.Length>2000||keys.Any(k=>k.Length>512))throw new PlanningValidationException("Selección demasiado grande.");}
        args.Add(("keys",keys));args.Add(("selected",query.SelectedKeys is not null));
        const string scope="""
            (@scope='History' OR batch_id=@batch::uuid OR EXISTS(SELECT 1 FROM planning.source_rows ss WHERE ss.batch_id=@batch::uuid AND ss.episode_id=enriched.episode_id))
            AND (@importfrom::date IS NULL OR EXISTS(SELECT 1 FROM planning.source_rows ss JOIN planning.batches bb ON bb.id=ss.batch_id WHERE ss.episode_id=enriched.episode_id AND (bb.created_at AT TIME ZONE 'America/Guatemala')::date>=@importfrom::date AND (@importuntil::date IS NULL OR (bb.created_at AT TIME ZONE 'America/Guatemala')::date<=@importuntil::date)))
            AND (@importuntil::date IS NULL OR EXISTS(SELECT 1 FROM planning.source_rows ss JOIN planning.batches bb ON bb.id=ss.batch_id WHERE ss.episode_id=enriched.episode_id AND (bb.created_at AT TIME ZONE 'America/Guatemala')::date<=@importuntil::date AND (@importfrom::date IS NULL OR (bb.created_at AT TIME ZONE 'America/Guatemala')::date>=@importfrom::date)))
            AND (@search='' OR CASE WHEN @multiple THEN upper(job)=ANY(@jobs) ELSE strpos(lower(job),lower(@search))>0 OR strpos(lower(coalesce(original_test,'')),lower(@search))>0 END)
            AND (@test='' OR strpos(lower(coalesce(original_test,'')),lower(@test))>0 OR strpos(lower(coalesce(equivalence,'')),lower(@test))>0)
            AND (@lab='' OR strpos(lower(coalesce(laboratory,'')),lower(@lab))>0) AND (@origin='' OR origin=@origin)
            """;
        if(!string.IsNullOrWhiteSpace(query.ColumnFilters))
        {
            Dictionary<string,string>? values;
            try{values=JsonSerializer.Deserialize<Dictionary<string,string>>(query.ColumnFilters);}catch(JsonException){throw new PlanningValidationException("Filtros de columna inválidos.");}
            if(values is null||values.Count>20)throw new PlanningValidationException("Máximo 20 filtros de columna.");
            foreach(var (field,value) in values)
            {
                if(value is null||value.Length>512)throw new PlanningValidationException("Valor de filtro inválido.");
                var dateField=field.EndsWith("From",StringComparison.Ordinal)?field[..^4]:field.EndsWith("Until",StringComparison.Ordinal)?field[..^5]:null;
                var parameter="f"+args.Count;
                if(dateField is "InDate" or "DueDate")
                {
                    if(!DateOnly.TryParseExact(value,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var date))throw new PlanningValidationException("Fecha de filtro inválida.");
                    filters.Append(CultureInfo.InvariantCulture,$" AND {ReviewFields[dateField]} {(field.EndsWith("From",StringComparison.Ordinal)?">=":"<=")} @{parameter}::date");args.Add((parameter,date));
                }
                else
                {
                    if(!ReviewFields.TryGetValue(field,out var column))throw new PlanningValidationException("Columna de filtro inválida.");
                    var expression=field=="State"?"CASE state WHEN 'Pending' THEN 'Pendiente' WHEN 'Held' THEN 'Retenido' WHEN 'Resolved' THEN 'Resuelto' WHEN 'Excluded' THEN 'Excluido justificadamente' ELSE state END":field=="Origin"?"CASE origin WHEN 'ReportList' THEN 'Report List' ELSE origin END":column;
                    filters.Append(CultureInfo.InvariantCulture,$" AND strpos(lower(coalesce(({expression})::text,'No informado')),lower(@{parameter}))>0");args.Add((parameter,field=="Usd"?value.Replace(",","",StringComparison.Ordinal):value));
                }
            }
        }
        var where="FROM enriched WHERE "+scope+" AND (category=@category OR (@category='MissingTests' AND category='ExecutionIdentity') OR (@category='Reviewed' AND "+IntervenedReviewSql+")) AND (@situation='All' OR (@situation='Open' AND state IN ('Pending','Held')) OR state=@situation) AND (NOT @selected OR key=ANY(@keys)) ";
        await using var c=await database.Source.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var tx=await c.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead,ct).ConfigureAwait(false);
        var counters=new List<PlanningReviewCounter>();
        await using(var cmd=Command(c,tx,ReviewCasesSql+"SELECT category,count(*)::int,count(*) FILTER(WHERE state='Pending')::int,count(*) FILTER(WHERE state='Held')::int,count(*) FILTER(WHERE state='Resolved')::int,count(*) FILTER(WHERE state='Excluded')::int FROM enriched WHERE "+scope+filters+" GROUP BY category",args.ToArray()))
        await using(var r=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))while(await r.ReadAsync(ct).ConfigureAwait(false))counters.Add(new(r.GetString(0),r.GetInt32(1),r.GetInt32(2),r.GetInt32(3),r.GetInt32(4),r.GetInt32(5)));
        foreach(var category in ReviewCategories)if(counters.All(c=>c.Category!=category))counters.Add(new(category,0,0,0,0));
        var identity=counters.Single(c=>c.Category=="ExecutionIdentity");var testsCounter=counters.Single(c=>c.Category=="MissingTests");counters.Remove(testsCounter);counters.Add(testsCounter with{Total=testsCounter.Total+identity.Total,Pending=testsCounter.Pending+identity.Pending,Held=testsCounter.Held+identity.Held,Resolved=testsCounter.Resolved+identity.Resolved,Excluded=testsCounter.Excluded+identity.Excluded});
        counters.RemoveAll(c=>c.Category=="Reviewed");
        await using(var cmd=Command(c,tx,ReviewCasesSql+"SELECT count(*)::int,count(*) FILTER(WHERE state='Held')::int,count(*) FILTER(WHERE state='Resolved')::int,count(*) FILTER(WHERE state='Excluded')::int FROM enriched WHERE "+scope+filters+" AND "+IntervenedReviewSql,args.ToArray()))
        await using(var r=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false)){await r.ReadAsync(ct).ConfigureAwait(false);counters.Add(new("Reviewed",r.GetInt32(0),0,r.GetInt32(1),r.GetInt32(2),r.GetInt32(3)));}
        int total;
        await using(var cmd=Command(c,tx,ReviewCasesSql+"SELECT count(*)::int "+where+filters,args.ToArray()))total=(int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
        var items=new List<PlanningReviewCase>();
        var sql=ReviewCasesSql+"""
            SELECT p.key,p.category,p.episode_id,p.incident_id,p.execution_id,p.job,p.in_date,p.due_date,p.official_state,
              p.buyer,p.vendor,p.service,p.usd,p.state,p.reason,p.version,p.original_test,p.equivalence,p.laboratory,p.batch_id,p.source_row,
              coalesce(du.nombre,decision.actor_name),coalesce(p.decided_at,decision.at),p.origin,p.imported_at FROM (SELECT *
            """+where+filters+" ORDER BY "+sort+(query.Descending?" DESC":" ASC")+" NULLS LAST,key LIMIT @take OFFSET @skip) p "+"""
            LEFT JOIN public.usuarios du ON du.id=p.decision_actor LEFT JOIN LATERAL (
              SELECT u.nombre AS actor_name,h.at FROM planning.history h JOIN public.usuarios u ON u.id=h.actor_id
              WHERE p.incident_id IS NOT NULL AND h.episode_id=p.episode_id AND h.action='IncidentDecision'
                AND h.details->>'incidentId'=p.incident_id::text ORDER BY h.at DESC,h.id DESC LIMIT 1
            ) decision ON true
            """;
        await using(var cmd=Command(c,tx,sql,args.ToArray()))
        await using(var r=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))while(await r.ReadAsync(ct).ConfigureAwait(false))
            items.Add(new(r.GetString(0),r.GetString(1),r.IsDBNull(2)?null:r.GetGuid(2),r.IsDBNull(3)?null:r.GetGuid(3),r.IsDBNull(4)?null:r.GetGuid(4),r.GetString(5),NullableDate(r,6),NullableDate(r,7),NullableString(r,8),NullableString(r,9),NullableString(r,10),NullableString(r,11),r.IsDBNull(12)?null:r.GetDecimal(12),r.GetString(13),NullableString(r,14),r.GetInt64(15),NullableString(r,16),NullableString(r,17),NullableString(r,18),r.IsDBNull(19)?null:r.GetGuid(19),r.GetInt32(20),NullableString(r,21),r.IsDBNull(22)?null:r.GetFieldValue<DateTimeOffset>(22),NullableString(r,23),r.IsDBNull(24)?null:r.GetFieldValue<DateTimeOffset>(24)));
        await tx.CommitAsync(ct).ConfigureAwait(false);
        return new(items,total,page,size,counters,(query.Scope=="Batch"?"Lote seleccionado":"Histórico acumulado")+" · categorías independientes. Solo Summary no implica pérdida; la cobertura temporal de Report List no es verificable.");
    }
    public async Task<byte[]> ExportReviewsAsync(PlanningReviewQuery query,CancellationToken ct)
    {
        var page=await ReviewWorkspacePage(query with{Page=1,PageSize=50001},50001,ct).ConfigureAwait(false);
        if(page.Total>50000)throw new PlanningValidationException("Máximo 50,000 casos por exportación; ajuste los filtros.");
        static string Cell(string? value){var text=value??"";if(text.Length>0&&"=+-@\t\r".Contains(text[0],StringComparison.Ordinal))text="'"+text;return "\""+text.Replace("\"","\"\"",StringComparison.Ordinal)+"\"";}
        var csv=new StringBuilder("\uFEFFJOB,Fecha ingreso,Due Date,Estado,Buyer,Vendor,Service,USD,Situación,Prueba,Equivalencia,Laboratorio,Motivo,Decidido por,Fecha decisión,Bandeja,Episodio,Lote,Fila fuente,Origen,Fecha de lote\r\n");
        foreach(var row in page.Items)csv.AppendLine(string.Join(',',new[]{row.Job,row.InDate?.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),row.DueDate?.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),row.OfficialState,row.Buyer,row.Vendor,row.Service,row.Usd?.ToString(CultureInfo.InvariantCulture),row.StateLabel,row.OriginalTest,row.Equivalence,row.Laboratory,row.Reason,row.DecidedBy,row.DecidedAt?.ToString("O",CultureInfo.InvariantCulture),row.Category,row.EpisodeId?.ToString(),row.BatchId?.ToString(),row.SourceRow.ToString(CultureInfo.InvariantCulture),row.Origin,row.ImportedAt?.ToString("O",CultureInfo.InvariantCulture)}.Select(Cell)));
        return Encoding.UTF8.GetBytes(csv.ToString());
    }
}

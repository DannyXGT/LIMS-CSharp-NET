using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lims.Application.Planning;
using Lims.Contracts.Planning;
using Npgsql;

namespace Lims.Infrastructure.Planning;

public sealed partial class PostgresPlanningStore
{
    private static readonly string[] ReactEmergencyServices=["EXPRESS","NEXT DAY","SAME DAY"];
    private static async Task<bool> HasReactLinks(NpgsqlConnection c,NpgsqlTransaction tx,CancellationToken ct)
    {await using var cmd=Command(c,tx,"SELECT to_regclass('planning.react_execution_links') IS NOT NULL");return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is true;}
    private const string ReactOperationalPredicate="NOT EXISTS(SELECT 1 FROM planning.react_execution_links admission WHERE admission.execution_id=x.id AND (admission.disposition<>'Active' OR admission.react_row_id IS NULL))";
    private static async Task<bool> HasReactCatalogue(NpgsqlConnection c,NpgsqlTransaction? tx,CancellationToken ct)
    {
        await using var cmd=Command(c,tx,"SELECT to_regclass('public.planificacion_test_map') IS NOT NULL");
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is true;
    }
    private static async Task RequireReactSchema(NpgsqlConnection c,NpgsqlTransaction tx,CancellationToken ct)
    {
        await using var cmd=Command(c,tx,"""
            SELECT to_regclass('public.planificacion_activa') IS NOT NULL
              AND to_regclass('public.planificacion_archivada') IS NOT NULL
              AND to_regclass('public.planificacion_test_map') IS NOT NULL
              AND to_regclass('public.planificacion_test_map_pendiente') IS NOT NULL
              AND to_regclass('planning.react_execution_links') IS NOT NULL
              AND EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public'
                AND table_name='planificacion_activa' AND column_name='manual_pinned')
            """);
        if(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is not true)
            throw new PlanningValidationException("Falta instalar la integración aditiva con las tablas React. Revise scripts/planning/install-html-react-integration.sql; no se crean ni alteran tablas durante la importación.");
    }
    private static long ReactCatalogueVersion(string value)=>BitConverter.ToInt64(SHA256.HashData(Encoding.UTF8.GetBytes(value)),0)&long.MaxValue;
    private static async Task<string> ReadReactStateToken(NpgsqlConnection c,NpgsqlTransaction tx,CancellationToken ct)
    {
        await using var cmd=Command(c,tx,"""
            SELECT md5(concat(
              (SELECT string_agg(to_jsonb(a)::text,'' ORDER BY a.id) FROM public.planificacion_activa a),
              (SELECT string_agg(to_jsonb(a)::text,'' ORDER BY a.id) FROM public.planificacion_archivada a),
              (SELECT string_agg(to_jsonb(m)::text,'' ORDER BY m.id) FROM public.planificacion_test_map m)))
            """);
        var tables=(string)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
        return PlanningImportReader.Hash(Encoding.UTF8.GetBytes(tables+Serialize(await ReactMappings(c,tx,ct).ConfigureAwait(false))));
    }
    private static async Task<List<PlanningEquivalence>> ReactMappings(NpgsqlConnection c,NpgsqlTransaction? tx,CancellationToken ct)
    {
        var source=new List<(string Test,string? Eq,string? Lab,bool No,string Json)>();
        var blacklist=new HashSet<string>();
        await using(var check=Command(c,tx,"SELECT to_regclass('public.planificacion_no_programar') IS NOT NULL"))
        if(await check.ExecuteScalarAsync(ct).ConfigureAwait(false) is true)
        {
            await using var cmd=Command(c,tx,"SELECT tecnico_norm FROM public.planificacion_no_programar");
            await using var r=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while(await r.ReadAsync(ct).ConfigureAwait(false))blacklist.Add(PlanningReconciler.TestKey(r.GetString(0)));
        }
        bool hasNames;
        await using(var available=Command(c,tx,"SELECT to_regclass('public.planificacion_nombre') IS NOT NULL"))
            hasNames=await available.ExecuteScalarAsync(ct).ConfigureAwait(false) is true;
        var catalogueSql=hasNames?"SELECT to_jsonb(m)::text,to_jsonb(p)::text FROM public.planificacion_test_map m LEFT JOIN public.planificacion_nombre p ON p.id=nullif(to_jsonb(m)->>'planificacion_nombre_id','')::bigint ORDER BY m.id":"SELECT to_jsonb(m)::text,NULL::text FROM public.planificacion_test_map m ORDER BY id";
        await using(var cmd=Command(c,tx,catalogueSql))
        await using(var r=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        while(await r.ReadAsync(ct).ConfigureAwait(false))
        {
            var raw=r.GetString(0);using var doc=JsonDocument.Parse(raw);var j=doc.RootElement;
            var masterRaw=r.IsDBNull(1)?"{}":r.GetString(1);using var master=JsonDocument.Parse(masterRaw);
            static string? Field(JsonElement record,string name)=>record.TryGetProperty(name,out var v)&&v.ValueKind!=JsonValueKind.Null&&!string.IsNullOrWhiteSpace(v.ToString())?v.ToString().Trim():null;
            string? Get(string name)=>Field(j,name);
            var test=Get("nombre_tecnico")??Get("test_name_tecnico");if(string.IsNullOrWhiteSpace(test))continue;
            var eq=Field(master.RootElement,"nombre_planificacion")??Get("nombre_planificacion")??Get("nombre_prueba_plan");var lab=Get("lab");
            var no=Get("activo")=="False"||Get("activo")=="false"||blacklist.Contains(PlanningReconciler.TestKey(test))||PlanningImportRules.Token(lab)=="NO PROGRAMAR";
            source.Add((test,no?null:eq,no?null:lab,no,raw+masterRaw));
        }
        return source.GroupBy(s=>PlanningReconciler.TestKey(s.Test),StringComparer.Ordinal).Select(g=>
        {
            var first=g.First();var conflict=g.Select(s=>(s.Eq,s.Lab,s.No)).Distinct().Count()>1;
            return new PlanningEquivalence(first.Test,conflict?null:first.Eq,conflict?null:first.Lab,!conflict&&first.No,
                ReactCatalogueVersion(string.Join('\n',g.Select(s=>$"{s.No}|{s.Eq}|{s.Lab}|{s.Json}"))));
        }).ToList();
    }
    private static async Task<PlanningPlan> PrepareReactPlan(NpgsqlConnection c,NpgsqlTransaction tx,PlanningPlan plan,CancellationToken ct)
    {
        var archived=new HashSet<string>();
        await using(var cmd=Command(c,tx,"SELECT job_prueba FROM public.planificacion_archivada WHERE lower(coalesce(archived_reason,'')) LIKE '%manual%'"))
        await using(var r=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
            while(await r.ReadAsync(ct).ConfigureAwait(false))archived.Add(PlanningReconciler.TestKey(r.GetString(0)));
        var executions=plan.Executions.ToDictionary(x=>x.Id);var admissions=(plan.Admissions??[]).ToArray();
        var activeIds=admissions.Where(a=>a.State=="Active").Select(a=>a.ExecutionId).ToHashSet();
        var collisions=plan.Executions.Where(x=>activeIds.Contains(x.Id))
            .GroupBy(x=>PlanningReconciler.TestKey(x.Job+" - "+x.Equivalence)).Where(g=>g.Select(x=>(x.EpisodeId,x.Laboratory)).Distinct().Count()>1).SelectMany(g=>g.Select(x=>x.Id)).ToHashSet();
        // Read identities in sets: a large workbook must not make one network round trip per execution.
        var ids=admissions.Select(a=>a.ExecutionId).ToArray();
        var priorLinks=new Dictionary<Guid,(string State,bool Removed)>();
        await using(var cmd=Command(c,tx,"SELECT execution_id,disposition,react_row_id IS NULL FROM planning.react_execution_links WHERE execution_id=ANY(@ids)",("ids",ids)))
        await using(var r=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
            while(await r.ReadAsync(ct).ConfigureAwait(false))priorLinks.Add(r.GetGuid(0),(r.GetString(1),r.GetBoolean(2)));
        var candidates=new Dictionary<long,(string Key,string Test,DateOnly? Date,bool Linked,List<(Guid Execution,Guid Episode,string Lab)> Owners)>();
        var keys=plan.Executions.Select(x=>x.Job+" - "+x.Equivalence).Distinct().ToArray();
        var sourceIds=plan.Executions.Where(x=>x.ReactRowId.HasValue).Select(x=>x.ReactRowId!.Value).Distinct().ToArray();
        await using(var cmd=Command(c,tx,"""
            SELECT a.id,a.job_prueba,coalesce(a.test_name_tecnico,''),coalesce(a.fecha_ingreso,a.reg_date),
              l.execution_id,owned.episode_id,coalesce(owned.laboratory,'')
            FROM public.planificacion_activa a
            LEFT JOIN planning.react_execution_links l ON l.react_row_id=a.id
            LEFT JOIN planning.executions owned ON owned.id=l.execution_id
            WHERE a.id=ANY(@sources) OR a.job_prueba=ANY(@keys)
            """,("sources",sourceIds),("keys",keys)))
        await using(var r=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        while(await r.ReadAsync(ct).ConfigureAwait(false))
        {
            var id=r.GetInt64(0);
            if(!candidates.TryGetValue(id,out var row))
                candidates.Add(id,row=(r.GetString(1),r.GetString(2),NullableDate(r,3),!r.IsDBNull(4),[]));
            if(!r.IsDBNull(5))row.Owners.Add((r.GetGuid(4),r.GetGuid(5),r.GetString(6)));
        }
        var byKey=candidates.GroupBy(p=>p.Value.Key,StringComparer.Ordinal).ToDictionary(g=>g.Key,g=>g.Select(p=>p.Key).ToArray(),StringComparer.Ordinal);
        var episodes=plan.Episodes.ToDictionary(e=>e.Id);
        for(var i=0;i<admissions.Length;i++)
        {
            var a=admissions[i];var x=executions[a.ExecutionId];
            if(priorLinks.TryGetValue(x.Id,out var prior)&&prior.State=="Active")
            {
                if(prior.Removed)
                    admissions[i]=a=a with{State="ReactIdentity",Reason="La fila React enlazada fue retirada. Se conserva el UUID y se requiere una conciliación explícita para reactivar."};
                else if(a.State=="OutsideDate"&&x.Action!="Create")
                    admissions[i]=a=a with{State="Active",Reason="Ejecución previamente admitida; una carga de otra fecha no la retira del trabajo activo."};
            }
            if(a.State!="Active")continue;
            if(archived.Contains(PlanningReconciler.TestKey(x.Job+" - "+x.Equivalence)))
                admissions[i]=a with{State="ManualArchived",Reason=PlanningImportRules.DispositionLabel("ManualArchived")};
            else if(collisions.Contains(a.ExecutionId))
                admissions[i]=a with{State="ReactIdentity",Reason="La combinación React corresponde a distintos ingresos o laboratorios. Se conservan los UUID separados; concilie antes de proyectar."};
            else
            {
                if(!episodes.TryGetValue(x.EpisodeId,out var episode))continue;
                var matches=x.ReactRowId is{} source
                    ? candidates.ContainsKey(source)?new[]{source}:[]
                    : byKey.GetValueOrDefault(x.Job+" - "+x.Equivalence)??[];
                if(matches.Any(id=>
                {
                    var row=candidates[id];
                    return row.Owners.Any(o=>o.Execution!=x.Id&&(o.Episode!=x.EpisodeId||o.Lab!=(x.Laboratory??"")))||
                        (!row.Linked&&x.ReactRowId!=id&&(PlanningReconciler.TestKey(row.Test)!=PlanningReconciler.TestKey(x.OriginalTest)||row.Date!=episode.Source.InDate));
                }))
                    admissions[i]=a with{State="ReactIdentity",Reason="La fila React existente tiene otra fecha, prueba fuente o UUID. Se conserva para conciliación."};
            }
        }
        return plan with{Admissions=admissions};
    }
    private static async Task ApplyReactPlan(NpgsqlConnection c,NpgsqlTransaction tx,Guid? batch,PlanningPlan plan,int actor,CancellationToken ct)
    {
        var executions=plan.Executions.ToDictionary(x=>x.Id);
        var episodes=(await Episodes(c,tx,plan.Executions.Select(x=>x.Job).Distinct().ToArray(),ct).ConfigureAwait(false))
            .ToDictionary(e=>e.Id,e=>new ProposedEpisode(e.Id,e.Job,e.Wip,false,0,e.Official));
        foreach(var admission in plan.Admissions??[])
        {
            var x=executions[admission.ExecutionId];var state=admission.State;var reason=admission.Reason;long? reactId=null;
            var e=episodes.GetValueOrDefault(x.EpisodeId);
            if(e is null)
            {
                var stored=(await Episodes(c,tx,[x.Job],ct).ConfigureAwait(false)).Single(v=>v.Id==x.EpisodeId);
                e=new(stored.Id,stored.Job,stored.Wip,false,0,stored.Official);
            }
            if(state=="Active")
            {
                var key=x.Job+" - "+x.Equivalence;
                await using(var prior=Command(c,tx,"""
                    SELECT own.react_row_id FROM planning.react_execution_links own WHERE own.execution_id=@id
                    AND NOT EXISTS(SELECT 1 FROM planning.react_execution_links other JOIN planning.executions member ON member.id=other.execution_id
                      WHERE other.react_row_id=own.react_row_id AND other.execution_id<>own.execution_id
                        AND (member.episode_id<>@episode OR coalesce(member.equivalence,'')<>coalesce(@eq,'') OR coalesce(member.laboratory,'')<>coalesce(@lab,'')))
                    """,("id",x.Id),("episode",x.EpisodeId),("eq",x.Equivalence),("lab",x.Laboratory)))
                    reactId=await prior.ExecuteScalarAsync(ct).ConfigureAwait(false) as long?;
                // Existing source identities are preserved. A conflicting date/name is never overwritten.
                await using(var cmd=Command(c,tx,"""
                    SELECT a.id,coalesce(a.test_name_tecnico,''),coalesce(a.fecha_ingreso,a.reg_date),
                      EXISTS(SELECT 1 FROM planning.react_execution_links l JOIN planning.executions owned ON owned.id=l.execution_id
                        WHERE l.react_row_id=a.id AND l.execution_id<>@execution AND (owned.episode_id<>@episode OR coalesce(owned.laboratory,'')<>coalesce(@lab,''))),
                      EXISTS(SELECT 1 FROM planning.react_execution_links l WHERE l.react_row_id=a.id)
                    FROM public.planificacion_activa a
                    WHERE (@source::bigint IS NOT NULL AND a.id=@source::bigint) OR (@source::bigint IS NULL AND a.job_prueba=@key) FOR UPDATE OF a
                    """,("key",key),("episode",x.EpisodeId),("lab",x.Laboratory),("source",x.ReactRowId),("execution",x.Id)))
                await using(var r=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
                if(await r.ReadAsync(ct).ConfigureAwait(false))
                {
                    if(r.GetBoolean(3)||
                        (!r.GetBoolean(4)&&x.ReactRowId!=r.GetInt64(0)&&(PlanningReconciler.TestKey(r.GetString(1))!=PlanningReconciler.TestKey(x.OriginalTest)||NullableDate(r,2)!=e.Source.InDate)))
                    {throw new PlanningConflictException("La identidad React cambió después de la revisión. Valide nuevamente.");}
                    else reactId=r.GetInt64(0);
                }
                if(state=="Active"&&reactId is null)
                {
                    await using var cmd=Command(c,tx,"""
                        INSERT INTO public.planificacion_activa(job_prueba,jobs,report_no,prueba,nombre_prueba_plan,test_name_tecnico,
                          laboratorio,lab,fecha_ingreso,reg_date,due_date,delivery_date,cliente,buyer_name,buyer_excel2,vendor,
                          servicio,service_type,tipo_prioridad,usd,usd_p,pi_total_amt,pi_currency,sample_status,status_tips,close_date,comentarios)
                        VALUES(@key,@job,@job,@eq,@eq,@test,@lab,@lab,@indate::date,@indate::date,@due::date,@due::date,@buyer,@buyer,@buyer,@vendor,
                          @service,@service,@priority,@usd::numeric,@over250,@amount::numeric,@currency,@status,@status,@close::date,@comment)
                        RETURNING id
                        """,("key",key),("job",x.Job),("eq",x.Equivalence),("test",x.OriginalTest),("lab",x.Laboratory),
                        ("indate",e.Source.InDate),("due",e.Source.DueDate),("buyer",e.Source.Buyer),("vendor",e.Source.Vendor),("service",e.Source.Service),
                        ("priority",e.Source.Service is null?null:IsEmergency(e.Source.Service)?e.Source.Service:"NO"),("usd",e.Source.Usd),("over250",e.Source.Usd is null?null:e.Source.Usd>250?"SI":"NO"),
                        ("amount",e.Source.Amount),("currency",e.Source.Currency),("status",e.Official?.SampleStatus),("close",e.Official?.CloseDate),
                        ("comment",x.Rule is null?"":$"Regla {x.Rule}; ejecución fuente {x.ParentExecutionId}"));
                    reactId=(long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
                }
                if(reactId is not null&&x.ReactRowId is null)
                {
                    await Execute(c,tx,"UPDATE public.planificacion_activa SET job_prueba=@key,prueba=@eq,nombre_prueba_plan=@eq,test_name_tecnico=@test,laboratorio=@lab,lab=@lab WHERE id=@id",ct,
                        ("key",key),("eq",x.Equivalence),("test",x.OriginalTest),("lab",x.Laboratory),("id",reactId)).ConfigureAwait(false);
                    if(x.Rule=="ReactSide2:v1")
                    {
                        await Execute(c,tx,"UPDATE public.planificacion_activa SET manual_pinned=true,fecha_ingreso=@indate::date,reg_date=@indate::date,due_date=@due::date,delivery_date=@due::date,laboratorio=@lab,lab=@lab,updated_at=now() WHERE id=@id",ct,
                            ("id",reactId),("indate",e.Source.InDate),("due",e.Source.DueDate),("lab",x.Laboratory)).ConfigureAwait(false);
                        await Execute(c,tx,"UPDATE planning.episodes SET manual_fields=(SELECT array_agg(DISTINCT field) FROM unnest(manual_fields||ARRAY['InDate','DueDate']) field) WHERE id=@id",ct,("id",x.EpisodeId)).ConfigureAwait(false);
                    }
                    else await Execute(c,tx,"""
                        UPDATE public.planificacion_activa SET fecha_ingreso=@indate::date,reg_date=@indate::date,due_date=@due::date,delivery_date=@due::date,
                          cliente=@buyer,buyer_name=@buyer,buyer_excel2=@buyer,vendor=@vendor,servicio=@service,service_type=@service,
                          usd=@usd::numeric,usd_p=@over250,pi_total_amt=@amount::numeric,pi_currency=@currency,sample_status=@status,status_tips=@status,close_date=@close::date,updated_at=now()
                        WHERE id=@id AND NOT manual_pinned
                        """,ct,("id",reactId),("indate",e.Source.InDate),("due",e.Source.DueDate),("buyer",e.Source.Buyer),("vendor",e.Source.Vendor),("service",e.Source.Service),
                        ("usd",e.Source.Usd),("over250",e.Source.Usd is null?null:e.Source.Usd>250?"SI":"NO"),("amount",e.Source.Amount),("currency",e.Source.Currency),("status",e.Official?.SampleStatus),("close",e.Official?.CloseDate)).ConfigureAwait(false);
                }
            }
            if(state=="MissingEquivalence")
                await Execute(c,tx,"""
                    INSERT INTO public.planificacion_test_map_pendiente(pending_key,nombre_tecnico,nombre_tecnico_norm,row_payload)
                    VALUES(@key,@test,@norm,@payload::jsonb) ON CONFLICT(pending_key) DO UPDATE SET row_payload=EXCLUDED.row_payload,updated_at=now()
                    """,ct,("key","execution:"+x.Id),("test",x.OriginalTest),("norm",PlanningImportRules.Token(x.OriginalTest)),
                    ("payload",Serialize(new{jobs=x.Job,job_prueba=x.Job+" - "+x.OriginalTest,test_name_tecnico=x.OriginalTest,execution_id=x.Id,batch_id=batch,episode_id=x.EpisodeId,
                        prueba=x.OriginalTest,nombre_prueba_plan=x.OriginalTest,laboratorio=x.Laboratory,lab=x.Laboratory,
                        fecha_ingreso=e.Source.InDate,due_date=e.Source.DueDate,cliente=e.Source.Buyer,buyer_name=e.Source.Buyer,buyer_excel2=e.Source.Buyer,
                        vendor=e.Source.Vendor,servicio=e.Source.Service,service_type=e.Source.Service,tipo_prioridad=IsEmergency(e.Source.Service)?e.Source.Service:"NO",
                        usd=e.Source.Usd,usd_p=e.Source.Usd is null?null:e.Source.Usd>250?"SI":"NO",pi_total_amt=e.Source.Amount,pi_currency=e.Source.Currency,
                        reg_date=e.Source.InDate,delivery_date=e.Source.DueDate,close_date=e.Official?.CloseDate,report_no=x.Job,applicant_name=(string?)null,
                        sample_status=e.Official?.SampleStatus,status_tips=e.Official?.SampleStatus,status=(string?)null,clasi_cliente=(string?)null,comentarios=""}))).ConfigureAwait(false);
            else await Execute(c,tx,"DELETE FROM public.planificacion_test_map_pendiente WHERE pending_key=@key",ct,("key","execution:"+x.Id)).ConfigureAwait(false);
            await Execute(c,tx,"""
                INSERT INTO planning.react_execution_links(execution_id,react_row_id,disposition,reason,rule,policy_version,batch_id)
                VALUES(@id,@react::bigint,@state,@reason,@rule,@policy,@batch::uuid)
                ON CONFLICT(execution_id) DO UPDATE SET react_row_id=coalesce(EXCLUDED.react_row_id,planning.react_execution_links.react_row_id),
                  disposition=EXCLUDED.disposition,reason=EXCLUDED.reason,rule=EXCLUDED.rule,batch_id=EXCLUDED.batch_id,updated_at=now()
                """,ct,("id",x.Id),("react",reactId),("state",state),("reason",reason),("rule",admission.Rule),("policy",PlanningImportRules.Version),("batch",batch)).ConfigureAwait(false);
            await Audit(c,tx,x.EpisodeId,"ImportAdmission",actor,reason,new{ExecutionId=x.Id,ReactRowId=reactId,State=state,admission.Rule,x.ParentExecutionId,Version=PlanningImportRules.Version,BatchId=batch},ct).ConfigureAwait(false);
        }
    }
    private static async Task SaveReactEquivalence(NpgsqlConnection c,NpgsqlTransaction tx,EquivalenceDecision request,CancellationToken ct)
    {
        await Execute(c,tx,"LOCK TABLE public.planificacion_test_map IN SHARE ROW EXCLUSIVE MODE",ct).ConfigureAwait(false);
        var key=PlanningReconciler.TestKey(request.OriginalTest);
        var current=(await ReactMappings(c,tx,ct).ConfigureAwait(false)).SingleOrDefault(m=>PlanningReconciler.TestKey(m.OriginalTest)==key);
        if(current?.Version!=request.Version)throw new PlanningConflictException("El catálogo de React cambió. Recargue las equivalencias.");
        long? id=null;
        await using(var cmd=Command(c,tx,"SELECT id FROM public.planificacion_test_map WHERE upper(regexp_replace(trim(nombre_tecnico),'\\s+',' ','g'))=@key FOR UPDATE",("key",key)))
        await using(var r=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        while(await r.ReadAsync(ct).ConfigureAwait(false))
        {if(id is not null)throw new PlanningValidationException("Hay varias entradas React para el mismo nombre técnico. Concilie el catálogo antes de modificarlo.");id=r.GetInt64(0);}
        var args=new (string,object?)[]{("id",id),("test",request.OriginalTest.Trim()),("eq",request.NoProgram?request.OriginalTest.Trim():request.Equivalence?.Trim()),("lab",request.NoProgram?"NO PROGRAMAR":request.Laboratory?.Trim()),("active",!request.NoProgram)};
        await Execute(c,tx,id is null?"""
            INSERT INTO public.planificacion_test_map(nombre_tecnico,test_name_tecnico,nombre_planificacion,nombre_prueba_plan,lab,activo)
            VALUES(@test,@test,@eq,@eq,@lab,@active)
            """:"""
            UPDATE public.planificacion_test_map SET nombre_planificacion=@eq,nombre_prueba_plan=@eq,lab=@lab,activo=@active,updated_at=now(),planificacion_nombre_id=NULL
            WHERE id=@id::bigint
            """,ct,args.Where(a=>id is not null||a.Item1!="id").ToArray()).ConfigureAwait(false);
        await using var exists=Command(c,tx,"SELECT to_regclass('public.planificacion_no_programar') IS NOT NULL");
        if(await exists.ExecuteScalarAsync(ct).ConfigureAwait(false) is true)
        {
            await Execute(c,tx,"DELETE FROM public.planificacion_no_programar WHERE upper(regexp_replace(trim(tecnico_norm),'\\s+',' ','g'))=@key",ct,("key",key)).ConfigureAwait(false);
            if(request.NoProgram)await Execute(c,tx,"INSERT INTO public.planificacion_no_programar(tecnico_norm) VALUES(@key) ON CONFLICT DO NOTHING",ct,("key",key.ToLowerInvariant())).ConfigureAwait(false);
        }
    }
    private static async Task ReclassifyReactPending(NpgsqlConnection c,NpgsqlTransaction tx,string test,int actor,Guid? batchId,CancellationToken ct)
    {
        await using(var available=Command(c,tx,"SELECT to_regclass('planning.react_execution_links') IS NOT NULL"))
            if(await available.ExecuteScalarAsync(ct).ConfigureAwait(false) is not true)return;
        await Execute(c,tx,"LOCK TABLE public.planificacion_activa,public.planificacion_archivada IN SHARE ROW EXCLUSIVE MODE",ct).ConfigureAwait(false);
        var batches=new List<(Guid Id,PlanningImportInput Input)>();
        await using(var cmd=Command(c,tx,batchId is not null?"""
            SELECT b.id,b.input::text FROM planning.batches b WHERE id=@batch::uuid AND status='Applied' AND input->'cut'->>'policy' IN ('HtmlReact','HtmlReactSide2')
            """:"""
            SELECT DISTINCT b.id,b.input::text FROM planning.react_execution_links l
            JOIN planning.executions x ON x.id=l.execution_id JOIN planning.batches b ON b.id=l.batch_id
            WHERE x.test_key=@key AND b.status='Applied'
            """,("key",PlanningReconciler.TestKey(test)),("batch",batchId)))
        await using(var r=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
            while(await r.ReadAsync(ct).ConfigureAwait(false))batches.Add((r.GetGuid(0),Read<PlanningImportInput>(r.GetString(1))));
        foreach(var batch in batches)
        {
            var jobs=batch.Input.Rows.Select(r=>r.Job).Distinct().ToArray();var episodes=await Episodes(c,tx,jobs,ct).ConfigureAwait(false);
            var existing=await Executions(c,tx,episodes.Select(e=>e.Id).ToArray(),ct).ConfigureAwait(false);
            var links=new List<StoredSourceIdentity>();
            await using(var cmd=Command(c,tx,"SELECT b.report_sha256,s.row_number,s.sheet,s.execution_id FROM planning.source_rows s JOIN planning.batches b ON b.id=s.batch_id WHERE s.batch_id=@id AND s.file IN ('Report List','Side2','React Active') AND s.execution_id IS NOT NULL",("id",batch.Id)))
            await using(var r=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
                while(await r.ReadAsync(ct).ConfigureAwait(false))links.Add(new(r.GetString(0),r.GetInt32(1),r.GetString(2),r.GetGuid(3)));
            // The archived input deliberately omits binary files; restore the original report hash for row identity.
            byte[] report;
            await using(var cmd=Command(c,tx,"SELECT report_data FROM planning.batches WHERE id=@id",("id",batch.Id)))report=(byte[])(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
            var input=batch.Input with{Report=report,Rows=batch.Input.Rows.Select(row=>
            {
                var current=episodes.SingleOrDefault(e=>e.Job==row.Job&&e.SourceInDate==row.Wip?.InDate);
                return row.File=="Summary"&&current is not null?row with{Wip=current.Wip,Official=current.Official}:row;
            }).ToArray()};
            var plan=PlanningReconciler.Plan(input,episodes,existing,await ReactMappings(c,tx,ct).ConfigureAwait(false),links);
            plan=await PrepareReactPlan(c,tx,plan,ct).ConfigureAwait(false);
            await CopyExecutions(c,plan.Executions,ct).ConfigureAwait(false);
            foreach(var created in plan.Executions.Where(x=>x.Action=="Create"))
                await Audit(c,tx,created.EpisodeId,"TestImported",actor,"Ejecución incorporada al conciliar la clasificación",new{ExecutionId=created.Id,created.Rule,created.ParentExecutionId,BatchId=batch.Id},ct).ConfigureAwait(false);
            await ApplyReactPlan(c,tx,batch.Id,plan,actor,ct).ConfigureAwait(false);
        }
    }
    private static bool IsEmergency(string? service)=>ReactEmergencyServices.Any(s=>PlanningImportRules.Token(service).Contains(s,StringComparison.Ordinal));
    private static async Task ProjectManualReactExecution(NpgsqlConnection c,NpgsqlTransaction tx,StoredEpisode episode,Guid execution,string test,string equivalent,string lab,int actor,CancellationToken ct)
    {
        if(!await HasReactLinks(c,tx,ct).ConfigureAwait(false))return;
        await RequireReactSchema(c,tx,ct).ConfigureAwait(false);
        await Execute(c,tx,"LOCK TABLE public.planificacion_activa,public.planificacion_archivada IN SHARE ROW EXCLUSIVE MODE",ct).ConfigureAwait(false);
        Guid? batch;
        await using(var cmd=Command(c,tx,"SELECT source_batch_id FROM planning.episodes WHERE id=@id",("id",episode.Id)))batch=await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) as Guid?;
        var source=new ProposedEpisode(episode.Id,episode.Job,episode.Wip,false,0,episode.Official);
        var proposed=new ProposedExecution(execution,episode.Id,episode.Job,test,equivalent,lab,false,"Observe",[],"ManualAssigned:v1",Origin:"Manual");
        var plan=new PlanningPlan([source],[proposed],[],[],[]);
        var input=new PlanningImportInput(new("Noon",episode.Wip.InDate??DateOnly.FromDateTime(DateTime.UtcNow),"HtmlReactSide2"),[],[],"","","",[],[],[]);
        plan=PlanningImportRules.Apply(input,plan,[episode],[]);plan=await PrepareReactPlan(c,tx,plan,ct).ConfigureAwait(false);
        await ApplyReactPlan(c,tx,batch,plan,actor,ct).ConfigureAwait(false);
    }
    private static async Task SyncReactWip(NpgsqlConnection c,NpgsqlTransaction tx,Guid episode,WipValues w,IReadOnlyList<string> changed,CancellationToken ct)
    {
        if(!await HasReactLinks(c,tx,ct).ConfigureAwait(false))return;
        foreach(var field in changed)
        {
            var (sql,value)=field switch
            {
                "Buyer"=>("cliente=@value,buyer_excel2=@value",(object?)w.Buyer),
                "Vendor"=>("vendor=@value",w.Vendor),"Service"=>("servicio=@value",w.Service),
                "InDate"=>("fecha_ingreso=@value::date,reg_date=@value::date",w.InDate),
                "DueDate"=>("due_date=@value::date,delivery_date=@value::date",w.DueDate),
                "Amount"=>("pi_total_amt=@value::numeric",w.Amount),"Currency"=>("pi_currency=@value",w.Currency),
                "Usd"=>("usd=@value::numeric,usd_p=CASE WHEN @value::numeric IS NULL THEN NULL WHEN @value::numeric>250 THEN 'SI' ELSE 'NO' END",w.Usd),
                _=>throw new PlanningValidationException("Campo WIP no admitido.")
            };
            await Execute(c,tx,"UPDATE public.planificacion_activa a SET "+sql+",updated_at=now() WHERE EXISTS(SELECT 1 FROM planning.react_execution_links l JOIN planning.executions x ON x.id=l.execution_id WHERE l.react_row_id=a.id AND x.episode_id=@episode)",ct,("value",value),("episode",episode)).ConfigureAwait(false);
        }
    }
}

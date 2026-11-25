using Lims.Application.Planning;
using Lims.Contracts.Planning;

namespace Lims.Infrastructure.Planning;

public sealed partial class PostgresPlanningStore
{
    private static readonly string[] EmergencyServices=["express","next day","same day"];
    public async Task<PlanningImportInspection?> InspectImportAsync(Guid id,CancellationToken ct)
    {
        await using var c=await database.Source.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var tx=await c.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead,ct).ConfigureAwait(false);
        PlanningImportInput input;PlanningPlan plan;PlanningImportPreview preview;string? actor;DateTimeOffset? applied;
        await using(var cmd=Command(c,tx,"SELECT b.input::text,b.plan::text,b.preview::text,b.status,u.nombre,b.applied_at FROM planning.batches b LEFT JOIN planning.actors u ON u.id=b.actor_id WHERE b.id=@id",("id",id)))
        await using(var r=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            if(!await r.ReadAsync(ct).ConfigureAwait(false))return null;
            input=Read<PlanningImportInput>(r.GetString(0));plan=Read<PlanningPlan>(r.GetString(1));preview=Read<PlanningImportPreview>(r.GetString(2)) with{Status=r.GetString(3)};
            actor=NullableString(r,4);applied=r.IsDBNull(5)?null:r.GetFieldValue<DateTimeOffset>(5);
        }
        var maps=(await Mappings(c,tx,ct).ConfigureAwait(false)).ToDictionary(m=>PlanningReconciler.TestKey(m.OriginalTest),StringComparer.Ordinal);
        var items=new List<PlanningImportInspectionRow>();var episodes=plan.Episodes.ToDictionary(e=>e.Id);
        var summaries=input.Rows.Where(r=>r.File=="Summary"||(r.File is "Side2" or "React Active")&&r.Wip is not null).Select(r=>r.Job).ToHashSet(StringComparer.Ordinal);
        var reports=input.Rows.Where(r=>PlanningReconciler.IsTestSource(r.File)).Select(r=>r.Job).ToHashSet(StringComparer.Ordinal);
        var reportOnly=reports.Except(summaries,StringComparer.Ordinal).ToArray();
        foreach(var stored in await Episodes(c,tx,reportOnly,ct).ConfigureAwait(false))
            episodes.TryAdd(stored.Id,new(stored.Id,stored.Job,stored.Wip,false,0,stored.Official,false));
        static bool Emergency(string? service)=>service is not null&&EmergencyServices.Any(s=>service.Contains(s,StringComparison.OrdinalIgnoreCase));
        PlanningImportInspectionRow Row(string category,ProposedEpisode e,string? original,string? eq,string? lab,string explanation,int source=0,int jobs=1,bool no=false)=>
            new(category,e.Job,e.Id,original,eq,lab,e.Source.Service,e.Source.InDate,e.Source.DueDate,e.Source.Buyer,e.Source.Vendor,e.Source.Usd,e.Official?.SampleStatus??e.Official?.JobStatus,explanation,source,jobs,no);
        foreach(var e in episodes.Values)
        {
            if(e.HasSummary&&!reports.Contains(e.Job))items.Add(Row("SummaryWithoutReport",e,null,null,null,"Ausente del Report List seleccionado. Su cobertura temporal no puede verificarse: no contiene fechas."));
            if(!summaries.Contains(e.Job))items.Add(Row("ReportWithoutSummary",e,null,null,null,"No aparece en este Summary. Cobertura no verificable sin fechas de Report List; conservar y revisar, sin asumir pérdida."));
            var missingBuyer=string.IsNullOrWhiteSpace(e.Source.Buyer);var missingVendor=string.IsNullOrWhiteSpace(e.Source.Vendor);
            if(missingBuyer||missingVendor)items.Add(Row("MissingParty",e,null,null,null,missingBuyer&&missingVendor?"Faltan Buyer y Vendor":missingBuyer?"Falta Buyer":"Falta Vendor"));
            else if(reports.Contains(e.Job))items.Add(Row("PartyComplemented",e,null,null,null,input.Cut.Policy switch
            {
                "ReactTable"=>"Datos conservados de los registros existentes de React y del WIP conocido; cada fuente queda en procedencia.",
                "HtmlReactSide2"=>"Datos comerciales conservados del WIP conocido; la carga complementaria aporta las fechas y pruebas declaradas.",
                _=>"Datos consolidados desde Summary; Report List no aporta Vendor. Se conserva procedencia de ambas fuentes."
            }));
            if(e.IsNew&&Emergency(e.Source.Service)&&!plan.Executions.Any(x=>x.EpisodeId==e.Id&&x.Action=="Create"))items.Add(Row("Emergency",e,null,null,null,"JOB nuevo con servicio de emergencia; fuente Summary, sin inventar pruebas."));
        }
        foreach(var x in plan.Executions.Where(x=>x.Action=="Create"))
            if(episodes.TryGetValue(x.EpisodeId,out var e)&&Emergency(e.Source.Service))items.Add(Row("Emergency",e,x.OriginalTest,x.Equivalence,x.Laboratory,"Ejecución nueva de servicio de emergencia",x.SourceRows.Count>0?x.SourceRows[0]:0));
        foreach(var admission in plan.Admissions??[])
        {
            var x=plan.Executions.Single(x=>x.Id==admission.ExecutionId);
            if(!episodes.TryGetValue(x.EpisodeId,out var e))continue;
            items.Add(Row("Admission",e,x.OriginalTest,x.Equivalence,x.Laboratory,admission.Reason,x.SourceRows.Count>0?x.SourceRows[0]:0,no:x.NoProgram));
            if(admission.Rule is not null)items.Add(Row("BusinessRule",e,x.OriginalTest,x.Equivalence,x.Laboratory,admission.Rule+" · "+admission.Reason));
        }
        foreach(var review in plan.Reviews.Where(r=>r.Category=="SourceConflict"))
            if(review.EpisodeId is{} episode&&episodes.TryGetValue(episode,out var e))items.Add(Row("SourceConflict",e,review.OriginalTest,null,null,review.Description,review.SourceRow));
        foreach(var group in input.Rows.Where(r=>r.File=="Report List").GroupBy(r=>string.IsNullOrWhiteSpace(r.Test)?"":PlanningReconciler.TestKey(r.Test),StringComparer.Ordinal))
        {
            var first=group.First();maps.TryGetValue(group.Key,out var map);
            if(map is not null)continue;
            if(plan.Admissions is not null&&!plan.Admissions.Any(a=>a.State=="MissingEquivalence"&&PlanningReconciler.TestKey(a.OriginalTest)==group.Key))continue;
            items.Add(new("MissingEquivalence",string.Join(", ",group.Select(r=>r.Job).Distinct(StringComparer.Ordinal)),null,first.Test,null,null,null,null,null,null,null,null,null,
                group.Key.Length==0?"Falta el nombre en el archivo fuente. Completar la fuente; no asignar una equivalencia inventada.":"Nombre original conservado; buscar una clasificación existente o decidir NO PROGRAMAR.",first.Row,group.Select(r=>r.Job).Distinct(StringComparer.Ordinal).Count()));
        }
        int jobsNow,testsNow;
        await using(var cmd=Command(c,tx,"SELECT (SELECT count(*)::int FROM planning.episodes),(SELECT count(*)::int FROM planning.executions)"))
        await using(var r=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false)){await r.ReadAsync(ct).ConfigureAwait(false);jobsNow=r.GetInt32(0);testsNow=r.GetInt32(1);}
        // Original preview counts are immutable. For a historical batch, reconstruct before/after from actual import audit chronology.
        int beforeJobs,beforeTests;
        await using(var cmd=Command(c,tx,"""
            SELECT count(*) FILTER(WHERE action='WipImported')::int,count(*) FILTER(WHERE action IN ('TestImported','ManualTestAdded'))::int
            FROM planning.history WHERE at<@at AND action IN ('WipImported','TestImported','ManualTestAdded')
            """,("at",applied??preview.CreatedAt)))
        await using(var r=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false)){await r.ReadAsync(ct).ConfigureAwait(false);beforeJobs=r.GetInt32(0);beforeTests=r.GetInt32(1);}
        if(applied is null){beforeJobs=jobsNow;beforeTests=testsNow;}
        var laboratories=plan.Executions.GroupBy(x=>x.Laboratory??"Sin laboratorio informado",StringComparer.Ordinal)
            .Select(g=>new PlanningLaboratoryCount(g.Key,g.Count(x=>x.Action=="Create"),g.Count(x=>x.Action!="Create"),g.Count(x=>x.NoProgram||string.Equals(x.Laboratory?.Trim(),"NO PROGRAMAR",StringComparison.OrdinalIgnoreCase)))).OrderBy(g=>g.Laboratory,StringComparer.Ordinal).ToArray();
        await tx.CommitAsync(ct).ConfigureAwait(false);
        return new(preview,items,plan.Executions.Where(x=>x.Action=="Create").Select(x=>(x.Job,x.OriginalTest)).Distinct().Count(),plan.Executions.Count(x=>x.Action!="Create"),items.Where(x=>x.Category=="Emergency").Select(x=>x.EpisodeId).Distinct().Count(),input.Rows.Count,
            items.Count(x=>x.Category=="MissingEquivalence"),beforeJobs,beforeTests,beforeJobs+preview.NewJobs,beforeTests+preview.NewTests,actor,applied,laboratories);
    }
    public async Task DecideReviewAsync(PlanningReviewDecision request,int actor,CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);var reason=Reason(request.Reason);
        if(request.State is not ("Pending" or "Held" or "Resolved" or "Excluded"))throw new PlanningValidationException("Situación inválida.");
        var page=await ReviewWorkspacePage(new(Category:request.Category,Situation:"All"),1,ct,request.Key).ConfigureAwait(false);
        var row=page.Items.SingleOrDefault()??throw new PlanningValidationException("El caso no existe en esta categoría.");
        if(row.IncidentId is{} incident){await DecideIncidentAsync(incident,new(request.Version,request.State,reason),actor,ct).ConfigureAwait(false);return;}
        if(request.State=="Resolved")throw new PlanningValidationException("Complete o concilie los datos de origen para resolver este caso; también puede retenerlo o excluirlo con justificación.");
        await using var c=await database.Source.OpenConnectionAsync(ct).ConfigureAwait(false);await using var tx=await c.BeginTransactionAsync(ct).ConfigureAwait(false);
        await Revision(c,tx,true,ct).ConfigureAwait(false);
        var fresh=await ReviewWorkspacePage(new(Category:request.Category,Situation:"All"),1,ct,request.Key).ConfigureAwait(false);
        row=fresh.Items.SingleOrDefault()??throw new PlanningConflictException("El caso ya no está disponible.");
        if(row.Version!=request.Version)throw new PlanningConflictException("Los datos o la revisión cambiaron. Actualice la bandeja.");
        if(row.State=="Resolved")throw new PlanningValidationException("El dato ya está completo. Su estado se deriva de la información persistida.");
        long? current=null;
        await using(var cmd=Command(c,tx,"SELECT version FROM planning.review_decisions WHERE case_key=@key FOR UPDATE",("key",row.Key)))
            if(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is long version)current=version;
        if((current??row.Version)!=request.Version)throw new PlanningConflictException("La revisión cambió. Actualice la bandeja.");
        await Execute(c,tx,"""
            INSERT INTO planning.review_decisions(case_key,category,episode_id,state,reason,actor_id,version) VALUES(@key,@category,@episode::uuid,@state,@reason,@actor,@version)
            ON CONFLICT(case_key) DO UPDATE SET state=EXCLUDED.state,reason=EXCLUDED.reason,actor_id=EXCLUDED.actor_id,decided_at=now(),version=planning.review_decisions.version+1
            """,ct,("key",row.Key),("category",row.Category),("episode",row.EpisodeId),("state",request.State),("reason",reason),("actor",actor),("version",request.Version+1)).ConfigureAwait(false);
        await Audit(c,tx,row.EpisodeId,"ReviewDecision",actor,reason,new{request.Key,row.Category,Before=row.State,request.State},ct).ConfigureAwait(false);
        await Bump(c,tx,ct).ConfigureAwait(false);await tx.CommitAsync(ct).ConfigureAwait(false);
    }
}

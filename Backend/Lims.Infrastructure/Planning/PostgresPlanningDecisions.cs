using Lims.Application.Planning;
using Lims.Contracts.Planning;
using Npgsql;

namespace Lims.Infrastructure.Planning;

public sealed partial class PostgresPlanningStore
{
    private static async Task CheckEpisode(NpgsqlConnection c,NpgsqlTransaction tx,Guid id,long version,CancellationToken ct)
    {
        await using var cmd=Command(c,tx,"SELECT version FROM planning.episodes WHERE id=@id FOR UPDATE",("id",id));
        var current=await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if(current is null)throw new PlanningValidationException("No existe el episodio.");
        if((long)current!=version)throw new PlanningConflictException("El expediente cambió. Recárguelo antes de guardar.");
    }
    public async Task EditWipAsync(Guid id,WipEdit request,int actor,CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);var reason=Reason(request.Reason);var w=request.Values;
        ArgumentNullException.ThrowIfNull(w);
        foreach(var value in new[]{w.Buyer,w.Vendor,w.Service}) if(value?.Length>512 || value?.Any(char.IsControl)==true)throw new PlanningValidationException("Texto WIP inválido o demasiado largo.");
        if(w.Amount<0 || w.Usd<0 || w.Amount>99999999999999.9999m || w.Usd>99999999999999.9999m || w.Currency?.Length>20)throw new PlanningValidationException("Valor económico inválido.");
        await using var c=await database.Source.OpenConnectionAsync(ct).ConfigureAwait(false);await using var tx=await c.BeginTransactionAsync(ct).ConfigureAwait(false);
        await Revision(c,tx,true,ct).ConfigureAwait(false);await CheckEpisode(c,tx,id,request.Version,ct).ConfigureAwait(false);
        var old=(await Episodes(c,tx,await JobNames(c,tx,id,ct).ConfigureAwait(false),ct).ConfigureAwait(false)).Single(e=>e.Id==id);
        if(w.Usd!=old.Wip.Usd && w.Usd is not null && !(w.Currency=="USD" && w.Amount==w.Usd) && string.IsNullOrWhiteSpace(request.UsdEvidence))
            throw new PlanningValidationException("La corrección de USD requiere una fuente verificada escrita; no se calcula una tasa implícita.");
        var changed=new List<string>();
        if(w.Buyer!=old.Wip.Buyer)changed.Add("Buyer");if(w.Vendor!=old.Wip.Vendor)changed.Add("Vendor");if(w.Service!=old.Wip.Service)changed.Add("Service");
        if(w.InDate!=old.Wip.InDate)changed.Add("InDate");if(w.DueDate!=old.Wip.DueDate)changed.Add("DueDate");if(w.Amount!=old.Wip.Amount)changed.Add("Amount");if(w.Currency!=old.Wip.Currency)changed.Add("Currency");if(w.Usd!=old.Wip.Usd)changed.Add("Usd");
        await Execute(c,tx,"UPDATE planning.episodes SET buyer=@buyer,vendor=@vendor,service=@service,in_date=@indate::date,due_date=@due::date,amount=@amount::numeric,currency=@currency,usd=@usd::numeric,manual_fields=@fields,version=version+1 WHERE id=@id",ct,
            ("buyer",w.Buyer),("vendor",w.Vendor),("service",w.Service),("indate",w.InDate),("due",w.DueDate),("amount",w.Amount),("currency",w.Currency),("usd",w.Usd),("fields",old.ManualFields.Concat(changed).Distinct(StringComparer.Ordinal).ToArray()),("id",id)).ConfigureAwait(false);
        await Audit(c,tx,id,"WipEdited",actor,reason,new { Before=old.Wip,After=w,Fields=changed,request.UsdEvidence },ct).ConfigureAwait(false);
        await RefreshIncidents(c,tx,id,actor,ct).ConfigureAwait(false);await Bump(c,tx,ct).ConfigureAwait(false);await tx.CommitAsync(ct).ConfigureAwait(false);
    }
    private static async Task<string[]> JobNames(NpgsqlConnection c,NpgsqlTransaction tx,Guid id,CancellationToken ct)
    {await using var cmd=Command(c,tx,"SELECT job FROM planning.episodes WHERE id=@id",("id",id));return [(string)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!];}
    public async Task AddManualTestAsync(Guid id,ManualTestRequest request,int actor,CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);var reason=Reason(request.Reason);
        var test=Required(request.OriginalTest,2048,"Prueba original");var equivalent=Required(request.Equivalence,512,"Equivalencia");var lab=Required(request.Laboratory,200,"Laboratorio");
        if(lab.Equals("NO PROGRAMAR",StringComparison.OrdinalIgnoreCase))throw new PlanningValidationException("NO PROGRAMAR es una decisión del catálogo, no un laboratorio para asignación manual de pruebas.");
        await using var c=await database.Source.OpenConnectionAsync(ct).ConfigureAwait(false);await using var tx=await c.BeginTransactionAsync(ct).ConfigureAwait(false);
        await Revision(c,tx,true,ct).ConfigureAwait(false);await CheckEpisode(c,tx,id,request.Version,ct).ConfigureAwait(false);
        if(request.CatalogueVersion is{} catalogueVersion)
        {
            var map=(await Mappings(c,tx,ct).ConfigureAwait(false)).SingleOrDefault(m=>PlanningReconciler.TestKey(m.OriginalTest)==PlanningReconciler.TestKey(test));
            if(map is null||map.Version!=catalogueVersion||map.NoProgram||map.Equivalence!=equivalent||map.Laboratory!=lab)
                throw new PlanningConflictException("La clasificación del catálogo cambió o no es válida. Recargue el catálogo antes de asignar la prueba.");
        }
        var execution=Guid.NewGuid();await InsertExecution(c,tx,execution,id,test,equivalent,lab,false,"Manual",ct).ConfigureAwait(false);
        await ReopenReportAlerts(c,tx,ct).ConfigureAwait(false);
        await Execute(c,tx,"UPDATE planning.episodes SET version=version+1 WHERE id=@id",ct,("id",id)).ConfigureAwait(false);
        await Audit(c,tx,id,"ManualTestAdded",actor,reason,new{ExecutionId=execution,OriginalTest=test,Equivalence=equivalent,Laboratory=lab,request.CatalogueVersion},ct).ConfigureAwait(false);
        await RefreshIncidents(c,tx,id,actor,ct).ConfigureAwait(false);await Bump(c,tx,ct).ConfigureAwait(false);await tx.CommitAsync(ct).ConfigureAwait(false);
    }
    public async Task DecideIncidentAsync(Guid id,IncidentDecision request,int actor,CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);var reason=Reason(request.Reason);
        if(request.State is not ("Held" or "Pending" or "Resolved" or "Excluded"))throw new PlanningValidationException("Seleccione pendiente, retener, excluir justificadamente o resolver manualmente.");
        await using var c=await database.Source.OpenConnectionAsync(ct).ConfigureAwait(false);await using var tx=await c.BeginTransactionAsync(ct).ConfigureAwait(false);
        await Revision(c,tx,true,ct).ConfigureAwait(false);Guid episode;
        if(request.State=="Resolved")
        {
            await using var valid=Command(c,tx,"""
                SELECT EXISTS(SELECT 1 FROM planning.incidents i WHERE i.id=@id AND i.kind='SummaryWithoutReport'
                AND EXISTS(SELECT 1 FROM planning.executions x WHERE x.episode_id=i.episode_id AND x.origin='Manual'
                  AND nullif(trim(x.equivalence),'') IS NOT NULL AND nullif(trim(x.laboratory),'') IS NOT NULL))
                """,("id",id));
            if(await valid.ExecuteScalarAsync(ct).ConfigureAwait(false) is not true)throw new PlanningValidationException("Resolver manualmente requiere una incidencia Solo Summary y pruebas manuales con equivalencia y laboratorio verificados.");
        }
        string? before;
        await using(var read=Command(c,tx,"SELECT state FROM planning.incidents WHERE id=@id",("id",id)))before=await read.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
        await using(var cmd=Command(c,tx,"UPDATE planning.incidents SET state=@state,reason=@reason,manual_resolved=(@state IN ('Resolved','Excluded')),version=version+1 WHERE id=@id AND version=@version AND (state NOT IN ('Resolved','Excluded') OR kind='SummaryWithoutReport' OR @state='Pending') RETURNING episode_id",("state",request.State),("reason",reason),("id",id),("version",request.Version)))
            episode=(Guid)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false)??throw new PlanningConflictException("La incidencia cambió o ya está resuelta. Recargue el expediente."));
        await Audit(c,tx,episode,"IncidentDecision",actor,reason,new { IncidentId=id,Before=before,request.State },ct).ConfigureAwait(false);
        await Bump(c,tx,ct).ConfigureAwait(false);await tx.CommitAsync(ct).ConfigureAwait(false);
    }
    public async Task SaveEquivalenceAsync(EquivalenceDecision request,int actor,CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);var reason=Reason(request.Reason);var test=Required(request.OriginalTest,2048,"Prueba original");
        var equivalent=request.NoProgram?null:Required(request.Equivalence,512,"Equivalencia");var lab=request.NoProgram?null:Required(request.Laboratory,200,"Laboratorio");var key=PlanningReconciler.TestKey(test);
        await using var c=await database.Source.OpenConnectionAsync(ct).ConfigureAwait(false);await using var tx=await c.BeginTransactionAsync(ct).ConfigureAwait(false);
        await Revision(c,tx,true,ct).ConfigureAwait(false);
        await using(var cmd=Command(c,tx,"SELECT version FROM planning.equivalences WHERE test_key=@key",("key",key)))
        {var current=await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);if((current is null && request.Version is not null)||(current is long version && request.Version!=version))throw new PlanningConflictException("La equivalencia cambió. Recargue el catálogo.");}
        await Execute(c,tx,"INSERT INTO planning.equivalences(test_key,original_test,equivalence,laboratory,no_program) VALUES(@key,@test,@equivalence,@lab,@no) ON CONFLICT(test_key) DO UPDATE SET equivalence=EXCLUDED.equivalence,laboratory=EXCLUDED.laboratory,no_program=EXCLUDED.no_program,version=planning.equivalences.version+1",ct,
            ("key",key),("test",test),("equivalence",equivalent),("lab",lab),("no",request.NoProgram)).ConfigureAwait(false);
        var episodes=new List<Guid>();
        await using(var cmd=Command(c,tx,"UPDATE planning.executions SET equivalence=@eq,laboratory=@lab,no_program=@no,version=version+1 WHERE test_key=@key AND origin='ReportList' RETURNING episode_id",("eq",equivalent),("lab",lab),("no",request.NoProgram),("key",key)))
        await using(var r=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))while(await r.ReadAsync(ct).ConfigureAwait(false))episodes.Add(r.GetGuid(0));
        foreach(var episode in episodes.Distinct())await Audit(c,tx,episode,"EquivalenceApplied",actor,reason,request,ct).ConfigureAwait(false);
        await Audit(c,tx,null,"EquivalenceSaved",actor,reason,request,ct).ConfigureAwait(false);await Bump(c,tx,ct).ConfigureAwait(false);await tx.CommitAsync(ct).ConfigureAwait(false);
    }
    public async Task ResolveSourceAsync(SourceResolution request,int actor,CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);var reason=Reason(request.Reason);
        if(request.CreateExecution==request.ExecutionId.HasValue)throw new PlanningValidationException("Seleccione una ejecución existente o cree una nueva explícitamente.");
        await using var c=await database.Source.OpenConnectionAsync(ct).ConfigureAwait(false);await using var tx=await c.BeginTransactionAsync(ct).ConfigureAwait(false);
        await Revision(c,tx,true,ct).ConfigureAwait(false);await CheckEpisode(c,tx,request.EpisodeId,request.Version,ct).ConfigureAwait(false);
        string test,job;
        await using(var cmd=Command(c,tx,"SELECT s.original_test,s.job FROM planning.source_rows s JOIN planning.batches b ON b.id=s.batch_id WHERE s.batch_id=@batch AND s.file='Report List' AND s.row_number=@row AND s.execution_id IS NULL AND b.status='Applied' FOR UPDATE OF s",("batch",request.BatchId),("row",request.ReportRow)))
        await using(var r=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false)) {if(!await r.ReadAsync(ct).ConfigureAwait(false))throw new PlanningConflictException("La fila ya fue vinculada o no existe.");test=r.GetString(0);job=r.GetString(1);}
        if(!(await JobNames(c,tx,request.EpisodeId,ct).ConfigureAwait(false)).Contains(job,StringComparer.Ordinal))throw new PlanningValidationException("La fila y el episodio deben pertenecer al mismo JOB.");
        var execution=request.ExecutionId??Guid.NewGuid();
        if(request.CreateExecution)
        {var map=(await Mappings(c,tx,ct).ConfigureAwait(false)).SingleOrDefault(m=>PlanningReconciler.TestKey(m.OriginalTest)==PlanningReconciler.TestKey(test));await InsertExecution(c,tx,execution,request.EpisodeId,test,map?.Equivalence,map?.Laboratory,map?.NoProgram==true,"ReportList",ct).ConfigureAwait(false);}
        else
        {var candidate=(await Executions(c,tx,[request.EpisodeId],ct).ConfigureAwait(false)).SingleOrDefault(t=>t.Id==execution);if(candidate is null || PlanningReconciler.TestKey(candidate.OriginalTest)!=PlanningReconciler.TestKey(test))throw new PlanningValidationException("La ejecución elegida no coincide con el episodio y la prueba originales.");}
        await Execute(c,tx,"UPDATE planning.source_rows SET episode_id=@episode,execution_id=@execution WHERE batch_id=@batch AND file='Report List' AND row_number=@row",ct,("episode",request.EpisodeId),("execution",execution),("batch",request.BatchId),("row",request.ReportRow)).ConfigureAwait(false);
        await Execute(c,tx,"UPDATE planning.episodes SET version=version+1 WHERE id=@id",ct,("id",request.EpisodeId)).ConfigureAwait(false);
        await RefreshSummaryTray(c,tx,[request.EpisodeId],ct).ConfigureAwait(false);
        await Audit(c,tx,request.EpisodeId,"SourceIdentityResolved",actor,reason,new{request.BatchId,request.ReportRow,ExecutionId=execution,request.CreateExecution},ct).ConfigureAwait(false);
        await RefreshIncidents(c,tx,request.EpisodeId,actor,ct).ConfigureAwait(false);await Bump(c,tx,ct).ConfigureAwait(false);await tx.CommitAsync(ct).ConfigureAwait(false);
    }
    public async Task<IReadOnlyList<PlanningAccessUser>> AccessUsersAsync(CancellationToken ct)
    {
        await using var c=await (identities?.Source ?? database.Source).OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd=Command(c,null,"SELECT u.id,u.nombre,coalesce(d.nombre,'Sin departamento'),coalesce(array_agg(p.permiso_key) FILTER(WHERE p.permiso_key=ANY(@codes)),'{}') FROM public.usuarios u LEFT JOIN public.departamentos d ON d.id=u.id_departamento LEFT JOIN public.usuarios_permisos p ON p.id_usuario=u.id WHERE u.activo GROUP BY u.id,u.nombre,d.nombre ORDER BY u.nombre",("codes",PlanningPermissions.All.ToArray()));
        await using var r=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);var users=new List<PlanningAccessUser>();
        while(await r.ReadAsync(ct).ConfigureAwait(false))users.Add(new(r.GetInt32(0),r.GetString(1),r.GetString(2),r.GetFieldValue<string[]>(3)));
        return users;
    }
    public async Task SaveAccessAsync(int userId,PlanningAccessChange request,int actor,CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);var reason=Reason(request.Reason);
        if(request.Permissions is null || request.Permissions.Any(p=>!PlanningPermissions.All.Contains(p,StringComparer.Ordinal)) || request.Permissions.Distinct(StringComparer.Ordinal).Count()!=request.Permissions.Count)
            throw new PlanningValidationException("Seleccione permisos de Planificación válidos, sin duplicados.");
        await using var c=await (identities?.Source ?? database.Source).OpenConnectionAsync(ct).ConfigureAwait(false);await using var tx=await c.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using(var check=Command(c,tx,"SELECT activo FROM public.usuarios WHERE id=@id FOR UPDATE",("id",userId)))if(await check.ExecuteScalarAsync(ct).ConfigureAwait(false) is not true)throw new PlanningValidationException("Usuario inexistente o inactivo.");
        string[] before;
        await using(var read=Command(c,tx,"SELECT coalesce(array_agg(permiso_key ORDER BY permiso_key),'{}') FROM public.usuarios_permisos WHERE id_usuario=@id AND permiso_key=ANY(@codes)",("id",userId),("codes",PlanningPermissions.All.ToArray())))
            before=(string[])(await read.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
        await Execute(c,tx,"DELETE FROM public.usuarios_permisos WHERE id_usuario=@id AND permiso_key=ANY(@codes)",ct,("id",userId),("codes",PlanningPermissions.All.ToArray())).ConfigureAwait(false);
        foreach(var code in request.Permissions)await Execute(c,tx,"INSERT INTO public.usuarios_permisos(id_usuario,permiso_key) VALUES(@id,@code)",ct,("id",userId),("code",code)).ConfigureAwait(false);
        await Execute(c,tx,"""
            INSERT INTO public.planning_access_history(id,user_id,actor_id,reason,before_permissions,after_permissions)
            VALUES(@id,@user,@actor,@reason,@before,@after)
            """,ct,("id",Guid.NewGuid()),("user",userId),("actor",actor),("reason",reason),("before",before),("after",request.Permissions.ToArray())).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
    }
}

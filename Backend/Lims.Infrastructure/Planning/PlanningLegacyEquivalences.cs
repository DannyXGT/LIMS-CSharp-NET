using System.Text;
using Lims.Application.Planning;
using Lims.Contracts.Planning;
using Npgsql;

namespace Lims.Infrastructure.Planning;

public sealed partial class PostgresPlanningStore
{
    public async Task<LegacyEquivalencePreview> LegacyEquivalencesAsync(CancellationToken ct)
    {
        await using var c=await database.Source.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var tx=await c.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead,ct).ConfigureAwait(false);
        var result=await ReadLegacyEquivalences(c,tx,ct).ConfigureAwait(false);await tx.CommitAsync(ct).ConfigureAwait(false);return result;
    }
    private static async Task<LegacyEquivalencePreview> ReadLegacyEquivalences(NpgsqlConnection c,NpgsqlTransaction tx,CancellationToken ct)
    {
        await using(var available=Command(c,tx,"SELECT to_regclass('public.planificacion_test_map') IS NOT NULL AND to_regclass('public.planificacion_no_programar') IS NOT NULL"))
            if(await available.ExecuteScalarAsync(ct).ConfigureAwait(false) is not true)throw new PlanningValidationException("No están disponibles las tablas legacy de equivalencias y NO PROGRAMAR en esta base. No se conectará a otra base para buscarlas.");
        var items=new List<LegacyEquivalence>();var conflicts=new List<string>();
        await using var cmd=Command(c,tx,"""
            SELECT m.id,m.nombre_tecnico,m.test_name_tecnico,m.nombre_planificacion,m.nombre_prueba_plan,m.lab,m.activo,
              EXISTS(SELECT 1 FROM public.planificacion_no_programar n WHERE n.tecnico_norm=m.nombre_tecnico_norm)
            FROM public.planificacion_test_map m ORDER BY m.id
            """);
        await using(var r=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        while(await r.ReadAsync(ct).ConfigureAwait(false))
        {
            var first=NullableString(r,1)?.Trim();var alias=NullableString(r,2)?.Trim();var name=NullableString(r,3)?.Trim();var alternate=NullableString(r,4)?.Trim();var lab=NullableString(r,5)?.Trim();var no=r.GetBoolean(7);
            if(!r.GetBoolean(6)&&!no)continue;
            var source=r.GetInt64(0);
            if(!string.IsNullOrEmpty(first)&&!string.IsNullOrEmpty(alias)&&PlanningReconciler.TestKey(first)!=PlanningReconciler.TestKey(alias))
            {conflicts.Add($"Mapa legacy {source}: nombres técnicos incompatibles.");continue;}
            var original=string.IsNullOrWhiteSpace(first)?alias:first;
            if(string.IsNullOrWhiteSpace(original)){conflicts.Add($"Mapa legacy {source}: falta nombre técnico original.");continue;}
            if(!no&&!string.IsNullOrWhiteSpace(name)&&!string.IsNullOrWhiteSpace(alternate)&&name!=alternate){conflicts.Add($"{original}: equivalencias legacy incompatibles.");continue;}
            var equivalent=string.IsNullOrWhiteSpace(name)?alternate:name;
            if(!no&&(string.IsNullOrWhiteSpace(equivalent)||string.IsNullOrWhiteSpace(lab))){conflicts.Add($"{original}: equivalencia/laboratorio incompletos.");continue;}
            items.Add(new(source,original,no?null:equivalent,no?null:lab,no));
        }
        var accepted=new List<LegacyEquivalence>();
        foreach(var group in items.GroupBy(x=>PlanningReconciler.TestKey(x.OriginalTest)))
        {if(group.Select(x=>(x.Equivalence,x.Laboratory,x.NoProgram)).Distinct().Count()>1)conflicts.Add($"{group.First().OriginalTest}: decisiones legacy duplicadas incompatibles.");else accepted.Add(group.First());}
        var fingerprint=PlanningImportReader.Hash(Encoding.UTF8.GetBytes(Serialize(new{Items=accepted,Conflicts=conflicts})));
        return new(fingerprint,accepted,conflicts);
    }
    public async Task ImportLegacyEquivalencesAsync(LegacyEquivalenceConfirmation request,int actor,CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);var reason=Reason(request.Reason);
        await using var c=await database.Source.OpenConnectionAsync(ct).ConfigureAwait(false);await using var tx=await c.BeginTransactionAsync(ct).ConfigureAwait(false);
        await Revision(c,tx,true,ct).ConfigureAwait(false);var snapshot=await ReadLegacyEquivalences(c,tx,ct).ConfigureAwait(false);
        if(request.Fingerprint!=snapshot.Fingerprint)throw new PlanningConflictException("El catálogo de origen cambió después de la revisión. Actualice la vista previa.");
        foreach(var item in snapshot.Items)
            await Execute(c,tx,"INSERT INTO planning.equivalences(test_key,original_test,equivalence,laboratory,no_program) VALUES(@key,@original,@eq,@lab,@no) ON CONFLICT(test_key) DO NOTHING",ct,
                ("key",PlanningReconciler.TestKey(item.OriginalTest)),("original",item.OriginalTest),("eq",item.Equivalence),("lab",item.Laboratory),("no",item.NoProgram)).ConfigureAwait(false);
        var affected=new List<Guid>();
        await using(var apply=Command(c,tx,"""
            UPDATE planning.executions x SET equivalence=m.equivalence,laboratory=m.laboratory,no_program=m.no_program,version=x.version+1
            FROM planning.equivalences m WHERE m.test_key=x.test_key AND x.origin='ReportList' AND x.equivalence IS NULL AND NOT x.no_program
            RETURNING x.episode_id
            """))
        await using(var r=await apply.ExecuteReaderAsync(ct).ConfigureAwait(false))while(await r.ReadAsync(ct).ConfigureAwait(false))affected.Add(r.GetGuid(0));
        foreach(var episode in affected.Distinct())await Audit(c,tx,episode,"LegacyEquivalenceApplied",actor,reason,new{snapshot.Fingerprint},ct).ConfigureAwait(false);
        // Existing decisions and previously classified/manual executions are preserved.
        await Audit(c,tx,null,"LegacyEquivalencesImported",actor,reason,new{Source="public.planificacion_test_map + public.planificacion_no_programar",snapshot.Fingerprint,snapshot.Items,snapshot.Conflicts},ct).ConfigureAwait(false);
        await Bump(c,tx,ct).ConfigureAwait(false);await tx.CommitAsync(ct).ConfigureAwait(false);
    }
}

using Lims.Application.Planning;
using Lims.Contracts.Planning;
using Npgsql;

namespace Lims.Infrastructure.Planning;

public sealed partial class PostgresPlanningStore
{
    public async Task<PlanningJobControl> SaveControlAsync(Guid episodeId,PlanningControlEdit request,int actor,CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);var reason=Reason(request.Reason);
        if(request.Version<0)throw new PlanningValidationException("Versión de control inválida.");
        var label=string.IsNullOrWhiteSpace(request.Label)?null:string.Join(' ',request.Label.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
        if(label?.Length>80||request.Label?.Any(char.IsControl)==true)throw new PlanningValidationException("La etiqueta admite hasta 80 caracteres sin controles.");
        await using var c=await database.Source.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var tx=await c.BeginTransactionAsync(ct).ConfigureAwait(false);
        await Revision(c,tx,true,ct).ConfigureAwait(false);
        string job;
        await using(var cmd=Command(c,tx,"SELECT upper(trim(job)) FROM planning.episodes WHERE id=@id",("id",episodeId)))
            job=(string)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false)??throw new PlanningValidationException("El JOB no existe."));
        var before=await Control(c,tx,job,ct).ConfigureAwait(false);
        if(before.Version!=request.Version)throw new PlanningConflictException("On Hold o la etiqueta cambiaron. Recargue el expediente antes de guardar.");
        var holdReason=request.OnHold?(before.OnHold?before.HoldReason:reason):null;
        await Execute(c,tx,"""
            INSERT INTO planning.job_controls(job,on_hold,label,hold_reason,updated_by)
            VALUES(@job,@hold,@label,@reason,@actor)
            ON CONFLICT(job) DO UPDATE SET on_hold=EXCLUDED.on_hold,label=EXCLUDED.label,hold_reason=EXCLUDED.hold_reason,
              version=planning.job_controls.version+1,updated_at=now(),updated_by=EXCLUDED.updated_by
            """,ct,("job",job),("hold",request.OnHold),("label",label),("reason",holdReason),("actor",actor)).ConfigureAwait(false);
        var saved=await Control(c,tx,job,ct).ConfigureAwait(false);
        // A JOB-level decision is visible in every episode's dossier, without merging their executions.
        var episodes=new List<Guid>();
        await using(var cmd=Command(c,tx,"SELECT id FROM planning.episodes WHERE upper(trim(job))=@job",("job",job)))
        await using(var r=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))while(await r.ReadAsync(ct).ConfigureAwait(false))episodes.Add(r.GetGuid(0));
        foreach(var id in episodes)await Audit(c,tx,id,"JobControlChanged",actor,reason,new{Before=before,After=saved},ct).ConfigureAwait(false);
        await Bump(c,tx,ct).ConfigureAwait(false);await tx.CommitAsync(ct).ConfigureAwait(false);return saved;
    }
    private static async Task<PlanningJobControl> Control(NpgsqlConnection c,NpgsqlTransaction tx,string job,CancellationToken ct)
    {
        await using var cmd=Command(c,tx,"SELECT on_hold,label,hold_reason,version,updated_at,updated_by FROM planning.job_controls WHERE job=upper(trim(@job))",("job",job));
        await using var r=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await r.ReadAsync(ct).ConfigureAwait(false)?new(job,r.GetBoolean(0),NullableString(r,1),NullableString(r,2),r.GetInt64(3),r.GetFieldValue<DateTimeOffset>(4),r.GetInt32(5)):new(job,false,null,null,0);
    }
}

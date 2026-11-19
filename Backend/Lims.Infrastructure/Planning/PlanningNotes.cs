using Lims.Application.Planning;
using Lims.Contracts.Planning;

namespace Lims.Infrastructure.Planning;

public sealed partial class PostgresPlanningStore
{
    public async Task<IReadOnlyList<PlanningNoteSaved>> EditNotesAsync(PlanningNoteBatch request,int actor,CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if(request.Cells is null || request.Cells.Count is <1 or >2000)throw new PlanningValidationException("Seleccione entre 1 y 2000 celdas por lote.");
        request=request with{Cells=request.Cells.Select(cell=>cell is {Column:"Observation",Value:{} text}?cell with{Value=text.Replace("\r\n","\n",StringComparison.Ordinal).Replace('\r','\n')}:cell).ToArray()};
        var reason=Reason(request.Reason);
        foreach(var cell in request.Cells)
        {
            if(cell is null || cell.ExecutionId==Guid.Empty || cell.Version<0 || cell.Column is not ("Responsible" or "Observation"))throw new PlanningValidationException("Solo se permite editar Responsable y Observación de una ejecución existente.");
            var max=cell.Column=="Responsible"?150:2000;
            if(cell.Value?.Length>max || cell.Value?.Any(c=>char.IsControl(c)&&c!='\n')==true || (cell.Column=="Responsible"&&cell.Value?.Contains('\n')==true))throw new PlanningValidationException($"Valor inválido de {cell.Column}; máximo {max} caracteres.");
        }
        if(request.Cells.GroupBy(c=>(c.ExecutionId,c.Column)).Any(g=>g.Count()>1))throw new PlanningValidationException("Una celda no puede aparecer dos veces en el lote.");
        await using var c=await database.Source.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var tx=await c.BeginTransactionAsync(ct).ConfigureAwait(false);
        await Revision(c,tx,true,ct).ConfigureAwait(false);
        await using(var held=Command(c,tx,"SELECT EXISTS(SELECT 1 FROM planning.executions x JOIN planning.episodes e ON e.id=x.episode_id JOIN planning.job_controls jc ON jc.job=upper(trim(e.job)) WHERE x.id=ANY(@ids) AND jc.on_hold)",("ids",request.Cells.Select(cell=>cell.ExecutionId).Distinct().ToArray())))
            if((bool)(await held.ExecuteScalarAsync(ct).ConfigureAwait(false))!)throw new PlanningValidationException("El lote contiene un JOB On Hold. No se guardó ninguna celda; consulte el motivo en el expediente.");
        var result=new List<PlanningNoteSaved>();var batch=Guid.NewGuid();
        // Consistent lock order avoids deadlocks between overlapping pastes. Import fields are never written here.
        foreach(var group in request.Cells.GroupBy(x=>x.ExecutionId).OrderBy(g=>g.Key))
        {
            Guid episode;string? responsible,observation;long version;
            await using(var cmd=Command(c,tx,"SELECT episode_id,responsible,observation,note_version FROM planning.executions WHERE id=@id FOR UPDATE",("id",group.Key)))
            await using(var reader=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
            {
                if(!await reader.ReadAsync(ct).ConfigureAwait(false))throw new PlanningConflictException("Una ejecución ya no está disponible. Recargue la consulta.");
                episode=reader.GetGuid(0);responsible=NullableString(reader,1);observation=NullableString(reader,2);version=reader.GetInt64(3);
            }
            if(group.Any(cell=>cell.Version!=version))throw new PlanningConflictException("Una celda cambió desde que se consultó. No se guardó ninguna celda del lote; recargue y revise.");
            var before=new{Responsible=responsible,Observation=observation};
            foreach(var cell in group){if(cell.Column=="Responsible")responsible=cell.Value;else observation=cell.Value;}
            await Execute(c,tx,"UPDATE planning.executions SET responsible=@owner,observation=@note,note_version=note_version+1 WHERE id=@id",ct,("owner",responsible),("note",observation),("id",group.Key)).ConfigureAwait(false);
            await Audit(c,tx,episode,"ConsultationNotesEdited",actor,reason,new{BatchId=batch,ExecutionId=group.Key,Before=before,After=new{Responsible=responsible,Observation=observation}},ct).ConfigureAwait(false);
            result.Add(new(group.Key,responsible,observation,version+1));
        }
        await tx.CommitAsync(ct).ConfigureAwait(false);return result;
    }
}

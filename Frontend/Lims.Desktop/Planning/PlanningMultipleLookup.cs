using Lims.Contracts.Planning;

namespace Lims.Desktop.Planning;

public sealed record PlanningJobList(IReadOnlyList<string> Jobs,int Entered)
{
    public int Duplicates=>Entered-Jobs.Count;
    public string Search=>string.Join(',',Jobs);
    public static PlanningJobList Parse(string text)
    {
        if(text.Length>20000)throw new FormatException("La lista supera los 20.000 caracteres.");
        var entries=text.Split([';',',','\r','\n','\t'],StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries);
        var jobs=entries.Select(j=>j.ToUpperInvariant()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if(jobs.Length>500)throw new FormatException("Máximo 500 JOBs únicos por consulta.");
        return new(jobs,entries.Length);
    }
}
public sealed record PlanningJobMatches(IReadOnlyList<string> Found,IReadOnlyList<string> Missing,int Executions);

public static class PlanningMultipleLookup
{
    // Reuse the authenticated, server-paged query. Never infer absence from just its first page.
    public static async Task<PlanningJobMatches> Find(PlanningApiClient api,PlanningJobList input,CancellationToken ct)
    {
        if(input.Jobs.Count==0)throw new FormatException("Ingrese al menos un JOB para buscar.");
        var found=new HashSet<string>(StringComparer.OrdinalIgnoreCase);int? total=null;int executions=0;
        for(int page=1;;page++)
        {
            var result=await api.Jobs(new PlanningQuery(Search:input.Search,SearchMode:"Exact",Page:page,PageSize:500),ct).ConfigureAwait(false);
            if(!result.IsSuccess||result.Value is not{} value)throw new InvalidOperationException(result.Error??"No se pudo consultar la lista de JOBs.");
            if(total is not null&&total!=value.Total)throw new InvalidOperationException("Los resultados cambiaron durante la consulta. Vuelva a buscar para verificar la lista completa.");
            total=value.Total;executions=value.Counts?.Executions??value.Total;
            foreach(var row in value.Items)found.Add(row.Job);
            if(found.Count==input.Jobs.Count||page*500>=total)break;
            if(value.Items.Count==0)throw new InvalidOperationException("La API devolvió una página incompleta. Vuelva a consultar antes de determinar JOBs no encontrados.");
        }
        return new(input.Jobs.Where(found.Contains).ToArray(),input.Jobs.Where(j=>!found.Contains(j)).ToArray(),executions);
    }
}

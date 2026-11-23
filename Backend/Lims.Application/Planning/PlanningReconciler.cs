using System.Text.Json;
using Lims.Contracts.Planning;

namespace Lims.Application.Planning;

public static class PlanningReconciler
{
    public static string TestKey(string value) => string.Join(' ',new string(value.Where(c=>!char.IsControl(c)).ToArray()).Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
    public static bool IsTestSource(string file)=>file is "Report List" or "Side2" or "React Active";
    private static bool ManualClassification(StoredExecution? execution)=>execution?.Origin=="Manual"||execution?.ExecutionType=="ReactSide2:v1";

    public static PlanningPlan Plan(PlanningImportInput input, IReadOnlyList<StoredEpisode> existing,
        IReadOnlyList<StoredExecution> tests, IReadOnlyList<PlanningEquivalence> mappings,
        IReadOnlyList<StoredSourceIdentity>? sourceIdentities=null)
    {
        ArgumentNullException.ThrowIfNull(input);
        var changes = new List<ImportChange>(); var reviews = new List<ImportReview>();
        var issues = input.Issues.ToList(); var episodes = new List<ProposedEpisode>(); var executions = new List<ProposedExecution>();
        PlanningPlan Result() => new(episodes, executions, changes, reviews, issues);
        void ClassificationChange(StoredExecution before,ProposedExecution after)
        {
            if(input.Cut.Policy=="Preserve"||(before.Equivalence,before.Laboratory,before.NoProgram)==(after.Equivalence,after.Laboratory,after.NoProgram))return;
            changes.Add(new("Classification","UpdateSource",after.Job,after.EpisodeId,after.OriginalTest,
                JsonSerializer.Serialize(new{before.Equivalence,before.Laboratory,before.NoProgram}),JsonSerializer.Serialize(new{after.Equivalence,after.Laboratory,after.NoProgram})));
        }
        if (issues.Any(i => i.Blocking)) return Result();
        var existingByJob=existing.ToLookup(e=>e.Job,StringComparer.Ordinal);
        var testsByEpisode=tests.ToLookup(t=>t.EpisodeId);
        var side2=input.Cut.Policy=="HtmlReactSide2";
        var table=input.Cut.Policy=="ReactTable";
        var reportJobs=input.Rows.Where(r=>IsTestSource(r.File)).Select(r=>r.Job).ToHashSet(StringComparer.Ordinal);
        var map = mappings.ToDictionary(m => TestKey(m.OriginalTest), StringComparer.Ordinal);
        var reportHash=PlanningImportReader.Hash(input.Report);
        var documentLinks=(sourceIdentities??[]).Where(s=>s.ReportHash==reportHash).ToLookup(s=>(s.Sheet,s.Row));
        var summaries = input.Rows.Where(r => r.File == "Summary"||(side2||table)&&r.Wip is not null).ToList();
        var summaryDatesByJob=summaries.GroupBy(r=>r.Job,StringComparer.Ordinal).ToDictionary(g=>g.Key,g=>g.Select(r=>r.Wip!.InDate).Distinct().Count(),StringComparer.Ordinal);
        foreach (var group in summaries.GroupBy(r => (r.Job, r.Wip!.InDate)))
        {
            var row = group.First();
            if (group.Select(r => (r.Wip,r.Official)).Distinct().Count() > 1)
            { issues.Add(new("Summary", row.Row, "WIP_CONFLICT", "Hay valores WIP incompatibles para el mismo JOB e ingreso.", true)); continue; }
            var old = existingByJob[row.Job].SingleOrDefault(e => e.SourceInDate == row.Wip!.InDate);
            if(old is null&&summaryDatesByJob[row.Job]==1)
                old=existingByJob[row.Job].SingleOrDefault(e=>e.SourceInDate is null&&e.Wip==new WipValues(null,null,null,null,null,null,null,null));
            var id = old?.Id ?? Guid.NewGuid();
            if(table&&old?.HasSummarySource==true)
            {
                if(row.Wip!=old.Wip||row.Official!=old.Official)reviews.Add(new("SourceConflict",row.Job,id,null,"Los datos de React difieren del Summary ya observado. Se conserva el WIP y estado de autoridad; revise ambas fuentes antes de corregir.",row.Row));
                row=row with{Wip=old.Wip,Official=old.Official};
            }
            if(side2&&old is not null)row=row with{Wip=old.Wip with{InDate=row.Wip!.InDate,DueDate=row.Wip.DueDate},Official=old.Official};
            episodes.Add(new(id, row.Job, row.Wip!, old is null, row.Row,row.Official,!side2));
            if (old is null || row.Wip != old.Wip)
                changes.Add(new("WIP", old is null ? "Create" : "UpdateSource", row.Job, id, null,
                    old is null ? "" : JsonSerializer.Serialize(old.Wip), JsonSerializer.Serialize(row.Wip)));
            if(row.Official!=old?.Official) changes.Add(new("OfficialState","UpdateSource",row.Job,id,null,JsonSerializer.Serialize(old?.Official),JsonSerializer.Serialize(row.Official)));
            if (old?.ManualFields.Count > 0) reviews.Add(new("ManualManaged", row.Job, id, null, "Los campos WIP corregidos manualmente se conservan; los valores nuevos quedan en procedencia."));
            if (existingByJob[row.Job].Any(e => e.Id != id)) reviews.Add(new("Episode", row.Job, id, null, "Ingreso distinto: episodio separado. No se trasladan pruebas ni decisiones del episodio anterior."));
            var effectiveVendor = old?.ManualFields.Contains("Vendor", StringComparer.Ordinal) == true ? old.Wip.Vendor : row.Wip!.Vendor;
            if (string.IsNullOrWhiteSpace(effectiveVendor)) reviews.Add(new("MissingVendor", row.Job, id, null, "Vendor pendiente; incidencia independiente de la asignación de pruebas."));
        }
        var incomingByJob=episodes.ToLookup(e=>e.Job,StringComparer.Ordinal);
        foreach (var jobGroup in input.Rows.Where(r => IsTestSource(r.File)).GroupBy(r => r.Job))
        {
            var incoming = incomingByJob[jobGroup.Key].ToArray();
            var known = existingByJob[jobGroup.Key].ToArray();
            var id = incoming.Length == 1 ? incoming[0].Id : incoming.Length == 0 && known.Length == 1 ? known[0].Id : (Guid?)null;
            if(incoming.Length==0&&known.Length==0)
            {
                id=Guid.NewGuid();
                episodes.Add(new(id.Value,jobGroup.Key,new(null,null,null,null,null,null,null,null),true,0,null,false));
                changes.Add(new("WIP","CreateWithoutSummary",jobGroup.Key,id,null,"","Report List conservado; cobertura Summary pendiente"));
            }
            if (incoming.Length == 0) reviews.Add(new("ReportWithoutSummary", jobGroup.Key, id, null, id is null ? "Report List sin Summary ni episodio inequívoco. Se conservan filas para conciliación." : "Fuera de cobertura Summary; se usa el episodio previamente conocido sin inventar WIP."));
            foreach(var unnamed in jobGroup.Where(r=>string.IsNullOrWhiteSpace(r.Test)))
                reviews.Add(new("MissingTestName",unnamed.Job,id,null,"Falta el nombre en la fuente; conservar sin crear una ejecución inventada.",unnamed.Row));
            foreach (var group in jobGroup.Where(r=>!string.IsNullOrWhiteSpace(r.Test)).GroupBy(r => TestKey(r.Test!)))
            {
                var row = group.First();
                // A byte-identical source document plus its persisted, reviewed row link is evidence.
                // Row order in a different document is never used as execution identity.
                var linked=group.Select(source=>(Source:source,Ids:documentLinks[(source.Sheet,source.Row)]
                    .Select(s=>s.ExecutionId).Distinct().ToArray())).ToArray();
                if(id is not null && linked.All(l=>l.Ids.Length==1 && testsByEpisode[id.Value].Any(t=>t.Id==l.Ids[0]&&TestKey(t.OriginalTest)==group.Key)))
                {
                    foreach(var link in linked)
                    {
                        var linkedExecution=testsByEpisode[id.Value].Single(t=>t.Id==link.Ids[0]);
                        map.TryGetValue(group.Key,out var current);
                        var reactAuthority=side2||(input.Cut.Policy is "HtmlReact" or "ReactTable")&&!ManualClassification(linkedExecution);
                        executions.Add(new(linkedExecution.Id,id.Value,row.Job,linkedExecution.OriginalTest,
                            reactAuthority?current?.Equivalence:ManualClassification(linkedExecution)?linkedExecution.Equivalence:current?.Equivalence??linkedExecution.Equivalence,
                            reactAuthority?current?.Laboratory:ManualClassification(linkedExecution)?linkedExecution.Laboratory:current?.Laboratory??linkedExecution.Laboratory,
                            ManualClassification(linkedExecution)&&!side2?linkedExecution.NoProgram:current?.NoProgram==true,"Observe",[link.Source.Row],side2?"ReactSide2:v1":linkedExecution.ExecutionType,Origin:linkedExecution.Origin,ReactRowId:link.Source.ReactRowId));
                        ClassificationChange(linkedExecution,executions[^1]);
                        if(linkedExecution.Origin=="Manual")reviews.Add(new("ManualManaged",row.Job,id,row.Test,"Vínculo fuente ya conciliado: se conserva la ejecución manual y su marcado.",link.Source.Row));
                    }
                    continue;
                }
                if (id is null || group.Count() > 1)
                { foreach (var source in group) reviews.Add(new("ExecutionIdentity", row.Job, id, source.Test, "La fuente no distingue episodios/repeticiones. Vincule explícitamente o cree una ejecución autorizada.", source.Row)); continue; }
                var candidates = testsByEpisode[id.Value].Where(t => TestKey(t.OriginalTest) == group.Key).ToArray();
                if (candidates.Length > 1)
                { reviews.Add(new("ExecutionIdentity", row.Job, id, row.Test, "Hay varias ejecuciones posibles. No se deduplican por JOB y nombre de prueba.", row.Row)); continue; }
                map.TryGetValue(group.Key, out var equivalence);
                var previous = candidates.SingleOrDefault();
                var reactClassification=side2||(input.Cut.Policy is "HtmlReact" or "ReactTable")&&!ManualClassification(previous);
                var action = previous is null ? "Create" : previous.Origin == "Manual" ? "LinkManual" : "Observe";
                executions.Add(new(previous?.Id ?? Guid.NewGuid(), id.Value, row.Job, row.Test!, reactClassification?equivalence?.Equivalence:previous?.Equivalence ?? equivalence?.Equivalence,
                    reactClassification?equivalence?.Laboratory:previous?.Laboratory ?? equivalence?.Laboratory, ManualClassification(previous)&&!side2?previous!.NoProgram:equivalence?.NoProgram == true, action, group.Select(r => r.Row).ToArray(),
                    side2?"ReactSide2:v1":previous?.ExecutionType,Origin:previous?.Origin??(side2?"Manual":"ReportList"),ReactRowId:row.ReactRowId));
                if(previous is not null)ClassificationChange(previous,executions[^1]);
                if (action != "Observe") changes.Add(new("Test", action, row.Job, id, row.Test, previous is null ? "" : JsonSerializer.Serialize(previous), "Vinculación trazada; se conserva identidad y estado operativo."));
                if (action == "LinkManual") reviews.Add(new("ManualManaged", row.Job, id, row.Test, "Coincidencia única: confirmar vinculará la fila a la prueba manual existente, sin crear otra ni cambiar su origen.", row.Row));
                if (previous?.Equivalence is null && equivalence is null) reviews.Add(new("MissingEquivalence", row.Job, id, row.Test, "Asignar equivalencia verificada o NO PROGRAMAR.", row.Row));
                if (incoming.Length > 0) reviews.Add(new("BothFiles", row.Job, id, row.Test, "JOB presente en ambos archivos.", row.Row));
            }
        }
        foreach (var episode in episodes.Where(e => e.HasSummary && !reportJobs.Contains(e.Job)))
        {
            var validManual = testsByEpisode[episode.Id].Any(t => t.Origin == "Manual" && !string.IsNullOrWhiteSpace(t.Equivalence) && !string.IsNullOrWhiteSpace(t.Laboratory));
            reviews.Add(new(validManual ? "ManualManaged" : "SummaryWithoutReport", episode.Job, episode.Id, null,
                validManual ? "Asignación manual válida: no se reabre la incidencia de pruebas faltantes." : "Guardar WIP y revisar/retener/agregar pruebas con autorización; no se inventan pruebas."));
        }
        var result=Result();
        return input.Cut.Policy is "HtmlReact" or "HtmlReactSide2" or "ReactTable"?PlanningImportRules.Apply(input,result,existing,tests):result;
    }
}

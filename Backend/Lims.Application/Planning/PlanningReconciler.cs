using System.Text.Json;
using Lims.Contracts.Planning;

namespace Lims.Application.Planning;

public static class PlanningReconciler
{
    public static string TestKey(string value) => value.Trim().ToUpperInvariant();

    public static PlanningPlan Plan(PlanningImportInput input, IReadOnlyList<StoredEpisode> existing,
        IReadOnlyList<StoredExecution> tests, IReadOnlyList<PlanningEquivalence> mappings)
    {
        ArgumentNullException.ThrowIfNull(input);
        var changes = new List<ImportChange>(); var reviews = new List<ImportReview>();
        var issues = input.Issues.ToList(); var episodes = new List<ProposedEpisode>(); var executions = new List<ProposedExecution>();
        PlanningPlan Result() => new(episodes, executions, changes, reviews, issues);
        if (issues.Any(i => i.Blocking)) return Result();
        var map = mappings.ToDictionary(m => TestKey(m.OriginalTest), StringComparer.Ordinal);
        var summaries = input.Rows.Where(r => r.File == "Summary").ToList();
        foreach (var group in summaries.GroupBy(r => (r.Job, r.Wip!.InDate)))
        {
            var row = group.First();
            if (group.Select(r => r.Wip).Distinct().Count() > 1)
            { issues.Add(new("Summary", row.Row, "WIP_CONFLICT", "Hay valores WIP incompatibles para el mismo JOB e ingreso.", true)); continue; }
            var old = existing.SingleOrDefault(e => e.Job == row.Job && e.SourceInDate == row.Wip!.InDate);
            var closedSource = JsonSerializer.Deserialize<Dictionary<string,string?>>(row.Fields)?.GetValueOrDefault("Close Date") is { Length: > 0 };
            if (closedSource && old is null) continue; // Archive the reference row without creating an active historical WIP episode.
            var id = old?.Id ?? Guid.NewGuid();
            episodes.Add(new(id, row.Job, row.Wip!, old is null, row.Row));
            if (old is null || row.Wip != old.Wip)
                changes.Add(new("WIP", old is null ? "Create" : "UpdateSource", row.Job, id, null,
                    old is null ? "" : JsonSerializer.Serialize(old.Wip), JsonSerializer.Serialize(row.Wip)));
            if (old?.ManualFields.Count > 0) reviews.Add(new("ManualManaged", row.Job, id, null, "Los campos WIP corregidos manualmente se conservan; los valores nuevos quedan en procedencia."));
            if (existing.Any(e => e.Job == row.Job && e.Id != id)) reviews.Add(new("Episode", row.Job, id, null, "Ingreso distinto: episodio separado. No se trasladan pruebas ni decisiones del episodio anterior."));
            var effectiveVendor = old?.ManualFields.Contains("Vendor", StringComparer.Ordinal) == true ? old.Wip.Vendor : row.Wip!.Vendor;
            if (string.IsNullOrWhiteSpace(effectiveVendor)) reviews.Add(new("MissingVendor", row.Job, id, null, "Vendor pendiente; incidencia independiente de la asignación de pruebas."));
        }
        foreach (var jobGroup in input.Rows.Where(r => r.File == "Report List").GroupBy(r => r.Job))
        {
            var incoming = episodes.Where(e => e.Job == jobGroup.Key).ToArray();
            var known = existing.Where(e => e.Job == jobGroup.Key).ToArray();
            var id = incoming.Length == 1 ? incoming[0].Id : incoming.Length == 0 && known.Length == 1 ? known[0].Id : (Guid?)null;
            if (incoming.Length == 0) reviews.Add(new("ReportWithoutSummary", jobGroup.Key, id, null, id is null ? "Report List sin Summary ni episodio inequívoco. Se conservan filas para conciliación." : "Fuera de cobertura Summary; se usa el episodio previamente conocido sin inventar WIP."));
            foreach (var group in jobGroup.GroupBy(r => TestKey(r.Test!)))
            {
                var row = group.First();
                var sourceSummaries = summaries.Where(r => r.Job == row.Job).ToArray();
                if (sourceSummaries.Length > 0 && sourceSummaries.All(r => JsonSerializer.Deserialize<Dictionary<string,string?>>(r.Fields)?.GetValueOrDefault("Close Date") is { Length: > 0 }))
                { foreach (var source in group) reviews.Add(new("ExternalClosed", row.Job, id, source.Test, "Close Date externo requiere conciliación explícita; no produce cierre local.", source.Row)); continue; }
                if (id is null || group.Count() > 1)
                { foreach (var source in group) reviews.Add(new("ExecutionIdentity", row.Job, id, source.Test, "La fuente no distingue episodios/repeticiones. Vincule explícitamente o cree una ejecución autorizada.", source.Row)); continue; }
                var summary = summaries.FirstOrDefault(r => r.Job == row.Job && r.Wip!.InDate == incoming.FirstOrDefault(e => e.Id == id)?.Source.InDate);
                if (summary is not null && JsonSerializer.Deserialize<Dictionary<string, string?>>(summary.Fields)?.GetValueOrDefault("Close Date") is { Length: > 0 })
                { reviews.Add(new("ExternalClosed", row.Job, id, row.Test, "Close Date externo requiere conciliación explícita; no produce cierre local.", row.Row)); continue; }
                var candidates = tests.Where(t => t.EpisodeId == id && TestKey(t.OriginalTest) == group.Key).ToArray();
                if (candidates.Length > 1)
                { reviews.Add(new("ExecutionIdentity", row.Job, id, row.Test, "Hay varias ejecuciones posibles. No se deduplican por JOB y nombre de prueba.", row.Row)); continue; }
                map.TryGetValue(group.Key, out var equivalence);
                var previous = candidates.SingleOrDefault();
                var action = previous is null ? "Create" : previous.Origin == "Manual" ? "LinkManual" : "Observe";
                executions.Add(new(previous?.Id ?? Guid.NewGuid(), id.Value, row.Job, row.Test!, previous?.Equivalence ?? equivalence?.Equivalence,
                    previous?.Laboratory ?? equivalence?.Laboratory, equivalence?.NoProgram == true, action, group.Select(r => r.Row).ToArray()));
                if (action != "Observe") changes.Add(new("Test", action, row.Job, id, row.Test, previous is null ? "" : JsonSerializer.Serialize(previous), "Vinculación trazada; se conserva identidad y estado operativo."));
                if (action == "LinkManual") reviews.Add(new("ManualManaged", row.Job, id, row.Test, "Coincidencia única: confirmar vinculará la fila a la prueba manual existente, sin crear otra ni cambiar su origen.", row.Row));
                if (previous?.Equivalence is null && equivalence is null) reviews.Add(new("MissingEquivalence", row.Job, id, row.Test, "Asignar equivalencia verificada o NO PROGRAMAR.", row.Row));
                if (incoming.Length > 0) reviews.Add(new("BothFiles", row.Job, id, row.Test, "JOB presente en ambos archivos.", row.Row));
            }
        }
        foreach (var episode in episodes.Where(e => !input.Rows.Any(r => r.File == "Report List" && r.Job == e.Job)))
        {
            var validManual = tests.Any(t => t.EpisodeId == episode.Id && t.Origin == "Manual" && !string.IsNullOrWhiteSpace(t.Equivalence) && !string.IsNullOrWhiteSpace(t.Laboratory));
            reviews.Add(new(validManual ? "ManualManaged" : "SummaryWithoutReport", episode.Job, episode.Id, null,
                validManual ? "Asignación manual válida: no se reabre la incidencia de pruebas faltantes." : "Guardar WIP y revisar/retener/agregar pruebas con autorización; no se inventan pruebas."));
        }
        return Result();
    }
}

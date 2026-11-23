using Lims.Application.Planning;
using Lims.Contracts.Planning;
using Npgsql;

namespace Lims.Infrastructure.Planning;

public sealed partial class PostgresPlanningStore
{
    private sealed record ReportCatalogue(string Sql,(string,object?)[] Args,IReadOnlyDictionary<string,PlanningEquivalence> Items)
    {
        public long Revision(long stored)=>Items.Count==0?stored:ReactCatalogueVersion(stored+"|"+string.Join('|',Items.OrderBy(x=>x.Key,StringComparer.Ordinal).Select(x=>x.Key+":"+x.Value.Version)));
    }
    private static async Task<ReportCatalogue> ReadReportCatalogue(NpgsqlConnection c,NpgsqlTransaction tx,CancellationToken ct)
    {
        if(!await HasReactCatalogue(c,tx,ct).ConfigureAwait(false))return new("",[],new Dictionary<string,PlanningEquivalence>());
        var maps=await ReactMappings(c,tx,ct).ConfigureAwait(false);
        var items=maps.ToDictionary(x=>PlanningReconciler.TestKey(x.OriginalTest),StringComparer.Ordinal);
        if(items.Count==0)return new("",[],items);
        const string sql="""
            WITH report_catalogue AS (
              SELECT * FROM unnest(@report_keys::text[],@report_names::text[],@report_labs::text[],@report_no::boolean[])
                AS m(test_key,equivalence,laboratory,no_program)
            ), report_executions AS (
              SELECT s.id,s.episode_id,s.original_test,s.test_key,
                CASE WHEN m.test_key IS NOT NULL THEN m.equivalence ELSE s.equivalence END AS equivalence,
                CASE WHEN m.test_key IS NOT NULL THEN m.laboratory ELSE s.laboratory END AS laboratory,
                CASE WHEN m.test_key IS NOT NULL THEN m.no_program ELSE s.no_program END AS no_program,
                s.origin,s.execution_type,s.digitized_at,s.digitized_by,s.tested_on,s.version,s.responsible,s.observation,s.note_version
              FROM planning.executions s LEFT JOIN report_catalogue m ON m.test_key=s.test_key
                AND s.origin='ReportList' AND s.execution_type IS NULL
                AND (s.digitized_at IS NULL OR s.tested_on IS NULL)
            )
            """;
        return new(sql,[("report_keys",items.Keys.ToArray()),("report_names",items.Values.Select(x=>x.Equivalence).ToArray()),
            ("report_labs",items.Values.Select(x=>x.Laboratory).ToArray()),("report_no",items.Values.Select(x=>x.NoProgram).ToArray())],items);
    }
}

namespace Lims.Infrastructure.Planning;

public sealed partial class PostgresPlanningStore
{
    // Consulta HTML excludes any hyphenated JOB; React also excludes REV without a hyphen.
    // Apply to the query before counting/paging. Source rows and dossiers remain intact.
    private const string VisiblePlanningJobSql="strpos(e.job,'-')=0 AND strpos(lower(e.job),'rev')=0";
    private const string ClassifiedReportTestSql="(x.no_program OR (nullif(trim(x.equivalence),'') IS NOT NULL AND upper(trim(x.equivalence))<>'SIN EQUIVALENCIA'))";
}

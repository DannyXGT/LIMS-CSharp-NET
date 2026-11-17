namespace Lims.Contracts.Planning;

public static class PlanningPermissions
{
    public const string View = "planning.view";
    public const string Import = "planning.import";
    public const string EditWip = "planning.wip.edit";
    public const string ManualTests = "planning.tests.manual";
    public const string Equivalences = "planning.equivalences.manage";
    public static IReadOnlyList<string> All { get; } = [View, Import, EditWip, ManualTests, Equivalences];
}

public sealed record WorkspaceAccess(bool Laboratory, bool Planning, bool Administration, IReadOnlyList<string> Permissions, string? Notice = null);
public sealed record PlanningQuery(string? Search = null, string? Test = null, string? Laboratory = null,
    string? Service = null, string? State = null, DateOnly? From = null, DateOnly? Until = null,
    string Sort = "job", bool Descending = false, int Page = 1, int PageSize = 100,
    string? Buyer = null, string? Vendor = null, string? Equivalence = null, string? Incident = null,
    string SearchMode = "Contains", DateOnly? InFrom = null, DateOnly? InUntil = null, bool Overdue = false);
public sealed record PlanningRow(Guid EpisodeId, Guid? ExecutionId, string Job, DateOnly? InDate, DateOnly? DueDate,
    string? OriginalTest, string? Equivalence, string? Laboratory, string? Service, DateTimeOffset? DigitizedAt,
    DateOnly? TestedOn, string DigitizationState, string? DigitizedBy, string? ExecutionType, string Origin,
    string? Buyer, string? Vendor, bool HasIncidents, string? Responsible = null, string? Observation = null, long NoteVersion = 0)
{
    [System.Text.Json.Serialization.JsonIgnore] public string OriginLabel => Origin == "ReportList" ? "Report List" : Origin == "Manual" ? "Manual" : "Summary";
    public override string ToString() => $"{Job} · {OriginalTest ?? "WIP sin pruebas"} · {DigitizationState}";
}
public sealed record PlanningCounts(int Jobs,int Executions,int Digitized,int Pending,int Unknown);
public sealed record PlanningPage(IReadOnlyList<PlanningRow> Items, int Total, int Page, int PageSize, PlanningCounts? Counts = null);
public sealed record PlanningNoteCell(Guid ExecutionId,long Version,string Column,string? Value);
public sealed record PlanningNoteBatch(IReadOnlyList<PlanningNoteCell> Cells,string Reason);
public sealed record PlanningNoteSaved(Guid ExecutionId,string? Responsible,string? Observation,long Version);
public sealed record WipValues(string? Buyer, string? Vendor, string? Service, DateOnly? InDate, DateOnly? DueDate,
    decimal? Amount, string? Currency, decimal? Usd);
public sealed record PlanningIncident(Guid Id, Guid EpisodeId, string Kind, string State, string? Reason, long Version);
public sealed record PlanningHistory(Guid Id, string Action, int ActorId, DateTimeOffset At, string Reason, string Details,string? ActorName=null);
public sealed record PlanningSource(Guid BatchId, string File, string Sheet, int Row, string Sha256, DateTimeOffset ImportedAt, string Fields);
public sealed record JobDossier(Guid EpisodeId, string Job, WipValues Wip, long Version,
    IReadOnlyList<PlanningRow> Tests, IReadOnlyList<PlanningIncident> Incidents,
    IReadOnlyList<PlanningHistory> History, IReadOnlyList<PlanningSource> Sources);
public sealed record WipEdit(long Version, WipValues Values, string Reason, string? UsdEvidence = null);
public sealed record ManualTestRequest(long Version, string OriginalTest, string Equivalence, string Laboratory, string Reason);
public sealed record IncidentDecision(long Version, string State, string Reason);
public sealed record EquivalenceDecision(string OriginalTest, string? Equivalence, string? Laboratory, bool NoProgram, long? Version, string Reason);
public sealed record PlanningEquivalence(string OriginalTest, string? Equivalence, string? Laboratory, bool NoProgram, long Version);
public sealed record ImportCut(string Window, DateOnly Day);
public sealed record ImportIssue(string File, int Row, string Code, string Message, bool Blocking = false);
public sealed record ImportReview(string Category, string Job, Guid? EpisodeId, string? OriginalTest, string Description, int SourceRow = 0, Guid? BatchId = null)
{
    [System.Text.Json.Serialization.JsonIgnore] public string CategoryLabel => PlanningLabels.Category(Category);
}
public sealed record ImportChange(string Entity, string Action, string Job, Guid? EpisodeId, string? OriginalTest, string Before, string After);
public sealed record FileCoverage(string File, string Name, string Sha256, int Rows, DateOnly? Earliest, DateOnly? Latest, string CoverageBasis);
public sealed record PlanningImportPreview(Guid Id, string Status, long BaseRevision, ImportCut Cut,
    IReadOnlyList<FileCoverage> Files, IReadOnlyList<ImportChange> Changes,
    IReadOnlyList<ImportReview> Reviews, IReadOnlyList<ImportIssue> Issues, DateTimeOffset CreatedAt,
    int NewJobs, int NewTests, int UpdatedJobs, int LinkedManualTests);
public sealed record PlanningImportResult(Guid Id, string Status, int NewJobs, int NewTests, int UpdatedJobs, int LinkedManualTests, bool AlreadyApplied);
public sealed record PlanningBatch(Guid Id, string Status, string SummaryName, string ReportName, string Window, DateOnly Day, DateTimeOffset CreatedAt, DateTimeOffset? AppliedAt);
public sealed record SourceResolution(Guid BatchId, int ReportRow, Guid EpisodeId, Guid? ExecutionId, bool CreateExecution, long Version, string Reason);
public sealed record PlanningAccessUser(int Id, string Name, string Department, IReadOnlyList<string> Permissions);
public sealed record PlanningAccessChange(IReadOnlyList<string> Permissions, string Reason);
public sealed record LegacyEquivalence(long SourceId,string OriginalTest,string? Equivalence,string? Laboratory,bool NoProgram);
public sealed record LegacyEquivalencePreview(string Fingerprint,IReadOnlyList<LegacyEquivalence> Items,IReadOnlyList<string> Conflicts);
public sealed record LegacyEquivalenceConfirmation(string Fingerprint,string Reason);
public static class PlanningLabels
{
    public static string Category(string code) => code switch
    {
        "BothFiles" => "En ambos archivos", "SummaryWithoutReport" => "Summary sin Report List", "MissingTests" => "Sin pruebas asignadas",
        "MissingVendor" => "Sin Vendor", "MissingEquivalence" => "Sin equivalencia", "ManualManaged" => "Gestionados manualmente",
        "ExecutionIdentity" => "Identidad por conciliar", "ReportWithoutSummary" => "Report List sin Summary", "ExternalClosed" => "Close Date externo", "Episode" => "Revisar episodio", _ => code
    };
    public static string State(string code) => code switch { "Preview" => "En revisión", "Applied" => "Confirmado", "Pending" => "Pendiente", "Held" => "Retenido", "Resolved" => "Resuelto", _ => code };
    public static string Permission(string code) => code switch { PlanningPermissions.View => "Consultar Planificación", PlanningPermissions.Import => "Revisar y confirmar importaciones", PlanningPermissions.EditWip => "Editar WIP y retener incidencias", PlanningPermissions.ManualTests => "Agregar pruebas y conciliar identidades", PlanningPermissions.Equivalences => "Administrar equivalencias y NO PROGRAMAR", _ => code };
}

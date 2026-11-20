namespace Lims.Contracts.Planning;

public static class PlanningPermissions
{
    public const string View = "planning.view";
    public const string Import = "planning.import";
    public const string EditWip = "planning.wip.edit";
    public const string ManualTests = "planning.tests.manual";
    public const string Equivalences = "planning.equivalences.manage";
    public const string ReportMark = "planning.report.mark";
    public const string ReportReview = "planning.report.review";
    public static IReadOnlyList<string> All { get; } = [View, Import, EditWip, ManualTests, Equivalences, ReportMark, ReportReview];
}

public sealed record WorkspaceAccess(bool Laboratory, bool Planning, bool Administration, IReadOnlyList<string> Permissions, string? Notice = null);
public sealed record PlanningQuery(string? Search = null, string? Test = null, string? Laboratory = null,
    string? Service = null, string? State = null, DateOnly? From = null, DateOnly? Until = null,
    string Sort = "job", bool Descending = false, int Page = 1, int PageSize = 100,
    string? Buyer = null, string? Vendor = null, string? Equivalence = null, string? Incident = null,
    string SearchMode = "Contains", DateOnly? InFrom = null, DateOnly? InUntil = null, bool Overdue = false,
    string Scope = "History", bool? OnHold = null, bool? Tagged = null, string? Label = null, string? ColumnFilters = null,Guid? BatchId=null);
public sealed record PlanningRow(Guid EpisodeId, Guid? ExecutionId, string Job, DateOnly? InDate, DateOnly? DueDate,
    string? OriginalTest, string? Equivalence, string? Laboratory, string? Service, DateTimeOffset? DigitizedAt,
    DateOnly? TestedOn, string DigitizationState, string? DigitizedBy, string? ExecutionType, string Origin,
    string? Buyer, string? Vendor, bool HasIncidents, string? Responsible = null, string? Observation = null, long NoteVersion = 0,
    bool OnHold = false, string? JobLabel = null, string? HoldReason = null, long ControlVersion = 0, decimal? Usd = null,bool NoProgram=false,string? SampleStatus=null,string? OfficialJobStatus=null)
{
    public string PlanningTest => ExecutionId is null ? "WIP sin pruebas" : NoProgram ? "NO PROGRAMAR" : string.IsNullOrWhiteSpace(Equivalence) ? "Sin equivalencia" : Equivalence;
    public string JobState => SampleStatus ?? OfficialJobStatus ?? "No informado";
    public bool Complete => ExecutionId is not null && DigitizedAt is not null && TestedOn is not null;
    public bool OperationalPending => ExecutionId is not null && !OnHold && !Complete;
    [System.Text.Json.Serialization.JsonIgnore] public string OriginLabel => Origin == "ReportList" ? "Report List" : Origin == "Manual" ? "Manual" : "Summary";
    public override string ToString() => $"{Job} · {PlanningTest} · {DigitizationState}";
}
public sealed record PlanningCounts(int Jobs,int Executions,int Digitized,int Pending,int Unknown,
    int Records = 0,int Overdue = 0,int OnHoldTests = 0,int OnHoldJobs = 0,int TaggedJobs = 0);
public sealed record PlanningScopeInfo(string Mode,DateOnly? DueFrom,DateOnly Today,int MissingTests,int MissingDueDates,
    int MissingDigitization,string Notice);
public sealed record PlanningPage(IReadOnlyList<PlanningRow> Items, int Total, int Page, int PageSize, PlanningCounts? Counts = null,PlanningScopeInfo? Scope = null);
public sealed record PlanningJobControl(string Job,bool OnHold,string? Label,string? HoldReason,long Version,DateTimeOffset? UpdatedAt=null,int? UpdatedBy=null);
public sealed record PlanningControlEdit(long Version,bool OnHold,string? Label,string Reason);
public sealed record PlanningNoteCell(Guid ExecutionId,long Version,string Column,string? Value);
public sealed record PlanningNoteBatch(IReadOnlyList<PlanningNoteCell> Cells,string Reason);
public sealed record PlanningNoteSaved(Guid ExecutionId,string? Responsible,string? Observation,long Version);
public sealed record WipValues(string? Buyer, string? Vendor, string? Service, DateOnly? InDate, DateOnly? DueDate,
    decimal? Amount, string? Currency, decimal? Usd);
public sealed record PlanningIncident(Guid Id, Guid EpisodeId, string Kind, string State, string? Reason, long Version);
public sealed record PlanningHistory(Guid Id, string Action, int ActorId, DateTimeOffset At, string Reason, string Details,string? ActorName=null);
public sealed record PlanningSource(Guid BatchId, string File, string Sheet, int Row, string Sha256, DateTimeOffset ImportedAt, string Fields);
public sealed record PlanningOfficialState(string? SampleStatus=null,string? JobStatus=null,DateOnly? CloseDate=null,decimal? ExchangeRate=null,string? CustomerStatus=null);
public sealed record JobDossier(Guid EpisodeId, string Job, WipValues Wip, long Version,
    IReadOnlyList<PlanningRow> Tests, IReadOnlyList<PlanningIncident> Incidents,
    IReadOnlyList<PlanningHistory> History, IReadOnlyList<PlanningSource> Sources,PlanningJobControl? Control=null,PlanningOfficialState? Official=null);
public sealed record WipEdit(long Version, WipValues Values, string Reason, string? UsdEvidence = null);
public sealed record ManualTestRequest(long Version, string OriginalTest, string Equivalence, string Laboratory, string Reason,long? CatalogueVersion=null);
public sealed record IncidentDecision(long Version, string State, string Reason);
public sealed record EquivalenceDecision(string OriginalTest, string? Equivalence, string? Laboratory, bool NoProgram, long? Version, string Reason);
public sealed record PlanningEquivalence(string OriginalTest, string? Equivalence, string? Laboratory, bool NoProgram, long Version);
public sealed record ImportCut(string Window, DateOnly Day);
public sealed record ImportIssue(string File, int Row, string Code, string Message, bool Blocking = false);
public sealed record ImportReview(string Category, string Job, Guid? EpisodeId, string? OriginalTest, string Description, int SourceRow = 0, Guid? BatchId = null)
{
    [System.Text.Json.Serialization.JsonIgnore] public string CategoryLabel => PlanningLabels.Category(Category);
}
public sealed record PlanningReviewPage(IReadOnlyList<ImportReview> Items,int Total,int Page,int PageSize);
public sealed record ImportChange(string Entity, string Action, string Job, Guid? EpisodeId, string? OriginalTest, string Before, string After);
public sealed record FileCoverage(string File, string Name, string Sha256, int Rows, DateOnly? Earliest, DateOnly? Latest, string CoverageBasis);
public sealed record PlanningImportPreview(Guid Id, string Status, long BaseRevision, ImportCut Cut,
    IReadOnlyList<FileCoverage> Files, IReadOnlyList<ImportChange> Changes,
    IReadOnlyList<ImportReview> Reviews, IReadOnlyList<ImportIssue> Issues, DateTimeOffset CreatedAt,
    int NewJobs, int NewTests, int UpdatedJobs, int LinkedManualTests,int UnchangedJobs=0,int AmbiguousRows=0,int PendingReview=0);
public sealed record PlanningImportResult(Guid Id, string Status, int NewJobs, int NewTests, int UpdatedJobs, int LinkedManualTests, bool AlreadyApplied);
public sealed record PlanningBatch(Guid Id, string Status, string SummaryName, string ReportName, string Window, DateOnly Day, DateTimeOffset CreatedAt, DateTimeOffset? AppliedAt,
    int ActorId=0,string? ActorName=null,int NewJobs=0,int NewTests=0,int UpdatedJobs=0,int WipChanges=0,int ReviewCases=0);
public sealed record PlanningReviewQuery(string Category="SummaryWithoutReport",string Situation="Open",string? Search=null,
    string Sort="job",bool Descending=false,int Page=1,int PageSize=100,string? ColumnFilters=null,
    string Scope="History",Guid? BatchId=null,DateOnly? ImportedFrom=null,DateOnly? ImportedUntil=null,string? Test=null,string? Laboratory=null,string? Origin=null,string? SelectedKeys=null,string Format="csv");
public sealed record PlanningReviewCounter(string Category,int Total,int Pending,int Held,int Resolved,int Excluded=0);
public sealed record PlanningReviewCase(string Key,string Category,Guid? EpisodeId,Guid? IncidentId,Guid? ExecutionId,
    string Job,DateOnly? InDate,DateOnly? DueDate,string? OfficialState,string? Buyer,string? Vendor,string? Service,decimal? Usd,
    string State,string? Reason,long Version,string? OriginalTest,string? Equivalence,string? Laboratory,Guid? BatchId,int SourceRow,
    string? DecidedBy,DateTimeOffset? DecidedAt,string? Origin=null,DateTimeOffset? ImportedAt=null)
{
    public string StateLabel=>PlanningLabels.State(State);
    public string InDateText=>InDate?.ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture)??"No informado";
    public string DueDateText=>DueDate?.ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture)??"No informado";
    public string OfficialStateText=>OfficialState??"No informado";
    public string BuyerText=>Buyer??"No informado";
    public string VendorText=>Vendor??"No informado";
    public string ServiceText=>Service??"No informado";
    public string UsdText=>Usd?.ToString("N2",System.Globalization.CultureInfo.InvariantCulture)??"No informado";
    public string ActionLabel=>EpisodeId is not null?"Abrir expediente":"Consultar lote";
}
public sealed record PlanningReviewWorkspace(IReadOnlyList<PlanningReviewCase> Items,int Total,int Page,int PageSize,
    IReadOnlyList<PlanningReviewCounter> Counters,string ScopeNotice);
public sealed record PlanningReviewDecision(string Key,string Category,long Version,string State,string Reason);
public sealed record PlanningImportInspectionRow(string Category,string Job,Guid? EpisodeId,string? OriginalTest,string? Equivalence,
    string? Laboratory,string? Service,DateOnly? InDate,DateOnly? DueDate,string? Buyer,string? Vendor,decimal? Usd,string? OfficialState,
    string Explanation,int SourceRow=0,int RelatedJobs=1,bool NoProgram=false)
{
    public string InDateText=>InDate?.ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture)??"No informado";
    public string DueDateText=>DueDate?.ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture)??"No informado";
}
public sealed record PlanningLaboratoryCount(string Laboratory,int NewTests,int ObservedTests,int NoProgram);
public sealed record PlanningImportInspection(PlanningImportPreview Preview,IReadOnlyList<PlanningImportInspectionRow> Items,
    int NewCombinations,int ExistingTests,int NewEmergencies,int ProcessedRows,int PendingEquivalences,
    int JobsBefore,int TestsBefore,int JobsAfter,int TestsAfter,string? ActorName,DateTimeOffset? AppliedAt,
    IReadOnlyList<PlanningLaboratoryCount> Laboratories);
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
    public static string State(string code) => code switch { "Preview" => "En revisión", "Applied" => "Confirmado", "Pending" => "Pendiente", "Held" => "Retenido", "Resolved" => "Resuelto", "Excluded"=>"Excluido justificadamente", _ => code };
    public static string Permission(string code) => code switch { PlanningPermissions.ReportMark => "Marcar Report List y escanear", PlanningPermissions.ReportReview => "Corregir marcados y permanencia", PlanningPermissions.View => "Consultar Planificación", PlanningPermissions.Import => "Revisar y confirmar importaciones", PlanningPermissions.EditWip => "Editar WIP y retener incidencias", PlanningPermissions.ManualTests => "Agregar pruebas y conciliar identidades", PlanningPermissions.Equivalences => "Administrar equivalencias y NO PROGRAMAR", _ => code };
}

using Lims.Contracts.Planning;

namespace Lims.Application.Planning;

public static class PlanningLimits
{
    public const int MaxFileBytes = 5 * 1024 * 1024;
    public const string ReaderProfile = "lims-html-react-v1";
}
public sealed record PlanningSourceRow(string File, string Sheet, int Row, string Job, string? Test, WipValues? Wip, string Fields,PlanningOfficialState? Official=null,string? SourceLaboratory=null,string? SourceEquivalence=null,long? ReactRowId=null);
public sealed record PlanningImportInput(ImportCut Cut, byte[] Summary, byte[] Report, string SummaryName, string ReportName,
    string Fingerprint, IReadOnlyList<FileCoverage> Files, IReadOnlyList<PlanningSourceRow> Rows, IReadOnlyList<ImportIssue> Issues,string? SourceSnapshotToken=null);
public sealed record StoredEpisode(Guid Id, string Job, WipValues Wip, long Version, IReadOnlyList<string> ManualFields, DateOnly? SourceInDate = null,PlanningOfficialState? Official=null,bool HasSummarySource=false);
public sealed record StoredExecution(Guid Id, Guid EpisodeId, string OriginalTest, string? Equivalence, string? Laboratory, string Origin,string? ExecutionType=null,bool NoProgram=false,bool HasReportSource=false);
public sealed record StoredSourceIdentity(string ReportHash,int Row,string Sheet,Guid ExecutionId);
public sealed record ProposedEpisode(Guid Id, string Job, WipValues Source, bool IsNew, int SourceRow,PlanningOfficialState? Official=null,bool HasSummary=true);
public sealed record ProposedExecution(Guid Id, Guid EpisodeId, string Job, string OriginalTest, string? Equivalence,
    string? Laboratory, bool NoProgram, string Action, IReadOnlyList<int> SourceRows,string? Rule=null,Guid? ParentExecutionId=null,string Origin="ReportList",long? ReactRowId=null);
public sealed record PlanningPlan(IReadOnlyList<ProposedEpisode> Episodes, IReadOnlyList<ProposedExecution> Executions,
    IReadOnlyList<ImportChange> Changes, IReadOnlyList<ImportReview> Reviews, IReadOnlyList<ImportIssue> Issues,
    IReadOnlyList<PlanningAdmission>? Admissions=null,string? ReactStateToken=null);
public sealed class PlanningConflictException(string message) : Exception(message);
public sealed class PlanningValidationException(string message) : Exception(message);

public interface IPlanningStore
{
    Task<ReportListPage> ReportListAsync(ReportListQuery query,CancellationToken ct);
    Task<ReportListResult> DigitizeReportAsync(ReportDigitization request,int actor,bool administrator,CancellationToken ct);
    Task<ReportSchedulePage> ReportSchedulesAsync(ReportScheduleQuery query,CancellationToken ct);
    Task<ReportScheduleDetail> ReportScheduleAsync(Guid id,CancellationToken ct,bool groupByClock=false);
    Task<ReportSchedule> ReviewReportScheduleAsync(ReportScheduleReview request,int actor,CancellationToken ct);
    Task<ReportClosurePage> ReportClosuresAsync(ReportScheduleQuery query,CancellationToken ct);
    Task<ReportClosurePage> ClosureWorkspaceAsync(ReportClosureQuery query,CancellationToken ct)=>throw new NotSupportedException();
    Task<ReportClosureDetail> ClosureDetailAsync(string job,string fingerprint,CancellationToken ct)=>throw new NotSupportedException();
    Task<ReportClosure> SaveClosureFollowUpAsync(ReportClosureFollowUp request,int actor,CancellationToken ct)=>throw new NotSupportedException();
    Task<byte[]> ExportClosuresAsync(ReportClosureQuery query,CancellationToken ct)=>throw new NotSupportedException();
    Task<byte[]> ExportReviewedSchedulesAsync(ReportScheduleQuery query,CancellationToken ct);
    Task<ReportListResult> CheckReportAsync(ReportListCheck request,int actor,bool administrator,CancellationToken ct);
    Task KeepReportAsync(ReportListKeep request,int actor,CancellationToken ct);
    Task<ReportListResult> ScanReportAsync(ReportListScan request,int actor,CancellationToken ct);
    Task<PlanningPage> QueryAsync(PlanningQuery query, CancellationToken ct);
    Task<byte[]> ExportAsync(PlanningQuery query, CancellationToken ct);
    Task<JobDossier?> DossierAsync(Guid episodeId, CancellationToken ct);
    Task<PlanningJobControl> SaveControlAsync(Guid episodeId,PlanningControlEdit request,int actor,CancellationToken ct);
    Task<IReadOnlyList<PlanningNoteSaved>> EditNotesAsync(PlanningNoteBatch request,int actor,CancellationToken ct);
    Task<PlanningImportPreview> PreviewAsync(PlanningImportInput input, int actor, CancellationToken ct);
    Task<PlanningImportPreview> PreviewReactTablesAsync(int actor,DateOnly day,CancellationToken ct)=>throw new NotSupportedException("El almacén no admite conciliación de tablas React.");
    Task<PlanningImportResult> ConfirmAsync(Guid id, int actor, CancellationToken ct);
    Task<IReadOnlyList<PlanningBatch>> BatchesAsync(CancellationToken ct);
    Task<IReadOnlyList<ImportReview>> ReviewsAsync(CancellationToken ct,string? category=null);
    Task<PlanningReviewPage> ReviewPageAsync(string? category,string? search,int page,int pageSize,CancellationToken ct);
    Task<PlanningReviewWorkspace> ReviewWorkspaceAsync(PlanningReviewQuery query,CancellationToken ct);
    Task<byte[]> ExportReviewsAsync(PlanningReviewQuery query,CancellationToken ct);
    Task<PlanningImportInspection?> InspectImportAsync(Guid id,CancellationToken ct);
    Task DecideReviewAsync(PlanningReviewDecision request,int actor,CancellationToken ct);
    Task<PlanningImportPreview?> BatchPreviewAsync(Guid id, CancellationToken ct);
    Task EditWipAsync(Guid id, WipEdit request, int actor, CancellationToken ct);
    Task AddManualTestAsync(Guid id, ManualTestRequest request, int actor, CancellationToken ct);
    Task DecideIncidentAsync(Guid id, IncidentDecision request, int actor, CancellationToken ct);
    Task<IReadOnlyList<PlanningEquivalence>> EquivalencesAsync(CancellationToken ct);
    Task SaveEquivalenceAsync(EquivalenceDecision request, int actor, CancellationToken ct);
    Task<LegacyEquivalencePreview> LegacyEquivalencesAsync(CancellationToken ct);
    Task ImportLegacyEquivalencesAsync(LegacyEquivalenceConfirmation request,int actor,CancellationToken ct);
    Task ResolveSourceAsync(SourceResolution request, int actor, CancellationToken ct);
    Task<IReadOnlyList<PlanningAccessUser>> AccessUsersAsync(CancellationToken ct);
    Task SaveAccessAsync(int userId, PlanningAccessChange request, int actor, CancellationToken ct);
}

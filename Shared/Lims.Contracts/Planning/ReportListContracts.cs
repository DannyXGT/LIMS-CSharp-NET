namespace Lims.Contracts.Planning;

public sealed record ReportListQuery(string? Search=null,string View="Pending",string? Laboratory=null,
    string? Test=null,string? Service=null,string? Buyer=null,int Page=1,int PageSize=100,
    string Sort="Default",bool Descending=false,string? VerifiedBy=null,long? Block=null,DateOnly? CheckedDate=null,long? Revision=null);
public sealed record ReportListItem(PlanningRow Execution,long Version,bool KeepInPlanning,long? BlockNumber,string CheckMode)
{
    public bool Archived => Execution.Complete && !KeepInPlanning;
    public string CheckedAtText => Execution.DigitizedAt?.ToOffset(TimeSpan.FromHours(-6)).ToString("yyyy-MM-dd HH:mm:ss",System.Globalization.CultureInfo.InvariantCulture)??"";
    public string CheckedDateText => Execution.DigitizedAt?.ToOffset(TimeSpan.FromHours(-6)).ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture)??"";
    public string CheckedTimeText => Execution.DigitizedAt?.ToOffset(TimeSpan.FromHours(-6)).ToString("HH:mm:ss",System.Globalization.CultureInfo.InvariantCulture)??"";
    public string BlockLabel => BlockNumber is {} n ? $"Bloque {n}" : "";
    public string KeepLabel => KeepInPlanning ? "Sí" : "No";
    public string ArchivedLabel => Archived ? "Sí" : "No";
    public override string ToString() => $"{Execution.Job} · {Execution.PlanningTest} · {Execution.DigitizationState} · {Execution.ExecutionId}";
}
public sealed record ReportListCapabilities(bool ScannerAvailable,string? ScannerSource);
public sealed record ReportListPage(IReadOnlyList<ReportListItem> Items,int Total,int Page,int PageSize,long Revision=0,ReportListCapabilities? Capabilities=null);
public sealed record ReportExecutionVersion(Guid ExecutionId,long Version);
public sealed record ReportListCheck(IReadOnlyList<ReportExecutionVersion> Executions,bool Checked,string Reason,bool AdminOverride=false,DateTimeOffset? CheckedAt=null);
public sealed record ReportListKeep(ReportExecutionVersion Execution,bool KeepInPlanning,string Reason);
public sealed record ReportListScan(string Barcode,string Reason,DateTimeOffset? CheckedAt=null);
public sealed record ReportListResult(IReadOnlyList<Guid> ExecutionIds,IReadOnlyList<string> ReadyJobs);

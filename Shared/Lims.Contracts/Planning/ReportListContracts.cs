namespace Lims.Contracts.Planning;

public sealed record ReportListQuery(string? Search=null,string View="Pending",string? Laboratory=null,
    string? Test=null,string? Service=null,string? Buyer=null,int Page=1,int PageSize=100,
    string Sort="Default",bool Descending=false,string? VerifiedBy=null,long? Block=null,DateOnly? CheckedDate=null,long? Revision=null,string? ColumnFilters=null,Guid? ExecutionId=null,int? Offset=null);
public sealed record ReportListItem(PlanningRow Execution,long Version,bool KeepInPlanning,long? BlockNumber,string CheckMode,long KeepVersion=0)
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
public sealed record ReportListPage(IReadOnlyList<ReportListItem> Items,int Total,int Page,int PageSize,long Revision=0,ReportListCapabilities? Capabilities=null,long? DataRevision=null,long? CatalogueRevision=null);
public sealed record ReportExecutionVersion(Guid ExecutionId,long Version);
public sealed record ReportListCheck(IReadOnlyList<ReportExecutionVersion> Executions,bool Checked,string Reason,bool AdminOverride=false,DateTimeOffset? CheckedAt=null);
public sealed record ReportListKeep(ReportExecutionVersion Execution,bool KeepInPlanning,string Reason,long? KeepVersion=null);
public sealed record ReportListScan(string Barcode,string Reason,DateTimeOffset? CheckedAt=null);
public sealed record ReportListResult(IReadOnlyList<Guid> ExecutionIds,IReadOnlyList<string> ReadyJobs);
public sealed record ReportDigitization(Guid SessionId,IReadOnlyList<ReportExecutionVersion> Executions,DateTimeOffset OperationalAt,string Reason,bool AdminOverride=false);
public sealed record ReportScheduleQuery(string? Search=null,string State="All",int Page=1,int PageSize=100,long? Revision=null,bool GroupByClock=false);
public sealed record ReportSchedule(Guid Id,DateTimeOffset OperationalAt,DateTimeOffset SavedAt,string DigitizedBy,int Tests,int Jobs,long Version,string State,string? ReviewedBy,DateTimeOffset? ReviewedAt,int Exceptions,IReadOnlyList<Guid>? SessionIds=null)
{
    public string DateText=>OperationalAt.ToOffset(TimeSpan.FromHours(-6)).ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture);
    public string TimeText=>OperationalAt.ToOffset(TimeSpan.FromHours(-6)).ToString("HH:mm",System.Globalization.CultureInfo.InvariantCulture);
    public override string ToString()=>$"{DateText} · {TimeText} · {Tests} pruebas · {Jobs} JOBs · {DigitizedBy} · {State}";
}
public sealed record ReportSchedulePage(IReadOnlyList<ReportSchedule> Items,int Total,int Page,int PageSize,long Revision);
public sealed record ReportScheduleMember(ReportListItem Item,long DigitizationVersion,bool Changed);
public sealed record ReportScheduleDetail(ReportSchedule Schedule,IReadOnlyList<ReportScheduleMember> Members,IReadOnlyList<PlanningHistory> History,IReadOnlyList<ReportSchedule>? Sessions=null);
public sealed record ReportSessionVersion(Guid SessionId,long Version);
public sealed record ReportScheduleReview(Guid SessionId,long Version,IReadOnlyList<ReportExecutionVersion> Executions,string Reason,IReadOnlyList<ReportSessionVersion>? ClockSessions=null);
public sealed record ReportClosure(string Job,int Required,int Digitized,DateTimeOffset LastDigitizedAt,DateOnly? DueDate)
{
    public override string ToString()=>$"{Job} · {Digitized}/{Required} pruebas digitadas · {LastDigitizedAt.ToOffset(TimeSpan.FromHours(-6)):yyyy-MM-dd HH:mm}";
}
public sealed record ReportClosurePage(IReadOnlyList<ReportClosure> Items,int Total,int Page,int PageSize,long Revision,string Coverage);

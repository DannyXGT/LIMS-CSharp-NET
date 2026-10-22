namespace Lims.Contracts.Alerts;

[Flags]
public enum AlertModule { None = 0, Standards = 1, Stock = 2, Intermedia = 4, Curva = 8, AQS = 16 }
public enum AlertPriority { Information, Warning, High, Critical }
public sealed record AlertItem(Guid Id, Guid SourceId, string SourceType, AlertModule Modules, int DepartmentId,
    string Material, string Code, string Lot, string MaterialType, string Category, string Condition,
    AlertPriority Priority, string Title, string Description, string Recommendation, decimal? AvailabilityPercent,
    decimal Available, string Unit, DateOnly Expiration, DateTimeOffset DetectedAt, DateTimeOffset UpdatedAt,
    DateTimeOffset? ResolvedAt, DateTimeOffset? AcknowledgedAt, int Revision, Guid? NotificationId = null, DateTimeOffset? ReadAt = null);
public sealed record AlertChange(Guid Id, string Action, string Condition, DateTimeOffset At, int? ActorUserId, string? Note);
public sealed record AlertDetail(AlertItem Alert, IReadOnlyList<AlertChange> History);
public sealed record AlertSummary(int Active, int Critical, int Upcoming, DateOnly? NearestExpiration, int Expired,
    int Pending, int Unread, DateTimeOffset? LastEvaluation, DateTimeOffset ServerTime, AlertMetrics? Metrics = null);
public sealed record AlertMetrics(int Depleted, int CriticalAvailability, int ExpiredPending,
    string? NearestMaterial, Guid? NearestAlertId, int? NearestDays);
public sealed record AlertPage(IReadOnlyList<AlertItem> Items, int Page, int PageSize, int Total, AlertSummary Summary);
public sealed record AlertFilter(string? Search = null, string? Category = null, AlertPriority? Priority = null,
    AlertModule? Module = null, string? State = "Active", DateOnly? From = null, DateOnly? Until = null,
    string? Sort = "Priority", int Page = 1, int PageSize = 30);
public sealed record NotificationItem(Guid Id, Guid AlertId, string Title, string Message, AlertPriority Priority,
    DateTimeOffset CreatedAt, DateTimeOffset? ReadAt, bool WindowsClaimed);
public sealed record NotificationPage(IReadOnlyList<NotificationItem> Items, int Unread, NotificationPreferences Preferences,
    bool WindowsEnabled, DateTimeOffset ServerTime);
public sealed record NotificationPreferences(bool WindowsEnabled = false, bool EmailEnabled = true, bool OnlyCriticalWindows = true);
public sealed record AlertScope(AlertModule Module, int DepartmentId);
public sealed record AlertRecipient(int UserId, int DepartmentId, bool Immediate = true, bool Daily = true);
public sealed record AlertSettings(int[] ExpirationDays, decimal LowPercent, decimal CriticalPercent,
    bool InternalEnabled, bool WindowsEnabled, bool EmailEnabled, int DailyHour, int DailyMinute, string TimeZoneId,
    IReadOnlyList<AlertScope> Scopes, IReadOnlyList<AlertRecipient> Recipients, Guid Version)
{
    public static AlertSettings Default => new([30, 15, 7, 1], 25, 10, true, true, false, 7, 0,
        "America/Guatemala", [], [], Guid.Empty);
}
public sealed record AlertDepartment(int Id, string Name);
public sealed record AlertUser(int Id, string Name, string Email, int? DepartmentId, bool Administrator);
public sealed record MailServiceStatus(bool Enabled, bool Configured, string Host, int Port, string Sender,
    string Authentication, string TransportSecurity, string Status);
public sealed record AlertAdministration(AlertSettings Settings, IReadOnlyList<AlertDepartment> Departments,
    IReadOnlyList<AlertUser> Users, MailServiceStatus Mail);
public sealed record DeliveryItem(Guid Id, int UserId, string Recipient, string Kind, string State, int Attempts,
    DateTimeOffset CreatedAt, DateTimeOffset? SentAt, DateTimeOffset NextAttemptAt, string? LastError);
public sealed record DeliveryPage(IReadOnlyList<DeliveryItem> Items, int Page, int Total, string? MailState = null);
public sealed record AlertAction(Guid Version, string? Note);
public sealed record MailTestRequest(Guid RequestId);
public sealed record AlertJobResult(bool Acquired, int Evaluated, int Changed, int Sent);

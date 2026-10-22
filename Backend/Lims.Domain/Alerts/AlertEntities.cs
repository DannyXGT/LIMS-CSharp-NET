namespace Lims.Domain.Alerts;

public sealed class MaterialAlert
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SourceId { get; set; }
    public string SourceType { get; set; } = "";
    public int Modules { get; set; }
    public int DepartmentId { get; set; }
    public string Material { get; set; } = "";
    public string SearchText { get; set; } = "";
    public string Code { get; set; } = "";
    public string Lot { get; set; } = "";
    public string MaterialType { get; set; } = "";
    public string Category { get; set; } = "";
    public string Condition { get; set; } = "";
    public int Priority { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Recommendation { get; set; } = "";
    public decimal? AvailabilityPercent { get; set; }
    public decimal Available { get; set; }
    public string Unit { get; set; } = "";
    public DateOnly Expiration { get; set; }
    public DateTimeOffset DetectedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public DateTimeOffset? AcknowledgedAt { get; set; }
    public int? AcknowledgedBy { get; set; }
    public int Revision { get; set; } = 1;
}
public sealed class AlertHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AlertId { get; set; }
    public string Action { get; set; } = "";
    public string Condition { get; set; } = "";
    public DateTimeOffset At { get; set; }
    public int? ActorUserId { get; set; }
    public string? Note { get; set; }
}
public sealed class UserNotification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AlertId { get; set; }
    public MaterialAlert Alert { get; set; } = null!;
    public int Revision { get; set; }
    public int UserId { get; set; }
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public int Priority { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
    public DateTimeOffset? WindowsClaimedAt { get; set; }
}
public sealed class AlertConfiguration
{
    public int Id { get; set; } = 1;
    public string Json { get; set; } = "";
    public Guid Version { get; set; }
    public int? ActorUserId { get; set; }
    public DateTimeOffset? ChangedAt { get; set; }
    public DateTimeOffset? LastEvaluation { get; set; }
}
public sealed class NotificationPreference
{
    public int UserId { get; set; }
    public bool WindowsEnabled { get; set; }
    public bool EmailEnabled { get; set; } = true;
    public bool OnlyCriticalWindows { get; set; } = true;
}
public sealed class EmailDelivery
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string DeduplicationKey { get; set; } = "";
    public Guid? AlertId { get; set; }
    public int? AlertRevision { get; set; }
    public int DepartmentId { get; set; }
    public int UserId { get; set; }
    public string Recipient { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Html { get; set; } = "";
    public string State { get; set; } = "Pending";
    public int Attempts { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public string? LastError { get; set; }
}
public sealed class EmailAttempt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DeliveryId { get; set; }
    public int Number { get; set; }
    public DateTimeOffset At { get; set; }
    public string Outcome { get; set; } = "";
    public string? Error { get; set; }
}
public sealed class AlertConfigurationAudit
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int ActorUserId { get; set; }
    public DateTimeOffset At { get; set; }
    public string Action { get; set; } = "";
    public string Json { get; set; } = "";
}

namespace Lims.Domain.Identity;

public sealed class UserPermissionSet
{
    public int UserId { get; set; }
    public Guid Version { get; set; }
}
public sealed class PermissionCode
{
    public string Code { get; set; } = string.Empty;
}
public sealed class UserPermissionAssignment
{
    public int UserId { get; set; }
    public string Code { get; set; } = string.Empty;
    public bool IsGranted { get; set; }
    public int ActorUserId { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
}
public sealed class UserPermissionAudit
{
    public Guid Id { get; set; }
    public int UserId { get; set; }
    public string Code { get; set; } = string.Empty;
    public int ActorUserId { get; set; }
    public bool Before { get; set; }
    public bool After { get; set; }
    public DateTimeOffset At { get; set; }
}

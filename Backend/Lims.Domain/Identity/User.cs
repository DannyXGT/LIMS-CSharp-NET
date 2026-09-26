namespace Lims.Domain.Identity;

public sealed class User
{
    private readonly List<UserPermissionGrant> _permissions = [];

    private User()
    {
    }

    public User(
        int id,
        string name,
        string email,
        string passwordHash,
        bool isActive,
        Role? role,
        Department? department,
        IEnumerable<UserPermissionGrant>? permissions = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);

        Id = id;
        Name = RequireText(name, nameof(name), 100);
        Email = RequireText(email, nameof(email), 150).ToLowerInvariant();
        PasswordHash = RequireText(passwordHash, nameof(passwordHash), 512);
        IsActive = isActive;
        Role = role;
        RoleId = role?.Id;
        Department = department;
        DepartmentId = department?.Id;

        if (permissions is not null)
        {
            _permissions.AddRange(permissions);
        }
    }

    public int Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Email { get; private set; } = string.Empty;

    public string PasswordHash { get; private set; } = string.Empty;

    public int? RoleId { get; private set; }

    public Role? Role { get; private set; }

    public int? DepartmentId { get; private set; }

    public Department? Department { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyCollection<UserPermissionGrant> Permissions => _permissions;

    public void ReplacePasswordHash(string passwordHash)
    {
        PasswordHash = RequireText(passwordHash, nameof(passwordHash), 512);
    }

    private static string RequireText(string value, string parameterName, int maximumLength)
    {
        var normalized = value.Trim();
        if (normalized.Length == 0 || normalized.Length > maximumLength)
        {
            throw new ArgumentException($"Value must contain between 1 and {maximumLength} characters.", parameterName);
        }

        return normalized;
    }
}

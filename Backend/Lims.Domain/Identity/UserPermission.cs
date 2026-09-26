namespace Lims.Domain.Identity;

public sealed class UserPermissionGrant
{
    public const int MaximumKeyLength = 120;

    private UserPermissionGrant()
    {
    }

    public UserPermissionGrant(int userId, string key)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(userId);

        UserId = userId;
        Key = NormalizeKey(key);
    }

    public int UserId { get; private set; }

    public string Key { get; private set; } = string.Empty;

    public static string NormalizeKey(string key)
    {
        var normalized = key.Trim();
        if (normalized.Length is 0 or > MaximumKeyLength)
        {
            throw new ArgumentException(
                $"Permission key must contain between 1 and {MaximumKeyLength} characters.",
                nameof(key));
        }

        return normalized;
    }
}

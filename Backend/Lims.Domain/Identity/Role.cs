namespace Lims.Domain.Identity;

public sealed class Role
{
    private Role()
    {
    }

    public Role(int id, string name)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);

        Id = id;
        Name = RequireName(name, nameof(name), 50);
    }

    public int Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    private static string RequireName(string value, string parameterName, int maximumLength)
    {
        var normalized = value.Trim();
        if (normalized.Length == 0 || normalized.Length > maximumLength)
        {
            throw new ArgumentException($"Value must contain between 1 and {maximumLength} characters.", parameterName);
        }

        return normalized;
    }
}

namespace Lims.Domain.Identity;

public sealed class Department
{
    private Department()
    {
    }

    public Department(int id, string name)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);

        var normalized = name.Trim();
        if (normalized.Length is 0 or > 100)
        {
            throw new ArgumentException("Department name must contain between 1 and 100 characters.", nameof(name));
        }

        Id = id;
        Name = normalized;
    }

    public int Id { get; private set; }

    public string Name { get; private set; } = string.Empty;
}

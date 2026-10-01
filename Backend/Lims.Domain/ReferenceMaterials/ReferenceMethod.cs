namespace Lims.Domain.ReferenceMaterials;

public sealed class ReferenceMethod
{
    public const int NameMaximumLength = 120;

    private ReferenceMethod()
    {
    }

    public ReferenceMethod(int id, string name, bool isActive)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);
        Id = id;
        Name = RequireText(name, nameof(name), NameMaximumLength);
        IsActive = isActive;
    }

    public int Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }

    private static string RequireText(string value, string parameterName, int maximumLength)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is 0 || normalized.Length > maximumLength)
        {
            throw new ArgumentException($"Value must contain between 1 and {maximumLength} characters.", parameterName);
        }

        return normalized;
    }
}

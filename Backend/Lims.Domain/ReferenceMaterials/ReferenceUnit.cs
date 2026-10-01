namespace Lims.Domain.ReferenceMaterials;

public sealed class ReferenceUnit
{
    public const int NameMaximumLength = 80;
    public const int SymbolMaximumLength = 16;

    private ReferenceUnit()
    {
    }

    public ReferenceUnit(int id, string name, string symbol, bool isActive)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);
        Id = id;
        Name = RequireText(name, nameof(name), NameMaximumLength);
        Symbol = RequireText(symbol, nameof(symbol), SymbolMaximumLength);
        IsActive = isActive;
    }

    public int Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Symbol { get; private set; } = string.Empty;
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

namespace Lims.Application.Authentication;

public sealed class CorporateIdentityPolicy
{
    public const string ConfigurationSection = "Authentication:CorporateIdentity";

    public string AllowedDomain { get; init; } = "intertek.com";

    public bool AllowLocalPartOnly { get; init; } = true;

    public void Validate()
    {
        var domain = AllowedDomain.Trim();
        if (domain.Length is 0 or > 100 || domain.Contains('@', StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Authentication corporate domain configuration is invalid.");
        }
    }
}

using System.Text.RegularExpressions;

namespace Lims.Application.Authentication;

public sealed partial class UserIdentifierNormalizer(CorporateIdentityPolicy policy)
{
    private readonly CorporateIdentityPolicy _policy = Validate(policy);

    public IdentifierNormalizationResult Normalize(string? identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return IdentifierNormalizationResult.Invalid("El usuario es obligatorio.");
        }

        var input = identifier.Trim();
        if (input.Any(char.IsWhiteSpace))
        {
            return IdentifierNormalizationResult.Invalid("El usuario no puede contener espacios.");
        }

        var parts = input.Split('@');
        string localPart;
        if (parts.Length == 1 && _policy.AllowLocalPartOnly)
        {
            localPart = parts[0];
        }
        else if (parts.Length == 2 &&
                 string.Equals(parts[1], _policy.AllowedDomain, StringComparison.OrdinalIgnoreCase))
        {
            localPart = parts[0];
        }
        else
        {
            return IdentifierNormalizationResult.Invalid("El usuario debe pertenecer al dominio corporativo configurado.");
        }

        localPart = localPart.ToLowerInvariant();
        if (localPart.Length is 0 or > 64 || !LocalPartPattern().IsMatch(localPart))
        {
            return IdentifierNormalizationResult.Invalid("El formato del usuario no es válido.");
        }

        var email = $"{localPart}@{_policy.AllowedDomain.ToLowerInvariant()}";
        return email.Length <= 150
            ? IdentifierNormalizationResult.Valid(email)
            : IdentifierNormalizationResult.Invalid("El usuario excede la longitud permitida.");
    }

    private static CorporateIdentityPolicy Validate(CorporateIdentityPolicy value)
    {
        ArgumentNullException.ThrowIfNull(value);
        value.Validate();
        return value;
    }

    [GeneratedRegex("^[a-z0-9]+(?:[._-][a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex LocalPartPattern();
}

public sealed record IdentifierNormalizationResult(bool IsValid, string? Email, string? Error)
{
    public static IdentifierNormalizationResult Valid(string email) => new(true, email, null);

    public static IdentifierNormalizationResult Invalid(string error) => new(false, null, error);
}

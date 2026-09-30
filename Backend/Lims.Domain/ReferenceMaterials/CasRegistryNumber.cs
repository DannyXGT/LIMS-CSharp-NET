using System.Text.RegularExpressions;

namespace Lims.Domain.ReferenceMaterials;

public static partial class CasRegistryNumber
{
    public static bool IsValid(string value)
    {
        var match = Pattern().Match(value);
        if (!match.Success)
        {
            return false;
        }

        var body = string.Concat(match.Groups[1].Value, match.Groups[2].Value);
        var sum = 0;
        for (var index = body.Length - 1; index >= 0; index--)
        {
            sum += (body[index] - '0') * (body.Length - index);
        }

        return sum % 10 == match.Groups[3].Value[0] - '0';
    }

    [GeneratedRegex("^(\\d{1,7})-(\\d{2})-(\\d)$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}

using System.Globalization;
using Lims.Contracts.ReferenceMaterials;

namespace Lims.Desktop.Validation;

internal static class ReferenceMaterialInput
{
    public static bool TryParseDecimal(
        string value,
        int maximumDecimalPlaces,
        out decimal result)
    {
        result = 0;
        var normalized = value.Trim();
        if (normalized.Length == 0 || normalized.Any(char.IsWhiteSpace))
        {
            return false;
        }

        var dotCount = normalized.Count(character => character == '.');
        var commaCount = normalized.Count(character => character == ',');
        if (dotCount + commaCount > 1 ||
            normalized.Any(character => !char.IsAsciiDigit(character) && character is not '.' and not ','))
        {
            return false;
        }

        var separatorIndex = Math.Max(normalized.IndexOf('.'), normalized.IndexOf(','));
        if (separatorIndex >= 0 && normalized.Length - separatorIndex - 1 > maximumDecimalPlaces)
        {
            return false;
        }

        normalized = normalized.Replace(',', '.');
        return decimal.TryParse(
            normalized,
            NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out result);
    }

    public static bool TryParsePackageCount(string value, out int result) =>
        int.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out result) && result >= 1;

    public static decimal ParseDecimal(string value) =>
        TryParseDecimal(value, 28, out var result) ? result : 0;

    public static int ParsePackageCount(string value) =>
        TryParsePackageCount(value, out var result) ? result : 0;

    public static string FormatDecimal(decimal value) =>
        ReferenceMaterialPresentation.Number(value);
}

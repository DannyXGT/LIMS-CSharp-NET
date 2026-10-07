using System.Globalization;

namespace Lims.Contracts.ReferencePreparations;

public static class StockPresentation
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("es-GT");
    public static string Quantity(decimal? value, string unit) => value is null ? $"— {unit}" : $"{Number(value.Value, unit)} {unit}";
    public static string Number(decimal value, string unit)
    {
        var digits = unit.Contains('/', StringComparison.Ordinal) ? 2 : unit is "g" or "mg" or "µg" or "μg" or "ug" ? 3 : 2;
        if (value != 0 && decimal.Round(value, digits) == 0)
        {
            while (digits < 28 && decimal.Round(value, digits) == 0) digits++;
            digits = Math.Min(28, digits + 2);
            if (digits > 9) return value.ToString("0.###E+0", Culture);
        }
        return value.ToString("0." + new string('#', digits), Culture);
    }
    public static string Difference(decimal? value) => value is null ? "— %" : value.Value.ToString("+0.##;-0.##;0", Culture) + " %";
}

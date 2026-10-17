using System.Globalization;
using System.Text.Json.Serialization;

namespace Lims.Contracts.ReferencePreparations;

public sealed record SolventOption(int Id, string Name, string Abbreviation, bool IsActive, int DisplayOrder)
{
    [JsonIgnore] public string Display => $"{Name} ({Abbreviation})";
    public override string ToString() => Display;
}
public sealed record StockSolventInput(int SolventId, decimal Percent);
public sealed record StockSolventDetail(int SolventId, string Name, string Abbreviation, decimal Percent, int Position)
{
    [JsonIgnore] public string PercentDisplay => StockSolventComposition.Percent(Percent) + " %";
}

public static class StockSolventComposition
{
    // Composition percentages only: no inferred mass/volume basis or reagent concentration.
    public const string Basis = "CompositionPercent";
    public const string BasisLabel = "Porcentajes de composición (%) · Base masa/volumen no especificada";
    public static string Percent(decimal value) => value.ToString("0.####", CultureInfo.CurrentCulture);
    public static string? Validate(IReadOnlyList<StockSolventInput>? rows, string? basis)
    {
        if (basis != Basis) return "La base debe ser porcentaje de composición; no se infiere masa ni volumen.";
        if (rows is null || rows.Count == 0) return "Agregue al menos un solvente y complete su proporción.";
        if (rows.Any(r => r is null || r.SolventId <= 0)) return "Seleccione un solvente en cada fila.";
        if (rows.Any(r => r.Percent <= 0 || r.Percent > 100 || decimal.Round(r.Percent, 4) != r.Percent))
            return "Cada proporción debe ser mayor que 0 y hasta 100 %, con hasta cuatro decimales.";
        if (rows.Select(r => r.SolventId).Distinct().Count() != rows.Count) return "No puede repetir un solvente en la mezcla.";
        var total = rows.Sum(r => r.Percent);
        return total == 100m ? null : total < 100m
            ? $"Falta {Percent(100m - total)} % para completar el 100 %."
            : $"La proporción total excede el 100 % en {Percent(total - 100m)} %.";
    }
    public static string Summary(IReadOnlyList<StockSolventDetail>? rows)
    {
        if (rows is null || rows.Count == 0) return "Sin composición especificada";
        var ordered = rows.OrderBy(r => r.Position).ToArray();
        return string.Join(" / ", ordered.Select(r => r.Abbreviation)) + " — " +
            string.Join(":", ordered.Select(r => Percent(r.Percent))) + (ordered.Length == 1 ? " %" : "");
    }
}

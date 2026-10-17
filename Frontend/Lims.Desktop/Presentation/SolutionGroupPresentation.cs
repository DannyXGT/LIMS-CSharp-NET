using Lims.Contracts.ReferencePreparations;
using System.Globalization;

namespace Lims.Desktop.Presentation;

// Presentation only. Concentrations and debits always come from SolutionGroupCalculator.
public sealed record SolutionSourceOption(IntermediateSource Source)
{
    public string Label => $"{Source.Code} · {ShortName(Source.Name)}";
    private static string ShortName(string text) => text.Length <= 48 ? text : text[..45] + "…";
}

public sealed record SolutionAnalyteRow(string Name, decimal SourceConcentration, decimal ResultConcentration, string Unit)
{
    // G29 preserves the decimal value, including very small concentrations.
    public string SourceDisplay => SourceConcentration.ToString("G29", CultureInfo.CurrentCulture);
    public string ResultDisplay => ResultConcentration.ToString("G29", CultureInfo.CurrentCulture);
    public static IReadOnlyList<SolutionAnalyteRow> From(SolutionPart part) => part.Results.Select((a, i) =>
        new SolutionAnalyteRow(a.Name, part.Source.Analytes[i].Concentration, a.Concentration, a.Unit)).ToArray();
}

public sealed record SolutionVolumePreview(decimal Taken, decimal Final, string Unit)
{
    public decimal Percent => IntermediateCalculator.OccupiedPercent(Taken, Final);
    public decimal Remaining => Final - Taken;
    public bool Exceeds => Taken > Final;
    public string Tone => Exceeds ? "Danger" : Percent >= 90 ? "Warning" : "Information";
    public string TakenDisplay => StockPresentation.Quantity(Taken, Unit);
    public string FinalDisplay => StockPresentation.Quantity(Final, Unit);
    public string RemainingDisplay => StockPresentation.Quantity(Math.Abs(Remaining), Unit);
    public string Error => Exceeds ? $"Exceso de {RemainingDisplay}. Reduzca el volumen tomado o aumente el aforo." : string.Empty;
    public static SolutionVolumePreview From(SolutionPart part) => new(
        StockUnits.ConvertVolume(part.VolumeTaken, part.VolumeUnit, part.FinalVolumeUnit), part.FinalVolume, part.FinalVolumeUnit);
}

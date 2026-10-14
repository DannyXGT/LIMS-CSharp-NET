using System.Globalization;

namespace Lims.Contracts.ReferencePreparations;

/// <summary>Presentation of the persisted solution balance, independent of the source standard's weight debit.</summary>
public sealed class StockAvailability
{
    public StockAvailability(decimal initialVolume, string unit, decimal? availableVolume)
    {
        Unit = unit;
        var validInitial = initialVolume > 0 && unit is "mL" or "L";
        InitialVolume = validInitial ? initialVolume : null;
        if (!validInitial || availableVolume is null || availableVolume > initialVolume) return;
        AvailableVolume = Math.Max(0, availableVolume.Value);
        ConsumedVolume = initialVolume - AvailableVolume.Value;
        RemainingPercent = AvailableVolume.Value / initialVolume * 100m;
    }

    public string Unit { get; }
    public decimal? InitialVolume { get; }
    public decimal? ConsumedVolume { get; }
    public decimal? AvailableVolume { get; }
    public decimal? RemainingPercent { get; }
    public bool IsCalculated => RemainingPercent.HasValue;
    public bool IsDepleted => IsCalculated && AvailableVolume == 0;
    public double ProgressValue => (double)(RemainingPercent ?? 0);
    public string InitialDisplay => StockPresentation.Quantity(InitialVolume, Unit);
    public string ConsumedDisplay => StockPresentation.Quantity(ConsumedVolume, Unit);
    public string AvailableDisplay => IsCalculated ? StockPresentation.Quantity(AvailableVolume, Unit) : "Sin disponibilidad calculada";
    public string PercentDisplay => RemainingPercent is { } percent ? percent.ToString("0.##", CultureInfo.GetCultureInfo("es-GT")) + " %" : "— %";
    public string Tone => RemainingPercent switch { null => "Neutral", < 25 => "Danger", <= 60 => "Warning", _ => "Information" };
}

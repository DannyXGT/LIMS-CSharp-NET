namespace Lims.Contracts.ReferencePreparations;

/// <summary>Explicit mass/volume conversions. ppm is not a mass concentration unit.</summary>
public static class StockUnits
{
    public static decimal MilligramsPerMassUnit(string unit) => unit switch
    {
        "g" => 1000m, "mg" => 1m, "µg" or "μg" or "ug" => 0.001m,
        _ => throw new ArgumentException("Seleccione una unidad de peso: g, mg o µg.", nameof(unit)),
    };
    public static decimal LitersPerVolumeUnit(string unit) => unit switch
    {
        "L" => 1m, "mL" => 0.001m,
        _ => throw new ArgumentException("Seleccione volumen en L o mL.", nameof(unit)),
    };
    public static decimal MilligramsPerLiter(string unit) => unit switch
    {
        "g/L" => 1000m, "mg/L" or "µg/mL" => 1m, "µg/L" => 0.001m,
        _ => throw new ArgumentException("Seleccione una unidad de concentración de masa admitida.", nameof(unit)),
    };
    public static decimal ConvertMass(decimal value, string from, string to) => value * MilligramsPerMassUnit(from) / MilligramsPerMassUnit(to);
    public static decimal ConvertVolume(decimal value, string from, string to) => value * LitersPerVolumeUnit(from) / LitersPerVolumeUnit(to);
    public static StockCalculation Calculate(decimal purity, decimal available, string sourceUnit, StockCalculationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request.TargetConcentration, "concentración objetivo");
        Validate(request.FinalVolume, "volumen final");
        if (purity is <= 0 or > 100) throw new ArgumentException("La pureza debe estar entre 0 y 100 %.", nameof(purity));
        var liters = request.FinalVolume * LitersPerVolumeUnit(request.FinalVolumeUnit);
        var concentrationFactor = MilligramsPerLiter(request.ConcentrationUnit);
        var massFactor = MilligramsPerMassUnit(sourceUnit);
        var calculated = request.TargetConcentration * concentrationFactor * liters / (purity / 100m) / massFactor;
        decimal? actual = null;
        decimal? real = null;
        if (request.ActualWeight is { } weight)
        {
            Validate(weight, "peso tomado");
            actual = ConvertMass(weight, request.ActualWeightUnit ?? sourceUnit, sourceUnit);
            // Reject unrepresentable debits at the existing balance/ledger precision. Never silently round a physical measurement.
            Validate(actual.Value, "peso tomado convertido a la unidad del estándar");
            real = actual * massFactor * (purity / 100m) / liters / concentrationFactor;
            if (real is <= 0 or >= 1000000000000m) throw new ArgumentException("La concentración real queda fuera del rango admitido.");
        }
        if (calculated is <= 0 or >= 1000000000000m) throw new ArgumentException("El peso a tomar queda fuera del rango admitido.");
        var needed = actual ?? calculated;
        return new(calculated, actual, sourceUnit, available, actual is null ? null : Math.Max(0, available - actual.Value),
            Math.Max(0, needed - available), needed <= available, purity, request.TargetConcentration, real,
            request.ConcentrationUnit, request.FinalVolume, request.FinalVolumeUnit,
            "Peso a tomar = C × V ÷ pureza; concentración real = peso tomado × pureza ÷ V (unidades convertidas)");
    }
    private static void Validate(decimal value, string name)
    {
        if (value <= 0 || value >= 1000000000000m || decimal.Round(value, 6) != value)
            throw new ArgumentException($"El valor de {name} debe ser positivo, menor que un billón y tener hasta seis decimales.");
    }
}

namespace Lims.Contracts.ReferencePreparations;

/// <summary>Each analyte is diluted independently. Concentrations of different substances are never added.</summary>
public static class IntermediateCalculator
{
    public static IntermediateCalculation Calculate(IntermediateCalculationRequest request, IReadOnlyList<IntermediateSource> sources)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateVolume(request.FinalVolume);
        _ = StockUnits.LitersPerVolumeUnit(request.FinalVolumeUnit);
        if (request.MethodId <= 0 || request.Components is null || request.Components.Count is < 1 or > 100 || request.Components.Any(c => c is null))
            throw new ArgumentException("Seleccione método y al menos un componente (máximo 100).");
        if (request.Components.Select(c => c.SourceId).Distinct().Count() != request.Components.Count)
            throw new ArgumentException("Un origen solo puede aparecer una vez. Ajuste su volumen en la misma fila.");
        var contributions = new List<IntermediateContribution>();
        decimal total = 0;
        var sufficient = true;
        foreach (var component in request.Components)
        {
            ValidateVolume(component.VolumeTaken);
            var source = sources.SingleOrDefault(s => s.Id == component.SourceId)
                ?? throw new ArgumentException("Seleccione un origen disponible para cada componente.");
            if (source.Version != component.SourceVersion) throw new ArgumentException("Un origen cambió. Actualice su disponibilidad.");
            if (source.MethodId != request.MethodId) throw new ArgumentException("Un componente no es compatible con el método seleccionado.");
            if (request.DilutedPreparationId is { } parent)
            {
                if (request.Components.Count != 1 || parent != source.Id || source.Kind != "Intermedia")
                    throw new ArgumentException("La dilución utiliza una alícuota de la Intermedia seleccionada.");
            }
            else if (source.Kind != "Stock") throw new ArgumentException("Para diluir una Intermedia previa, selecciónela en Solución a diluir.");
            var debit = StockUnits.ConvertVolume(component.VolumeTaken, component.VolumeUnit, source.VolumeUnit);
            ValidateVolume(debit);
            sufficient &= debit <= source.AvailableVolume;
            var taken = StockUnits.ConvertVolume(component.VolumeTaken, component.VolumeUnit, request.FinalVolumeUnit);
            total += taken;
            var results = source.Analytes.Select(a => a with { Concentration = a.Concentration * taken / request.FinalVolume }).ToArray();
            if (results.Length == 0 || results.Any(a => a.Concentration <= 0 || a.Concentration >= 1000000000000m))
                throw new ArgumentException("La concentración del origen queda fuera del rango admitido.");
            contributions.Add(new(source, component.VolumeTaken, component.VolumeUnit, source.AvailableVolume - debit, results));
        }
        // Distinct analyte identities are retained even when display names match. CAS allows pooling the same chemical.
        var final = contributions.SelectMany(c => c.Results).GroupBy(a => string.IsNullOrWhiteSpace(a.CasNumber) ? a.MaterialId.ToString("D") : a.CasNumber.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First() with { Concentration = group.Sum(a => a.Concentration * StockUnits.MilligramsPerLiter(a.Unit)), Unit = "mg/L" }).ToArray();
        if (final.Any(a => a.Concentration >= 1000000000000m)) throw new ArgumentException("La concentración resultante excede el rango admitido.");
        return new(total, request.FinalVolume, request.FinalVolumeUnit, request.FinalVolume - total,
            total / request.FinalVolume * 100m, total > request.FinalVolume, sufficient, contributions, final);
    }

    public static decimal OccupiedPercent(decimal taken, decimal finalVolume) => finalVolume > 0 ? taken / finalVolume * 100m : 0;
    public static void ValidateVolume(decimal value)
    {
        if (value <= 0 || value >= 1000000000000m || decimal.Round(value, 6) != value)
            throw new ArgumentException("El volumen debe ser positivo, menor que un billón y tener hasta seis decimales.");
    }
}

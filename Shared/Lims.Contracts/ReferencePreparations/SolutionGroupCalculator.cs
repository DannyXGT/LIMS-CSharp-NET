namespace Lims.Contracts.ReferencePreparations;

/// <summary>TrazaLab Works/AQS: independent dilutions, each with its own aforo. Never add concentrations across solutions.</summary>
public static class SolutionGroupCalculator
{
    public static SolutionCalculation Calculate(SolutionCalculationRequest request, IReadOnlyList<IntermediateSource> sources)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.MethodId <= 0 || request.Parts is null || request.Parts.Count is < 1 or > 100 || request.Parts.Any(p => p is null))
            throw new ArgumentException("Seleccione un método y entre 1 y 100 soluciones completas.");
        var parts = new List<SolutionPart>();
        foreach (var part in request.Parts)
        {
            var source = sources.SingleOrDefault(s => s.Id == part.SourceId)
                ?? throw new ArgumentException("Seleccione un origen disponible para cada solución.");
            if ((source.Kind != "Intermedia" && !Lims.Contracts.ReferenceMaterials.MaterialPresentations.IsCommercial(source.Kind)) || source.MethodId != request.MethodId)
                throw new ArgumentException("El origen debe ser una Intermedia o solución comercial compatible con el método.");
            if (source.Version != part.SourceVersion) throw new ArgumentException("Un origen cambió. Actualice su disponibilidad.");
            IntermediateCalculator.ValidateVolume(part.VolumeTaken);
            IntermediateCalculator.ValidateVolume(part.FinalVolume);
            var taken = StockUnits.ConvertVolume(part.VolumeTaken, part.VolumeUnit, part.FinalVolumeUnit);
            if (taken > part.FinalVolume) throw new ArgumentException("El volumen tomado no puede superar su aforo.");
            IntermediateCalculator.ValidateVolume(StockUnits.ConvertVolume(part.VolumeTaken, part.VolumeUnit, source.VolumeUnit));
            if (source.Analytes.Count == 0) throw new ArgumentException("El origen no tiene concentraciones trazables.");
            var results = source.Analytes.Select(a =>
            {
                _ = StockUnits.MilligramsPerLiter(a.Unit);
                var concentration = a.Concentration * taken / part.FinalVolume;
                if (concentration <= 0 || concentration >= 1000000000000m)
                    throw new ArgumentException("La concentración resultante queda fuera del rango admitido.");
                return a with { Concentration = concentration, ExpandedUncertainty = a.ExpandedUncertainty * taken / part.FinalVolume };
            }).ToArray();
            parts.Add(new(parts.Count, source, part.VolumeTaken, part.VolumeUnit, part.FinalVolume, part.FinalVolumeUnit, part.IsInternalStandard, results));
        }
        // A multianalyte source is one physical aliquot, irrespective of its number of analytes.
        var debits = parts.GroupBy(p => p.Source.Id).Select(g =>
        {
            var source = g.First().Source;
            var debit = g.Sum(p => StockUnits.ConvertVolume(p.VolumeTaken, p.VolumeUnit, source.VolumeUnit));
            IntermediateCalculator.ValidateVolume(debit);
            return new SolutionSourceDebit(source, debit, source.AvailableVolume - debit);
        }).ToArray();
        IntermediateCalculator.ValidateVolume(parts.Sum(p => StockUnits.ConvertVolume(p.FinalVolume, p.FinalVolumeUnit, "mL")));
        return new(parts, debits, debits.All(d => d.RemainingVolume >= 0));
    }
}

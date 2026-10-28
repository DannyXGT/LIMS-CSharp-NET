namespace Lims.Contracts.ReferencePreparations;

/// <summary>TrazaLab Works/AQS: independent dilutions, each with its own aforo. Never add concentrations across solutions.</summary>
public static class SolutionGroupCalculator
{
    public static SolutionCalculation Calculate(SolutionCalculationRequest request, IReadOnlyList<IntermediateSource> sources,
        IReadOnlyList<SolventOption>? solvents = null)
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
            SolutionInternalStandard? internalStandard = null;
            if (part.InternalStandard is { } standard)
            {
                var origin = sources.SingleOrDefault(s => s.Id == standard.SourceId)
                    ?? throw new ArgumentException("Seleccione una Intermedia disponible como estándar interno.");
                if (!origin.CanBeInternalStandard || origin.MethodId != request.MethodId || origin.Version != standard.SourceVersion)
                    throw new ArgumentException("El estándar interno debe ser una Intermedia vigente y compatible; actualice sus orígenes.");
                if (origin.Analytes.Count == 0) throw new ArgumentException("El estándar interno no tiene concentraciones trazables.");
                IntermediateCalculator.ValidateVolume(standard.VolumeTaken);
                var standardTaken = StockUnits.ConvertVolume(standard.VolumeTaken, standard.VolumeUnit, part.FinalVolumeUnit);
                taken += standardTaken;
                var standardResults = origin.Analytes.Select(a => Dilute(a, standardTaken, part.FinalVolume)).ToArray();
                internalStandard = new(origin, standard.VolumeTaken, standard.VolumeUnit, standardResults);
            }
            if (taken > part.FinalVolume) throw new ArgumentException("La suma del origen y el estándar interno no puede superar el aforo.");
            SolventOption? solvent = null;
            if (part.SolventId is { } solventId)
                solvent = solvents?.SingleOrDefault(s => s.Id == solventId && s.IsActive)
                    ?? throw new ArgumentException("Seleccione un solvente activo del catálogo.");
            IntermediateCalculator.ValidateVolume(StockUnits.ConvertVolume(part.VolumeTaken, part.VolumeUnit, source.VolumeUnit));
            if (source.Analytes.Count == 0) throw new ArgumentException("El origen no tiene concentraciones trazables.");
            var mainTaken = StockUnits.ConvertVolume(part.VolumeTaken, part.VolumeUnit, part.FinalVolumeUnit);
            var results = source.Analytes.Select(a => Dilute(a, mainTaken, part.FinalVolume)).ToArray();
            parts.Add(new(parts.Count, source, part.VolumeTaken, part.VolumeUnit, part.FinalVolume, part.FinalVolumeUnit, part.IsInternalStandard, results, solvent, internalStandard));
        }
        // A multianalyte source is one physical aliquot, irrespective of its number of analytes.
        var aliquots = parts.SelectMany(p => p.InternalStandard is { } s
            ? new[] { (p.Source, p.VolumeTaken, p.VolumeUnit), (s.Source, s.VolumeTaken, s.VolumeUnit) }
            : new[] { (p.Source, p.VolumeTaken, p.VolumeUnit) });
        var debits = aliquots.GroupBy(p => p.Source.Id).Select(g =>
        {
            var source = g.First().Source;
            var debit = g.Sum(p => StockUnits.ConvertVolume(p.VolumeTaken, p.VolumeUnit, source.VolumeUnit));
            IntermediateCalculator.ValidateVolume(debit);
            return new SolutionSourceDebit(source, debit, source.AvailableVolume - debit);
        }).ToArray();
        IntermediateCalculator.ValidateVolume(parts.Sum(p => StockUnits.ConvertVolume(p.FinalVolume, p.FinalVolumeUnit, "mL")));
        return new(parts, debits, debits.All(d => d.RemainingVolume >= 0));
    }

    private static IntermediateAnalyte Dilute(IntermediateAnalyte analyte, decimal taken, decimal final)
    {
        _ = StockUnits.MilligramsPerLiter(analyte.Unit);
        var concentration = analyte.Concentration * taken / final;
        if (concentration <= 0 || concentration >= 1000000000000m)
            throw new ArgumentException("La concentración resultante queda fuera del rango admitido.");
        return analyte with { Concentration = concentration, ExpandedUncertainty = analyte.ExpandedUncertainty * taken / final };
    }
}

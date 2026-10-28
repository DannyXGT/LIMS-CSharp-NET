using System.Text.Json;
using Lims.Contracts.ReferenceMaterials;
using Lims.Contracts.ReferencePreparations;
using Lims.Domain.ReferenceMaterials;

namespace Lims.Application.ReferenceMaterials;

public static class CommercialMaterials
{
    public static void Apply(ReferenceMaterial material, CommercialCertificate? certificate, int actor, DateTimeOffset now)
    {
        if (!MaterialPresentations.IsCommercial(material.Presentation))
        {
            if (material.Presentation != MaterialPresentations.Solid || certificate is not null)
                throw new ArgumentException("Presentación de material inválida.");
            return;
        }
        if (certificate is null ||
            certificate.Components is null || certificate.Components.Count == 0 || certificate.Components.Any(c => c is null) ||
            (material.Presentation == MaterialPresentations.Solution && certificate.Components.Count != 1) ||
            (material.Presentation == MaterialPresentations.Mix && certificate.Components.Count < 2))
            throw new ArgumentException("Indique la composición (un analito para solución individual, dos o más para Mix).");
        if (certificate.Solvents is not null || certificate.CompositionBasis is not null)
        {
            var error = StockSolventComposition.Validate(certificate.Solvents, certificate.CompositionBasis);
            if (error is not null) throw new ArgumentException(error);
        }
        if (certificate.NominalConcentration is <= 0 || (certificate.NominalConcentration.HasValue && string.IsNullOrWhiteSpace(certificate.NominalUnit)) ||
            certificate.CoverageFactor is <= 0 || certificate.UseAfterOpeningMonths is <= 0 or > 1200 || certificate.UseAfterOpeningDays is <= 0 or > 36500 ||
            (certificate.UseAfterOpeningMonths.HasValue && certificate.UseAfterOpeningDays.HasValue) ||
            (HistoricalEntryPolicy.EnforcePreparationDates &&
            (certificate.OpenedDate > DateOnly.FromDateTime(now.ToOffset(TimeSpan.FromHours(-6)).DateTime) || certificate.OpenedDate < material.ReceivedDate)))
            throw new ArgumentException("Revise concentración nominal, cobertura y condiciones de apertura documentadas.");
        if (certificate.UseNominalConcentration)
        {
            if (certificate.Components.Count != 1 || certificate.NominalConcentration is null || string.IsNullOrWhiteSpace(certificate.NominalUnit))
                throw new ArgumentException("La concentración nominal requiere un producto individual y valor y unidad documentados.");
            _ = StockUnits.MilligramsPerLiter(certificate.NominalUnit);
        }
        var submittedIds = certificate.Components.Where(c => c.Id != Guid.Empty).Select(c => c.Id).ToArray();
        if (submittedIds.Distinct().Count() != submittedIds.Length)
            throw new ArgumentException("Los componentes certificados deben tener identidades únicas.");
        var knownIds = material.CertifiedComponents.Select(c => c.Id).ToHashSet();
        // Every new physical bottle owns new component identities, including replacement bottles.
        var entries = certificate.Components.Select(c => c with { Id = knownIds.Contains(c.Id) ? c.Id : Guid.NewGuid() }).ToArray();
        foreach (var expression in certificate.OtherExpressions ?? [])
            if (expression.Value <= 0 || expression.ExpandedUncertainty < 0 || string.IsNullOrWhiteSpace(expression.Unit))
                throw new ArgumentException("Expresión certificada adicional inválida.");
        DateOnly effective = material.ExpirationDate;
        if (certificate.OpenedDate is { } opened && certificate.UseAfterOpeningMonths is { } months)
        {
            var limit = opened.AddMonths(months);
            if (limit < effective) effective = limit;
        }
        if (certificate.OpenedDate is { } openedDay && certificate.UseAfterOpeningDays is { } days)
        {
            var limit = openedDay.AddDays(days);
            if (limit < effective) effective = limit;
        }
        certificate = certificate with { Components = entries };
        material.ConfigureCommercial(material.Presentation, JsonSerializer.Serialize(certificate), certificate.OpenedDate, effective,
            (certificate.UseAfterOpeningMonths.HasValue || certificate.UseAfterOpeningDays.HasValue) && !certificate.OpenedDate.HasValue,
            entries.Select(c => new CertifiedMaterialComponent(c.Id, material.Id, c.Name, c.CasNumber,
                c.CertifiedConcentration, c.ConcentrationUnit, c.ExpandedUncertainty, c.PurityPercent, c.Lot, c.AdditionalData)).ToArray(), actor, now);
    }

    public static CommercialCertificate? Certificate(ReferenceMaterial material)
    {
        if (material.CommercialJson is null) return null;
        var certificate = JsonSerializer.Deserialize<CommercialCertificate>(material.CommercialJson)!;
        // Preserve certificate order independently of the database's row retrieval order.
        return certificate with
        {
            Components = certificate.Components.Select(entry => material.CertifiedComponents.Single(c => c.Id == entry.Id))
                .Select(c => new CertifiedComponent(c.Id, c.Name, c.CasNumber,
                c.CertifiedConcentration, c.ConcentrationUnit, c.ExpandedUncertainty, c.PurityPercent, c.Lot, c.AdditionalData)).ToArray()
        };
    }

    public static IntermediateSource Source(ReferenceMaterial material)
    {
        var certificate = Certificate(material) ?? throw new ArgumentException("Certificado comercial no disponible.");
        // No conversion of mass fraction (µg/g) without documented density. Unsupported units remain on the certificate.
        foreach (var c in certificate.ComponentsForUse()) _ = StockUnits.MilligramsPerLiter(c.ConcentrationUnit);
        var role = material.CommercialRoles.OrderBy(r => r.Kind == "Stock" ? 0 : 1).FirstOrDefault();
        return new(material.Id, material.Presentation, role?.Code ?? material.CatalogNumber ?? material.Name, material.Name,
            material.MethodId, material.Method.Name, material.AvailableQuantity, material.TotalQuantity, material.Unit.Symbol,
            material.ReceivedDate, material.EffectiveExpirationDate ?? material.ExpirationDate, material.Version,
            certificate.ComponentsForUse().Select(c => new IntermediateAnalyte(c.Id, c.Name, c.CasNumber, c.Lot ?? material.Lot,
                c.CertifiedConcentration, c.ConcentrationUnit, material.Id, c.Id, c.ExpandedUncertainty)).ToArray(),
            material.Lot, material.Brand, material.CatalogNumber, certificate.Matrix, certificate.Reference,
            material.CommercialRoles.Any(r => r.Kind == "Intermedia"));
    }
}

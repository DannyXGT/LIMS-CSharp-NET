using Lims.Application.ReferenceMaterials;
using Lims.Contracts.ReferenceMaterials;
using Lims.Domain.ReferenceMaterials;

namespace Lims.Application.Tests.ReferenceMaterials;

public sealed class CommercialCertificateTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
    private static ReferenceMaterial Bottle(DateOnly expiration) => new(Guid.NewGuid(), "Al comercial de prueba", null, "TEST", new(22, "Metales", true),
        null, "TEST-LOT", "Prueba aislada", new(2026, 1, 1), expiration, 10, new(1, "Mililitro", "mL", true), 1, "Ambiente", new(1, "Laboratorio", true), 42, Now, MaterialPresentations.Solution);
    private static CommercialCertificate Certificate => new("CERT-TEST", "4 % HNO3 (base no declarada)", "NIST SRM 3101a",
        1000, "µg/mL", null, new(2026, 10, 1), null, [new(Guid.NewGuid(), "Aluminio", "7429-90-5", 999, "µg/mL", 5)], [new(974, "µg/g", 5)], 30);

    [Fact]
    public void DocumentedDaysUseExactCalendarDaysAndDoNotExtendManufacturerLimit()
    {
        var bottle = Bottle(new(2026, 10, 20)); CommercialMaterials.Apply(bottle, Certificate, 42, Now);
        Assert.Equal(new DateOnly(2026, 10, 20), bottle.EffectiveExpirationDate);
        var laterExpiry = Bottle(new(2027, 10, 20)); CommercialMaterials.Apply(laterExpiry, Certificate, 42, Now);
        Assert.Equal(new DateOnly(2026, 10, 31), laterExpiry.EffectiveExpirationDate);
        var source = CommercialMaterials.Source(laterExpiry);
        Assert.Equal(999, Assert.Single(source.Analytes).Concentration); Assert.Equal("µg/mL", source.Analytes[0].Unit);
        Assert.Equal(974, CommercialMaterials.Certificate(laterExpiry)!.OtherExpressions![0].Value);
    }

    [Fact]
    public void MissingOpeningAndAmbiguousOrInvalidPeriodsCannotEnableConsumption()
    {
        var bottle = Bottle(new(2027, 10, 20)); CommercialMaterials.Apply(bottle, Certificate with { OpenedDate = null }, 42, Now);
        Assert.True(bottle.OpeningRequired);
        Assert.Throws<ArgumentException>(() => bottle.ConsumeCommercial(1, 42, Now, new(2026, 10, 17)));
        Assert.Throws<ArgumentException>(() => CommercialMaterials.Apply(bottle, Certificate with { UseAfterOpeningMonths = 15 }, 42, Now));
        Assert.Throws<ArgumentException>(() => CommercialMaterials.Apply(bottle, Certificate with { UseAfterOpeningDays = 0 }, 42, Now));
    }
    [Fact]
    public void NominalSelectionPreservesCertificateAndChangesOnlyConcentrationUsed()
    {
        var bottle = Bottle(new(2027, 10, 20));
        CommercialMaterials.Apply(bottle, Certificate with { UseNominalConcentration = true }, 42, Now);
        var saved = CommercialMaterials.Certificate(bottle)!;
        Assert.True(saved.UseNominalConcentration);
        Assert.Equal(999, saved.Components[0].CertifiedConcentration);
        Assert.Equal(1000, Assert.Single(CommercialMaterials.Source(bottle).Analytes).Concentration);
        Assert.Null(Assert.Single(CommercialMaterials.Source(bottle).Analytes).ExpandedUncertainty);
        Assert.Equal(999, Assert.Single((saved with { UseNominalConcentration = false }).ComponentsForUse()).CertifiedConcentration);
        Assert.Throws<ArgumentException>(() => CommercialMaterials.Apply(bottle, Certificate with { UseNominalConcentration = true, NominalConcentration = null }, 42, Now));
        Assert.Throws<ArgumentException>(() => CommercialMaterials.Apply(bottle, Certificate with { UseNominalConcentration = true, NominalUnit = "ppm" }, 42, Now));
    }

}

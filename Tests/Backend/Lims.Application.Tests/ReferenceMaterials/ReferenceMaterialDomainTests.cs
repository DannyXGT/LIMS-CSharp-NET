using Lims.Domain.ReferenceMaterials;

namespace Lims.Application.Tests.ReferenceMaterials;

public sealed class ReferenceMaterialDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 6, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("64-17-5", true)]
    [InlineData("7732-18-5", true)]
    [InlineData("64-17-6", false)]
    [InlineData("not-cas", false)]
    public void CasValidationChecksFormatAndChecksum(string value, bool expected) =>
        Assert.Equal(expected, CasRegistryNumber.IsValid(value));

    [Fact]
    public void NewMaterialPreservesPresentationAndDerivesTotalQuantity()
    {
        var material = CreateMaterial();

        Assert.Equal(ReferenceMaterialStatus.Active, material.Status);
        Assert.Equal(50m, material.TotalQuantity);
        Assert.Equal(50m, material.AvailableQuantity);
        Assert.Equal(7, material.CreatedByUserId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100.0001)]
    public void PurityOutsideConfirmedRangeIsRejected(decimal purity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateMaterial(purity));
    }

    [Fact]
    public void ArchiveRecordsActorTimestampReasonAndPreventsFurtherChanges()
    {
        var material = CreateMaterial();

        material.Archive("Certificado sustituido", 9, Now.AddMinutes(5));

        Assert.Equal(ReferenceMaterialStatus.Archived, material.Status);
        Assert.Equal(9, material.ArchivedByUserId);
        Assert.Equal("Certificado sustituido", material.ArchiveReason);
        Assert.Throws<InvalidOperationException>(() => material.Archive("Otra vez", 9, Now.AddMinutes(6)));
    }

    [Fact]
    public void ExpirationBeforeReceiptIsRejected()
    {
        Assert.Throws<ArgumentException>(() => new ReferenceMaterial(
            Guid.NewGuid(),
            "Etanol CRM",
            "64-17-5",
            "CAT-001",
            "GC",
            99.5m,
            "LOT-2026",
            "Proveedor",
            new DateOnly(2026, 9, 30),
            new DateOnly(2026, 9, 29),
            10m,
            MeasurementUnit.Gram,
            5,
            "2–8 °C",
            "Laboratorio",
            7,
            Now));
    }

    [Fact]
    public void EffectiveStatusReportsExpiredWithoutDestroyingRecordedLifecycleState()
    {
        var material = CreateMaterial();

        Assert.Equal(ReferenceMaterialStatus.Expired, material.EffectiveStatus(new DateOnly(2027, 10, 1)));
        Assert.Equal(ReferenceMaterialStatus.Active, material.Status);
    }

    private static ReferenceMaterial CreateMaterial(decimal purity = 99.5m) => new(
        Guid.NewGuid(),
        "Etanol CRM",
        "64-17-5",
        "CAT-001",
        "GC",
        purity,
        "LOT-2026",
        "Proveedor",
        new DateOnly(2026, 9, 30),
        new DateOnly(2027, 9, 30),
        10m,
        MeasurementUnit.Gram,
        5,
        "2–8 °C",
        "Laboratorio",
        7,
        Now);
}

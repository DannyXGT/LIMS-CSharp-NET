using Lims.Domain.ReferenceMaterials;

namespace Lims.Application.Tests.ReferenceMaterials;

public sealed class ReferenceMaterialDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 6, 0, 0, TimeSpan.Zero);
    private static readonly ReferenceMethod Method = new(1, "APEOs", true);
    private static readonly ReferenceUnit Unit = new(2, "Gramo", "g", true);
    private static readonly ReferenceLocation Location = new(1, "Laboratorio", true);

    [Theory]
    [InlineData("64-17-5", true)]
    [InlineData("7732-18-5", true)]
    [InlineData("64-17-6", false)]
    [InlineData("not-cas", false)]
    public void CasValidationChecksFormatAndChecksum(string value, bool expected) =>
        Assert.Equal(expected, CasRegistryNumber.IsValid(value));

    [Fact]
    public void NewMaterialPreservesCatalogsTemperatureAndDerivedQuantity()
    {
        var material = CreateMaterial();

        Assert.Equal(ReferenceMaterialStatus.Active, material.Status);
        Assert.Equal(Method.Id, material.MethodId);
        Assert.Equal(Unit.Id, material.UnitId);
        Assert.Equal(Location.Id, material.LocationId);
        Assert.Equal("2-8 °C", material.StorageTemperature);
        Assert.Equal(50m, material.TotalQuantity);
        Assert.Equal(50m, material.AvailableQuantity);
        Assert.Equal(100m, material.RemainingPercentage);
        Assert.Equal(7, material.CreatedByUserId);
        Assert.Equal(7, material.UpdatedByUserId);
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
            Guid.NewGuid(), "Etanol CRM", "64-17-5", "CAT-001", Method, 99.5m,
            "LOT-2026", "Proveedor", new DateOnly(2026, 9, 30), new DateOnly(2026, 9, 29),
            10m, Unit, 5, "4 °C", Location, 7, Now));
    }

    [Fact]
    public void StorageTemperatureIsRequiredButAllowsLaboratoryText()
    {
        Assert.Throws<ArgumentException>(() => CreateMaterial(storageTemperature: " "));
        Assert.Equal("T ambiente", CreateMaterial(storageTemperature: " T ambiente ").StorageTemperature);
    }

    [Fact]
    public void EffectiveStatusReportsExpiredWithoutDestroyingRecordedLifecycleState()
    {
        var material = CreateMaterial();

        Assert.Equal(ReferenceMaterialStatus.Expired, material.EffectiveStatus(new DateOnly(2027, 10, 1)));
        Assert.Equal(ReferenceMaterialStatus.Active, material.Status);
    }

    [Fact]
    public void NativeMaterialRequiresPositiveActor()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReferenceMaterial(
            Guid.NewGuid(), "Etanol CRM", "64-17-5", "CAT-001", Method, 99.5m,
            "LOT-2026", "Proveedor", new DateOnly(2026, 9, 30), new DateOnly(2027, 9, 30),
            10m, Unit, 5, "4 °C", Location, 0, Now));
    }

    [Fact]
    public void NativeMaterialRejectsInactiveCatalogValues()
    {
        var inactiveUnit = new ReferenceUnit(6, "Kilogramo", "kg", false);

        Assert.Throws<InvalidOperationException>(() => new ReferenceMaterial(
            Guid.NewGuid(), "Etanol CRM", "64-17-5", "CAT-001", Method, 99.5m,
            "LOT-2026", "Proveedor", new DateOnly(2026, 9, 30), new DateOnly(2027, 9, 30),
            10m, inactiveUnit, 5, "4 °C", Location, 7, Now));
    }

    private static ReferenceMaterial CreateMaterial(
        decimal purity = 99.5m,
        string storageTemperature = "2-8 °C") => new(
        Guid.NewGuid(), "Etanol CRM", "64-17-5", "CAT-001", Method, purity,
        "LOT-2026", "Proveedor", new DateOnly(2026, 9, 30), new DateOnly(2027, 9, 30),
        10m, Unit, 5, storageTemperature, Location, 7, Now);
}

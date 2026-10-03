using System.Globalization;
using System.Text.Json;
using Lims.Contracts.ReferenceMaterials;

namespace Lims.Application.Tests.ReferenceMaterials;

public sealed class ReferenceMaterialPresentationTests
{
    [Theory]
    [InlineData("98.0900", "98.09 %")]
    [InlineData("100.000000", "100 %")]
    [InlineData("0.0001", "0.0001 %")]
    public void PurityRemovesStorageScale(string value, string expected) =>
        Assert.Equal(expected, ReferenceMaterialPresentation.Purity(decimal.Parse(value, CultureInfo.InvariantCulture)));

    [Theory]
    [InlineData("100.000000", "100 mg")]
    [InlineData("100.500000", "100.5 mg")]
    [InlineData("0.025000", "0.025 mg")]
    [InlineData("0.000001", "0.000001 mg")]
    public void QuantityPreservesSignificantDecimals(string value, string expected) =>
        Assert.Equal(expected, ReferenceMaterialPresentation.Quantity(decimal.Parse(value, CultureInfo.InvariantCulture), "mg"));

    [Fact]
    public void ApplicationFormatsAreIndependentOfWorkstationCulture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            Assert.Equal("98.09 %", ReferenceMaterialPresentation.Purity(98.0900m));
            Assert.Equal("06/05/2029", ReferenceMaterialPresentation.Date(new DateOnly(2029, 5, 6)));
        }
        finally { CultureInfo.CurrentCulture = original; }
    }

    [Theory]
    [InlineData("2026-10-03T13:51:40+00:00", "03/10/2026 · 07:51")]
    [InlineData("2026-10-03T02:15:00+00:00", "02/10/2026 · 20:15")]
    [InlineData("2026-10-03T07:51:40-06:00", "03/10/2026 · 07:51")]
    public void AuditInstantsUseGuatemalaIncludingDateRollover(string instant, string expected) =>
        Assert.Equal(expected, ReferenceMaterialPresentation.LocalTimestamp(DateTimeOffset.Parse(instant, CultureInfo.InvariantCulture)));

    [Fact]
    public void BusinessDatesAreNotConvertedAsUtcInstants()
    {
        var detail = Detail();
        Assert.Equal("03/10/2026 → 06/05/2029", detail.ValidityDisplay);
        Assert.Equal("03/10/2026 · 07:51", detail.CreatedAtDisplay);
    }

    [Fact]
    public void AvailabilityIsDerivedAndNullIsNotInterpretedAsZero()
    {
        var detail = Detail();
        Assert.Equal("100 mg disponibles", detail.AvailabilityHeadline);
        Assert.Equal("100 mg × 1 unidad", detail.PresentationDisplay);
        Assert.Equal("100 % disponible", detail.AvailabilityPercentDisplay);
        Assert.Equal("50 % disponible", (detail with { AvailableQuantity = 100m, PackageCount = 2 }).AvailabilityPercentDisplay);
        Assert.Equal("0 % disponible", (detail with { AvailableQuantity = 0m }).AvailabilityPercentDisplay);
        Assert.False((detail with { AvailableQuantity = null }).HasAvailabilityPercent);
        Assert.False((detail with { PresentationQuantity = 0 }).HasAvailabilityPercent);
        Assert.Equal("Disponibilidad no informada", (detail with { AvailableQuantity = null }).AvailabilityHeadline);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("NULL")]
    public void MissingValuesNeverExposeNullOrIds(string? missing)
    {
        var detail = Detail() with { CreatedByName = missing, CasNumber = missing, CatalogNumber = missing };
        Assert.Equal("Usuario no disponible", detail.CreatedByDisplay);
        Assert.False(detail.HasCas);
        Assert.False(detail.HasCatalog);
        Assert.Equal("No informado", ReferenceMaterialPresentation.Optional(missing));
        Assert.Equal(string.Empty, ReferenceMaterialPresentation.LocalTimestamp(null));
    }

    [Fact]
    public void ArchiveAndReplacementFieldsRequireMatchingStateAndValues()
    {
        var detail = Detail() with { ArchiveReason = "Fin de uso", ArchivedByUserId = 2, ArchivedAt = Detail().CreatedAt };
        Assert.False(detail.HasArchiveReason);
        Assert.False(detail.HasArchiveActor);
        Assert.False(detail.HasArchiveTimestamp);
        var archived = detail with { Status = "Archived" };
        Assert.True(archived.HasArchiveReason);
        Assert.True(archived.HasArchiveActor);
        Assert.True(archived.HasArchiveTimestamp);
        Assert.False((archived with { ArchiveReason = " " }).HasArchiveReason);
        Assert.False((archived with { ArchivedByUserId = null }).HasArchiveActor);
        Assert.False((archived with { ArchivedAt = null }).HasArchiveTimestamp);
        Assert.False(detail.HasReplacement);
        Assert.True((detail with { Status = "Replaced", ReplacedByMaterialId = Guid.NewGuid() }).HasReplacement);
        Assert.Equal("Estándar no disponible", (detail with { ReplacedByMaterialName = null }).ReplacementDisplay);
    }

    [Theory]
    [InlineData("Active", "Activo", "Success")]
    [InlineData("ExpiringSoon", "Por vencer", "Warning")]
    [InlineData("Expired", "Vencido", "Danger")]
    [InlineData("Depleted", "Agotado", "Warning")]
    [InlineData("Blocked", "Bloqueado", "Danger")]
    [InlineData("Archived", "Archivado", "Neutral")]
    [InlineData("FutureInternalCode", "Estado no disponible", "Neutral")]
    public void StatesHaveHumanLabelsAndSemanticTones(string code, string label, string tone)
    {
        Assert.Equal(label, ReferenceMaterialPresentation.StatusLabel(code));
        Assert.Equal(tone, ReferenceMaterialPresentation.StatusTone(code));
    }

    [Fact]
    public void DetailJsonAddsNamesAndPreservesInternalValuesWithoutSerializingUiFlags()
    {
        var detail = Detail();
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var json = JsonSerializer.Serialize(detail, options);
        using var document = JsonDocument.Parse(json);
        Assert.Equal("Danny Jimenez", document.RootElement.GetProperty("createdByName").GetString());
        Assert.Equal("Ana López", document.RootElement.GetProperty("updatedByName").GetString());
        Assert.True(document.RootElement.TryGetProperty("archivedByName", out _));
        Assert.Equal(2, document.RootElement.GetProperty("createdByUserId").GetInt32());
        Assert.False(document.RootElement.TryGetProperty("hasArchiveReason", out _));
        var roundTrip = JsonSerializer.Deserialize<ReferenceMaterialDetail>(json, options)!;
        Assert.Equal(detail, roundTrip);
        Assert.Equal(detail.CreatedAt, roundTrip.CreatedAt);
        Assert.Equal(detail.ReceivedDate, roundTrip.ReceivedDate);
        Assert.Equal(detail.PurityPercent, roundTrip.PurityPercent);
        Assert.Equal(detail.Version, roundTrip.Version);
    }

    [Fact]
    public void RecordFallbacksForScreenReadersContainOnlyHumanNames()
    {
        var detail = Detail();
        Assert.Equal("Naphthol AS", detail.ToString());
        var summary = new ReferenceMaterialSummary(detail.Id, detail.Name, detail.CasNumber, detail.CatalogNumber,
            detail.Method, detail.Lot, detail.Brand, detail.PurityPercent, detail.ExpirationDate,
            detail.Status, detail.TotalQuantity, detail.Unit, detail.AvailableQuantity, detail.Version);
        Assert.Equal("Naphthol AS", summary.ToString());
        Assert.Equal("Phtalatos", new ReferenceMethodOption(123, "Phtalatos").ToString());
        Assert.Equal("Laboratorio", new ReferenceLocationOption(456, "Laboratorio").ToString());
        Assert.Equal("mg — Miligramo", new ReferenceUnitOption(789, "Miligramo", "mg").ToString());
    }

    [Fact]
    public void OlderDetailPayloadCanStillBeReadWithHumanFallback()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(Detail()))!.AsObject();
        node.Remove("CreatedByName");
        node.Remove("UpdatedByName");
        node.Remove("ArchivedByName");
        node.Remove("ReplacedByMaterialName");
        var detail = node.Deserialize<ReferenceMaterialDetail>()!;
        Assert.Equal("Usuario no disponible", detail.CreatedByDisplay);
        Assert.Equal(2, detail.CreatedByUserId);
    }

    private static ReferenceMaterialDetail Detail() => new(
        Guid.NewGuid(), "Naphthol AS", "92-77-3", "DRE-C15431000", 1, "Phtalatos", 98.0900m,
        "H1622997", "Dr. Ehrenstorfer", new DateOnly(2026, 10, 3), new DateOnly(2029, 5, 6),
        100.000000m, 3, "mg", 1, 100m, 100m, "20 °C ± 4 °C", 1, "Laboratorio", "Active",
        2, new DateTimeOffset(2026, 10, 3, 13, 51, 40, TimeSpan.Zero), 3,
        new DateTimeOffset(2026, 10, 3, 13, 51, 40, TimeSpan.Zero), null, null, null, null,
        Guid.NewGuid(), "Danny Jimenez", "Ana López");
}

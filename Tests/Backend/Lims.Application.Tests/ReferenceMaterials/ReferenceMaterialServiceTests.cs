using Lims.Application.ReferenceMaterials;
using Lims.Application.ReferenceMaterials.Ports;
using Lims.Contracts.Errors;
using Lims.Contracts.ReferenceMaterials;
using Lims.Domain.ReferenceMaterials;

namespace Lims.Application.Tests.ReferenceMaterials;

public sealed class ReferenceMaterialServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CreatePersistsDecimalValuesAndDoesNotReplaceExistingMaterial()
    {
        var repository = new FakeRepository();
        var service = new ReferenceMaterialService(repository, new FixedTimeProvider(Now));

        var result = await service.CreateAsync(CreateRequest(), 42, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(repository.Items);
        Assert.Equal("Active", result.Value?.Status);
        Assert.Equal(25m, result.Value?.TotalQuantity);
        Assert.Equal(25m, result.Value?.AvailableQuantity);
    }

    [Fact]
    public async Task InvalidCasReturnsFieldValidationError()
    {
        var service = new ReferenceMaterialService(new FakeRepository(), new FixedTimeProvider(Now));
        var request = CreateRequest() with { CasNumber = "64-17-6" };

        var result = await service.CreateAsync(request, 42, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.ValidationError, result.Error?.Code);
        Assert.Contains("casNumber", result.Error?.ValidationErrors?.Keys ?? []);
    }

    [Fact]
    public async Task InactiveOrUnknownCatalogUnitReturnsFieldValidationError()
    {
        var service = new ReferenceMaterialService(new FakeRepository(), new FixedTimeProvider(Now));
        var request = CreateRequest() with { UnitId = 6 };

        var result = await service.CreateAsync(request, 42, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.ValidationError, result.Error?.Code);
        Assert.Contains("unitId", result.Error?.ValidationErrors?.Keys ?? []);
    }

    [Fact]
    public async Task FreeFormStorageTemperatureIsPreserved()
    {
        var service = new ReferenceMaterialService(new FakeRepository(), new FixedTimeProvider(Now));
        var request = CreateRequest() with { StorageTemperature = "2-8 °C" };

        var result = await service.CreateAsync(request, 42, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("2-8 °C", result.Value?.StorageTemperature);
    }

    [Fact]
    public async Task StaleVersionCannotArchiveMaterial()
    {
        var repository = new FakeRepository();
        var service = new ReferenceMaterialService(repository, new FixedTimeProvider(Now));
        var created = await service.CreateAsync(CreateRequest(), 42, CancellationToken.None);

        var result = await service.ArchiveAsync(
            created.Value!.Id,
            new ArchiveReferenceMaterialRequest("Fin de uso", Guid.NewGuid()),
            42,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.Conflict, result.Error?.Code);
        Assert.Equal(ReferenceMaterialStatus.Active, repository.Items.Single().Status);
    }

    [Fact]
    public async Task EmptyIdentifiersNeverTriggerAutomaticReplacement()
    {
        var repository = new FakeRepository();
        var service = new ReferenceMaterialService(repository, new FixedTimeProvider(Now));
        var request = CreateRequest() with { CasNumber = null, CatalogNumber = null };

        var first = await service.CreateAsync(request, 42, CancellationToken.None);
        var second = await service.CreateAsync(request with { Lot = "LOT-2027" }, 42, CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(2, repository.Items.Count);
        Assert.All(repository.Items, material => Assert.Equal(ReferenceMaterialStatus.Active, material.Status));
    }

    [Fact]
    public async Task ExplicitReplacementCreatesNewMaterialAndLinksSourceAtomically()
    {
        var repository = new FakeRepository();
        var service = new ReferenceMaterialService(repository, new FixedTimeProvider(Now));
        var original = await service.CreateAsync(CreateRequest(), 42, CancellationToken.None);
        var saveCallsBeforeReplacement = repository.SaveCalls;

        var result = await service.ReplaceAsync(
            original.Value!.Id,
            new ReplaceReferenceMaterialRequest(
                CreateRequest() with { Lot = "LOT-2027" },
                "Nuevo lote certificado",
                original.Value.Version),
            84,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, repository.Items.Count);
        var source = repository.Items.Single(item => item.Id == original.Value.Id);
        Assert.Equal(ReferenceMaterialStatus.Replaced, source.Status);
        Assert.Equal(result.Value!.Id, source.ReplacedByMaterialId);
        Assert.Equal("Nuevo lote certificado", source.ArchiveReason);
        Assert.Equal(saveCallsBeforeReplacement + 1, repository.SaveCalls);
    }

    [Fact]
    public async Task UpdateChangesEditableFieldsAndKeepsUnconsumedBalanceAligned()
    {
        var repository = new FakeRepository();
        var service = new ReferenceMaterialService(repository, new FixedTimeProvider(Now));
        var created = await service.CreateAsync(CreateRequest(), 42, CancellationToken.None);
        var request = CreateRequest();

        var result = await service.UpdateAsync(
            created.Value!.Id,
            new UpdateReferenceMaterialRequest(
                request.Name,
                request.CasNumber,
                request.CatalogNumber,
                request.MethodId,
                request.PurityPercent,
                request.Lot,
                "Marca actualizada",
                request.ReceivedDate,
                request.ExpirationDate,
                10m,
                request.UnitId,
                4,
                request.StorageTemperature,
                request.LocationId,
                created.Value.Version),
            84,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Marca actualizada", result.Value?.Brand);
        Assert.Equal(40m, result.Value?.TotalQuantity);
        Assert.Equal(40m, result.Value?.AvailableQuantity);
        Assert.Equal(84, result.Value?.UpdatedByUserId);
    }

    [Fact]
    public async Task ArchiveMarksMaterialAndReturnsAuditData()
    {
        var repository = new FakeRepository();
        var service = new ReferenceMaterialService(repository, new FixedTimeProvider(Now));
        var created = await service.CreateAsync(CreateRequest(), 42, CancellationToken.None);

        var result = await service.ArchiveAsync(
            created.Value!.Id,
            new ArchiveReferenceMaterialRequest("Certificado retirado", created.Value.Version),
            84,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Archived", result.Value?.Status);
        Assert.Equal("Certificado retirado", result.Value?.ArchiveReason);
        Assert.Equal(84, result.Value?.ArchivedByUserId);
    }

    [Fact]
    public async Task ListPassesSearchStatusMethodDateAndPagingToRepository()
    {
        var repository = new FakeRepository();
        var service = new ReferenceMaterialService(repository, new FixedTimeProvider(Now));

        var result = await service.ListAsync(" etanol ", "Active", " APEOs ", 2, 10, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("etanol", repository.LastSearch);
        Assert.Equal(ReferenceMaterialStatus.Active, repository.LastStatus);
        Assert.Equal("APEOs", repository.LastMethod);
        Assert.Equal(new DateOnly(2026, 9, 30), repository.LastAsOfDate);
        Assert.Equal(2, repository.LastPage);
        Assert.Equal(10, repository.LastPageSize);
    }

    private static CreateReferenceMaterialRequest CreateRequest() => new(
        "Etanol CRM",
        "64-17-5",
        "CAT-001",
        1,
        99.5m,
        "LOT-2026",
        "Proveedor",
        new DateOnly(2026, 9, 30),
        new DateOnly(2027, 9, 30),
        5m,
        2,
        5,
        "4 °C",
        1);

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }

    private sealed class FakeRepository : IReferenceMaterialRepository
    {
        private static readonly ReferenceMethod Method = new(1, "APEOs", true);
        private static readonly ReferenceUnit Unit = new(2, "Gramo", "g", true);
        private static readonly ReferenceLocation Location = new(1, "Laboratorio", true);

        public List<ReferenceMaterial> Items { get; } = [];
        public int SaveCalls { get; private set; }
        public string? LastSearch { get; private set; }
        public ReferenceMaterialStatus? LastStatus { get; private set; }
        public string? LastMethod { get; private set; }
        public DateOnly LastAsOfDate { get; private set; }
        public int LastPage { get; private set; }
        public int LastPageSize { get; private set; }

        public Task<ReferenceMaterialCatalogSelection> ResolveCatalogsAsync(
            int methodId,
            int unitId,
            int locationId,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ReferenceMaterialCatalogSelection(
                methodId == Method.Id ? Method : null,
                unitId == Unit.Id ? Unit : null,
                locationId == Location.Id ? Location : null));

        public Task<IReadOnlyList<ReferenceMethod>> ListActiveMethodsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ReferenceMethod>>([Method]);

        public Task<IReadOnlyList<ReferenceUnit>> ListActiveUnitsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ReferenceUnit>>([Unit]);

        public Task<IReadOnlyList<ReferenceLocation>> ListActiveLocationsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ReferenceLocation>>([Location]);

        public Task<(IReadOnlyList<ReferenceMaterial> Items, int TotalCount)> SearchAsync(
            string? search,
            ReferenceMaterialStatus? status,
            string? method,
            DateOnly asOfDate,
            int page,
            int pageSize,
            CancellationToken cancellationToken)
        {
            LastSearch = search;
            LastStatus = status;
            LastMethod = method;
            LastAsOfDate = asOfDate;
            LastPage = page;
            LastPageSize = pageSize;
            return Task.FromResult(((IReadOnlyList<ReferenceMaterial>)Items, Items.Count));
        }

        public Task<ReferenceMaterial?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Items.SingleOrDefault(item => item.Id == id));

        public void Add(ReferenceMaterial material) => Items.Add(material);

        public Task<bool> SaveChangesAsync(CancellationToken cancellationToken)
        {
            SaveCalls++;
            return Task.FromResult(true);
        }
    }
}

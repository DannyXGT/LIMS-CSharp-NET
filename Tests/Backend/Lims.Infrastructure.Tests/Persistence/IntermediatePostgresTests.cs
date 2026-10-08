using Lims.Application.ReferencePreparations;
using Lims.Contracts.Errors;
using Lims.Contracts.ReferencePreparations;
using Lims.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Lims.Infrastructure.Tests.Persistence;

public sealed class IntermediatePostgresTests
{
    [StockPostgresFact]
    public async Task MultiComponentCommitBalancesSnapshotsActorAndIdempotentRequestAreConsistent()
    {
        await using var fixture = await StockPostgresTests.Fixture.CreateAsync(10000);
        var sources = await Stocks(fixture);
        await using var db = fixture.Context(); var service = Service(db);
        var request = Request(sources, 10, 15);
        var created = await service.CreateAsync(request, 84, CancellationToken.None);
        Assert.True(created.IsSuccess, created.Error?.Message);
        var detail = created.Value!;
        Assert.Equal(25, detail.Calculation.TotalVolumeTaken);
        Assert.Equal(2, detail.Consumptions.Count); Assert.Equal(2, detail.Calculation.Components.Count);
        Assert.Equal("Ana López", detail.Preparation.PreparedBy); Assert.All(detail.Consumptions, m => Assert.Equal("Ana López", m.Actor));
        Assert.Equal(990, await db.ReferencePreparations.Where(p => p.Id == sources[0].Id).Select(p => p.AvailableVolume).SingleAsync());
        Assert.Equal(985, await db.ReferencePreparations.Where(p => p.Id == sources[1].Id).Select(p => p.AvailableVolume).SingleAsync());
        var repeat = await service.CreateAsync(request, 84, CancellationToken.None);
        Assert.True(repeat.IsSuccess); Assert.Equal(detail.Preparation.Code, repeat.Value!.Preparation.Code);
        Assert.Equal(2, await db.ReferenceMaterialMovements.CountAsync(m => m.PreparationId == request.RequestId));
        Assert.Equal(ErrorCodes.Conflict, (await service.CreateAsync(request with { Name = "Otro nombre" }, 84, CancellationToken.None)).Error?.Code);
        var stored = await db.ReferencePreparations.SingleAsync(p => p.Id == request.RequestId);
        Assert.Null(stored.SourceMaterialId); Assert.Equal("Intermedia", stored.Kind); Assert.Equal(84, stored.PreparedByUserId);
        Assert.All(await db.ReferencePreparationComponents.Where(c => c.PreparationId == stored.Id).ToArrayAsync(), c => Assert.Contains("STK-", c.SourceSnapshotJson, StringComparison.Ordinal));
    }
    [StockPostgresFact]
    public async Task AvailabilityFailureOrWriteFailureRollsBackAllSourcesComponentsAndMovements()
    {
        await using var fixture = await StockPostgresTests.Fixture.CreateAsync(10000); var sources = await Stocks(fixture);
        await using (var db = fixture.Context())
        {
            var result = await Service(db).CreateAsync(Request(sources, 10, 1001) with { Calculation = Request(sources, 10, 1001).Calculation with { FinalVolume = 2000 } }, 42, CancellationToken.None);
            Assert.Equal(ErrorCodes.InvalidState, result.Error?.Code);
        }
        await using (var failing = fixture.Context(new FailAfterSave()))
            await Assert.ThrowsAsync<IOException>(() => Service(failing).CreateAsync(Request(sources, 10, 15), 42, CancellationToken.None));
        await using var check = fixture.Context();
        Assert.Empty(await check.ReferencePreparations.Where(p => p.Kind == "Intermedia").ToArrayAsync());
        Assert.Empty(await check.ReferencePreparationComponents.ToArrayAsync());
        Assert.Empty(await check.ReferenceMaterialMovements.Where(m => m.Kind == "IntermediateConsumption").ToArrayAsync());
        Assert.All(await check.ReferencePreparations.Where(p => p.Kind == "Stock").ToArrayAsync(), p => Assert.Equal(1000, p.AvailableVolume));
    }
    [StockPostgresFact]
    public async Task ConcurrentRequestsCannotOverconsumeAndLoserHasNoPartialDebit()
    {
        await using var fixture = await StockPostgresTests.Fixture.CreateAsync(10000); var sources = await Stocks(fixture);
        var barrier = new SaveBarrier(); await using var a = fixture.Context(barrier); await using var b = fixture.Context(barrier);
        var request = Request(sources, 600, 10); request = request with { Calculation = request.Calculation with { FinalVolume = 1000 } };
        var results = await Task.WhenAll(Service(a).CreateAsync(request, 42, CancellationToken.None), Service(b).CreateAsync(request with { RequestId = Guid.NewGuid() }, 84, CancellationToken.None));
        Assert.Single(results, r => r.IsSuccess); Assert.Single(results, r => r.Error?.Code == ErrorCodes.Conflict);
        await using var check = fixture.Context();
        Assert.Equal(400, await check.ReferencePreparations.Where(p => p.Id == sources[0].Id).Select(p => p.AvailableVolume).SingleAsync());
        Assert.Equal(990, await check.ReferencePreparations.Where(p => p.Id == sources[1].Id).Select(p => p.AvailableVolume).SingleAsync());
        Assert.Equal(2, await check.ReferencePreparationComponents.CountAsync());
        Assert.Equal(1, await check.ReferencePreparations.CountAsync(p => p.Kind == "Intermedia"));
    }
    [StockPostgresFact]
    public async Task ParentDilutionConsumesOneAliquotRetainsAnalyticalSnapshotAndGeneratesUniqueCode()
    {
        await using var fixture = await StockPostgresTests.Fixture.CreateAsync(10000); var sources = await Stocks(fixture);
        await using var db = fixture.Context(); var service = Service(db);
        var parent = (await service.CreateAsync(Request(sources, 10, 15), 42, CancellationToken.None)).Value!;
        var option = (await service.OptionsAsync(CancellationToken.None)).Value!.Sources.Single(s => s.Id == parent.Preparation.Id);
        var request = new CreateIntermediateRequest(Guid.NewGuid(), "Dilución de Intermedia", new(option.MethodId, 100, "mL", [new(option.Id, option.Version, 10, "mL")], option.Id), Today, Today.AddDays(10), null);
        var child = await service.CreateAsync(request, 84, CancellationToken.None);
        Assert.True(child.IsSuccess, child.Error?.Message); Assert.Single(child.Value!.Consumptions);
        Assert.Equal(90, await db.ReferencePreparations.Where(p => p.Id == option.Id).Select(p => p.AvailableVolume).SingleAsync());
        Assert.NotEqual(parent.Preparation.Code, child.Value.Preparation.Code);
        Assert.Equal(parent.Preparation.Results[0].Concentration / 10m, child.Value.Preparation.Results[0].Concentration);
        Assert.Equal(2, child.Value.Preparation.Results.Count);
        var reopened = (await service.GetAsync(child.Value.Preparation.Id, CancellationToken.None)).Value!;
        Assert.Equal(option.Version, reopened.Calculation.Components[0].Source.Version);
        Assert.Equal(100, reopened.Calculation.Components[0].Source.AvailableVolume);
    }
    [StockPostgresFact]
    public async Task InvalidDatesActorMethodAndStaleSourceLeaveNoPreparation()
    {
        await using var fixture = await StockPostgresTests.Fixture.CreateAsync(10000); var sources = await Stocks(fixture);
        await using var db = fixture.Context(); var service = Service(db); var request = Request(sources, 10, 15);
        Assert.Equal(ErrorCodes.InvalidSession, (await service.CreateAsync(request, 0, CancellationToken.None)).Error?.Code);
        foreach (var invalid in new[] { request with { PreparationDate = Today.AddDays(1) }, request with { ExpirationDate = Today.AddDays(-1) }, request with { Calculation = request.Calculation with { MethodId = -1 } } })
            Assert.Equal(ErrorCodes.ValidationError, (await service.CreateAsync(invalid, 42, CancellationToken.None)).Error?.Code);
        var stale = request with { Calculation = request.Calculation with { Components = [request.Calculation.Components[0] with { SourceVersion = Guid.NewGuid() }] } };
        Assert.Equal(ErrorCodes.Conflict, (await service.CreateAsync(stale, 42, CancellationToken.None)).Error?.Code);
        Assert.Empty(await db.ReferencePreparationComponents.ToArrayAsync());
    }
    private static DateOnly Today => DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-6)).DateTime);
    private static IntermediateService Service(LimsDbContext db) => new(new IntermediateRepository(db), TimeProvider.System);
    private static CreateIntermediateRequest Request(IntermediateSource[] sources, decimal a, decimal b) => new(Guid.NewGuid(), "Mezcla de validación",
        new(sources[0].MethodId, 100, "mL", [new(sources[0].Id, sources[0].Version, a, "mL"), new(sources[1].Id, sources[1].Version, b, "mL")]), Today, Today.AddDays(30), "Datos ficticios");
    private static async Task<IntermediateSource[]> Stocks(StockPostgresTests.Fixture fixture)
    {
        await using var db = fixture.Context(); var service = new StockService(new StockRepository(db), TimeProvider.System);
        Assert.True((await service.CreateAsync(fixture.Request(1000), 42, CancellationToken.None)).IsSuccess);
        var old = fixture.Source;
        var material = new Lims.Domain.ReferenceMaterials.ReferenceMaterial(Guid.NewGuid(), "Otro analito", "50-00-0", "CAT-002", old.Method, 100,
            "LOT-002", "Marca", old.ReceivedDate, old.ExpirationDate, 10000, old.Unit, 1, old.StorageTemperature, old.Location, 42, DateTimeOffset.UtcNow);
        // Use tracked catalogue entities from this DbContext rather than attaching duplicate navigation instances.
        db.ChangeTracker.Clear();
        material = new(material.Id, material.Name, material.CasNumber, material.CatalogNumber, await db.ReferenceMethods.SingleAsync(m => m.Id == old.MethodId), 100,
            material.Lot, material.Brand, old.ReceivedDate, old.ExpirationDate, 10000, await db.ReferenceUnits.SingleAsync(u => u.Id == old.UnitId), 1,
            old.StorageTemperature, await db.ReferenceLocations.SingleAsync(l => l.Id == old.LocationId), 42, DateTimeOffset.UtcNow);
        db.ReferenceMaterials.Add(material); await db.SaveChangesAsync();
        var request = fixture.Request(1000); request = request with { Calculation = request.Calculation with { SourceMaterialId = material.Id, SourceVersion = material.Version } };
        Assert.True((await service.CreateAsync(request, 42, CancellationToken.None)).IsSuccess);
        return (await new IntermediateRepository(db).OptionsAsync(Today, CancellationToken.None)).Sources.Where(s => s.Kind == "Stock").ToArray();
    }
    private sealed class FailAfterSave : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default) => throw new IOException("Injected failure after writes");
    }
    private sealed class SaveBarrier : SaveChangesInterceptor
    {
        private int _calls; private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _calls) == 2) _ready.TrySetResult();
            await _ready.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken); return result;
        }
    }
}

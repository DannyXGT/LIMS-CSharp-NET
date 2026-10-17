using Lims.Application.ReferencePreparations;
using Lims.Contracts.Errors;
using Lims.Contracts.ReferencePreparations;
using Lims.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Lims.Infrastructure.Tests.Persistence;

public sealed class SolutionGroupPostgresTests
{
    [StockPostgresFact]
    public async Task SeveralPhysicalOriginsCommitTogetherAndInjectedFailureRestoresBoth()
    {
        await using var f = await StockPostgresTests.Fixture.CreateAsync(10000); var first = await Source(f);
        IntermediateSource second;
        await using (var setup = f.Context())
        {
            var intermediate = new IntermediateService(new IntermediateRepository(setup), TimeProvider.System);
            var stock = (await intermediate.OptionsAsync(default)).Value!.Sources.Single(s => s.Kind == "Stock");
            var created = await intermediate.CreateAsync(new(Guid.NewGuid(), "Segundo origen físico", new(stock.MethodId, 100, "mL", [new(stock.Id, stock.Version, 10, "mL")]), Today, Today.AddDays(200), null), 42, default);
            Assert.True(created.IsSuccess, created.Error?.Message);
            second = (await Service(setup).OptionsAsync(default)).Sources.Single(s => s.Id == created.Value!.Preparation.Id);
        }
        var request = Request(first, "MULTI", 10);
        request = request with { Calculation = request.Calculation with { Parts = [request.Calculation.Parts[0], new(second.Id, second.Version, 15, "mL", 50, "mL", true)] } };
        await using (var failing = f.Context(new FailAfterSave()))
            await Assert.ThrowsAsync<IOException>(() => Service(failing).CreateAsync("Curva", request, 42, default));
        await using var check = f.Context(); var service = Service(check);
        Assert.Equal(100, (await Fresh(service, first.Id)).AvailableVolume); Assert.Equal(100, (await Fresh(service, second.Id)).AvailableVolume);
        Assert.Empty(await check.ReferencePreparationComponents.Where(c => c.PreparationId == request.RequestId).ToArrayAsync());
        Assert.Empty(await check.ReferenceMaterialMovements.Where(m => m.PreparationId == request.RequestId).ToArrayAsync());
        var saved = await service.CreateAsync("AQS", request, 84, default);
        Assert.True(saved.IsSuccess, saved.Error?.Message); Assert.Equal(2, saved.Value!.Consumptions.Count);
        Assert.Equal(90, (await Fresh(service, first.Id)).AvailableVolume); Assert.Equal(85, (await Fresh(service, second.Id)).AvailableVolume);
    }
    [StockPostgresFact]
    public async Task DerivedIntermediateAndCurveCompeteAgainstTheSameVersionAndBalance()
    {
        await using var f = await StockPostgresTests.Fixture.CreateAsync(10000); var source = await Source(f);
        var barrier = new SaveBarrier(); await using var a = f.Context(barrier); await using var b = f.Context(barrier);
        var curve = Service(a).CreateAsync("Curva", Request(source, "COMPETE", 60), 42, default);
        var intermediate = new IntermediateService(new IntermediateRepository(b), TimeProvider.System).CreateAsync(new(Guid.NewGuid(), "Derivada concurrente",
            new(source.MethodId, 100, "mL", [new(source.Id, source.Version, 60, "mL")], source.Id), Today, Today.AddDays(2), null), 84, default);
        await Task.WhenAll(curve, intermediate);
        var curveResult = await curve; var intermediateResult = await intermediate;
        Assert.NotEqual(curveResult.IsSuccess, intermediateResult.IsSuccess);
        Assert.Equal(ErrorCodes.Conflict, curveResult.IsSuccess ? intermediateResult.Error?.Code : curveResult.Error?.Code);
        await using var check = f.Context();
        Assert.Equal(40, await check.ReferencePreparations.Where(p => p.Id == source.Id).Select(p => p.AvailableVolume).SingleAsync());
        Assert.Single(await check.ReferenceMaterialMovements.Where(m => m.SourcePreparationId == source.Id).ToArrayAsync());
    }
    [StockPostgresFact]
    public async Task MigrationSqlIsAdditiveIdempotentPreservesStockAndGrantsNoUserPermissions()
    {
        await using var f = await StockPostgresTests.Fixture.CreateAsync(10000);
        var source = await Source(f);
        await using var db = f.Context();
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE reference_preparations DROP CONSTRAINT ck_reference_preparations_kind_status; ALTER TABLE reference_preparations ADD CONSTRAINT ck_reference_preparations_kind_status CHECK (kind IN ('Stock','Intermedia')); DELETE FROM permission_catalog WHERE permission_code IN ('curva.view','curva.create','aqs.view','aqs.create'); CREATE TABLE \"__EFMigrationsHistory\" (\"MigrationId\" varchar(150) PRIMARY KEY, \"ProductVersion\" varchar(32) NOT NULL); INSERT INTO \"__EFMigrationsHistory\" VALUES ('20261013065016_PreservePreparationDecimalScale','10.0.10');");
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../artifacts/sql/reference_curve_aqs.sql"));
        var script = await File.ReadAllTextAsync(path);
        await db.Database.ExecuteSqlRawAsync(script); await db.Database.ExecuteSqlRawAsync(script);
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Empty(await db.Set<Lims.Domain.Identity.UserPermissionAssignment>().ToArrayAsync());
        Assert.Equal(4, await db.Set<Lims.Domain.Identity.PermissionCode>().CountAsync(p => p.Code.StartsWith("curva.") || p.Code.StartsWith("aqs.")));
        Assert.True((await Service(db).CreateAsync("AQS", Request(source, "MIGRATION-AQS", 10), 42, default)).IsSuccess);
        Assert.Single(await db.ReferencePreparations.Where(p => p.Kind == "Stock").ToArrayAsync());
    }
    [StockPostgresFact]
    public async Task Aqs10Curve15Share75AndDerivedIntermediateConsumesTheSameBalance()
    {
        await using var f = await StockPostgresTests.Fixture.CreateAsync(10000);
        var source = await Source(f);
        await using var db = f.Context(); var service = Service(db);
        var aqs = await service.CreateAsync("AQS", Request(source, "AQS-10", 10), 42, default);
        Assert.True(aqs.IsSuccess, aqs.Error?.Message);
        Assert.Equal(90, (await Fresh(service, source.Id)).AvailableVolume);
        source = await Fresh(service, source.Id);
        var curveRequest = Request(source, "CURVA-15", 15);
        var curve = await service.CreateAsync("Curva", curveRequest, 84, default);
        Assert.True(curve.IsSuccess, curve.Error?.Message);
        source = await Fresh(service, source.Id); Assert.Equal(75, source.AvailableVolume);
        Assert.Equal(75, (await new IntermediateRepository(db).OptionsAsync(Today, default)).Sources.Single(s => s.Id == source.Id).AvailableVolume);
        var replay = await service.CreateAsync("Curva", curveRequest, 84, default);
        Assert.True(replay.IsSuccess); Assert.Equal(75, (await Fresh(service, source.Id)).AvailableVolume);
        Assert.Equal(ErrorCodes.Conflict, (await service.CreateAsync("Curva", curveRequest with { Code = "CAMBIO" }, 84, default)).Error?.Code);
        var derived = await new IntermediateService(new IntermediateRepository(db), TimeProvider.System).CreateAsync(
            new(Guid.NewGuid(), "Derivada", new(source.MethodId, 100, "mL", [new(source.Id, source.Version, 5, "mL")], source.Id), Today, Today.AddDays(2), null), 42, default);
        Assert.True(derived.IsSuccess, derived.Error?.Message); Assert.Equal(70, (await Fresh(service, source.Id)).AvailableVolume);
        var detail = (await service.GetAsync("Curva", curve.Value!.Preparation.Id, default)).Value!;
        Assert.Equal(source.Id, detail.Calculation.Parts[0].Source.Id); Assert.Equal("LOT-001", detail.Calculation.Parts[0].Results[0].Lot);
        Assert.Equal("Ana López", detail.Preparation.PreparedBy); Assert.Equal(15, Assert.Single(detail.Consumptions).Quantity);
        var history = (await new IntermediateService(new IntermediateRepository(db), TimeProvider.System).GetAsync(source.Id, default)).Value!.UsageHistory!;
        Assert.Equal(3, history.Count); Assert.Equal(30, history.Sum(h => h.Quantity));
        Assert.Equal(0, await db.ReferencePreparations.Where(p => p.Id == detail.Preparation.Id).Select(p => p.AvailableVolume).SingleAsync());
        Assert.DoesNotContain((await service.OptionsAsync(default)).Sources, s => s.Id == detail.Preparation.Id);
    }
    [StockPostgresFact]
    public async Task VariablePointsSameOriginCommitAsOnePhysicalDebitAndListAsOneGroup()
    {
        await using var f = await StockPostgresTests.Fixture.CreateAsync(10000); var source = await Source(f);
        await using var db = f.Context(); var service = Service(db);
        var request = Request(source, "CURVA-GRUPO", 10);
        request = request with { Calculation = request.Calculation with { Parts = [request.Calculation.Parts[0], request.Calculation.Parts[0] with { VolumeTaken = 15, FinalVolume = 50, IsInternalStandard = true }] } };
        var result = await service.CreateAsync("Curva", request, 42, default);
        Assert.True(result.IsSuccess, result.Error?.Message); Assert.Equal(2, result.Value!.Calculation.Parts.Count);
        Assert.Equal(25, Assert.Single(result.Value.Consumptions).Quantity); Assert.Equal(75, (await Fresh(service, source.Id)).AvailableVolume);
        Assert.Equal(1, await db.ReferencePreparationComponents.CountAsync(c => c.PreparationId == request.RequestId));
        var page = (await service.ListAsync("Curva", source.Code, 1, 1, default)).Value!;
        Assert.Equal(1, page.TotalCount); Assert.Equal(2, Assert.Single(page.Items).ComponentCount);
        Assert.Empty((await service.ListAsync("AQS", null, 1, 25, default)).Value!.Items);
        Assert.Empty((await service.ListAsync("Curva", "%_", 1, 25, default)).Value!.Items);
        Assert.Empty((await service.ListAsync("Curva", null, 2, 1, default)).Value!.Items);
        Assert.Equal(ErrorCodes.NotFound, (await service.GetAsync("AQS", request.RequestId, default)).Error?.Code);
    }
    [StockPostgresFact]
    public async Task InsufficientStaleDuplicateCodeAndInjectedWriteFailureRollbackEntireGroup()
    {
        await using var f = await StockPostgresTests.Fixture.CreateAsync(10000); var source = await Source(f);
        await using (var db = f.Context())
        {
            var service = Service(db); var request = Request(source, "FAIL", 60);
            var excessive = request with { Calculation = request.Calculation with { Parts = [request.Calculation.Parts[0], request.Calculation.Parts[0]] } };
            Assert.Equal(ErrorCodes.InvalidState, (await service.CreateAsync("AQS", excessive, 42, default)).Error?.Code);
            var stale = request with { Calculation = request.Calculation with { Parts = [request.Calculation.Parts[0] with { SourceVersion = Guid.NewGuid() }] } };
            Assert.Equal(ErrorCodes.Conflict, (await service.CreateAsync("Curva", stale, 42, default)).Error?.Code);
        }
        await using (var failing = f.Context(new FailAfterSave()))
            await Assert.ThrowsAsync<IOException>(() => Service(failing).CreateAsync("AQS", Request(source, "FAIL", 10), 42, default));
        await using var check = f.Context();
        Assert.Equal(100, await check.ReferencePreparations.Where(p => p.Id == source.Id).Select(p => p.AvailableVolume).SingleAsync());
        Assert.Empty(await check.ReferencePreparations.Where(p => p.Kind == "AQS" || p.Kind == "Curva").ToArrayAsync());
        var s = Service(check); Assert.True((await s.CreateAsync("AQS", Request(source, "SAME", 10), 42, default)).IsSuccess);
        source = await Fresh(s, source.Id);
        Assert.Equal(ErrorCodes.Conflict, (await s.CreateAsync("Curva", Request(source, "SAME", 10), 42, default)).Error?.Code);
        Assert.Equal(90, (await Fresh(s, source.Id)).AvailableVolume);
    }
    [StockPostgresFact]
    public async Task ConcurrentCurveAndAqsCannotOverdrawAndLoserHasNoComponentsOrMovements()
    {
        await using var f = await StockPostgresTests.Fixture.CreateAsync(10000); var source = await Source(f);
        var barrier = new SaveBarrier(); await using var a = f.Context(barrier); await using var b = f.Context(barrier);
        var result = await Task.WhenAll(Service(a).CreateAsync("Curva", Request(source, "CURVA-CONCURRENT", 60), 42, default),
            Service(b).CreateAsync("AQS", Request(source, "AQS-CONCURRENT", 60), 84, default));
        var winner = Assert.Single(result, r => r.IsSuccess).Value!; Assert.Single(result, r => r.Error?.Code == ErrorCodes.Conflict);
        await using var check = f.Context();
        Assert.Equal(40, await check.ReferencePreparations.Where(p => p.Id == source.Id).Select(p => p.AvailableVolume).SingleAsync());
        Assert.Equal(1, await check.ReferencePreparations.CountAsync(p => p.Kind == "Curva" || p.Kind == "AQS"));
        Assert.Equal(1, await check.ReferencePreparationComponents.CountAsync(c => c.PreparationId == winner.Preparation.Id));
        Assert.Single(await check.ReferenceMaterialMovements.Where(m => m.SourcePreparationId == source.Id).ToArrayAsync());
    }
    private static SolutionGroupService Service(LimsDbContext db) => new(new SolutionGroupRepository(db), TimeProvider.System);
    private static DateOnly Today => DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-6)).Date);
    private static CreateSolutionGroupRequest Request(IntermediateSource s, string code, decimal taken) => new(Guid.NewGuid(), code, "Validación aislada",
        new(s.MethodId, [new(s.Id, s.Version, taken, "mL", 100, "mL")]), Today, Today.AddDays(2), "No producción");
    private static async Task<IntermediateSource> Fresh(SolutionGroupService service, Guid id) => (await service.OptionsAsync(default)).Sources.Single(s => s.Id == id);
    private static async Task<IntermediateSource> Source(StockPostgresTests.Fixture f)
    {
        await using var db = f.Context(); var stock = new StockService(new StockRepository(db), TimeProvider.System);
        Assert.True((await stock.CreateAsync(f.Request(1000), 42, default)).IsSuccess);
        var service = new IntermediateService(new IntermediateRepository(db), TimeProvider.System);
        var s = (await service.OptionsAsync(default)).Value!.Sources.Single();
        var result = await service.CreateAsync(new(Guid.NewGuid(), "Intermedia base", new(s.MethodId, 100, "mL", [new(s.Id, s.Version, 10, "mL")]), Today, Today.AddDays(200), null), 42, default);
        Assert.True(result.IsSuccess, result.Error?.Message);
        return (await service.OptionsAsync(default)).Value!.Sources.Single(p => p.Kind == "Intermedia");
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

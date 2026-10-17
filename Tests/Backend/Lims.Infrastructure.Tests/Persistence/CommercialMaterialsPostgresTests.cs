using Lims.Application.ReferenceMaterials;
using Lims.Application.ReferencePreparations;
using Lims.Contracts.ReferenceMaterials;
using Lims.Contracts.ReferencePreparations;
using Lims.Domain.ReferenceMaterials;
using Lims.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Lims.Infrastructure.Tests.Persistence;

public sealed class CommercialMaterialsPostgresTests
{
    private static DateOnly Today => ReferenceMaterialPresentation.ApplicationToday();
    private static ReferenceMaterialService Materials(LimsDbContext db) => new(new ReferenceMaterialRepository(db), TimeProvider.System);
    private static IntermediateService Intermediates(LimsDbContext db) => new(new IntermediateRepository(db), TimeProvider.System);
    private static SolutionGroupService Groups(LimsDbContext db) => new(new SolutionGroupRepository(db), TimeProvider.System);
    private static CommercialCertificate Certificate(int count = 1) => new("CERT-TEST-ISOLATED", "Acetonitrilo", "Certificado de prueba aislada",
        50, "µg/mL", 2, Today.AddDays(-1), 15,
        Enumerable.Range(0, count).Select(i => new CertifiedComponent(Guid.NewGuid(), $"Analito {i + 1}", null,
            i switch { 0 => 50, 1 => 49.9m, 2 => 50.1m, _ => 50 + i / 10m }, "µg/mL", 0.2m, null, $"COMP-{i}", "Dato certificado de prueba")).ToArray(),
        [new(974, "µg/g", 5)]);
    private static CreateReferenceMaterialRequest Request(int count = 1) => new("Frasco de prueba aislada", null, "CAT-COMMERCIAL-TEST", 1,
        null, "LOT-BOTTLE", "Marca de prueba", Today.AddMonths(-1), Today.AddMonths(20), 10, 1, 1, "Ambiente", 1,
        count == 1 ? MaterialPresentations.Solution : MaterialPresentations.Mix, Certificate(count));
    private static CreateIntermediateRequest Dilute(ReferenceMaterialDetail material, decimal volume) => new(Guid.NewGuid(), "Dilución comercial",
        new(material.MethodId, 10, "mL", [new(material.Id, material.Version, volume, "mL")]), Today, Today.AddDays(5), null);
    private static CreateSolutionGroupRequest Group(ReferenceMaterialDetail m, string code, decimal volume) => new(Guid.NewGuid(), code, code,
        new(m.MethodId, [new(m.Id, m.Version, volume, "mL", 10, "mL", false)]), Today, Today.AddDays(1), null, null);

    [StockPostgresFact]
    public async Task DirectRegistrationHasNoPreparationAndSharesPhysicalBalanceAcrossRolesAndConsumption()
    {
        await using var fixture = await StockPostgresTests.Fixture.CreateAsync(100);
        await using var db = fixture.Context();
        var registrations = new CommercialPreparationService(db, TimeProvider.System);
        var input = Request() with { Commercial = new("", "", null, null, null, null, null, null,
            [new(Guid.NewGuid(), "Analito comercial", null, 50, "µg/mL")]) };
        var request = new RegisterCommercialPreparationRequest(Guid.NewGuid(), null, null, input);
        var saved = await registrations.RegisterAsync("Stock", request, 42, default);
        Assert.True(saved.IsSuccess, saved.Error?.Message);
        var material = saved.Value!.Material;
        Assert.StartsWith("STK-", saved.Value.Code, StringComparison.Ordinal);
        Assert.Equal("", material.Commercial!.Reference); Assert.Null(material.PurityPercent);
        Assert.Empty(await db.ReferencePreparations.ToArrayAsync()); Assert.Empty(await db.ReferenceMaterialMovements.ToArrayAsync());
        Assert.Equal(material.Id, (await registrations.RegisterAsync("Stock", request, 42, default)).Value!.Material.Id);
        Assert.False((await registrations.RegisterAsync("Stock", request with { NewMaterial = input with { Lot = "CHANGED" } }, 42, default)).IsSuccess);
        var associated = await registrations.RegisterAsync("Intermedia", new(Guid.NewGuid(), material.Id, material.Version, null), 42, default);
        Assert.True(associated.IsSuccess, associated.Error?.Message);
        Assert.StartsWith("INT-", associated.Value!.Code, StringComparison.Ordinal);
        Assert.Equal(material.Version, associated.Value.Material.Version);
        Assert.Equal(2, await db.Set<ReferenceCommercialRole>().CountAsync());
        Assert.Equal(2, await db.ReferenceMaterials.CountAsync()); // fixture solid and one real commercial bottle
        var stockPage = await new StockRepository(db).ListAsync("Analito comercial", 1, 25, default);
        var stock = Assert.Single(stockPage.Items); Assert.True(stock.IsCommercial); Assert.Null(stock.ActualWeight); Assert.Null(stock.TargetConcentration);
        Assert.Equal(50, stock.ActualConcentration);
        var intermediatePage = await new IntermediateRepository(db).ListAsync(null, 1, 25, Today, default);
        Assert.True(Assert.Single(intermediatePage.Items).IsCommercial);
        var consumed = await Intermediates(db).CreateAsync(Dilute(material, 2), 42, default);
        Assert.True(consumed.IsSuccess, consumed.Error?.Message);
        db.ChangeTracker.Clear();
        Assert.Equal(8, (await registrations.GetAsync("Stock", material.Id, default)).Value!.Material.AvailableQuantity);
        Assert.Equal(8, (await registrations.GetAsync("Intermedia", material.Id, default)).Value!.Material.AvailableQuantity);
        Assert.Equal(8, (await registrations.OptionsAsync("Stock", null, default)).Materials.Single().AvailableQuantity);
        Assert.Single((await registrations.GetAsync("Stock", material.Id, default)).Value!.Material.Movements!);
        Assert.False((await registrations.RegisterAsync("Stock", new(Guid.NewGuid(), material.Id, material.Version, null), 42, default)).IsSuccess);
    }

    [StockPostgresFact]
    public async Task MixDirectRegistrationAcceptsThirtyComponentsAndInternalPreparationsCoexistInPagedList()
    {
        await using var fixture = await StockPostgresTests.Fixture.CreateAsync(100);
        await using var db = fixture.Context(); var registrations = new CommercialPreparationService(db, TimeProvider.System);
        var input = Request(30) with { Commercial = Certificate(30) with { Reference = "", Matrix = "", OpenedDate = null, UseAfterOpeningMonths = null } };
        var saved = await registrations.RegisterAsync("Intermedia", new(Guid.NewGuid(), null, null, input), 42, default);
        Assert.True(saved.IsSuccess, saved.Error?.Message);
        Assert.Equal(30, saved.Value!.Material.Commercial!.Components.Count);
        Assert.False((await registrations.RegisterAsync("Stock", new(Guid.NewGuid(), saved.Value.Material.Id, saved.Value.Material.Version, null), 42, default)).IsSuccess);
        var internalStock = await new StockService(new StockRepository(db), TimeProvider.System).CreateAsync(fixture.Request(50), 42, default);
        Assert.True(internalStock.IsSuccess, internalStock.Error?.Message);
        var intermediate = await Intermediates(db).CreateAsync(Dilute(saved.Value.Material, 1), 42, default);
        Assert.True(intermediate.IsSuccess, intermediate.Error?.Message);
        var repository = new IntermediateRepository(db);
        var first = await repository.ListAsync(null, 1, 1, Today, default);
        var second = await repository.ListAsync(null, 2, 1, Today, default);
        Assert.Equal(2, first.TotalCount); Assert.NotEqual(first.Items[0].Id, second.Items[0].Id);
        Assert.NotEqual(first.Items[0].IsCommercial, second.Items[0].IsCommercial);
        var search = await repository.ListAsync("Analito 30", 1, 25, Today, default);
        Assert.Equal(saved.Value.Material.Id, Assert.Single(search.Items).Id);
        Assert.Equal(30, search.Items[0].Results.Count);
        var material = (await Materials(db).GetAsync(saved.Value.Material.Id, default)).Value!;
        var curve = await Groups(db).CreateAsync("Curva", Group(material, "DIRECT-REGISTERED-MIX-CURVE", 2), 42, default);
        Assert.True(curve.IsSuccess, curve.Error?.Message);
        Assert.Equal(2, Assert.Single(curve.Value!.Consumptions).Quantity);
        material = (await Materials(db).GetAsync(material.Id, default)).Value!;
        Assert.Equal(7, material.AvailableQuantity);
        var aqs = await Groups(db).CreateAsync("AQS", Group(material, "DIRECT-REGISTERED-MIX-AQS", 1), 42, default);
        Assert.True(aqs.IsSuccess, aqs.Error?.Message);
        Assert.Equal(4.99m, aqs.Value!.Calculation.Parts[0].Results[1].Concentration);
        Assert.Equal(6, (await registrations.GetAsync("Intermedia", material.Id, default)).Value!.Material.AvailableQuantity);
    }

    [StockPostgresFact]
    public async Task DirectRegistrationRollsBackPhysicalMaterialIfAssociationCannotCommit()
    {
        await using var fixture = await StockPostgresTests.Fixture.CreateAsync(100);
        await using (var db = fixture.Context(new FailOnAssociationSave()))
            await Assert.ThrowsAsync<IOException>(() => new CommercialPreparationService(db, TimeProvider.System).RegisterAsync("Stock", new(Guid.NewGuid(), null, null, Request()), 42, default));
        await using var check = fixture.Context();
        Assert.Single(await check.ReferenceMaterials.ToArrayAsync());
        Assert.Empty(await check.Set<ReferenceCommercialRole>().ToArrayAsync());
    }

    private sealed class FailOnAssociationSave : SaveChangesInterceptor
    {
        private int _calls;
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (++_calls == 2) throw new IOException("Injected after role write");
            return ValueTask.FromResult(result);
        }
    }
    [StockPostgresFact]
    public async Task ConcurrentRetriesRegisterExactlyOnePhysicalBottleAndOneRole()
    {
        await using var fixture = await StockPostgresTests.Fixture.CreateAsync(100);
        await using var first = fixture.Context(); await using var second = fixture.Context();
        var request = new RegisterCommercialPreparationRequest(Guid.NewGuid(), null, null, Request());
        var results = await Task.WhenAll(new CommercialPreparationService(first, TimeProvider.System).RegisterAsync("Stock", request, 42, default),
            new CommercialPreparationService(second, TimeProvider.System).RegisterAsync("Stock", request, 42, default));
        Assert.All(results, r => Assert.True(r.IsSuccess, r.Error?.Message));
        Assert.Equal(results[0].Value!.Material.Id, results[1].Value!.Material.Id);
        await using var check = fixture.Context();
        Assert.Equal(2, await check.ReferenceMaterials.CountAsync());
        Assert.Single(await check.Set<ReferenceCommercialRole>().ToArrayAsync());
        Assert.Empty(await check.ReferencePreparations.ToArrayAsync());
        Assert.Empty(await check.ReferenceMaterialMovements.ToArrayAsync());
    }
    [StockPostgresFact]
    public async Task CommercialRoleMigrationIsAdditiveIdempotentAndPreservesPhysicalInventory()
    {
        await using var fixture = await StockPostgresTests.Fixture.CreateAsync(100);
        await using var db = fixture.Context();
        var material = (await Materials(db).CreateAsync(Request(20), 42, default)).Value!;
        var preparation = await Intermediates(db).CreateAsync(Dilute(material, 2), 42, default);
        Assert.True(preparation.IsSuccess, preparation.Error?.Message);
        var migration = new Lims.Infrastructure.Persistence.Migrations.AddCommercialPreparationRoles();
        foreach (var command in db.GetService<IMigrationsSqlGenerator>().Generate(migration.DownOperations)) await db.Database.ExecuteSqlRawAsync(command.CommandText);
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE \"__EFMigrationsHistory\" (\"MigrationId\" varchar(150) PRIMARY KEY, \"ProductVersion\" varchar(32) NOT NULL); INSERT INTO \"__EFMigrationsHistory\" VALUES ('20261017063950_AddCertifiedCommercialMaterials', '10.0.10');");
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../artifacts/sql/commercial_preparation_roles.sql"));
        var script = await File.ReadAllTextAsync(path);
        await db.Database.ExecuteSqlRawAsync(script); await db.Database.ExecuteSqlRawAsync(script);
        db.ChangeTracker.Clear();
        Assert.Equal(8, (await Materials(db).GetAsync(material.Id, default)).Value!.AvailableQuantity);
        Assert.Single(await db.ReferencePreparations.ToArrayAsync()); Assert.Single(await db.ReferenceMaterialMovements.ToArrayAsync());
        Assert.Empty(await db.Set<ReferenceCommercialRole>().ToArrayAsync());
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [StockPostgresFact]
    public async Task MixPersistsThirtyComponentsAndOnePhysicalInventoryWithoutPhantomStock()
    {
        await using var fixture = await StockPostgresTests.Fixture.CreateAsync(100);
        await using var db = fixture.Context(); var service = Materials(db); var request = Request(30);
        var created = await service.CreateAsync(request, 42, default);
        Assert.True(created.IsSuccess, created.Error?.Message); var id = created.Value!.Id;
        var certified = created.Value.Commercial!.Components;
        db.ChangeTracker.Clear(); var material = (await service.GetAsync(id, default)).Value!;
        Assert.Equal(MaterialPresentations.Mix, material.Presentation); Assert.Null(material.PurityPercent);
        Assert.Equal(30, material.Commercial!.Components.Count);
        Assert.Equal(certified, material.Commercial.Components);
        Assert.Equal(request.Commercial!.Components.Select((c, i) => c with { Id = certified[i].Id }), material.Commercial.Components);
        Assert.Equal(974, Assert.Single(material.Commercial.OtherExpressions!).Value);
        Assert.Equal("µg/g", material.Commercial.OtherExpressions![0].Unit);
        Assert.Equal(10, material.AvailableQuantity);
        Assert.Equal(2, await db.ReferenceMaterials.CountAsync()); // Existing fixture solid plus one bottle.
        Assert.Empty(await db.ReferencePreparations.ToArrayAsync());
        Assert.Empty(await db.ReferenceMaterialMovements.ToArrayAsync());
        var options = (await Intermediates(db).OptionsAsync(default)).Value!;
        var source = Assert.Single(options.Sources); Assert.Equal(id, source.Id); Assert.Equal(30, source.Analytes.Count);
        Assert.All(source.Analytes, a => Assert.Equal(id, a.PhysicalMaterialId));
        var another = (await service.CreateAsync(request with { Lot = "ANOTHER-PHYSICAL-BOTTLE" }, 42, default)).Value!;
        Assert.Empty(another.Commercial!.Components.Select(c => c.Id).Intersect(certified.Select(c => c.Id)));
        Assert.NotEqual(id, another.Id);
    }

    [StockPostgresFact]
    public async Task MixDilutesIndependentlyConsumesOneAliquotAndRetryDoesNotDebitAgain()
    {
        await using var fixture = await StockPostgresTests.Fixture.CreateAsync(100);
        await using var db = fixture.Context(); var materials = Materials(db); var service = Intermediates(db);
        var material = (await materials.CreateAsync(Request(3), 42, default)).Value!;
        var request = Dilute(material, 1); var result = await service.CreateAsync(request, 42, default);
        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal([5m, 4.99m, 5.01m], result.Value!.Calculation.Results.Select(a => a.Concentration));
        Assert.All(result.Value.Calculation.Results, a => Assert.Equal("µg/mL", a.Unit));
        Assert.Equal(1, Assert.Single(result.Value.Consumptions).Quantity);
        var saved = await service.CreateAsync(request, 42, default); Assert.True(saved.IsSuccess);
        db.ChangeTracker.Clear();
        var detail = (await materials.GetAsync(material.Id, default)).Value!;
        Assert.Equal(9, detail.AvailableQuantity); Assert.Single(detail.Movements!);
        var component = await db.ReferencePreparationComponents.SingleAsync();
        Assert.Equal(material.Id, component.SourceMaterialId); Assert.Null(component.SourcePreparationId);
        var second = await service.CreateAsync(Dilute(detail, 2), 42, default); Assert.True(second.IsSuccess, second.Error?.Message);
        db.ChangeTracker.Clear(); Assert.Equal(7, (await materials.GetAsync(material.Id, default)).Value!.AvailableQuantity);
        Assert.Equal(2, await db.ReferencePreparations.CountAsync());
        Assert.Equal(2, await db.ReferenceMaterialMovements.CountAsync());
        var reopened = (await service.GetAsync(result.Value.Preparation.Id, default)).Value!;
        Assert.Equal(material.Id, reopened.Calculation.Components[0].Source.Id);
        Assert.Equal(50, reopened.Calculation.Components[0].Source.Analytes[0].Concentration);
    }

    [StockPostgresFact]
    public async Task CertifiedIndividualSeparatesNominalAndSupportsDirectCurveAqsAndIntermediate()
    {
        await using var fixture = await StockPostgresTests.Fixture.CreateAsync(100);
        await using var db = fixture.Context(); var materials = Materials(db);
        var input = Request() with { Commercial = Certificate() with { NominalConcentration = 1000,
            Components = [new(Guid.NewGuid(), "Aluminio", "7429-90-5", 999, "µg/mL", 5)] } };
        var material = (await materials.CreateAsync(input, 42, default)).Value!;
        Assert.Equal(1000, material.Commercial!.NominalConcentration);
        var curve = Group(material, "DIRECT-CURVE", 1);
        curve = curve with { Calculation = curve.Calculation with { Parts = [curve.Calculation.Parts[0], curve.Calculation.Parts[0]] } };
        var c = await Groups(db).CreateAsync("Curva", curve, 42, default);
        Assert.True(c.IsSuccess, c.Error?.Message); Assert.Equal(2, Assert.Single(c.Value!.Consumptions).Quantity);
        Assert.All(c.Value.Calculation.Parts, p => Assert.Equal(99.9m, p.Results[0].Concentration));
        var invalidSource = material with { Id = c.Value.Preparation.Id, Version = c.Value.Version };
        Assert.False((await Intermediates(db).CreateAsync(Dilute(invalidSource, 1), 42, default)).IsSuccess);
        Assert.False((await Groups(db).CreateAsync("AQS", Group(invalidSource, "INVALID-PARENT-KIND", 1), 42, default)).IsSuccess);
        db.ChangeTracker.Clear(); material = (await materials.GetAsync(material.Id, default)).Value!;
        var aqs = await Groups(db).CreateAsync("AQS", Group(material, "DIRECT-AQS", 1), 42, default);
        Assert.True(aqs.IsSuccess, aqs.Error?.Message);
        db.ChangeTracker.Clear(); material = (await materials.GetAsync(material.Id, default)).Value!;
        Assert.Equal(7, material.AvailableQuantity);
        var intermediate = await Intermediates(db).CreateAsync(Dilute(material, 1), 42, default);
        Assert.True(intermediate.IsSuccess, intermediate.Error?.Message);
        var source = (await Groups(db).OptionsAsync(default)).Sources.Single(s => s.Id == intermediate.Value!.Preparation.Id);
        var downstream = await Groups(db).CreateAsync("AQS", new(Guid.NewGuid(), "FROM-INTERMEDIATE", "Desde Intermedia",
            new(source.MethodId, [new(source.Id, source.Version, 1, "mL", 10, "mL", false)]), Today, Today.AddDays(1), null, null), 42, default);
        Assert.True(downstream.IsSuccess, downstream.Error?.Message);
        db.ChangeTracker.Clear(); Assert.Equal(6, (await materials.GetAsync(material.Id, default)).Value!.AvailableQuantity);
        Assert.Equal(9, await db.ReferencePreparations.Where(p => p.Id == source.Id).Select(p => p.AvailableVolume).SingleAsync());
        Assert.Empty(await db.ReferencePreparations.Where(p => p.Kind == "Stock").ToArrayAsync());
    }

    [StockPostgresFact]
    public async Task ExpiredMissingOpeningAndMassFractionAreStoredButNeverOfferedOrConsumed()
    {
        await using var fixture = await StockPostgresTests.Fixture.CreateAsync(100);
        await using var db = fixture.Context(); var materials = Materials(db);
        var expired = Request() with { ReceivedDate = new(2023, 1, 1), ExpirationDate = new(2025, 3, 31),
            Commercial = Certificate() with { OpenedDate = new(2023, 3, 1) } };
        foreach (var request in new[] { expired, Request() with { Commercial = Certificate() with { OpenedDate = null } },
            Request() with { Commercial = Certificate() with { Components = [new(Guid.NewGuid(), "Aluminio", null, 974, "µg/g", 5)] } } })
        {
            var registered = await materials.CreateAsync(request, 42, default);
            Assert.True(registered.IsSuccess, registered.Error?.Message);
            Assert.DoesNotContain((await Intermediates(db).OptionsAsync(default)).Value!.Sources, s => s.Id == registered.Value!.Id);
            Assert.False((await Intermediates(db).CreateAsync(Dilute(registered.Value!, 1), 42, default)).IsSuccess);
            Assert.False((await Groups(db).CreateAsync("Curva", Group(registered.Value!, Guid.NewGuid().ToString("N"), 1), 42, default)).IsSuccess);
        }
        Assert.Empty(await db.ReferencePreparations.ToArrayAsync()); Assert.Empty(await db.ReferenceMaterialMovements.ToArrayAsync());
    }

    [StockPostgresFact]
    public async Task OpeningEditsAreVersionedAuditedAndNeverExtendManufacturerExpiration()
    {
        await using var fixture = await StockPostgresTests.Fixture.CreateAsync(100);
        await using var db = fixture.Context(); var service = Materials(db); var input = Request() with { ExpirationDate = Today.AddMonths(2), Commercial = Certificate() with { OpenedDate = null } };
        var material = (await service.CreateAsync(input, 42, default)).Value!;
        Assert.Empty(material.OpeningHistory!); Assert.Equal(input.ExpirationDate, material.EffectiveExpirationDate);
        var request = new UpdateReferenceMaterialRequest(input.Name, null, input.CatalogNumber, input.MethodId, null, input.Lot,
            input.Brand, input.ReceivedDate, input.ExpirationDate, input.PresentationQuantity, input.UnitId, 1,
            input.StorageTemperature, input.LocationId, material.Version, input.Presentation, input.Commercial! with { OpenedDate = Today });
        var changed = await service.UpdateAsync(material.Id, request, 42, default); Assert.True(changed.IsSuccess, changed.Error?.Message);
        Assert.Equal(input.ExpirationDate, changed.Value!.EffectiveExpirationDate); Assert.Single(changed.Value.OpeningHistory!);
        Assert.False((await service.UpdateAsync(material.Id, request, 42, default)).IsSuccess);
        request = request with { Version = changed.Value.Version, Commercial = request.Commercial! with { OpenedDate = Today.AddDays(-10), UseAfterOpeningMonths = 1 } };
        var revised = await service.UpdateAsync(material.Id, request, 42, default); Assert.True(revised.IsSuccess, revised.Error?.Message);
        Assert.Equal(Today.AddDays(-10).AddMonths(1), revised.Value!.EffectiveExpirationDate);
        db.ChangeTracker.Clear(); Assert.Equal(2, (await service.GetAsync(material.Id, default)).Value!.OpeningHistory!.Count);
    }

    [StockPostgresFact]
    public async Task FailureAfterWritesRollsBackBottleComponentsAndLedger()
    {
        await using var fixture = await StockPostgresTests.Fixture.CreateAsync(100);
        ReferenceMaterialDetail material;
        await using (var setup = fixture.Context()) material = (await Materials(setup).CreateAsync(Request(3), 42, default)).Value!;
        await using (var failing = fixture.Context(new FailAfterSave()))
            await Assert.ThrowsAsync<IOException>(() => Intermediates(failing).CreateAsync(Dilute(material, 1), 42, default));
        await using var check = fixture.Context();
        Assert.Equal(10, (await Materials(check).GetAsync(material.Id, default)).Value!.AvailableQuantity);
        Assert.Empty(await check.ReferencePreparations.ToArrayAsync()); Assert.Empty(await check.ReferenceMaterialMovements.ToArrayAsync());
        Assert.Empty(await check.ReferencePreparationComponents.ToArrayAsync());
    }
    private sealed class FailAfterSave : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
            => throw new IOException("Injected after all commercial writes");
    }

    [StockPostgresFact]
    public async Task CompetingIntermediateAndCurveCannotOverdrawCommercialBottle()
    {
        await using var fixture = await StockPostgresTests.Fixture.CreateAsync(100);
        ReferenceMaterialDetail material;
        await using (var setup = fixture.Context()) material = (await Materials(setup).CreateAsync(Request(3), 42, default)).Value!;
        var barrier = new SaveBarrier();
        await using var first = fixture.Context(barrier); await using var second = fixture.Context(barrier);
        var intermediate = Intermediates(first).CreateAsync(Dilute(material, 6), 42, default);
        var curve = Groups(second).CreateAsync("Curva", Group(material, "COMMERCIAL-RACE", 6), 42, default);
        await Task.WhenAll(intermediate, curve);
        Assert.NotEqual((await intermediate).IsSuccess, (await curve).IsSuccess);
        await using var check = fixture.Context();
        Assert.Equal(4, (await Materials(check).GetAsync(material.Id, default)).Value!.AvailableQuantity);
        Assert.Single(await check.ReferencePreparations.ToArrayAsync());
        Assert.Single(await check.ReferenceMaterialMovements.ToArrayAsync());
        Assert.Single(await check.ReferencePreparationComponents.ToArrayAsync());
    }
    private sealed class SaveBarrier : SaveChangesInterceptor
    {
        private int _calls;
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _calls) == 2) _ready.TrySetResult();
            await _ready.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            return result;
        }
    }

    [StockPostgresFact]
    public async Task AdditiveMigrationAndIdempotentScriptRetainExistingSolidAndStock()
    {
        await using var fixture = await StockPostgresTests.Fixture.CreateAsync(100);
        await using var db = fixture.Context();
        var stock = await new StockService(new StockRepository(db), TimeProvider.System).CreateAsync(fixture.Request(50), 42, default);
        Assert.True(stock.IsSuccess, stock.Error?.Message);
        var before = await db.ReferenceMaterials.AsNoTracking().SingleAsync(); var balance = before.AvailableQuantity;
        var migration = new Lims.Infrastructure.Persistence.Migrations.AddCertifiedCommercialMaterials();
        foreach (var command in db.GetService<IMigrationsSqlGenerator>().Generate(migration.DownOperations))
            await db.Database.ExecuteSqlRawAsync(command.CommandText);
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE \"__EFMigrationsHistory\" (\"MigrationId\" varchar(150) PRIMARY KEY, \"ProductVersion\" varchar(32) NOT NULL); INSERT INTO \"__EFMigrationsHistory\" VALUES ('20261017031716_AddStockSolventComposition', '10.0.10');");
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../artifacts/sql/certified_commercial_materials.sql"));
        var script = await File.ReadAllTextAsync(path);
        await db.Database.ExecuteSqlRawAsync(script); await db.Database.ExecuteSqlRawAsync(script);
        db.ChangeTracker.Clear();
        Assert.Equal(balance, (await db.ReferenceMaterials.SingleAsync()).AvailableQuantity);
        Assert.Single(await db.ReferencePreparations.ToArrayAsync());
        Assert.Single(await db.ReferenceMaterialMovements.ToArrayAsync());
        Assert.Equal("µg", (await db.ReferenceUnits.SingleAsync(u => u.Id == 4)).Symbol);
        Assert.False(db.Database.HasPendingModelChanges());
    }
}

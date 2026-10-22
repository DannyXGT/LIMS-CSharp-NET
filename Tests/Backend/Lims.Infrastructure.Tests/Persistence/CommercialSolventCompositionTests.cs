using System.Text.Json;
using Lims.Application.ReferenceMaterials;
using Lims.Contracts.ReferenceMaterials;
using Lims.Contracts.ReferencePreparations;
using Lims.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lims.Infrastructure.Tests.Persistence;

public sealed class CommercialSolventCompositionTests
{
    private static CreateReferenceMaterialRequest Input(IReadOnlyList<StockSolventInput>? solvents, string? basis = StockSolventComposition.Basis) =>
        new("Producto comercial de prueba", null, null, 1, null, "LOTE-PRUEBA", "Fabricante de prueba",
            new(2026, 1, 1), new(2099, 1, 1), 10, 1, 1, "Ambiente", 1, MaterialPresentations.Solution,
            new("", "", null, null, null, null, null, null, [new(Guid.NewGuid(), "Analito", null, 1000, "µg/mL")], Solvents: solvents, CompositionBasis: basis));

    [StockPostgresFact]
    public async Task AllThreeOperationsShareThePersistentCatalogAndBothSearchKeysFindTheSameThf()
    {
        await using var fixture = await StockPostgresTests.Fixture.CreateAsync(100);
        await using var db = fixture.Context();
        var stock = new StockRepository(db);
        var all = await stock.SolventsAsync(null, default);
        Assert.Equal(12, all.Count);
        Assert.Equal("Ciclohexano", all.Single(s => s.Name == "Ciclohexano").Abbreviation);
        var byName = Assert.Single(await stock.SolventsAsync("Tetrahidrofurano", default));
        var byAbbreviation = Assert.Single(await stock.SolventsAsync("THF", default));
        Assert.Equal(byName, byAbbreviation);
        var service = new CommercialPreparationService(db, TimeProvider.System);
        foreach (var kind in new[] { "Stock", "Intermedia" })
        {
            var options = await service.OptionsAsync(kind, null, default);
            Assert.True(options.SupportsSolventComposition);
            Assert.Equal(all, options.Solvents);
        }
    }

    [StockPostgresFact]
    public async Task CommercialMixturesRoundTripThroughFreshContextsAndReuseTheStockSolventTable()
    {
        foreach (var kind in new[] { "Stock", "Intermedia" })
        foreach (var composition in new StockSolventInput[][] { [new(4, 100)], [new(4, 50), new(3, 50)], [new(4, 30), new(8, 20), new(3, 50)] })
        {
            await using var fixture = await StockPostgresTests.Fixture.CreateAsync(100);
            var request = new RegisterCommercialPreparationRequest(Guid.NewGuid(), null, null, Input(composition));
            Guid id;
            await using (var db = fixture.Context())
            {
                var service = new CommercialPreparationService(db, TimeProvider.System);
                var saved = await service.RegisterAsync(kind, request, 42, default);
                Assert.True(saved.IsSuccess, saved.Error?.Message); id = saved.Value!.Material.Id;
                Assert.True((await service.RegisterAsync(kind, request, 42, default)).IsSuccess);
            }
            await using var fresh = fixture.Context();
            var reloaded = (await new CommercialPreparationService(fresh, TimeProvider.System).GetAsync(kind, id, default)).Value!;
            Assert.Equal(composition, reloaded.Material.Commercial!.Solvents);
            Assert.Equal(StockSolventComposition.Basis, reloaded.Material.Commercial.CompositionBasis);
            Assert.Equal(composition.Select(r => r.Percent), reloaded.Material.Commercial.SolventDetails!.Select(r => r.Percent));
            Assert.All(reloaded.Material.Commercial.SolventDetails!, r => Assert.False(string.IsNullOrWhiteSpace(r.Name)));
            var persisted = await fresh.StockSolvents.Where(s => s.MaterialId == id).OrderBy(s => s.Position).ToArrayAsync();
            Assert.Equal(composition.Length, persisted.Length);
            Assert.All(persisted, r => Assert.Null(r.PreparationId));
            Assert.Equal(10, reloaded.Material.AvailableQuantity);
            Assert.Empty(await fresh.ReferenceMaterialMovements.ToArrayAsync());
            Assert.Single(await fresh.Set<Lims.Domain.ReferenceMaterials.ReferenceCommercialRole>().ToArrayAsync());
        }
    }

    [StockPostgresFact]
    public async Task InvalidOrMissingCompositionCannotCreateCommercialProducts()
    {
        await using var fixture = await StockPostgresTests.Fixture.CreateAsync(100);
        await using var db = fixture.Context(); var service = new CommercialPreparationService(db, TimeProvider.System);
        foreach (var kind in new[] { "Stock", "Intermedia" })
        foreach (var rows in new StockSolventInput[]?[] { null, [], [new(4, 90)], [new(4, 110)], [new(4, 50), new(4, 50)], [new(999999, 100)], [new(4, 99.99999m)] })
        {
            var result = await service.RegisterAsync(kind, new(Guid.NewGuid(), null, null, Input(rows)), 42, default);
            Assert.False(result.IsSuccess);
        }
        Assert.False((await service.RegisterAsync("Stock", new(Guid.NewGuid(), null, null, Input([new(4, 100)], "v/v")), 42, default)).IsSuccess);
        Assert.Single(await db.ReferenceMaterials.ToArrayAsync()); Assert.Empty(await db.StockSolvents.ToArrayAsync());
    }

    [StockPostgresFact]
    public async Task ExistingBottlesWithoutHistoricalCompositionRemainReadableAndCanBeAssociated()
    {
        await using var fixture = await StockPostgresTests.Fixture.CreateAsync(100);
        await using var db = fixture.Context();
        var legacy = await new ReferenceMaterialService(new ReferenceMaterialRepository(db), TimeProvider.System).CreateAsync(Input(null, null), 42, default);
        Assert.True(legacy.IsSuccess, legacy.Error?.Message);
        var material = legacy.Value!;
        var result = await new CommercialPreparationService(db, TimeProvider.System).RegisterAsync("Stock", new(Guid.NewGuid(), material.Id, material.Version, null), 42, default);
        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(material.Version, result.Value!.Material.Version);
        Assert.Null(result.Value.Material.Commercial!.Solvents);
        Assert.Empty(await db.StockSolvents.ToArrayAsync());
    }

    [StockPostgresFact]
    public async Task CatalogRepairIsIdempotentAndPreservesExistingIdsExtraRecordsAndHistoricalForeignKeys()
    {
        await using var fixture = await StockPostgresTests.Fixture.CreateAsync(100);
        await using var db = fixture.Context();
        await db.Database.ExecuteSqlRawAsync("UPDATE reference_solvents SET abbreviation = 'Cyclohexano' WHERE id = 7; UPDATE reference_solvents SET is_active = false WHERE id = 4; DELETE FROM reference_solvents WHERE id = 1; INSERT INTO reference_solvents (id,name,abbreviation,is_active,display_order) VALUES (1001, 'Etanol', 'EtOH', true, 20);");
        var script = await File.ReadAllTextAsync(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../artifacts/sql/ensure_reference_solvents.sql")));
        await db.Database.ExecuteSqlRawAsync(script); await db.Database.ExecuteSqlRawAsync(script);
        Assert.Equal(13, await db.ReferenceSolvents.CountAsync());
        Assert.Equal(4, (await db.ReferenceSolvents.SingleAsync(r => r.Abbreviation == "MeOH")).Id);
        Assert.True((await db.ReferenceSolvents.SingleAsync(r => r.Id == 4)).IsActive);
        Assert.Equal("EtOH", (await db.ReferenceSolvents.SingleAsync(r => r.Id == 1001)).Abbreviation);
        Assert.Single(await db.ReferenceSolvents.Where(r => r.Abbreviation == "THF").ToArrayAsync());
        Assert.Equal("Ciclohexano", (await db.ReferenceSolvents.SingleAsync(r => r.Id == 7)).Abbreviation);
    }

    [StockPostgresFact]
    public async Task ProvisionPersistentUiValidationDatabaseWhenExplicitlyRequested()
    {
        var folder = Environment.GetEnvironmentVariable("LIMS_SOLVENT_UI_DIRECTORY");
        if (string.IsNullOrWhiteSpace(folder)) return;
        await using var fixture = await StockPostgresTests.Fixture.CreateAsync(250);
        await using var db = fixture.Context();
        await db.Database.ExecuteSqlRawAsync("UPDATE usuarios SET nombre = 'Usuario de prueba' WHERE id = 42");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "ui-database.json"), JsonSerializer.Serialize(new { ConnectionString = db.Database.GetConnectionString() }));
        await File.WriteAllTextAsync(Path.Combine(folder, "persistent-catalog.json"), JsonSerializer.Serialize(await new StockRepository(db).SolventsAsync(null, default)));
    }
}

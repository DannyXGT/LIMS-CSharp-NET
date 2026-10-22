using Lims.Application.ReferenceMaterials;
using Lims.Contracts.ReferenceMaterials;
using Lims.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Lims.Infrastructure.Tests.Persistence;

public sealed class CommercialMigrationReconciliationTests
{
    private static Task<string> Script() => File.ReadAllTextAsync(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
        "../../../../../../artifacts/sql/certified_commercial_materials.sql")));

    [StockPostgresFact]
    public Task LegacyQuantityConstraintIsRenamedBeforeCommercialSchemaIsCompleted() => VerifyLegacyQuantityAsync(false);

    [StockPostgresFact]
    public Task DuplicateLegacyQuantityConstraintKeepsExistingCorrectlyNamedGuard() => VerifyLegacyQuantityAsync(true);

    private static async Task VerifyLegacyQuantityAsync(bool hasCorrectQuantityConstraint)
    {
        await using var fixture = await StockPostgresTests.Fixture.CreateAsync(100);
        await using var db = fixture.Context();
        var stock = await new Lims.Application.ReferencePreparations.StockService(new StockRepository(db), TimeProvider.System)
            .CreateAsync(fixture.Request(50), 42, default);
        Assert.True(stock.IsSuccess, stock.Error?.Message);
        db.ChangeTracker.Clear();
        var service = new ReferenceMaterialService(new ReferenceMaterialRepository(db), TimeProvider.System);
        var before = (await service.GetAsync(fixture.Source.Id, default)).Value!;
        var movementId = await db.ReferenceMaterialMovements.Select(m => m.Id).SingleAsync();
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE reference_materials DROP CONSTRAINT ck_reference_materials_presentation; ALTER TABLE reference_materials ADD CONSTRAINT ck_reference_materials_presentation CHECK (presentation_quantity > 0::numeric); DROP TABLE reference_certified_components;");
        if (!hasCorrectQuantityConstraint)
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE reference_materials DROP CONSTRAINT ck_reference_materials_presentation_quantity");
        var script = await Script();
        await db.Database.ExecuteSqlRawAsync(script);
        await db.Database.ExecuteSqlRawAsync(script);
        db.ChangeTracker.Clear();
        var after = (await service.GetAsync(before.Id, default)).Value!;
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(before), System.Text.Json.JsonSerializer.Serialize(after));
        Assert.Equal(stock.Value!.Preparation.Id, (await db.ReferencePreparations.SingleAsync()).Id);
        Assert.Equal(movementId, await db.ReferenceMaterialMovements.Select(m => m.Id).SingleAsync());
        var constraints = await db.Database.SqlQueryRaw<string>("SELECT conname::text AS \"Value\" FROM pg_constraint WHERE conrelid='reference_materials'::regclass AND conname IN ('ck_reference_materials_presentation','ck_reference_materials_presentation_quantity') AND convalidated").ToArrayAsync();
        Assert.Equal(2, constraints.Length);
        var quantity = await db.Database.SqlQueryRaw<string>("SELECT pg_get_constraintdef(oid) AS \"Value\" FROM pg_constraint WHERE conrelid='reference_materials'::regclass AND conname='ck_reference_materials_presentation_quantity'").SingleAsync();
        Assert.Contains("presentation_quantity >", quantity, StringComparison.Ordinal);
        var presentation = await db.Database.SqlQueryRaw<string>("SELECT pg_get_constraintdef(oid) AS \"Value\" FROM pg_constraint WHERE conrelid='reference_materials'::regclass AND conname='ck_reference_materials_presentation'").SingleAsync();
        Assert.Contains("CommercialMix", presentation, StringComparison.Ordinal);
        Assert.True(await db.Database.SqlQueryRaw<bool>("SELECT to_regclass('reference_certified_components') IS NOT NULL AS \"Value\"").SingleAsync());
        Assert.Equal("20261017063950_AddCertifiedCommercialMaterials", await db.Database.SqlQueryRaw<string>("SELECT \"MigrationId\" AS \"Value\" FROM \"__EFMigrationsHistory\"").SingleAsync());
    }

    [StockPostgresFact]
    public async Task NamingCollisionWithIncompatibleQuantityConstraintRollsBackWithoutLosingEitherGuard()
    {
        await using var fixture = await StockPostgresTests.Fixture.CreateAsync(100);
        await using var db = fixture.Context();
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE reference_materials DROP CONSTRAINT ck_reference_materials_presentation; ALTER TABLE reference_materials ADD CONSTRAINT ck_reference_materials_presentation CHECK (presentation_quantity > 0::numeric); ALTER TABLE reference_materials DROP CONSTRAINT ck_reference_materials_presentation_quantity; ALTER TABLE reference_materials ADD CONSTRAINT ck_reference_materials_presentation_quantity CHECK (presentation_quantity >= 0);");
        var script = await Script();
        var failure = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(script));
        Assert.Equal("55000", failure.SqlState);
        Assert.Contains("Incompatible positive-quantity constraint", failure.MessageText, StringComparison.Ordinal);
        await db.Database.ExecuteSqlRawAsync("ROLLBACK;");
        Assert.Equal(100, (await db.ReferenceMaterials.SingleAsync()).AvailableQuantity);
        var presentation = await db.Database.SqlQueryRaw<string>("SELECT pg_get_constraintdef(oid) AS \"Value\" FROM pg_constraint WHERE conrelid='reference_materials'::regclass AND conname='ck_reference_materials_presentation'").SingleAsync();
        Assert.Contains("presentation_quantity >", presentation, StringComparison.Ordinal);
        Assert.DoesNotContain("CommercialMix", presentation, StringComparison.Ordinal);
        Assert.False(await db.Database.SqlQueryRaw<bool>("SELECT to_regclass('\"__EFMigrationsHistory\"') IS NOT NULL AS \"Value\"").SingleAsync());
    }

    [StockPostgresFact]
    public async Task CompleteCommercialSchemaWithoutHistoryIsVerifiedAndRecordedWithoutChangingInventory()
    {
        await using var fixture = await StockPostgresTests.Fixture.CreateAsync(100);
        await using var db = fixture.Context();
        var today = ReferenceMaterialPresentation.ApplicationToday();
        var service = new ReferenceMaterialService(new ReferenceMaterialRepository(db), TimeProvider.System);
        var saved = await service.CreateAsync(new("Commercial reconciliation fixture", null, null, 1, null,
            "TEST-LOT", "Test brand", today.AddDays(-1), today.AddYears(1), 10, 1, 2, "Ambient", 1,
            MaterialPresentations.Solution, new("", "", null, null, null, null, null, null,
                [new(Guid.NewGuid(), "Fixture analyte", null, 49.9m, "µg/mL")])), 42, default);
        Assert.True(saved.IsSuccess, saved.Error?.Message);
        db.ChangeTracker.Clear();
        var before = (await service.GetAsync(saved.Value!.Id, default)).Value!;
        var script = await Script();
        await db.Database.ExecuteSqlRawAsync(script);
        await db.Database.ExecuteSqlRawAsync(script);
        db.ChangeTracker.Clear();
        var after = (await service.GetAsync(before.Id, default)).Value!;
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(before), System.Text.Json.JsonSerializer.Serialize(after));
        Assert.Equal(2, await db.ReferenceMaterials.CountAsync());
        Assert.Empty(await db.ReferencePreparations.ToArrayAsync());
        Assert.Empty(await db.ReferenceMaterialMovements.ToArrayAsync());
        var history = await db.Database.SqlQueryRaw<string>("SELECT \"MigrationId\" AS \"Value\" FROM \"__EFMigrationsHistory\"").ToArrayAsync();
        Assert.Equal("20261017063950_AddCertifiedCommercialMaterials", Assert.Single(history));
    }

    [StockPostgresFact]
    public async Task MissingKnownIndexCheckAndForeignKeyAreCompletedBeforeRecordingHistory()
    {
        await using var fixture = await StockPostgresTests.Fixture.CreateAsync(100);
        await using var db = fixture.Context();
        await db.Database.ExecuteSqlRawAsync("DROP INDEX \"IX_reference_material_opening_history_actor_user_id\"; ALTER TABLE reference_certified_components DROP CONSTRAINT ck_certified_component_values; ALTER TABLE reference_preparation_components DROP CONSTRAINT \"FK_reference_preparation_components_reference_materials_source~\";");
        var script = await Script();
        await db.Database.ExecuteSqlRawAsync(script);
        await db.Database.ExecuteSqlRawAsync(script);
        Assert.Equal(100, (await db.ReferenceMaterials.SingleAsync()).AvailableQuantity);
        Assert.True(await db.Database.SqlQueryRaw<bool>("SELECT EXISTS(SELECT 1 FROM pg_constraint WHERE conrelid = 'reference_certified_components'::regclass AND conname = 'ck_certified_component_values' AND convalidated) AS \"Value\"").SingleAsync());
    }

    [StockPostgresFact]
    public async Task IncompatiblePresentationConstraintIsPreservedAndHistoryIsNotInvented()
    {
        await using var fixture = await StockPostgresTests.Fixture.CreateAsync(100);
        await using var db = fixture.Context();
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE reference_materials DROP CONSTRAINT ck_reference_materials_presentation; ALTER TABLE reference_materials ADD CONSTRAINT ck_reference_materials_presentation CHECK (presentation = 'Solid');");
        var script = await Script();
        var failure = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(script));
        Assert.Equal("55000", failure.SqlState);
        Assert.Contains("Incompatible check constraint", failure.MessageText, StringComparison.Ordinal);
        await db.Database.ExecuteSqlRawAsync("ROLLBACK;");
        Assert.False(await db.Database.SqlQueryRaw<bool>("SELECT to_regclass('\"__EFMigrationsHistory\"') IS NOT NULL AS \"Value\"").SingleAsync());
        Assert.Equal(100, (await db.ReferenceMaterials.SingleAsync()).AvailableQuantity);
        var definition = await db.Database.SqlQueryRaw<string>("SELECT pg_get_constraintdef(oid) AS \"Value\" FROM pg_constraint WHERE conrelid='reference_materials'::regclass AND conname='ck_reference_materials_presentation'").SingleAsync();
        Assert.Contains("=", definition, StringComparison.Ordinal);
        Assert.DoesNotContain("CommercialMix", definition, StringComparison.Ordinal);
    }

    [StockPostgresFact]
    public async Task RecordedMigrationWithMissingIndexIsRejectedWithoutSilentlyRecreatingIt()
    {
        await using var fixture = await StockPostgresTests.Fixture.CreateAsync(100);
        await using var db = fixture.Context();
        await db.Database.OpenConnectionAsync();
        var script = await Script();
        await db.Database.ExecuteSqlRawAsync(script);
        await db.Database.ExecuteSqlRawAsync("DROP INDEX \"IX_reference_material_opening_history_actor_user_id\"");
        var failure = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(script));
        Assert.Equal("55000", failure.SqlState);
        Assert.Contains("indexes", failure.MessageText, StringComparison.Ordinal);
        await db.Database.ExecuteSqlRawAsync("ROLLBACK;");
        Assert.False(await db.Database.SqlQueryRaw<bool>("SELECT to_regclass('\"IX_reference_material_opening_history_actor_user_id\"') IS NOT NULL AS \"Value\"").SingleAsync());
        Assert.Equal(100, (await db.ReferenceMaterials.SingleAsync()).AvailableQuantity);
    }
}

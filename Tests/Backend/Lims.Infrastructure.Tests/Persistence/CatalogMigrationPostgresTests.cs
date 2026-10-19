using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Lims.Infrastructure.Tests.Persistence;

public sealed class CatalogMigrationPostgresTests
{
    [StockPostgresFact]
    public async Task UnitGuardAcceptsNativeCatalogUnitsAndStillRejectsHistoricalMismatch()
    {
        await using var fixture = await StockPostgresTests.Fixture.CreateAsync(100);
        await using var db = fixture.Context();
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE reference_materials ADD COLUMN legacy_id integer, ADD COLUMN unit text;");
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../artifacts/sql/migrate_reference_material_catalogs.sql"));
        var script = await File.ReadAllTextAsync(path);
        var start = script.IndexOf("-- BEGIN UNIT MAPPING GUARD", StringComparison.Ordinal);
        var end = script.IndexOf("-- END UNIT MAPPING GUARD", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        // Execute the shipped guard against this fixture's isolated search_path.
        var guard = "DO $test$ BEGIN\n" + script[start..end].Replace("public.", "", StringComparison.Ordinal) + "\nEND $test$;";
        var unitIds = await db.ReferenceUnits.Where(u => u.Symbol == "mg" || u.Symbol == "g" || u.Symbol == "mL").Select(u => u.Id).ToArrayAsync();
        Assert.Equal(3, unitIds.Length);
        foreach (var id in unitIds)
        {
            await db.Database.ExecuteSqlAsync($"UPDATE reference_materials SET legacy_id = NULL, unit = NULL, unit_id = {id}");
            await db.Database.ExecuteSqlRawAsync(guard);
            await db.Database.ExecuteSqlRawAsync("UPDATE reference_materials SET unit = ''");
            await db.Database.ExecuteSqlRawAsync(guard);
        }
        var mg = await db.ReferenceUnits.Where(u => u.Symbol == "mg").Select(u => u.Id).SingleAsync();
        await db.Database.ExecuteSqlAsync($"UPDATE reference_materials SET unit_id = {mg}, legacy_id = 123, unit = 'Gram'");
        var mismatch = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(guard));
        Assert.Equal("P0001", mismatch.SqlState);
        Assert.Equal("unit mapping mismatch detected", mismatch.MessageText);
        await db.Database.ExecuteSqlRawAsync("UPDATE reference_materials SET unit = 'Milligram'");
        await db.Database.ExecuteSqlRawAsync(guard);
        await db.Database.ExecuteSqlRawAsync("UPDATE reference_materials SET unit = NULL");
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(guard));
        db.ChangeTracker.Clear();
        var material = await db.ReferenceMaterials.SingleAsync();
        Assert.Equal(mg, material.UnitId);
        Assert.Equal(100, material.AvailableQuantity);
        Assert.Equal(100, material.PresentationQuantity);
    }
}

using System.Data.Common;
using Lims.Domain.ReferenceMaterials;
using Lims.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Lims.Infrastructure.Tests.Persistence;

public sealed class ReferenceMaterialDisplayTests
{
    [Fact]
    public async Task ResolvesAllActorsInOneQueryIncludingInactiveHistoricalUsers()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE usuarios (id INTEGER PRIMARY KEY, nombre TEXT, activo INTEGER);
            INSERT INTO usuarios VALUES (42, 'Danny Jimenez', 1), (84, 'Ana López', 0);
            """;
        await command.ExecuteNonQueryAsync();
        var counter = new QueryCounter();
        await using var context = new LimsDbContext(new DbContextOptionsBuilder<LimsDbContext>()
            .UseSqlite(connection).AddInterceptors(counter).Options);
        var material = Material();
        material.Archive("Fin de uso", 84, DateTimeOffset.UtcNow);

        var result = await new ReferenceMaterialRepository(context).ResolveDisplayContextAsync(material, CancellationToken.None);

        Assert.Equal("Danny Jimenez", result.CreatedByName);
        Assert.Equal("Ana López", result.UpdatedByName);
        Assert.Equal("Ana López", result.ArchivedByName);
        Assert.Equal(1, counter.ReaderCalls);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task ResolvesReplacementNameWithoutReturningItsGuidAsDisplayText()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var replacementId = Guid.NewGuid();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE usuarios (id INTEGER PRIMARY KEY, nombre TEXT);
            CREATE TABLE reference_materials (id TEXT PRIMARY KEY, name TEXT);
            INSERT INTO reference_materials VALUES ($id, 'Naphthol AS nuevo lote');
            """;
        command.Parameters.AddWithValue("$id", replacementId);
        await command.ExecuteNonQueryAsync();
        await using var context = new LimsDbContext(new DbContextOptionsBuilder<LimsDbContext>().UseSqlite(connection).Options);
        var material = Material();
        material.ReplaceWith(replacementId, "Nuevo lote", 84, DateTimeOffset.UtcNow);

        var result = await new ReferenceMaterialRepository(context).ResolveDisplayContextAsync(material, CancellationToken.None);

        Assert.Null(result.CreatedByName);
        Assert.Null(result.UpdatedByName);
        Assert.Null(result.ArchivedByName);
        Assert.Equal("Naphthol AS nuevo lote", result.ReplacedByMaterialName);
    }

    private static ReferenceMaterial Material() => new(
        Guid.NewGuid(), "Naphthol AS", "92-77-3", "DRE-C15431000", new ReferenceMethod(1, "Phtalatos", true),
        98.09m, "H1622997", "Dr. Ehrenstorfer", new DateOnly(2026, 10, 3), new DateOnly(2029, 5, 6),
        100m, new ReferenceUnit(3, "Miligramo", "mg", true), 1, "20 °C ± 4 °C",
        new ReferenceLocation(1, "Laboratorio", true), 42, DateTimeOffset.UtcNow);

    private sealed class QueryCounter : DbCommandInterceptor
    {
        public int ReaderCalls { get; private set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            ReaderCalls++;
            return ValueTask.FromResult(result);
        }
    }
}

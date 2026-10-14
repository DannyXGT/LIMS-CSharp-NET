using Lims.Application.Permissions;
using Lims.Contracts.Errors;
using Lims.Contracts.Permissions;
using Lims.Domain.Identity;
using Lims.Infrastructure.Persistence;
using Lims.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Lims.Infrastructure.Tests.Persistence;

public sealed class PermissionPostgresTests
{
    [StockPostgresFact]
    public async Task MigrationAndGrantRevokeHistoryRunOnIsolatedPostgreSqlOnly()
    {
        var settings = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("LIMS_STOCK_TEST_CONNECTION"));
        if (settings.Host != "127.0.0.1" || settings.Port != 55439 || settings.Database != "lims_stock_test")
            throw new InvalidOperationException("Permission tests require the isolated loopback test database.");
        var schema = "permissions_" + Guid.NewGuid().ToString("N");
        await using (var connection = new NpgsqlConnection(settings.ConnectionString))
        {
            await connection.OpenAsync(); await using var command = new NpgsqlCommand("CREATE SCHEMA " + schema, connection); await command.ExecuteNonQueryAsync();
        }
        settings.SearchPath = schema;
        await using var db = new LimsDbContext(new DbContextOptionsBuilder<LimsDbContext>().UseNpgsql(settings.ConnectionString, o => o.EnableRetryOnFailure()).Options);
        // Real existing identity shape, reproduced only in the guarded disposable test schema.
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE roles (id integer PRIMARY KEY, nombre varchar(50) NOT NULL);
            CREATE TABLE departamentos (id integer PRIMARY KEY, nombre varchar(100) NOT NULL);
            CREATE TABLE usuarios (id integer PRIMARY KEY, nombre varchar(100) NOT NULL, email varchar(150) NOT NULL,
                password_hash text NOT NULL, id_rol integer REFERENCES roles(id), id_departamento integer REFERENCES departamentos(id),
                activo boolean NOT NULL, fecha_creacion timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP);
            CREATE TABLE usuarios_permisos (id_usuario integer REFERENCES usuarios(id), permiso_key varchar(120), PRIMARY KEY(id_usuario, permiso_key));
            """);
        var migration = new AddIndividualPermissions();
        var generator = db.GetService<IMigrationsSqlGenerator>();
        foreach (var command in generator.Generate(migration.UpOperations, db.Model)) await db.Database.ExecuteSqlRawAsync(command.CommandText);
        var role = new Role(1, "Administrador"); var analyst = new Role(2, "Analista"); var department = new Department(7, PermissionCatalog.DepartmentName);
        db.Users.AddRange(new User(42, "Administrador de prueba", "admin@example.invalid", "test", true, role, department), new User(84, "Ana López", "ana@example.invalid", "test", true, analyst, department));
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var repository = new PermissionRepository(db, new() { EnforceIndividualPermissions = true }, TimeProvider.System);
        Assert.Equal(2, (await repository.UsersAsync(42, null, default)).Value!.Items.Count);
        var granted = await repository.SaveAsync(42, 84, new([PermissionCatalog.StockView, PermissionCatalog.StockCreate], Guid.Empty), default);
        Assert.True(granted.IsSuccess, granted.Error?.Message);
        var revoked = await repository.SaveAsync(42, 84, new([PermissionCatalog.StockView], granted.Value!.Version), default);
        Assert.True(revoked.IsSuccess, revoked.Error?.Message);
        Assert.DoesNotContain(PermissionCatalog.StockCreate, (await repository.EffectiveAsync(84, default))!.Permissions);
        var history = (await repository.HistoryAsync(42, 84, 1, default)).Value!;
        Assert.Equal(10, history.Total); Assert.Contains(history.Items, h => h.Action == "Revocado" && h.Actor == "Administrador de prueba");
        Assert.Equal(ErrorCodes.Conflict, (await repository.SaveAsync(42, 84, new([], granted.Value.Version), default)).Error?.Code);
        var tasks = new List<Task<Lims.Application.Common.OperationResult<UserPermissionState>>>();
        await using var otherDb = new LimsDbContext(new DbContextOptionsBuilder<LimsDbContext>().UseNpgsql(settings.ConnectionString, o => o.EnableRetryOnFailure()).Options);
        var other = new PermissionRepository(otherDb, new() { EnforceIndividualPermissions = true }, TimeProvider.System);
        tasks.Add(repository.SaveAsync(42, 84, new([], revoked.Value!.Version), default));
        tasks.Add(other.SaveAsync(42, 84, new([PermissionCatalog.IntermediateView], revoked.Value.Version), default));
        var results = await Task.WhenAll(tasks);
        Assert.Single(results, r => r.IsSuccess); Assert.Single(results, r => r.Error?.Code == ErrorCodes.Conflict);
        // Retain the isolated schema as reproducible evidence, matching the existing test cluster lifecycle.
    }
}

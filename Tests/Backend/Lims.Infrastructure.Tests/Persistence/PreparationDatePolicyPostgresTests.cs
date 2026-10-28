using Lims.Application.ReferencePreparations;
using Lims.Contracts.ReferencePreparations;
using Lims.Domain.ReferencePreparations;
using Lims.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Lims.Infrastructure.Tests.Persistence;

public sealed class PreparationDatePolicyPostgresTests
{
    [StockPostgresFact]
    public async Task DeploymentSqlRequiresPredecessorIsIdempotentAndRetainsAuditChecks()
    {
        await using var f = await StockPostgresTests.Fixture.CreateAsync(10000);
        await using var db = f.Context();
        var migration = new Lims.Infrastructure.Persistence.Migrations.PausePreparationDateValidation();
        var generator = db.GetService<IMigrationsSqlGenerator>();
        foreach (var command in generator.Generate(migration.DownOperations)) await db.Database.ExecuteSqlRawAsync(command.CommandText);
        var scriptPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../artifacts/sql/pause_preparation_date_validation.sql"));
        var script = await File.ReadAllTextAsync(scriptPath);
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(script));
        await db.Database.ExecuteSqlRawAsync("ROLLBACK");
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE \"__EFMigrationsHistory\" (\"MigrationId\" varchar(150) PRIMARY KEY, \"ProductVersion\" varchar(32) NOT NULL); INSERT INTO \"__EFMigrationsHistory\" VALUES ('20261024173757_AllowHistoricalStockDates', '10.0.10')");
        await db.Database.ExecuteSqlRawAsync(script); await db.Database.ExecuteSqlRawAsync(script);
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Equal(1, await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = '20261028045900_PausePreparationDateValidation'").SingleAsync());
        var historical = ReferencePreparation.Intermediate(Guid.NewGuid(), "PAUSED-SQL", "Prueba SQL", 1, "Método", 10, "mL", "[]",
            new(2020, 1, 1), new(2019, 1, 1), 42, null, DateTimeOffset.UtcNow, new string('A', 64), null, false);
        db.ReferencePreparations.Add(historical); await db.SaveChangesAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            foreach (var command in generator.Generate(migration.DownOperations)) await db.Database.ExecuteSqlRawAsync(command.CommandText);
        });
        await transaction.RollbackAsync();
        Assert.Equal(1, await db.ReferencePreparations.CountAsync());
        var audit = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("UPDATE reference_preparations SET updated_at = created_at - interval '1 minute'"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, audit.SqlState);
    }

    [StockPostgresFact]
    public async Task StockIntermediateCurveAndAqsKeepArbitraryDatesAndSharePhysicalBalances()
    {
        await using var f = await StockPostgresTests.Fixture.CreateAsync(10000);
        await using var db = f.Context();
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-6)).DateTime);
        var stockRequest = f.Request(1000) with { PreparationDate = today.AddDays(30), ExpirationDate = today.AddYears(-1) };
        var stock = await new StockService(new StockRepository(db), TimeProvider.System).CreateAsync(stockRequest, 42, default);
        Assert.True(stock.IsSuccess, stock.Error?.Message);
        var intermediates = new IntermediateService(new IntermediateRepository(db), TimeProvider.System);
        var source = (await intermediates.OptionsAsync(default)).Value!.Sources.Single();
        var intermediate = await intermediates.CreateAsync(new(Guid.NewGuid(), "Fechas históricas de prueba",
            new(source.MethodId, 100, "mL", [new(source.Id, source.Version, 10, "mL")]),
            new(2020, 1, 1), new(2019, 1, 1), null), 42, default);
        Assert.True(intermediate.IsSuccess, intermediate.Error?.Message);
        var groups = new SolutionGroupService(new SolutionGroupRepository(db), TimeProvider.System);
        foreach (var kind in new[] { "Curva", "AQS" })
        {
            var origin = (await groups.OptionsAsync(default)).Sources.Single(s => s.Id == intermediate.Value!.Preparation.Id);
            var request = new CreateSolutionGroupRequest(Guid.NewGuid(), kind + "-FECHAS-LIBRES", null,
                new(origin.MethodId, [new(origin.Id, origin.Version, 2, "mL", 10, "mL", SolventId: 4,
                    InternalStandard: new(origin.Id, origin.Version, 1, "mL"))]), today.AddYears(1), today.AddYears(-2), null);
            var saved = await groups.CreateAsync(kind, request, 42, default);
            Assert.True(saved.IsSuccess, saved.Error?.Message);
            Assert.Equal(request.PreparationDate, saved.Value!.Preparation.PreparationDate);
            Assert.Equal(request.ExpirationDate, saved.Value.Preparation.ExpirationDate);
            Assert.Equal(3, Assert.Single(saved.Value.Consumptions).Quantity);
        }
        Assert.Equal(94, await db.ReferencePreparations.Where(p => p.Id == intermediate.Value!.Preparation.Id).Select(p => p.AvailableVolume).SingleAsync());
        Assert.Equal(4, await db.ReferenceMaterialMovements.CountAsync());
        var audit = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("UPDATE reference_preparations SET updated_at = created_at - interval '1 minute'"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, audit.SqlState);
    }

    [Fact]
    public void DomainDateChecksRemainAvailableForReactivation()
    {
        var date = new DateOnly(2020, 1, 1); var expiration = date.AddDays(-1);
        ReferencePreparation Create(bool enforce) => ReferencePreparation.Intermediate(Guid.NewGuid(), "TEST", "Prueba", 1, "Método",
            10, "mL", "[]", date, expiration, 42, null, DateTimeOffset.UtcNow, new string('A', 64), null, enforce);
        Assert.Throws<ArgumentException>(() => Create(true));
        var paused = Create(false);
        Assert.Throws<InvalidOperationException>(() => paused.ConsumeVolume(1, 42, DateTimeOffset.UtcNow, date, true));
        paused.ConsumeVolume(1, 42, DateTimeOffset.UtcNow, date, false);
        Assert.Equal(9, paused.AvailableVolume);
    }
}

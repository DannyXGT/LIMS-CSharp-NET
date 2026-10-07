using Lims.Application.ReferencePreparations;
using Lims.Contracts.Errors;
using Lims.Contracts.ReferencePreparations;
using Lims.Domain.ReferenceMaterials;
using Lims.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace Lims.Infrastructure.Tests.Persistence;

/// <summary>Runs only against a separately provisioned loopback test database, never InterDB.</summary>
public sealed class StockPostgresFactAttribute : FactAttribute
{
    public StockPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("LIMS_STOCK_TEST_CONNECTION")))
            Skip = "Ejecute scripts/test-stock.ps1 para provisionar PostgreSQL local aislado.";
    }
}

public sealed class StockPostgresTests
{
    [StockPostgresFact]
    public async Task MeasuredWeightAndConvertedDebitArePersistedSeparatelyFromTheoreticalWeight()
    {
        await using var fixture = await Fixture.CreateAsync(149.102007m, 99.11m);
        await using var context = fixture.Context();
        var request = fixture.Request(100) with
        {
            Calculation = fixture.Request(100).Calculation with { FinalVolume = 10, ActualWeight = 1.008980m, ActualWeightUnit = "mg" },
            StorageTemperature = "2 a 8 °C",
        };
        var result = await Service(context).CreateAsync(request, 84, CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error?.Message);
        var preparation = await context.ReferencePreparations.AsNoTracking().SingleAsync();
        Assert.NotEqual(preparation.CalculatedWeight, preparation.ActualWeight);
        Assert.Equal(1.008980m, preparation.ActualWeight);
        Assert.Equal(99.11m, preparation.PurityPercentUsed);
        Assert.Equal("mg", preparation.CalculatedWeightUnit);
        Assert.Equal("mg", preparation.ActualWeightUnit);
        Assert.Equal("2 a 8 °C", preparation.StorageTemperature);
        Assert.Equal(148.093027m, (await context.ReferenceMaterials.AsNoTracking().SingleAsync()).AvailableQuantity);
        Assert.Equal(preparation.ActualWeight, (await context.ReferenceMaterialMovements.SingleAsync()).Quantity);
        Assert.Equal(84, preparation.PreparedByUserId);
        Assert.Equal(request.ExpirationDate, preparation.ExpirationDate);
    }
    [StockPostgresFact]
    public async Task MissingWeightOrExpirationIsRejectedWithoutAnyWrites()
    {
        await using var fixture = await Fixture.CreateAsync(100);
        await using var context = fixture.Context();
        var service = Service(context);
        var valid = fixture.Request(50);
        foreach (var invalid in new[] { valid with { Calculation = valid.Calculation with { ActualWeight = null } }, valid with { ExpirationDate = null }, valid with { ExpirationDate = valid.PreparationDate.AddDays(-1) } })
            Assert.Equal(ErrorCodes.ValidationError, (await service.CreateAsync(invalid, 42, CancellationToken.None)).Error?.Code);
        Assert.Empty(await context.ReferencePreparations.ToArrayAsync());
        Assert.Empty(await context.ReferenceMaterialMovements.ToArrayAsync());
        Assert.Equal(100, (await context.ReferenceMaterials.SingleAsync()).AvailableQuantity);
    }
    [StockPostgresFact]
    public async Task CreatesPreparationMovementAndRemainingBalanceWithServerActorAndCalculation()
    {
        await using var fixture = await Fixture.CreateAsync(150, 50);
        await using var context = fixture.Context();
        var request = fixture.Request(50);
        var result = await Service(context).CreateAsync(request, 84, CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(100, result.Value!.Calculation.CalculatedWeight);
        Assert.Equal(50, result.Value.Consumption.BalanceAfter);
        Assert.Equal("Ana López", result.Value.Preparation.PreparedBy);
        Assert.Equal("Ana López", result.Value.Consumption.Actor);
        Assert.Equal(84, (await context.ReferenceMaterialMovements.SingleAsync()).ActorUserId);
        Assert.Equal(84, (await context.ReferenceMaterials.AsNoTracking().SingleAsync()).UpdatedByUserId);
        Assert.NotEqual(fixture.Source.Version, (await context.ReferenceMaterials.AsNoTracking().SingleAsync()).Version);
    }

    [StockPostgresFact]
    public async Task ExactDebitDepletesSourceAndExcludesItFromSelector()
    {
        await using var fixture = await Fixture.CreateAsync(100);
        await using var context = fixture.Context();
        var service = Service(context);
        Assert.True((await service.CreateAsync(fixture.Request(100), 42, CancellationToken.None)).IsSuccess);
        Assert.Equal(0, (await context.ReferenceMaterials.AsNoTracking().SingleAsync()).AvailableQuantity);
        Assert.Empty((await service.SourcesAsync(null, 1, 25, CancellationToken.None)).Value!.Items);
    }

    [StockPostgresFact]
    public async Task InsufficientAvailabilityLeavesAllThreeRecordsUnchanged()
    {
        await using var fixture = await Fixture.CreateAsync(80);
        await using var context = fixture.Context();
        var result = await Service(context).CreateAsync(fixture.Request(100), 42, CancellationToken.None);
        Assert.Equal(ErrorCodes.InvalidState, result.Error?.Code);
        Assert.Empty(await context.ReferencePreparations.ToArrayAsync());
        Assert.Empty(await context.ReferenceMaterialMovements.ToArrayAsync());
        Assert.Equal(80, (await context.ReferenceMaterials.AsNoTracking().SingleAsync()).AvailableQuantity);
    }

    [StockPostgresFact]
    public async Task FailureAfterDatabaseWritesRollsBackPreparationConsumptionAndSourceBalance()
    {
        await using var fixture = await Fixture.CreateAsync(100);
        await using var context = fixture.Context(new FailAfterSave());
        await Assert.ThrowsAsync<IOException>(() => Service(context).CreateAsync(fixture.Request(50), 42, CancellationToken.None));
        await using var verify = fixture.Context();
        Assert.Empty(await verify.ReferencePreparations.ToArrayAsync());
        Assert.Empty(await verify.ReferenceMaterialMovements.ToArrayAsync());
        var source = await verify.ReferenceMaterials.SingleAsync();
        Assert.Equal(100, source.AvailableQuantity);
        Assert.Equal(fixture.Source.Version, source.Version);
    }

    [StockPostgresFact]
    public async Task TwoSimultaneousConsumersCannotOverdrawAndLoserRollsBack()
    {
        await using var fixture = await Fixture.CreateAsync(100);
        var barrier = new SaveBarrier();
        await using var first = fixture.Context(barrier);
        await using var second = fixture.Context(barrier);
        var results = await Task.WhenAll(Service(first).CreateAsync(fixture.Request(75), 42, CancellationToken.None),
            Service(second).CreateAsync(fixture.Request(75), 84, CancellationToken.None));
        Assert.Single(results, result => result.IsSuccess);
        Assert.Single(results, result => result.Error?.Code == ErrorCodes.Conflict);
        await using var verify = fixture.Context();
        Assert.Equal(25, (await verify.ReferenceMaterials.SingleAsync()).AvailableQuantity);
        Assert.Single(await verify.ReferencePreparations.ToArrayAsync());
        Assert.Single(await verify.ReferenceMaterialMovements.ToArrayAsync());
    }

    [StockPostgresFact]
    public async Task RetryingExecutionStrategyReloadsSourceAfterRollbackWithoutDoubleDebit()
    {
        await using var fixture = await Fixture.CreateAsync(100);
        var interceptor = new FailOnceAfterSave();
        await using var context = fixture.Context(interceptor, retryIo: true);
        var result = await Service(context).CreateAsync(fixture.Request(50), 42, CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(2, interceptor.Calls);
        Assert.Equal(50, (await context.ReferenceMaterials.AsNoTracking().SingleAsync()).AvailableQuantity);
        Assert.Single(await context.ReferenceMaterialMovements.ToArrayAsync());
    }

    [StockPostgresFact]
    public async Task RepeatedRequestIsIdempotentAndChangedPayloadCannotReuseIt()
    {
        await using var fixture = await Fixture.CreateAsync(100);
        await using var context = fixture.Context();
        var request = fixture.Request(50);
        var first = await Service(context).CreateAsync(request, 42, CancellationToken.None);
        var repeated = await Service(context).CreateAsync(request, 42, CancellationToken.None);
        var changed = await Service(context).CreateAsync(request with { Name = "Otro nombre" }, 42, CancellationToken.None);
        Assert.True(first.IsSuccess);
        Assert.True(repeated.IsSuccess);
        Assert.Equal(first.Value!.Preparation.Code, repeated.Value!.Preparation.Code);
        Assert.Equal(ErrorCodes.Conflict, changed.Error?.Code);
        Assert.Equal(50, (await context.ReferenceMaterials.AsNoTracking().SingleAsync()).AvailableQuantity);
        Assert.Single(await context.ReferenceMaterialMovements.ToArrayAsync());
    }

    [StockPostgresFact]
    public async Task PreviewIsReadOnlyAndCreateRejectsAChangedSourceVersion()
    {
        await using var fixture = await Fixture.CreateAsync(100);
        var request = fixture.Request(50);
        await using var context = fixture.Context();
        Assert.True((await Service(context).PreviewAsync(request.Calculation, CancellationToken.None)).IsSuccess);
        Assert.Equal(100, (await context.ReferenceMaterials.SingleAsync()).AvailableQuantity);
        await using var other = fixture.Context();
        var source = await other.ReferenceMaterials.SingleAsync();
        source.Archive("Fin de uso", 84, DateTimeOffset.UtcNow);
        await other.SaveChangesAsync();
        var result = await Service(context).CreateAsync(request, 42, CancellationToken.None);
        Assert.Equal(ErrorCodes.Conflict, result.Error?.Code);
        Assert.Empty(await context.ReferencePreparations.ToArrayAsync());
    }

    [StockPostgresFact]
    public async Task SearchFindsNameCasCatalogAndLotWithoutTreatingWildcardsAsData()
    {
        await using var fixture = await Fixture.CreateAsync(100);
        await using var context = fixture.Context();
        var service = Service(context);
        foreach (var search in new[] { "PRUEBA", "64-17-5", "CAT-001", "LOT-001" })
            Assert.Single((await service.SourcesAsync(search, 1, 25, CancellationToken.None)).Value!.Items);
        Assert.Empty((await service.SourcesAsync("%", 1, 25, CancellationToken.None)).Value!.Items);
        Assert.True((await service.CreateAsync(fixture.Request(50), 42, CancellationToken.None)).IsSuccess);
        Assert.Single((await service.ListAsync("Stock", 1, 25, CancellationToken.None)).Value!.Items);
    }

    [StockPostgresFact]
    public async Task HistoricalDetailKeepsOriginalPurityLotAndFormulaAfterSourceIsEdited()
    {
        await using var fixture = await Fixture.CreateAsync(100);
        await using var context = fixture.Context();
        var request = fixture.Request(50);
        Assert.True((await Service(context).CreateAsync(request, 42, CancellationToken.None)).IsSuccess);
        await using var other = fixture.Context();
        var material = await other.ReferenceMaterials.Include(item => item.Method).Include(item => item.Unit).Include(item => item.Location).SingleAsync();
        material.Update("Nombre corregido", material.CasNumber, material.CatalogNumber, material.Method, 99, "LOT-NUEVO", material.Brand,
            material.ReceivedDate, material.ExpirationDate, material.PresentationQuantity, material.Unit, 1, material.StorageTemperature, material.Location, 84, DateTimeOffset.UtcNow);
        await other.SaveChangesAsync();
        var detail = (await Service(context).GetAsync(request.RequestId, CancellationToken.None)).Value!;
        Assert.Equal("LOT-001", detail.Source.Lot);
        Assert.Equal(100, detail.Calculation.PurityPercent);
        Assert.Equal(StockCalculator.MassFormula, detail.Calculation.Formula);
        Assert.Equal(50, detail.Consumption.Quantity);
    }

    [StockPostgresFact]
    public async Task DatabaseRejectsNegativeBalanceAndSourceDeletionWithTraceableConsumption()
    {
        await using var fixture = await Fixture.CreateAsync(100);
        await using var context = fixture.Context();
        Assert.True((await Service(context).CreateAsync(fixture.Request(50), 42, CancellationToken.None)).IsSuccess);
        var negative = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync("UPDATE reference_materials SET available_quantity = -1"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, negative.SqlState);
        var deletion = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync("DELETE FROM reference_materials"));
        Assert.Equal(PostgresErrorCodes.RestrictViolation, deletion.SqlState);
    }

    [StockPostgresFact]
    public async Task NewMigrationAndIdempotentSqlAreEquivalentAndCanBeAppliedTwiceInIsolatedSchema()
    {
        await using var fixture = await Fixture.CreateAsync(100);
        await using var context = fixture.Context();
        // The fixture model owns these tables; remove only the two empty Stock tables in this isolated schema.
        await context.Database.ExecuteSqlRawAsync("DROP TABLE reference_material_movements; DROP TABLE reference_preparations; DROP SEQUENCE reference_stock_code_sequence;");
        await context.Database.ExecuteSqlRawAsync("CREATE TABLE \"__EFMigrationsHistory\" (\"MigrationId\" varchar(150) PRIMARY KEY, \"ProductVersion\" varchar(32) NOT NULL); INSERT INTO \"__EFMigrationsHistory\" VALUES ('20261001225215_FinalizeReferenceMaterialCatalogs', '10.0.10');");
        var scriptPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../artifacts/sql/reference_stock.sql"));
        var script = await File.ReadAllTextAsync(scriptPath);
        await context.Database.ExecuteSqlRawAsync(script);
        await context.Database.ExecuteSqlRawAsync(script);
        Assert.False(context.Database.HasPendingModelChanges());
        Assert.True((await Service(context).CreateAsync(fixture.Request(50), 42, CancellationToken.None)).IsSuccess);
    }

    private static StockService Service(LimsDbContext context) => new(new StockRepository(context), TimeProvider.System);
    private sealed class Fixture(string connectionString, ReferenceMaterial source) : IAsyncDisposable
    {
        public ReferenceMaterial Source { get; } = source;
        public LimsDbContext Context(IInterceptor? interceptor = null, bool retryIo = false)
        {
            var builder = new DbContextOptionsBuilder<LimsDbContext>().UseNpgsql(connectionString, options =>
                options.EnableRetryOnFailure(2, TimeSpan.Zero, retryIo ? ["XX000"] : null));
            if (interceptor is not null) builder.AddInterceptors(interceptor);
            return new LimsDbContext(builder.Options);
        }
        public CreateStockRequest Request(decimal concentration) => new(Guid.NewGuid(), "Stock de prueba",
            new(Source.Id, Source.Version, concentration, "mg/L", 1000, "mL", concentration * 100m / Source.PurityPercent, "mg"),
            DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-6)).DateTime), "Datos ficticios de validación", new DateOnly(2099, 12, 31), "Ambiente");
        public static async Task<Fixture> CreateAsync(decimal available, decimal purity = 100)
        {
            var connection = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("LIMS_STOCK_TEST_CONNECTION"));
            if (connection.Host != "127.0.0.1" || connection.Database != "lims_stock_test" || connection.Port != 55439)
                throw new InvalidOperationException("Stock tests require the isolated loopback database on port 55439.");
            var schema = "stock_" + Guid.NewGuid().ToString("N");
            await using (var admin = new NpgsqlConnection(connection.ConnectionString))
            {
                await admin.OpenAsync();
                await using var command = new NpgsqlCommand("CREATE SCHEMA " + schema, admin);
                await command.ExecuteNonQueryAsync();
            }
            connection.SearchPath = schema;
            await using var context = new LimsDbContext(new DbContextOptionsBuilder<LimsDbContext>().UseNpgsql(connection.ConnectionString).Options);
            var script = context.Database.GenerateCreateScript();
            if (!script.Contains("CREATE TABLE usuarios", StringComparison.Ordinal) && !script.Contains("CREATE TABLE \"usuarios\"", StringComparison.Ordinal))
                await context.Database.ExecuteSqlRawAsync("CREATE TABLE usuarios (id integer PRIMARY KEY, nombre varchar(100), activo boolean);");
            await context.Database.ExecuteSqlRawAsync(script);
            await context.Database.ExecuteSqlRawAsync("INSERT INTO usuarios (id, nombre, activo) VALUES (42, 'Danny Jimenez', true), (84, 'Ana López', false);");
            var material = new ReferenceMaterial(Guid.NewGuid(), "Estándar de prueba", "64-17-5", "CAT-001", await context.ReferenceMethods.FirstAsync(),
                purity, "LOT-001", "Marca ficticia", new DateOnly(2026, 1, 1), new DateOnly(2099, 12, 31), available,
                await context.ReferenceUnits.SingleAsync(item => item.Symbol == "mg"), 1, "Ambiente",
                await context.ReferenceLocations.FirstAsync(), 42, DateTimeOffset.UtcNow);
            context.ReferenceMaterials.Add(material);
            await context.SaveChangesAsync();
            return new Fixture(connection.ConnectionString, material);
        }
        // The provisioned test cluster is retained for evidence; the wrapper stops its process.
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class FailAfterSave : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default) => throw new IOException("Injected failure after all writes");
    }
    private sealed class FailOnceAfterSave : SaveChangesInterceptor
    {
        public int Calls { get; private set; }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Calls == 1) throw new PostgresException("Injected retry after all writes", "ERROR", "ERROR", "XX000");
            return ValueTask.FromResult(result);
        }
    }
    private sealed class SaveBarrier : SaveChangesInterceptor
    {
        private int _calls;
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _calls) == 2) _ready.TrySetResult();
            await _ready.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            return result;
        }
    }
}

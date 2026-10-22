using Lims.Application.ReferencePreparations;
using Lims.Contracts.Errors;
using Lims.Contracts.ReferencePreparations;
using Lims.Domain.ReferenceMaterials;
using Lims.Infrastructure.Persistence;
using Lims.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
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
    public Task RecurringCalculatedWeightRoundTripsWithoutOverflowAndCanBeDiluted() => AssertDecimalRoundTripAsync(1000);

    [StockPostgresFact]
    public Task RecurringActualConcentrationRoundTripsWithoutOverflowAndCanBeDiluted() => AssertDecimalRoundTripAsync(3);

    private static async Task AssertDecimalRoundTripAsync(decimal finalVolume)
    {
        await using var fixture = await Fixture.CreateAsync(150, 99.11m);
        await using var context = fixture.Context();
        var request = fixture.Request(100);
        request = request with { Calculation = request.Calculation with { ActualWeight = 100m, FinalVolume = finalVolume } };
        var expected = StockCalculator.Calculate(fixture.Source, request.Calculation);
        var result = await Service(context).CreateAsync(request, 42, CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(expected.CalculatedWeight, result.Value!.Calculation.CalculatedWeight);
        Assert.Equal(expected.ActualConcentration, result.Value.Calculation.ActualConcentration);
        Assert.True((await Service(context).GetAsync(request.RequestId, CancellationToken.None)).IsSuccess);
        Assert.Single((await Service(context).ListAsync(null, 1, 25, CancellationToken.None)).Value!.Items);

        var intermediate = new IntermediateService(new IntermediateRepository(context), TimeProvider.System);
        var options = await intermediate.OptionsAsync(CancellationToken.None);
        var source = Assert.Single(options.Value!.Sources);
        var volumeTaken = Math.Min(25, source.InitialVolume / 4);
        var dilution = new CreateIntermediateRequest(Guid.NewGuid(), "Dilución de Stock con peso periódico",
            new(source.MethodId, 100, "mL", [new(source.Id, source.Version, volumeTaken, "mL")]),
            request.PreparationDate, request.ExpirationDate!.Value, null);
        var diluted = await intermediate.CreateAsync(dilution, 42, CancellationToken.None);
        Assert.True(diluted.IsSuccess, diluted.Error?.Message);
        Assert.Single(diluted.Value!.Consumptions);
        Assert.Equal(expected.ActualConcentration!.Value * volumeTaken / 100, diluted.Value.Calculation.Results[0].Concentration);
    }

    [StockPostgresFact]
    public async Task PrecisionMigrationRepairsExistingValuesAndTheDeployedFixedScaleModel()
    {
        await using var fixture = await Fixture.CreateAsync(300, 99.11m);
        await using var context = fixture.Context();
        await context.Database.ExecuteSqlRawAsync("ALTER TABLE reference_preparations ALTER COLUMN calculated_weight TYPE numeric(40,28), ALTER COLUMN actual_concentration TYPE numeric(40,28)");
        await using var legacy = new LimsDbContext(new DbContextOptionsBuilder<LimsDbContext>()
            .UseNpgsql(context.Database.GetConnectionString(), options => options.EnableRetryOnFailure(2))
            .ReplaceService<IModelCustomizer, FixedScaleModelCustomizer>().Options);
        var request = fixture.Request(100);
        request = request with { Calculation = request.Calculation with { ActualWeight = 100m } };
        await Assert.ThrowsAsync<OverflowException>(() => Service(legacy).CreateAsync(request, 42, CancellationToken.None));
        Assert.Empty(await context.ReferencePreparations.ToArrayAsync());
        Assert.Empty(await context.ReferenceMaterialMovements.ToArrayAsync());
        Assert.Equal(300, (await context.ReferenceMaterials.SingleAsync()).AvailableQuantity);

        // Simulate a historic fixed-scale row, then prove normalization preserves its exact value.
        var historical = await Service(context).CreateAsync(request with
        {
            RequestId = Guid.NewGuid(),
            Calculation = request.Calculation with { TargetConcentration = 99.11m },
        }, 42, CancellationToken.None);
        Assert.True(historical.IsSuccess, historical.Error?.Message);
        var expected = StockCalculator.Calculate(fixture.Source, request.Calculation).CalculatedWeight;
        var historicalId = historical.Value!.Preparation.Id;
        await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE reference_preparations SET target_concentration = {100m}, calculated_weight = {expected} WHERE id = {historicalId}");
        var before = await context.Database.SqlQuery<string>($"SELECT calculated_weight::text AS \"Value\" FROM reference_preparations WHERE id = {historicalId}").SingleAsync();
        await Assert.ThrowsAsync<OverflowException>(() => Service(legacy).GetAsync(historicalId, CancellationToken.None));

        var commands = context.GetService<IMigrationsSqlGenerator>().Generate(new PreservePreparationDecimalScale().UpOperations, context.Model);
        foreach (var command in commands) await context.Database.ExecuteSqlRawAsync(command.CommandText);
        var repairedHistory = await Service(legacy).GetAsync(historicalId, CancellationToken.None);
        Assert.Equal(expected, repairedHistory.Value!.Calculation.CalculatedWeight);
        Assert.True(await context.Database.SqlQuery<bool>($"SELECT calculated_weight = CAST({before} AS numeric) AS \"Value\" FROM reference_preparations WHERE id = {historicalId}").SingleAsync());
        var fresh = (await new StockRepository(legacy).FindSourceAsync(fixture.Source.Id, CancellationToken.None))!;
        var repairedRequest = request with { Calculation = request.Calculation with { SourceVersion = fresh.Version } };
        Assert.True((await Service(legacy).CreateAsync(repairedRequest, 42, CancellationToken.None)).IsSuccess);
        Assert.Equal(2, await context.ReferenceMaterialMovements.CountAsync());
    }

    private sealed class FixedScaleModelCustomizer(ModelCustomizerDependencies dependencies) : ModelCustomizer(dependencies)
    {
        public override void Customize(ModelBuilder modelBuilder, DbContext context)
        {
            base.Customize(modelBuilder, context);
            modelBuilder.Entity<Lims.Domain.ReferencePreparations.ReferencePreparation>().Property(p => p.CalculatedWeight).HasColumnType("numeric(40,28)").HasPrecision(40, 28);
            modelBuilder.Entity<Lims.Domain.ReferencePreparations.ReferencePreparation>().Property(p => p.ActualConcentration).HasColumnType("numeric(40,28)").HasPrecision(40, 28);
        }
    }

    [StockPostgresFact]
    public async Task PrecisionMigrationRejectsUnrepresentableHistoryWithoutRoundingIt()
    {
        await using var fixture = await Fixture.CreateAsync(150);
        await using var context = fixture.Context();
        Assert.True((await Service(context).CreateAsync(fixture.Request(50), 42, CancellationToken.None)).IsSuccess);
        await context.Database.ExecuteSqlRawAsync("ALTER TABLE reference_preparations ALTER COLUMN calculated_weight TYPE numeric(40,28), ALTER COLUMN actual_concentration TYPE numeric(40,28)");
        await context.Database.ExecuteSqlRawAsync("UPDATE reference_preparations SET actual_concentration = 123456789012.1234567890123456789012345678");
        var before = await context.Database.SqlQueryRaw<string>("SELECT actual_concentration::text AS \"Value\" FROM reference_preparations").SingleAsync();
        var commands = context.GetService<IMigrationsSqlGenerator>().Generate(new PreservePreparationDecimalScale().UpOperations, context.Model);
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            var error = await Assert.ThrowsAsync<PostgresException>(async () =>
            {
                foreach (var command in commands) await context.Database.ExecuteSqlRawAsync(command.CommandText);
            });
            Assert.Equal(PostgresErrorCodes.NumericValueOutOfRange, error.SqlState);
            await transaction.RollbackAsync();
        }
        var after = await context.Database.SqlQueryRaw<string>("SELECT actual_concentration::text AS \"Value\" FROM reference_preparations").SingleAsync();
        Assert.Equal(before, after);
        Assert.Single(await context.ReferenceMaterialMovements.ToArrayAsync());
    }

    [StockPostgresFact]
    public async Task ListAndDetailExposeCurrentSolutionBalanceSeparatelyFromSourceWeight()
    {
        await using var fixture = await Fixture.CreateAsync(100);
        await using var context = fixture.Context();
        var service = Service(context);
        var request = fixture.Request(50);
        var created = await service.CreateAsync(request, 42, CancellationToken.None);
        Assert.True(created.IsSuccess, created.Error?.Message);
        Assert.Equal(1000m, created.Value!.Preparation.AvailableVolume);
        var preparation = await context.ReferencePreparations.SingleAsync(p => p.Id == request.RequestId);
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-6)).DateTime);
        preparation.ConsumeVolume(400, 42, DateTimeOffset.UtcNow, today);
        await context.SaveChangesAsync();
        var listed = Assert.Single((await service.ListAsync(null, 1, 25, CancellationToken.None)).Value!.Items);
        var detail = (await service.GetAsync(request.RequestId, CancellationToken.None)).Value!;
        Assert.Equal(600m, listed.AvailableVolume);
        Assert.Equal(600m, detail.Preparation.AvailableVolume);
        Assert.Equal(400m, detail.Preparation.Availability.ConsumedVolume);
        Assert.Equal(60m, detail.Preparation.Availability.RemainingPercent);
        Assert.Equal("Warning", detail.Preparation.Availability.Tone);
        Assert.Equal(50m, detail.Consumption.Quantity);
        preparation.ConsumeVolume(600, 42, DateTimeOffset.UtcNow, today);
        await context.SaveChangesAsync();
        var depleted = (await service.GetAsync(request.RequestId, CancellationToken.None)).Value!;
        Assert.Equal(0m, depleted.Preparation.AvailableVolume);
        Assert.Equal("Agotado", depleted.Preparation.InventoryLabel);
        Assert.Equal("Danger", depleted.Preparation.Availability.Tone);
    }
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
    public async Task HistoricalExpiredStandardCanBeSelectedPreviewedAndConsumedForExpiredStock()
    {
        await using var fixture = await Fixture.CreateAsync(100);
        await using var context = fixture.Context();
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-6)).DateTime);
        var source = await context.ReferenceMaterials.Include(item => item.Method).Include(item => item.Unit).Include(item => item.Location).SingleAsync();
        source.Update(source.Name, source.CasNumber, source.CatalogNumber, source.Method, source.PurityPercent,
            source.Lot, source.Brand, today.AddDays(-90), today.AddDays(-30), source.PresentationQuantity,
            source.Unit, 1, source.StorageTemperature, source.Location, 42, DateTimeOffset.UtcNow);
        await context.SaveChangesAsync();
        var service = Service(context);
        var selected = Assert.Single((await service.SourcesAsync(null, 1, 25, CancellationToken.None)).Value!.Items);
        var request = fixture.Request(50) with
        {
            Calculation = fixture.Request(50).Calculation with { SourceVersion = selected.Version },
            PreparationDate = today.AddDays(-60),
            ExpirationDate = today.AddDays(-40),
        };
        Assert.True((await service.PreviewAsync(request.Calculation, CancellationToken.None)).IsSuccess);
        var result = await service.CreateAsync(request, 42, CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal("Expired", result.Value!.Preparation.Status);
        Assert.Equal(request.ExpirationDate, result.Value.Preparation.ExpirationDate);
        Assert.Equal(50m, (await context.ReferenceMaterials.AsNoTracking().SingleAsync()).AvailableQuantity);
        Assert.Single(await context.ReferenceMaterialMovements.ToArrayAsync());
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
        Assert.Empty(await verify.StockSolvents.ToArrayAsync());
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
        await context.Database.ExecuteSqlRawAsync("DROP TABLE stock_solvents; DROP TABLE reference_solvents; DROP TABLE reference_material_movements; DROP TABLE reference_preparation_components; DROP TABLE reference_preparations; DROP SEQUENCE reference_stock_code_sequence; DROP SEQUENCE reference_intermediate_code_sequence;");
        await context.Database.ExecuteSqlRawAsync("CREATE TABLE \"__EFMigrationsHistory\" (\"MigrationId\" varchar(150) PRIMARY KEY, \"ProductVersion\" varchar(32) NOT NULL); INSERT INTO \"__EFMigrationsHistory\" VALUES ('20261001225215_FinalizeReferenceMaterialCatalogs', '10.0.10');");
        var scriptPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../artifacts/sql/reference_stock.sql"));
        var script = await File.ReadAllTextAsync(scriptPath);
        await context.Database.ExecuteSqlRawAsync(script);
        await context.Database.ExecuteSqlRawAsync(script);
        var intermediatePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../artifacts/sql/reference_intermediate.sql"));
        var intermediateScript = await File.ReadAllTextAsync(intermediatePath);
        await context.Database.ExecuteSqlRawAsync(intermediateScript);
        await context.Database.ExecuteSqlRawAsync(intermediateScript);
        var precisionPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../artifacts/sql/reference_preparation_decimal_scale.sql"));
        var precisionScript = await File.ReadAllTextAsync(precisionPath);
        await context.Database.ExecuteSqlRawAsync(precisionScript);
        await context.Database.ExecuteSqlRawAsync(precisionScript);
        var solventScript = await File.ReadAllTextAsync(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../artifacts/sql/stock_solvent_composition.sql")));
        await context.Database.ExecuteSqlRawAsync(solventScript);
        await context.Database.ExecuteSqlRawAsync(solventScript);
        var commercialSolvents = await File.ReadAllTextAsync(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../artifacts/sql/commercial_solvent_composition.sql")));
        await context.Database.ExecuteSqlRawAsync(commercialSolvents);
        await context.Database.ExecuteSqlRawAsync(commercialSolvents);
        Assert.Equal(12, await context.ReferenceSolvents.CountAsync());
        Assert.False(context.Database.HasPendingModelChanges());
        Assert.True((await Service(context).CreateAsync(fixture.Request(50), 42, CancellationToken.None)).IsSuccess);
    }

    [StockPostgresFact]
    public async Task SolventCatalogHasTwelveRealNamesAndSearchesAbbreviationAndName()
    {
        await using var f = await Fixture.CreateAsync(100);
        await using var db = f.Context(); var service = Service(db);
        var all = (await service.SolventsAsync(null, default)).Value!;
        Assert.Equal<string>(["THF", "Hxn", "ACN", "MeOH", "Tolueno", "MTBE", "Ciclohexano", "H2O", "HNO3", "HCl", "Acetona", "DMSO"], all.Select(s => s.Abbreviation));
        Assert.Equal("Metanol", Assert.Single((await service.SolventsAsync("meoh", default)).Value!).Name);
        Assert.Equal("H2O", Assert.Single((await service.SolventsAsync("agua", default)).Value!).Abbreviation);
        Assert.Empty((await service.SolventsAsync("%", default)).Value!);
        await db.Database.ExecuteSqlRawAsync("UPDATE reference_solvents SET is_active = false WHERE id = 4");
        Assert.Empty((await service.SolventsAsync("MeOH", default)).Value!);
    }

    [StockPostgresFact]
    public async Task CompositionRoundTripsInUserOrderWithCompoundAndOriginalCalculation()
    {
        foreach (var rows in new StockSolventInput[][] { [new(3, 100)], [new(4, 50), new(8, 50)], [new(4, 30), new(3, 20), new(8, 50)], [new(8, 66.6667m), new(4, 33.3333m)] })
        {
            await using var f = await Fixture.CreateAsync(100);
            var request = f.Request(50) with { Solvents = rows, Compound = "Analito corregido" };
            await using (var db = f.Context())
            {
                var result = await Service(db).CreateAsync(request, 42, default);
                Assert.True(result.IsSuccess, result.Error?.Message);
                // Identical request cannot duplicate the Stock, ledger or composition.
                Assert.True((await Service(db).CreateAsync(request, 42, default)).IsSuccess);
                Assert.Equal(rows.Length, await db.StockSolvents.CountAsync());
                Assert.Single(await db.ReferenceMaterialMovements.ToArrayAsync());
            }
            await using var reopened = f.Context();
            var detail = (await Service(reopened).GetAsync(request.RequestId, default)).Value!;
            Assert.Equal("Analito corregido", detail.Compound);
            Assert.Equal(StockSolventComposition.Basis, detail.CompositionBasis);
            Assert.Equal(rows.Select(r => r.SolventId), detail.Solvents!.Select(r => r.SolventId));
            Assert.Equal(rows.Select(r => r.Percent), detail.Solvents!.Select(r => r.Percent));
            Assert.Equal(Enumerable.Range(0, rows.Length), detail.Solvents!.Select(r => r.Position));
            Assert.Equal(50, detail.Calculation.ActualWeight);
            Assert.Equal(50, detail.Calculation.ActualConcentration);
            Assert.Equal(StockCalculator.MassFormula, detail.Calculation.Formula);
            Assert.Equal(50, (await reopened.ReferenceMaterials.SingleAsync()).AvailableQuantity);
        }
    }

    [StockPostgresFact]
    public async Task InvalidCompositionAndUnknownOrInactiveCatalogRowsNeverConsumeSource()
    {
        await using var f = await Fixture.CreateAsync(100);
        await using var db = f.Context(); var service = Service(db); var valid = f.Request(50);
        foreach (var rows in new StockSolventInput[][] { [], [new(4, -1), new(8, 101)], [new(4, 0), new(8, 100)], [new(4, 40), new(8, 40)], [new(4, 50), new(8, 60)], [new(4, 50), new(4, 50)], [new(0, 100)], [new(999, 100)], [new(4, 33.33333m), new(8, 66.66667m)] })
            Assert.Equal(ErrorCodes.ValidationError, (await service.CreateAsync(valid with { Solvents = rows }, 42, default)).Error?.Code);
        Assert.Equal(ErrorCodes.ValidationError, (await service.CreateAsync(valid with { Compound = " " }, 42, default)).Error?.Code);
        Assert.Equal(ErrorCodes.ValidationError, (await service.CreateAsync(valid with { CompositionBasis = "v/v" }, 42, default)).Error?.Code);
        await db.Database.ExecuteSqlRawAsync("UPDATE reference_solvents SET is_active = false WHERE id = 4");
        Assert.Equal(ErrorCodes.ValidationError, (await service.CreateAsync(valid, 42, default)).Error?.Code);
        Assert.Empty(await db.ReferencePreparations.ToArrayAsync()); Assert.Empty(await db.StockSolvents.ToArrayAsync());
        Assert.Empty(await db.ReferenceMaterialMovements.ToArrayAsync());
        Assert.Equal(100, (await db.ReferenceMaterials.AsNoTracking().SingleAsync()).AvailableQuantity);
    }

    [StockPostgresFact]
    public async Task HistoricalStockWithoutCompositionPreservesNotesAndDoesNotInventCompound()
    {
        await using var f = await Fixture.CreateAsync(100);
        await using var db = f.Context(); var request = f.Request(50); var service = Service(db);
        Assert.True((await service.CreateAsync(request, 42, default)).IsSuccess);
        // Simulate a pre-migration row in this isolated test schema only.
        await db.Database.ExecuteSqlRawAsync("DELETE FROM stock_solvents; UPDATE reference_preparations SET compound = NULL, composition_basis = NULL");
        var historical = (await service.GetAsync(request.RequestId, default)).Value!;
        Assert.Null(historical.Compound); Assert.Empty(historical.Solvents!);
        Assert.Equal("Sin composici\u00f3n especificada", historical.CompositionDisplay);
        Assert.Equal(request.Notes, historical.Notes);
    }

    private static StockService Service(LimsDbContext context) => new(new StockRepository(context), TimeProvider.System);
    internal sealed class Fixture(string connectionString, ReferenceMaterial source) : IAsyncDisposable
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
            DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-6)).DateTime), "Datos ficticios de validación", new DateOnly(2099, 12, 31), "Ambiente", "Compuesto de prueba", [new(4, 100m)], StockSolventComposition.Basis);
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

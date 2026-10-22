extern alias PersistenceEngine;
using System.Text.Json;
using Lims.Contracts.ReferencePreparations;
using Lims.Desktop.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Db = PersistenceEngine::Lims.Infrastructure.Persistence.LimsDbContext;
using CommercialService = PersistenceEngine::Lims.Infrastructure.Persistence.CommercialPreparationService;
using StockRepository = PersistenceEngine::Lims.Infrastructure.Persistence.StockRepository;

namespace Lims.Desktop.VisualHarness;

/// <summary>Real production persistence against an explicitly provisioned loopback schema. Never InterDB.</summary>
internal static class PersistentPreparationValidation
{
    public static bool Enabled => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("LIMS_SOLVENT_TEST_CONFIG"));
    public static Db Context()
    {
        var path = Environment.GetEnvironmentVariable("LIMS_SOLVENT_TEST_CONFIG") ?? throw new InvalidOperationException("Persistent validation was not configured.");
        using var config = JsonDocument.Parse(File.ReadAllText(path));
        var connection = new NpgsqlConnectionStringBuilder(config.RootElement.GetProperty("ConnectionString").GetString());
        if (connection.Host != "127.0.0.1" || connection.Port != 55439 || connection.Database != "lims_stock_test" || connection.SearchPath?.StartsWith("stock_", StringComparison.Ordinal) != true)
            throw new InvalidOperationException("Only the isolated solvent validation database is allowed.");
        return new Db(new DbContextOptionsBuilder<Db>().UseNpgsql(connection.ConnectionString, o => o.EnableRetryOnFailure()).Options);
    }
    public static async Task<ApiCallResult<IReadOnlyList<SolventOption>>> CatalogAsync(string? search, CancellationToken ct)
    {
        await using var db = Context();
        return new(true, await new StockRepository(db).SolventsAsync(search, ct), null, 200);
    }
    public static async Task<ApiCallResult<CommercialPreparationOptions>> OptionsAsync(string kind, string? search, CancellationToken ct)
    {
        await using var db = Context();
        return new(true, await new CommercialService(db, TimeProvider.System).OptionsAsync(kind, search, ct), null, 200);
    }
    public static async Task<ApiCallResult<CommercialPreparationDetail>> RegisterAsync(string kind, RegisterCommercialPreparationRequest request, CancellationToken ct)
    {
        await using var db = Context(); var result = await new CommercialService(db, TimeProvider.System).RegisterAsync(kind, request, 42, ct);
        return new(result.IsSuccess, result.Value, result.Error is null ? null : new(result.Error.Code, result.Error.Message, ""), result.IsSuccess ? 200 : 422);
    }
    public static async Task<ApiCallResult<CommercialPreparationDetail>> GetAsync(string kind, Guid id, CancellationToken ct)
    {
        await using var db = Context(); var result = await new CommercialService(db, TimeProvider.System).GetAsync(kind, id, ct);
        return new(result.IsSuccess, result.Value, result.Error is null ? null : new(result.Error.Code, result.Error.Message, ""), result.IsSuccess ? 200 : 404);
    }
}

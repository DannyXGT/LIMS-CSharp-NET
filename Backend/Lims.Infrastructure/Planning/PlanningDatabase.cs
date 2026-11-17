using Npgsql;

namespace Lims.Infrastructure.Planning;

public sealed class PlanningDatabase(string connectionString) : IAsyncDisposable
{
    public NpgsqlDataSource Source { get; } = NpgsqlDataSource.Create(connectionString);
    public ValueTask DisposeAsync() => Source.DisposeAsync();
}

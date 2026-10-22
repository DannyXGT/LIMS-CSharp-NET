using Lims.Contracts.Alerts;
using Lims.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Lims.Infrastructure.Alerts;

public sealed class AlertJob(LimsDbContext db, AlertEngine engine, AlertDeliveryProcessor deliveries)
{
    public async Task<AlertJobResult> RunAsync(CancellationToken ct)
    {
        // Dedicated, non-pooled session: the lock covers evaluation, outbox recovery and SMTP across all hosts.
        var cs = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString()) { Pooling = false };
        await using var connection = new NpgsqlConnection(cs.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key)", connection);
        command.Parameters.AddWithValue("key", AlertData.JobLock);
        if (await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is not true) return new(false, 0, 0, 0);
        var evaluated = await engine.EvaluateAsync(ct).ConfigureAwait(false);
        var sent = await deliveries.ProcessAsync(ct).ConfigureAwait(false);
        return new(true, evaluated.Evaluated, evaluated.Changed, sent);
        // Disposing the unpooled connection releases the session lock, including after faults.
    }
}

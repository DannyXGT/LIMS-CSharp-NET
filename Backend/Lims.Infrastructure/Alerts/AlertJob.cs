using Lims.Contracts.Alerts;
using Lims.Domain.Alerts;
using Lims.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Lims.Infrastructure.Alerts;

public sealed class AlertJob(LimsDbContext db, AlertEngine engine, AlertDeliveryProcessor deliveries, TimeProvider clock)
{
    public async Task<AlertJobResult> RunAsync(CancellationToken ct, int? actor = null, bool sendEmail = true)
    {
        // Dedicated, non-pooled session: the lock covers evaluation, outbox recovery and SMTP across all hosts.
        var cs = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString()) { Pooling = false };
        await using var connection = new NpgsqlConnection(cs.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key)", connection);
        command.Parameters.AddWithValue("key", AlertData.JobLock);
        if (await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is not true) return new(false, 0, 0, 0);
        // Owning the global lock proves that any previous unfinished host has stopped.
        await db.Set<AlertEvaluation>().Where(r => r.CompletedAt == null)
            .ExecuteUpdateAsync(b => b.SetProperty(r => r.State, "Interrupted")
                .SetProperty(r => r.CompletedAt, clock.GetUtcNow())
                .SetProperty(r => r.Error, "Ejecución interrumpida; recuperada por el siguiente ciclo."), ct).ConfigureAwait(false);
        var run = new AlertEvaluation { StartedAt = clock.GetUtcNow(), ActorUserId = actor };
        db.Add(run); await db.SaveChangesAsync(ct).ConfigureAwait(false);
        try
        {
            if ((await AlertData.SettingsAsync(db, ct).ConfigureAwait(false)).Scopes.Count == 0)
                throw new InvalidOperationException("alerts.scopes_missing");
            var evaluated = await engine.EvaluateAsync(ct, run.Id).ConfigureAwait(false);
            var sent = sendEmail ? await deliveries.ProcessAsync(ct).ConfigureAwait(false) : 0;
            await db.Set<AlertEvaluation>().Where(r => r.Id == run.Id).ExecuteUpdateAsync(b =>
                b.SetProperty(r => r.State, "Succeeded").SetProperty(r => r.CompletedAt, clock.GetUtcNow())
                    .SetProperty(r => r.Sent, sent), ct).ConfigureAwait(false);
            return new(true, evaluated.Evaluated, evaluated.Changed, sent, run.Id);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // A direct update avoids saving rolled-back tracked changes; never persist provider text or secrets.
            using var persistTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var detail = error is PostgresException pg ? $"PostgreSQL {pg.SqlState}" : error.GetType().Name;
            var message = error is InvalidOperationException { Message: "alerts.scopes_missing" }
                ? "Configure los ámbitos y departamentos reales antes de evaluar." : $"El ciclo falló ({detail}). Revise el registro del servidor.";
            await db.Set<AlertEvaluation>().Where(r => r.Id == run.Id).ExecuteUpdateAsync(b =>
                b.SetProperty(r => r.State, r => r.State == "Evaluated" ? "DeliveryFailed" : "Failed")
                    .SetProperty(r => r.CompletedAt, clock.GetUtcNow()).SetProperty(r => r.Error, message), persistTimeout.Token).ConfigureAwait(false);
            throw;
        }
        // Disposing the unpooled connection releases the session lock, including after faults.
    }
}

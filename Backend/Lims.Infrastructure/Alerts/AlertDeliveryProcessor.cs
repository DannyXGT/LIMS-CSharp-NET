using System.Net.Mail;
using Lims.Contracts.Alerts;
using Lims.Contracts.Permissions;
using Lims.Domain.Alerts;
using Lims.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lims.Infrastructure.Alerts;

public sealed class AlertDeliveryProcessor(LimsDbContext db, AlertAccess access, AlertMailOptions options,
    IAlertEmailTransport transport, TimeProvider clock)
{
    public async Task<int> ProcessAsync(CancellationToken ct)
    {
        if (!options.Enabled || !options.Status().Configured) return 0;
        db.ChangeTracker.Clear();
        // The scheduler's session advisory lock proves any previous Sending owner has ended.
        var interrupted = await db.Set<EmailDelivery>().Where(d => d.State == "Sending").ToArrayAsync(ct).ConfigureAwait(false);
        foreach (var row in interrupted)
        {
            row.State = "Uncertain"; row.LastError = "Ejecución interrumpida durante SMTP. Verificar el Message-ID con el proveedor antes de reenviar.";
            var attempt = await db.Set<EmailAttempt>().SingleAsync(a => a.DeliveryId == row.Id && a.Number == row.Attempts, ct).ConfigureAwait(false);
            attempt.Outcome = row.State; attempt.Error = row.LastError;
        }
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        var now = clock.GetUtcNow();
        var ids = await db.Set<EmailDelivery>().Where(d => (d.State == "Pending" || d.State == "Retry") && d.NextAttemptAt <= now)
            .OrderBy(d => d.NextAttemptAt).ThenBy(d => d.Id).Take(100).Select(d => d.Id).ToArrayAsync(ct).ConfigureAwait(false);
        var sent = 0;
        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested(); db.ChangeTracker.Clear();
            var row = await db.Set<EmailDelivery>().SingleAsync(d => d.Id == id, ct).ConfigureAwait(false);
            var settings = await AlertData.SettingsAsync(db, ct).ConfigureAwait(false);
            var actor = await access.ActorAsync(row.UserId, ct).ConfigureAwait(false);
            var address = await db.Users.AsNoTracking().Where(u => u.Id == row.UserId && u.IsActive).Select(u => u.Email).SingleOrDefaultAsync(ct).ConfigureAwait(false);
            var recipient = settings.Recipients.SingleOrDefault(r => r.UserId == row.UserId && r.DepartmentId == row.DepartmentId);
            var test = row.Kind == "Test";
            var authorized = actor is not null && address == row.Recipient &&
                (test ? actor.Administrator && actor.Has(PermissionCatalog.NotificationsConfigure) : settings.EmailEnabled && recipient is not null && actor.Has(PermissionCatalog.AlertsView) && (row.Kind == "Immediate" ? recipient.Immediate : recipient.Daily));
            if (authorized && !test)
            {
                var pref = await db.Set<NotificationPreference>().AsNoTracking().SingleOrDefaultAsync(p => p.UserId == row.UserId, ct).ConfigureAwait(false);
                var mask = settings.Scopes.Where(s => s.DepartmentId == row.DepartmentId).Aggregate(0, (sum, s) => sum | (int)s.Module);
                var visible = AlertAccess.Visible(db.Set<MaterialAlert>().AsNoTracking(), actor!).Where(a => a.DepartmentId == row.DepartmentId && a.ResolvedAt == null && (a.Modules & mask) != 0);
                if (row.Kind == "Immediate") visible = visible.Where(a => a.Id == row.AlertId && a.Revision == row.AlertRevision && a.Priority == (int)AlertPriority.Critical);
                else if (pref?.EmailEnabled == false) authorized = false;
                var alerts = authorized ? await visible.ToArrayAsync(ct).ConfigureAwait(false) : [];
                authorized = authorized && alerts.Length > 0;
                if (authorized) row.Html = AlertEmailTemplate.Render(row.Subject, "Condiciones vigentes y autorizadas al momento de enviar.", alerts.Select(AlertData.Item));
            }
            if (!authorized)
            {
                row.State = "Cancelled"; row.LastError = "Condición, destinatario, canal o autorización ya no vigente.";
                await db.SaveChangesAsync(ct).ConfigureAwait(false); continue;
            }
            row.State = "Sending"; row.Attempts++; row.LastError = null;
            var attemptRow = new EmailAttempt { DeliveryId = row.Id, Number = row.Attempts, At = clock.GetUtcNow(), Outcome = "Sending" };
            db.Add(attemptRow);
            // Persist the claim before touching SMTP. After a crash, never blindly resend an ambiguous acceptance.
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(40));
                await transport.SendAsync(row.Id, row.Recipient, row.Subject, row.Html, timeout.Token).ConfigureAwait(false);
                row.State = "Sent"; row.SentAt = clock.GetUtcNow(); attemptRow.Outcome = "Sent"; sent++;
            }
            catch (SmtpException error) when ((int)error.StatusCode is >= 400 and <= 599)
            {
                // Only a definite SMTP rejection is retried automatically; never log transport text or credentials.
                row.State = row.Attempts < options.MaxAttempts && (int)error.StatusCode < 500 ? "Retry" : "Failed";
                row.NextAttemptAt = clock.GetUtcNow().AddMinutes(Math.Min(360, Math.Pow(2, row.Attempts)));
                row.LastError = $"SMTP rechazó la entrega: código {(int)error.StatusCode}.";
                attemptRow.Outcome = row.State; attemptRow.Error = row.LastError;
            }
            catch (Exception error) when (error is SmtpException or OperationCanceledException or System.IO.IOException or InvalidOperationException)
            {
                row.State = "Uncertain"; row.LastError = "No se confirmó la aceptación SMTP. Verificar Message-ID antes de reenviar.";
                attemptRow.Outcome = row.State; attemptRow.Error = row.LastError;
            }
            // Cancellation after SMTP must not discard its confirmed outcome.
            using var persistTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await db.SaveChangesAsync(persistTimeout.Token).ConfigureAwait(false);
        }
        return sent;
    }
}

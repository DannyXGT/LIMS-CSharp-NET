using System.Text.Json;
using Lims.Application.Alerts;
using Lims.Contracts.Alerts;
using Lims.Domain.Alerts;
using Lims.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lims.Infrastructure.Alerts;

public sealed class AlertEngine(LimsDbContext db, AlertSourceReader sources, AlertAccess access, TimeProvider clock)
{
    public async Task<(int Evaluated, int Changed)> EvaluateAsync(CancellationToken ct) =>
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            await AlertData.LockConfigurationAsync(db, ct).ConfigureAwait(false);
            var settings = await AlertData.SettingsAsync(db, ct).ConfigureAwait(false);
            var now = clock.GetUtcNow(); var local = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById(settings.TimeZoneId));
            var today = DateOnly.FromDateTime(local.DateTime);
            var sourceRows = await sources.ReadAsync(settings, today, ct).ConfigureAwait(false);
            var active = await db.Set<MaterialAlert>().Where(a => a.ResolvedAt == null).ToArrayAsync(ct).ConfigureAwait(false);
            var index = active.ToDictionary(a => (a.SourceType, a.SourceId, a.Category));
            var retained = new HashSet<Guid>(); var changed = 0;
            foreach (var source in sourceRows)
            foreach (var condition in AlertRules.Evaluate(source, settings, today))
            {
                var key = (source.SourceType, source.Id, condition.Category);
                var created = !index.TryGetValue(key, out var alert);
                if (created)
                {
                    alert = new() { SourceId = source.Id, SourceType = source.SourceType, Category = condition.Category, DetectedAt = now };
                    db.Add(alert); index.Add(key, alert);
                }
                var transitioned = created || alert!.Condition != condition.Code || alert.Priority != (int)condition.Priority || alert.DepartmentId != source.DepartmentId || alert.Modules != (int)source.Modules;
                var escalated = !created && (int)condition.Priority > alert!.Priority;
                alert!.Modules = (int)source.Modules; alert.DepartmentId = source.DepartmentId; alert.Material = source.Name;
                alert.Code = source.Code; alert.Lot = source.Lot; alert.MaterialType = source.MaterialType;
                alert.SearchText = (source.Name + " " + source.Code + " " + source.Lot + " " + source.Search).ToUpperInvariant();
                alert.AvailabilityPercent = source.Percent; alert.Available = source.Available; alert.Unit = source.Unit; alert.Expiration = source.Expiration;
                alert.Title = condition.Title; alert.Description = condition.Description; alert.Recommendation = condition.Recommendation;
                alert.Condition = condition.Code; alert.Priority = (int)condition.Priority; alert.UpdatedAt = now;
                retained.Add(alert.Id);
                if (transitioned)
                {
                    if (!created) alert.Revision++;
                    if (escalated) { alert.AcknowledgedAt = null; alert.AcknowledgedBy = null; }
                    db.Add(new AlertHistory { AlertId = alert.Id, Action = created ? "Detectada" : "Condición actualizada", Condition = alert.Condition, At = now }); changed++;
                }
            }
            foreach (var alert in active.Where(a => !retained.Contains(a.Id)))
            {
                alert.ResolvedAt = now; alert.UpdatedAt = now; alert.Revision++;
                db.Add(new AlertHistory { AlertId = alert.Id, Action = "Resuelta por reevaluación", Condition = alert.Condition, At = now, Note = "La condición dejó de aplicar o el registro salió del ámbito operativo configurado." }); changed++;
            }
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            // Catch up newly authorized recipients without generating duplicates on refresh or repeated runs.
            var users = await db.Users.AsNoTracking().Where(u => u.IsActive).Select(u => new { u.Id, u.Email }).ToArrayAsync(ct).ConfigureAwait(false);
            var activeIds = index.Values.Where(a => a.ResolvedAt == null).Select(a => a.Id).ToArray();
            var notifications = await db.Set<UserNotification>().Where(n => activeIds.Contains(n.AlertId)).Select(n => new { n.AlertId, n.Revision, n.UserId }).ToArrayAsync(ct).ConfigureAwait(false);
            var known = notifications.Select(n => (n.AlertId, n.Revision, n.UserId)).ToHashSet();
            var dayKey = $"daily:{today:yyyyMMdd}:";
            var keys = (await db.Set<EmailDelivery>().Where(d => d.AlertId != null && activeIds.Contains(d.AlertId.Value) || d.DeduplicationKey.StartsWith(dayKey))
                .Select(d => d.DeduplicationKey).ToArrayAsync(ct).ConfigureAwait(false)).ToHashSet(StringComparer.Ordinal);
            foreach (var u in users)
            {
                var actor = await access.ActorAsync(u.Id, ct).ConfigureAwait(false);
                if (actor is null) continue;
                var visible = index.Values.Where(a => a.ResolvedAt == null && actor.Sees(a.DepartmentId, (AlertModule)a.Modules)).ToArray();
                foreach (var a in visible)
                    if (known.Add((a.Id, a.Revision, u.Id))) db.Add(new UserNotification { AlertId = a.Id, Revision = a.Revision, UserId = u.Id, Title = a.Title + " — " + a.Material, Message = a.Description, Priority = a.Priority, CreatedAt = now });
                foreach (var recipient in settings.Recipients.Where(r => r.UserId == u.Id))
                {
                    var scoped = visible.Where(a => a.DepartmentId == recipient.DepartmentId).ToArray();
                    if (settings.EmailEnabled && recipient.Immediate)
                        foreach (var a in scoped.Where(a => a.Priority == (int)AlertPriority.Critical))
                            Queue($"immediate:{a.Id:D}:{a.Revision}:{u.Id}", "Immediate", a.Title + " — " + a.Material, [a], a.Id, a.Revision);
                    if (settings.EmailEnabled && recipient.Daily && scoped.Length > 0 && local.TimeOfDay >= new TimeSpan(settings.DailyHour, settings.DailyMinute, 0))
                        Queue($"daily:{today:yyyyMMdd}:{u.Id}", "Daily", $"Resumen diario de alertas — {today:dd/MM/yyyy}", scoped, null, null);
                    void Queue(string key, string kind, string subject, MaterialAlert[] rows, Guid? alertId, int? revision)
                    {
                        if (!keys.Add(key)) return;
                        db.Add(new EmailDelivery { DeduplicationKey = key, UserId = u.Id, DepartmentId = recipient.DepartmentId, Recipient = u.Email,
                            Kind = kind, AlertId = alertId, AlertRevision = revision, Subject = subject, Html = AlertEmailTemplate.Render(subject, "Condiciones detectadas por el motor central de LIMS.", rows.Select(AlertData.Item)), CreatedAt = now, NextAttemptAt = now });
                    }
                }
            }
            var configuration = await db.Set<AlertConfiguration>().SingleOrDefaultAsync(ct).ConfigureAwait(false);
            if (configuration is null) { configuration = new() { Json = JsonSerializer.Serialize(settings), Version = Guid.Empty }; db.Add(configuration); }
            configuration.LastEvaluation = now;
            await db.SaveChangesAsync(ct).ConfigureAwait(false); await tx.CommitAsync(ct).ConfigureAwait(false);
            return (sourceRows.Count, changed);
        }).ConfigureAwait(false);
}

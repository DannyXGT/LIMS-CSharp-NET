using Lims.Contracts.Alerts;
using Lims.Contracts.Dashboard;
using Lims.Domain.Alerts;
using Lims.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lims.Infrastructure.Alerts;

public sealed class AlertDashboard(LimsDbContext db, AlertAccess access, TimeProvider clock)
{
    public async Task<DashboardAlertState?> GetAsync(int userId, CancellationToken ct)
    {
        var user = await access.ActorAsync(userId, ct).ConfigureAwait(false);
        if (user is null || !user.Has(Lims.Contracts.Permissions.PermissionCatalog.AlertsView)) return null;
        var settings = await AlertData.SettingsAsync(db, ct).ConfigureAwait(false);
        if (settings.Scopes.Count == 0) return null;
        var active = AlertAccess.Visible(db.Set<MaterialAlert>().AsNoTracking(), user).Where(a => a.ResolvedAt == null);
        var counts = await active.GroupBy(a => 1).Select(g => new { Active = g.Count(), Critical = g.Count(a => a.Priority == (int)AlertPriority.Critical),
            Pending = g.Count(a => a.AcknowledgedAt == null), Upcoming = g.Count(a => a.Category == "Expiration" && a.Condition != "Expired"), Expired = g.Count(a => a.Condition == "Expired") }).SingleOrDefaultAsync(ct).ConfigureAwait(false);
        var expiry = await active.Where(a => a.Category == "Expiration" && a.Condition != "Expired").OrderBy(a => a.Expiration).ThenBy(a => a.Id).Take(6).ToArrayAsync(ct).ConfigureAwait(false);
        var low = await active.Where(a => a.Category == "Availability").OrderBy(a => a.AvailabilityPercent).ThenBy(a => a.Id).Take(6).ToArrayAsync(ct).ConfigureAwait(false);
        var now = clock.GetUtcNow(); var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById(settings.TimeZoneId)).DateTime);
        DashboardMaterial Row(MaterialAlert a) => new(a.SourceId, a.SourceType == "ReferenceMaterial" ? "Standard" : ((AlertModule)a.Modules).ToString(), a.Material, a.Lot, a.Code, a.Expiration, a.Expiration.DayNumber - today.DayNumber, a.AvailabilityPercent, a.Id, a.Priority == (int)AlertPriority.Critical ? "Critical" : "Low");
        var summary = new AlertSummary(counts?.Active ?? 0, counts?.Critical ?? 0, counts?.Upcoming ?? 0, expiry.FirstOrDefault()?.Expiration, counts?.Expired ?? 0, counts?.Pending ?? 0,
            await AlertAccess.Visible(db.Set<UserNotification>(), user).CountAsync(n => n.ReadAt == null, ct).ConfigureAwait(false),
            await db.Set<AlertConfiguration>().Select(c => c.LastEvaluation).SingleOrDefaultAsync(ct).ConfigureAwait(false), now);
        return new(summary, settings.LowPercent, settings.CriticalPercent, settings.ExpirationDays.Max(), expiry.Select(Row).ToArray(), low.Select(Row).ToArray());
    }
}

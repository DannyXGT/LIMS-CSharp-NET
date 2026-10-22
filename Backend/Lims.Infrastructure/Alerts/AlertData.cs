using System.Text.Json;
using Lims.Contracts.Alerts;
using Lims.Domain.Alerts;
using Lims.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lims.Infrastructure.Alerts;

public static class AlertData
{
    public const long ConfigurationLock = 5839024101;
    public const long JobLock = 5839024102;
    public static async Task<AlertSettings> SettingsAsync(LimsDbContext db, CancellationToken ct)
    {
        var row = await db.Set<AlertConfiguration>().AsNoTracking().SingleOrDefaultAsync(ct).ConfigureAwait(false);
        return row is null ? AlertSettings.Default : JsonSerializer.Deserialize<AlertSettings>(row.Json)! with { Version = row.Version };
    }
    public static AlertItem Item(MaterialAlert a) => new(a.Id, a.SourceId, a.SourceType, (AlertModule)a.Modules, a.DepartmentId,
        a.Material, a.Code, a.Lot, a.MaterialType, a.Category, a.Condition, (AlertPriority)a.Priority, a.Title,
        a.Description, a.Recommendation, a.AvailabilityPercent, a.Available, a.Unit, a.Expiration, a.DetectedAt,
        a.UpdatedAt, a.ResolvedAt, a.AcknowledgedAt, a.Revision);
    public static DeliveryItem Delivery(EmailDelivery d) => new(d.Id, d.UserId, d.Recipient, d.Kind, d.State, d.Attempts, d.CreatedAt, d.SentAt, d.NextAttemptAt, d.LastError);
    public static NotificationPreferences Preference(NotificationPreference? p) => p is null ? new() : new(p.WindowsEnabled, p.EmailEnabled, p.OnlyCriticalWindows);
    public static async Task LockConfigurationAsync(LimsDbContext db, CancellationToken ct)
    {
        if (db.Database.IsNpgsql())
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({ConfigurationLock})", ct).ConfigureAwait(false);
    }
}

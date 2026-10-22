using System.Text.Json;
using Lims.Application.Alerts;
using Lims.Application.Common;
using Lims.Contracts.Alerts;
using Lims.Contracts.Errors;
using Lims.Contracts.Permissions;
using Lims.Domain.Alerts;
using Lims.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lims.Infrastructure.Alerts;

public sealed class AlertService(LimsDbContext db, AlertAccess access, AlertMailOptions mail, TimeProvider clock) : IAlertService
{
    private static OperationResult<T> Denied<T>() => OperationResult.Failure<T>(new(ErrorCodes.Forbidden, "No tiene autorización para esta operación o registro."));
    private static OperationResult<T> Invalid<T>(string message) => OperationResult.Failure<T>(new(ErrorCodes.ValidationError, message));
    private static OperationResult<T> Conflict<T>() => OperationResult.Failure<T>(new(ErrorCodes.Conflict, "La configuración cambió. Recargue antes de guardar."));

    public async Task<OperationResult<AlertPage>> ListAsync(int actor, AlertFilter filter, CancellationToken ct)
    {
        var user = await access.ActorAsync(actor, ct).ConfigureAwait(false);
        if (user is null || !user.Has(PermissionCatalog.AlertsView)) return Denied<AlertPage>();
        if (filter.Page is < 1 or > 100000 || filter.PageSize is < 1 or > 100 || filter.Search?.Length > 200 ||
            filter.Priority is { } priority && !Enum.IsDefined(priority) || filter.From > filter.Until)
            return Invalid<AlertPage>("Revise los filtros y la paginación.");
        var visible = AlertAccess.Visible(db.Set<MaterialAlert>().AsNoTracking(), user);
        var active = visible.Where(a => a.ResolvedAt == null);
        var next = await active.Where(a => a.Category == "Expiration" && a.Condition != "Expired")
            .OrderBy(a => a.Expiration).ThenBy(a => a.Id).Select(a => new { a.Material, a.Id, a.Expiration }).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        var settings = await AlertData.SettingsAsync(db, ct).ConfigureAwait(false);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById(settings.TimeZoneId)).DateTime);
        var metrics = new AlertMetrics(await active.CountAsync(a => a.Condition == "Depleted", ct).ConfigureAwait(false),
            await active.CountAsync(a => a.Condition == "Critical", ct).ConfigureAwait(false),
            await active.CountAsync(a => a.Condition == "Expired" && a.AcknowledgedAt == null, ct).ConfigureAwait(false),
            next?.Material, next?.Id, next is null ? null : next.Expiration.DayNumber - today.DayNumber);
        var summary = new AlertSummary(await active.CountAsync(ct).ConfigureAwait(false),
            await active.CountAsync(a => a.Priority == (int)AlertPriority.Critical, ct).ConfigureAwait(false),
            await active.CountAsync(a => a.Category == "Expiration" && a.Condition != "Expired", ct).ConfigureAwait(false),
            await active.Where(a => a.Category == "Expiration" && a.Condition != "Expired").Select(a => (DateOnly?)a.Expiration).MinAsync(ct).ConfigureAwait(false),
            await active.CountAsync(a => a.Condition == "Expired", ct).ConfigureAwait(false),
            await active.CountAsync(a => a.AcknowledgedAt == null, ct).ConfigureAwait(false),
            await AlertAccess.Visible(db.Set<UserNotification>(), user).CountAsync(n => n.ReadAt == null, ct).ConfigureAwait(false),
            await db.Set<AlertConfiguration>().Select(c => c.LastEvaluation).SingleOrDefaultAsync(ct).ConfigureAwait(false), clock.GetUtcNow(), metrics);
        var query = visible;
        if (!string.IsNullOrWhiteSpace(filter.Search)) { var term = filter.Search.Trim().ToUpperInvariant(); query = query.Where(a => a.SearchText.Contains(term)); }
        query = filter.Category switch { "Upcoming" => query.Where(a => a.Category == "Expiration" && a.Condition != "Expired"), "Expired" => query.Where(a => a.Condition == "Expired"), { Length: > 0 } => query.Where(a => a.Category == filter.Category), _ => query };
        if (filter.Priority is { } p) query = query.Where(a => a.Priority == (int)p);
        if (filter.Module is { } m && m != AlertModule.None) query = query.Where(a => (a.Modules & (int)m) != 0);
        query = filter.State switch { "Resolved" => query.Where(a => a.ResolvedAt != null), "Pending" => query.Where(a => a.ResolvedAt == null && a.AcknowledgedAt == null), "All" => query, "Active" or null or "" => query.Where(a => a.ResolvedAt == null), _ => query.Where(a => false) };
        if (filter.From is { } from) { var at = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(-6)).ToUniversalTime(); query = query.Where(a => a.DetectedAt >= at); }
        if (filter.Until is { } until) { var at = new DateTimeOffset(until.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(-6)).ToUniversalTime(); query = query.Where(a => a.DetectedAt < at); }
        var total = await query.CountAsync(ct).ConfigureAwait(false);
        var ordered = filter.Sort switch { "Newest" => query.OrderByDescending(a => a.DetectedAt).ThenBy(a => a.Id), "Expiration" => query.OrderBy(a => a.Expiration).ThenBy(a => a.Id), _ => query.OrderByDescending(a => a.Priority).ThenByDescending(a => a.UpdatedAt).ThenBy(a => a.Id) };
        var rows = await ordered.Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize).ToArrayAsync(ct).ConfigureAwait(false);
        var ids = rows.Select(a => a.Id).ToArray();
        var notifications = await db.Set<UserNotification>().AsNoTracking().Where(n => n.UserId == actor && ids.Contains(n.AlertId))
            .OrderByDescending(n => n.Revision).ThenByDescending(n => n.CreatedAt).Select(n => new { n.Id, n.AlertId, n.ReadAt }).ToArrayAsync(ct).ConfigureAwait(false);
        var personal = notifications.DistinctBy(n => n.AlertId).ToDictionary(n => n.AlertId);
        return OperationResult.Success(new AlertPage(rows.Select(a => personal.TryGetValue(a.Id, out var n) ? AlertData.Item(a) with { NotificationId = n.Id, ReadAt = n.ReadAt } : AlertData.Item(a)).ToArray(), filter.Page, filter.PageSize, total, summary));
    }
    public async Task<OperationResult<AlertDetail>> DetailAsync(int actor, Guid id, CancellationToken ct)
    {
        var user = await access.ActorAsync(actor, ct).ConfigureAwait(false);
        if (user is null) return Denied<AlertDetail>();
        var row = await AlertAccess.Visible(db.Set<MaterialAlert>().AsNoTracking(), user).SingleOrDefaultAsync(a => a.Id == id, ct).ConfigureAwait(false);
        if (row is null) return Denied<AlertDetail>();
        var changes = await db.Set<AlertHistory>().AsNoTracking().Where(h => h.AlertId == id).OrderByDescending(h => h.At).ThenBy(h => h.Id).ToArrayAsync(ct).ConfigureAwait(false);
        var notification = await db.Set<UserNotification>().AsNoTracking().Where(n => n.UserId == actor && n.AlertId == id)
            .OrderByDescending(n => n.Revision).ThenByDescending(n => n.CreatedAt).Select(n => new { n.Id, n.ReadAt }).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        var item = notification is null ? AlertData.Item(row) : AlertData.Item(row) with { NotificationId = notification.Id, ReadAt = notification.ReadAt };
        return OperationResult.Success(new AlertDetail(item, changes.Select(h => new AlertChange(h.Id, h.Action, h.Condition, h.At, h.ActorUserId, h.Note)).ToArray()));
    }
    public async Task<OperationResult<AlertDetail>> AcknowledgeAsync(int actor, Guid id, string? note, CancellationToken ct)
    {
        var user = await access.ActorAsync(actor, ct).ConfigureAwait(false);
        if (user is null || !user.Has(PermissionCatalog.AlertsManage)) return Denied<AlertDetail>();
        if (note?.Length > 1000) return Invalid<AlertDetail>("La nota admite hasta 1000 caracteres.");
        var recorded = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear(); await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            await AlertData.LockConfigurationAsync(db, ct).ConfigureAwait(false);
            var a = await AlertAccess.Visible(db.Set<MaterialAlert>(), user).SingleOrDefaultAsync(a => a.Id == id, ct).ConfigureAwait(false);
            if (a is null) return false;
            if (a.ResolvedAt == null && a.AcknowledgedAt == null)
            {
                a.AcknowledgedAt = clock.GetUtcNow(); a.AcknowledgedBy = actor;
                db.Add(new AlertHistory { AlertId = id, Action = "Atendida", Condition = a.Condition, At = a.AcknowledgedAt.Value, ActorUserId = actor, Note = note });
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
            await tx.CommitAsync(ct).ConfigureAwait(false); return true;
        }).ConfigureAwait(false);
        if (!recorded) return Denied<AlertDetail>();
        return await DetailAsync(actor, id, ct).ConfigureAwait(false);
    }
    public async Task<OperationResult<NotificationPage>> NotificationsAsync(int actor, CancellationToken ct)
    {
        var user = await access.ActorAsync(actor, ct).ConfigureAwait(false);
        if (user is null || !user.Has(PermissionCatalog.AlertsView)) return Denied<NotificationPage>();
        var query = AlertAccess.Visible(db.Set<UserNotification>().AsNoTracking(), user);
        var rows = await query.OrderBy(n => n.ReadAt != null).ThenByDescending(n => n.CreatedAt).ThenBy(n => n.Id).Take(60).ToArrayAsync(ct).ConfigureAwait(false);
        var pref = await db.Set<NotificationPreference>().AsNoTracking().SingleOrDefaultAsync(p => p.UserId == actor, ct).ConfigureAwait(false);
        var settings = await AlertData.SettingsAsync(db, ct).ConfigureAwait(false);
        return OperationResult.Success(new NotificationPage(rows.Select(n => new NotificationItem(n.Id, n.AlertId, n.Title, n.Message, (AlertPriority)n.Priority, n.CreatedAt, n.ReadAt, n.WindowsClaimedAt != null)).ToArray(),
            await query.CountAsync(n => n.ReadAt == null, ct).ConfigureAwait(false), AlertData.Preference(pref), settings.WindowsEnabled, clock.GetUtcNow()));
    }
    public async Task<OperationResult<bool>> ReadAsync(int actor, Guid? id, CancellationToken ct)
    {
        var user = await access.ActorAsync(actor, ct).ConfigureAwait(false);
        if (user is null || !user.Has(PermissionCatalog.AlertsView)) return Denied<bool>();
        var query = AlertAccess.Visible(db.Set<UserNotification>(), user).Where(n => n.ReadAt == null);
        if (id is { } value) query = query.Where(n => n.Id == value);
        var now = clock.GetUtcNow();
        var changed = await query.ExecuteUpdateAsync(b => b.SetProperty(n => n.ReadAt, now), ct).ConfigureAwait(false);
        return OperationResult.Success(id is null || changed > 0 || await AlertAccess.Visible(db.Set<UserNotification>(), user).AnyAsync(n => n.Id == id, ct).ConfigureAwait(false));
    }
    public async Task<OperationResult<bool>> ClaimWindowsAsync(int actor, Guid id, CancellationToken ct)
    {
        var user = await access.ActorAsync(actor, ct).ConfigureAwait(false);
        if (user is null || !user.Has(PermissionCatalog.AlertsView)) return Denied<bool>();
        var settings = await AlertData.SettingsAsync(db, ct).ConfigureAwait(false);
        var pref = await db.Set<NotificationPreference>().AsNoTracking().SingleOrDefaultAsync(p => p.UserId == actor, ct).ConfigureAwait(false);
        if (!settings.WindowsEnabled || pref?.WindowsEnabled != true) return OperationResult.Success(false);
        var cutoff = clock.GetUtcNow().AddHours(-1); var now = clock.GetUtcNow();
        var query = AlertAccess.Visible(db.Set<UserNotification>(), user).Where(n => n.Id == id && n.ReadAt == null && n.WindowsClaimedAt == null && n.CreatedAt >= cutoff && n.Alert.ResolvedAt == null && n.Revision == n.Alert.Revision);
        if (pref.OnlyCriticalWindows) query = query.Where(n => n.Priority == (int)AlertPriority.Critical);
        return OperationResult.Success(await query.ExecuteUpdateAsync(b => b.SetProperty(n => n.WindowsClaimedAt, now), ct).ConfigureAwait(false) == 1);
    }
    public async Task<OperationResult<NotificationPreferences>> PreferencesAsync(int actor, NotificationPreferences preferences, CancellationToken ct)
    {
        var user = await access.ActorAsync(actor, ct).ConfigureAwait(false);
        if (user is null || !user.Has(PermissionCatalog.AlertsView)) return Denied<NotificationPreferences>();
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear(); await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            await AlertData.LockConfigurationAsync(db, ct).ConfigureAwait(false);
            var row = await db.Set<NotificationPreference>().SingleOrDefaultAsync(p => p.UserId == actor, ct).ConfigureAwait(false);
            if (row is null) { row = new() { UserId = actor }; db.Add(row); }
            row.WindowsEnabled = preferences.WindowsEnabled; row.EmailEnabled = preferences.EmailEnabled; row.OnlyCriticalWindows = preferences.OnlyCriticalWindows;
            await db.SaveChangesAsync(ct).ConfigureAwait(false); await tx.CommitAsync(ct).ConfigureAwait(false);
            return OperationResult.Success(preferences);
        }).ConfigureAwait(false);
    }
    public async Task<OperationResult<AlertAdministration>> AdministrationAsync(int actor, CancellationToken ct)
    {
        var user = await access.ActorAsync(actor, ct).ConfigureAwait(false);
        if (user is null || !user.Administrator || !user.Has(PermissionCatalog.NotificationsConfigure)) return Denied<AlertAdministration>();
        var departments = await db.Departments.AsNoTracking().OrderBy(d => d.Name).Select(d => new AlertDepartment(d.Id, d.Name)).ToArrayAsync(ct).ConfigureAwait(false);
        var users = await db.Users.AsNoTracking().Where(u => u.IsActive).OrderBy(u => u.Name).Select(u => new AlertUser(u.Id, u.Name, u.Email, u.DepartmentId, u.Role != null && u.Role.Name == "Administrador")).ToArrayAsync(ct).ConfigureAwait(false);
        return OperationResult.Success(new AlertAdministration(await AlertData.SettingsAsync(db, ct).ConfigureAwait(false), departments, users, mail.Status()));
    }
    public async Task<OperationResult<AlertAdministration>> ConfigureAsync(int actor, AlertSettings settings, CancellationToken ct)
    {
        var user = await access.ActorAsync(actor, ct).ConfigureAwait(false);
        if (user is null || !user.Administrator || !user.Has(PermissionCatalog.NotificationsConfigure)) return Denied<AlertAdministration>();
        if (AlertRules.Validate(settings) is { } error) return Invalid<AlertAdministration>(error);
        var departments = await db.Departments.Select(d => d.Id).ToArrayAsync(ct).ConfigureAwait(false);
        if (settings.Scopes.Any(s => !departments.Contains(s.DepartmentId))) return Invalid<AlertAdministration>("Seleccione departamentos existentes.");
        foreach (var recipient in settings.Recipients)
        {
            var target = await access.ActorAsync(recipient.UserId, ct).ConfigureAwait(false);
            if (target is null || !target.Has(PermissionCatalog.AlertsView) || !settings.Scopes.Any(s => s.DepartmentId == recipient.DepartmentId && target.Sees(s.DepartmentId, s.Module)))
                return Invalid<AlertAdministration>("Un destinatario no está activo o no tiene permisos en el departamento seleccionado.");
        }
        // A physical commercial bottle can appear in both workspaces, but cannot acquire two owners.
        var commercialDepartments = settings.Scopes.Where(s => s.Module is AlertModule.Stock or AlertModule.Intermedia).Select(s => s.DepartmentId).Distinct().ToArray();
        if (commercialDepartments.Length > 1 && await db.ReferenceMaterials.AnyAsync(m => m.CommercialRoles.Any(r => r.Kind == "Stock") && m.CommercialRoles.Any(r => r.Kind == "Intermedia"), ct).ConfigureAwait(false))
            return Invalid<AlertAdministration>("Stock e Intermedia comparten frascos físicos; seleccione el mismo departamento para ambos ámbitos.");
        var saved = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear(); await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            await AlertData.LockConfigurationAsync(db, ct).ConfigureAwait(false);
            var row = await db.Set<AlertConfiguration>().SingleOrDefaultAsync(ct).ConfigureAwait(false);
            if ((row?.Version ?? Guid.Empty) != settings.Version) return false;
            if (row is null) { row = new(); db.Add(row); }
            row.Version = Guid.NewGuid(); row.Json = JsonSerializer.Serialize(settings with { Version = row.Version }); row.ActorUserId = actor; row.ChangedAt = clock.GetUtcNow();
            db.Add(new AlertConfigurationAudit { ActorUserId = actor, At = clock.GetUtcNow(), Action = "Configuración", Json = row.Json });
            await db.SaveChangesAsync(ct).ConfigureAwait(false); await tx.CommitAsync(ct).ConfigureAwait(false); return true;
        }).ConfigureAwait(false);
        return saved ? await AdministrationAsync(actor, ct).ConfigureAwait(false) : Conflict<AlertAdministration>();
    }
    public async Task<OperationResult<DeliveryPage>> DeliveriesAsync(int actor, int page, CancellationToken ct)
    {
        var user = await access.ActorAsync(actor, ct).ConfigureAwait(false);
        if (user is null || !user.Has(PermissionCatalog.DeliveryHistory)) return Denied<DeliveryPage>();
        page = Math.Clamp(page, 1, 100000);
        var query = db.Set<EmailDelivery>().AsNoTracking();
        var visibleIds = AlertAccess.Visible(db.Set<MaterialAlert>(), user).Select(a => a.Id);
        if (!user.Administrator) query = query.Where(d => d.DepartmentId == user.DepartmentId && (d.AlertId == null ? d.UserId == actor : visibleIds.Contains(d.AlertId.Value)));
        var rows = await query.OrderByDescending(d => d.CreatedAt).ThenBy(d => d.Id).Skip((page - 1) * 30).Take(30).ToArrayAsync(ct).ConfigureAwait(false);
        return OperationResult.Success(new DeliveryPage(rows.Select(AlertData.Delivery).ToArray(), page, await query.CountAsync(ct).ConfigureAwait(false),
            !mail.Status().Configured ? "NotConfigured" : !mail.Enabled ? "Disabled" : "Configured"));
    }
    public async Task<OperationResult<DeliveryItem>> TestEmailAsync(int actor, Guid requestId, CancellationToken ct)
    {
        var user = await access.ActorAsync(actor, ct).ConfigureAwait(false);
        if (user is null || !user.Administrator || !user.Has(PermissionCatalog.NotificationsConfigure)) return Denied<DeliveryItem>();
        if (requestId == Guid.Empty) return Invalid<DeliveryItem>("La solicitud debe tener un identificador válido.");
        if (!mail.Status().Configured || !mail.Enabled) return Invalid<DeliveryItem>("SMTP pendiente de configuración segura en el servidor.");
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear(); await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            await AlertData.LockConfigurationAsync(db, ct).ConfigureAwait(false);
            var key = $"test:{actor}:{requestId:D}";
            var existing = await db.Set<EmailDelivery>().SingleOrDefaultAsync(d => d.DeduplicationKey == key, ct).ConfigureAwait(false);
            if (existing is not null) return OperationResult.Success(AlertData.Delivery(existing));
            var cutoff = clock.GetUtcNow().AddMinutes(-5);
            if (await db.Set<EmailDelivery>().AnyAsync(d => d.Kind == "Test" && d.UserId == actor && d.CreatedAt >= cutoff, ct).ConfigureAwait(false))
                return Invalid<DeliveryItem>("Espere cinco minutos entre pruebas de correo.");
            var email = await db.Users.Where(u => u.Id == actor).Select(u => u.Email).SingleAsync(ct).ConfigureAwait(false);
            var delivery = new EmailDelivery { DeduplicationKey = key, UserId = actor, DepartmentId = user.DepartmentId ?? 0, Recipient = email, Kind = "Test", Subject = "LIMS — Prueba autorizada de notificaciones", Html = AlertEmailTemplate.Render("Prueba de correo", "Prueba solicitada desde la configuración de LIMS. No corresponde a una incidencia de inventario.", []), CreatedAt = clock.GetUtcNow(), NextAttemptAt = clock.GetUtcNow() };
            db.Add(delivery); db.Add(new AlertConfigurationAudit { ActorUserId = actor, At = clock.GetUtcNow(), Action = "Prueba SMTP encolada", Json = JsonSerializer.Serialize(new { delivery.Id, delivery.Recipient }) });
            await db.SaveChangesAsync(ct).ConfigureAwait(false); await tx.CommitAsync(ct).ConfigureAwait(false);
            return OperationResult.Success(AlertData.Delivery(delivery));
        }).ConfigureAwait(false);
    }
}

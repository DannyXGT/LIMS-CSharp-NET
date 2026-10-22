using Lims.Application.Alerts;
using Lims.Application.Permissions;
using Lims.Contracts.Alerts;
using Lims.Contracts.Permissions;
using Lims.Domain.Alerts;
using Lims.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lims.Infrastructure.Alerts;

public sealed record AlertActor(int Id, int? DepartmentId, bool Administrator, IReadOnlyList<string> Permissions)
{
    public bool Has(string permission) => Administrator || Permissions.Contains(permission, StringComparer.Ordinal);
    public bool Sees(int department, AlertModule modules) => Has(PermissionCatalog.AlertsView) &&
        (Administrator || DepartmentId == department && AlertRules.CanSee(modules, Permissions));
}

public sealed class AlertAccess(LimsDbContext db, IPermissionRepository permissions)
{
    public async Task<AlertActor?> ActorAsync(int userId, CancellationToken ct)
    {
        var u = await db.Users.AsNoTracking().Where(u => u.Id == userId && u.IsActive)
            .Select(u => new { u.Id, u.DepartmentId }).SingleOrDefaultAsync(ct).ConfigureAwait(false);
        var effective = u is null ? null : await permissions.EffectiveAsync(userId, ct).ConfigureAwait(false);
        return u is null || effective is null ? null : new(u.Id, u.DepartmentId, effective.Role == "Administrador", effective.Permissions);
    }

    public static IQueryable<MaterialAlert> Visible(IQueryable<MaterialAlert> query, AlertActor actor)
    {
        if (!actor.Has(PermissionCatalog.AlertsView)) return query.Where(_ => false);
        if (actor.Administrator) return query;
        var mask = Enum.GetValues<AlertModule>().Where(m => actor.Permissions.Contains(AlertRules.ViewPermission(m), StringComparer.Ordinal)).Aggregate(0, (sum, m) => sum | (int)m);
        return query.Where(a => a.DepartmentId == actor.DepartmentId && (a.Modules & mask) != 0);
    }
    public static IQueryable<UserNotification> Visible(IQueryable<UserNotification> query, AlertActor actor)
    {
        if (!actor.Has(PermissionCatalog.AlertsView)) return query.Where(_ => false);
        if (actor.Administrator) return query.Where(n => n.UserId == actor.Id);
        var mask = Enum.GetValues<AlertModule>().Where(m => actor.Permissions.Contains(AlertRules.ViewPermission(m), StringComparer.Ordinal)).Aggregate(0, (sum, m) => sum | (int)m);
        return query.Where(n => n.UserId == actor.Id && n.Alert.DepartmentId == actor.DepartmentId && (n.Alert.Modules & mask) != 0);
    }
}

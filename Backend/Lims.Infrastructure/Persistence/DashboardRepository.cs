using Lims.Application.Dashboard;
using Lims.Application.Permissions;
using Lims.Contracts.Dashboard;
using Lims.Contracts.Permissions;
using Lims.Domain.ReferenceMaterials;
using Lims.Domain.ReferencePreparations;
using Microsoft.EntityFrameworkCore;

namespace Lims.Infrastructure.Persistence;
public sealed class DashboardRepository(LimsDbContext db, IPermissionRepository permissions, DashboardOptions options, TimeProvider clock) : IDashboardRepository
{
    public async Task<DashboardResponse?> GetAsync(int userId, CancellationToken ct)
    {
        var identity = await db.Users.AsNoTracking().Where(u => u.Id == userId && u.IsActive)
            .Select(u => new { u.Name, Department = u.Department == null ? null : u.Department.Name }).SingleOrDefaultAsync(ct);
        var effective = await permissions.EffectiveAsync(userId, ct);
        if (identity is null || effective is null) return null;
        var access = DashboardSelection.Resolve(effective.Role, identity.Department, effective.Permissions, options);
        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(now.ToOffset(TimeSpan.FromHours(-6)).DateTime);
        var window = options.ExpirationWindowDays is > 0 and <= 3660 ? options.ExpirationWindowDays : null;
        var until = today.AddDays(window ?? 0);
        DashboardStandards? standards = null;
        DashboardPreparations? stock = null, intermediate = null;
        var expiry = new List<DashboardMaterial>(); var low = new List<DashboardMaterial>(); var activity = new List<DashboardActivity>();
        // Sequential queries: one scoped DbContext, no concurrent readers, no desktop inventory download.
        if (access.Standards)
        {
            var all = db.ReferenceMaterials.AsNoTracking();
            var usable = all.Where(m => m.Status == ReferenceMaterialStatus.Active && m.AvailableQuantity > 0 && m.ExpirationDate >= today);
            var counts = await all.GroupBy(m => m.Status == ReferenceMaterialStatus.Active ?
                    (m.AvailableQuantity <= 0 ? ReferenceMaterialStatus.Depleted : m.ExpirationDate < today ? ReferenceMaterialStatus.Expired : ReferenceMaterialStatus.Active) : m.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() }).ToArrayAsync(ct);
            var states = counts.OrderBy(c => c.Status).Select(c => new DashboardState(c.Status.ToString(), c.Count)).ToArray();
            standards = new(counts.Sum(c => c.Count), counts.Where(c => c.Status == ReferenceMaterialStatus.Active).Sum(c => c.Count),
                await usable.CountAsync(m => m.PresentationQuantity > 0 && m.PackageCount > 0 && m.AvailableQuantity < m.PresentationQuantity * m.PackageCount * .25m, ct),
                await usable.CountAsync(m => m.PresentationQuantity > 0 && m.PackageCount > 0 && m.AvailableQuantity <= m.PresentationQuantity * m.PackageCount * .60m, ct),
                window is null ? null : await usable.CountAsync(m => m.ExpirationDate <= until, ct),
                counts.Where(c => c.Status == ReferenceMaterialStatus.Expired).Sum(c => c.Count), states);
            if (access.Standards)
            {
                var rows = await all.Where(m => m.Status != ReferenceMaterialStatus.Archived && m.Status != ReferenceMaterialStatus.Replaced && m.Status != ReferenceMaterialStatus.Retired && m.ExpirationDate >= today && (window == null || m.ExpirationDate <= until)).OrderBy(m => m.ExpirationDate).ThenBy(m => m.Id).Take(6)
                    .Select(m => new { m.Id, m.Name, m.Lot, Method = m.Method.Name, m.ExpirationDate,
                        Percent = m.PresentationQuantity > 0 && m.PackageCount > 0 && m.AvailableQuantity <= m.PresentationQuantity * m.PackageCount ? (decimal?)(m.AvailableQuantity / (m.PresentationQuantity * m.PackageCount) * 100) : null }).ToArrayAsync(ct);
                expiry.AddRange(rows.Select(m => new DashboardMaterial(m.Id, "Standard", m.Name, m.Lot, m.Method, m.ExpirationDate, m.ExpirationDate.DayNumber - today.DayNumber, m.Percent)));
            }
            var lowRows = await usable.Where(m => m.PresentationQuantity > 0 && m.PackageCount > 0 && m.AvailableQuantity <= m.PresentationQuantity * m.PackageCount * .60m)
                .OrderBy(m => m.AvailableQuantity / (m.PresentationQuantity * m.PackageCount)).ThenBy(m => m.Id).Take(6)
                .Select(m => new { m.Id, m.Name, m.Lot, Method = m.Method.Name, m.ExpirationDate, Percent = m.AvailableQuantity / (m.PresentationQuantity * m.PackageCount) * 100 }).ToArrayAsync(ct);
            low.AddRange(lowRows.Select(m => new DashboardMaterial(m.Id, "Standard", m.Name, m.Lot, m.Method, m.ExpirationDate, m.ExpirationDate.DayNumber - today.DayNumber, m.Percent)));
            // Creation and archive timestamps are persisted facts; UpdatedAt is deliberately not an event log.
            activity.AddRange(await all.OrderByDescending(m => m.CreatedAt).ThenBy(m => m.Id).Take(6)
                .Select(m => new DashboardActivity("Estándar registrado", m.Name, db.Users.Where(u => u.Id == m.CreatedByUserId).Select(u => u.Name).FirstOrDefault() ?? "Usuario no disponible", m.CreatedAt, "Standard", m.Id)).ToArrayAsync(ct));
            activity.AddRange(await all.Where(m => m.ArchivedAt != null).OrderByDescending(m => m.ArchivedAt).ThenBy(m => m.Id).Take(6)
                .Select(m => new DashboardActivity(m.Status == ReferenceMaterialStatus.Replaced ? "Estándar reemplazado" : "Estándar archivado", m.Name, db.Users.Where(u => u.Id == m.ArchivedByUserId).Select(u => u.Name).FirstOrDefault() ?? "Usuario no disponible", m.ArchivedAt!.Value, "Standard", m.Id)).ToArrayAsync(ct));
        }
        foreach (var kind in new[] { "Stock", "Intermedia" })
        {
            if (kind == "Stock" ? !access.Stock : !access.Intermediate) continue;
            var all = db.ReferencePreparations.AsNoTracking().Where(p => p.Kind == kind);
            // Same operational conditions as IntermediateRepository.OptionsAsync.
            var usable = all.Where(p => p.Status == "Active" && p.AvailableVolume > 0 && p.ExpirationDate >= today && p.PreparationDate <= today &&
                p.MethodId != null && db.ReferenceMethods.Any(m => m.Id == p.MethodId && m.IsActive) && (p.Kind == "Intermedia" || p.SourceMaterial != null && p.SourceMaterial.Status == ReferenceMaterialStatus.Active));
            var validBalance = usable.Where(p => p.FinalVolume > 0 && (p.FinalVolumeUnit == "mL" || p.FinalVolumeUnit == "L") && p.AvailableVolume <= p.FinalVolume);
            var summary = new DashboardPreparations(await usable.CountAsync(ct), await validBalance.CountAsync(p => p.AvailableVolume < p.FinalVolume * .25m, ct),
                await validBalance.CountAsync(p => p.AvailableVolume <= p.FinalVolume * .60m, ct), window is null ? null : await usable.CountAsync(p => p.ExpirationDate <= until, ct),
                await all.CountAsync(p => p.Status == "Active" && p.ExpirationDate < today, ct));
            if (kind == "Stock") stock = summary; else intermediate = summary;
            if (kind is "Stock" or "Intermedia")
            {
                var rows = await all.Where(p => p.Status == "Active" && p.ExpirationDate >= today && (window == null || p.ExpirationDate <= until)).OrderBy(p => p.ExpirationDate).ThenBy(p => p.Id).Take(6)
                    .Select(p => new { p.Id, p.Name, Lot = p.Kind == "Stock" ? p.SourceLot : p.Code, Method = p.SourceMethod, p.ExpirationDate,
                        Percent = p.FinalVolume > 0 && (p.FinalVolumeUnit == "mL" || p.FinalVolumeUnit == "L") && p.AvailableVolume <= p.FinalVolume ? (decimal?)(p.AvailableVolume / p.FinalVolume * 100) : null }).ToArrayAsync(ct);
                expiry.AddRange(rows.Select(p => new DashboardMaterial(p.Id, kind, p.Name, p.Lot, p.Method, p.ExpirationDate, p.ExpirationDate.DayNumber - today.DayNumber, p.Percent)));
            }
            var lowRows = await validBalance.Where(p => p.AvailableVolume <= p.FinalVolume * .60m).OrderBy(p => p.AvailableVolume / p.FinalVolume).ThenBy(p => p.Id).Take(6)
                .Select(p => new { p.Id, p.Name, Lot = p.Kind == "Stock" ? p.SourceLot : p.Code, Method = p.SourceMethod, p.ExpirationDate, Percent = p.AvailableVolume / p.FinalVolume * 100 }).ToArrayAsync(ct);
            low.AddRange(lowRows.Select(p => new DashboardMaterial(p.Id, kind, p.Name, p.Lot, p.Method, p.ExpirationDate, p.ExpirationDate.DayNumber - today.DayNumber, p.Percent)));
            activity.AddRange(await all.OrderByDescending(p => p.CreatedAt).ThenBy(p => p.Id).Take(6)
                .Select(p => new DashboardActivity("Preparación " + kind + " registrada", p.Name, db.Users.Where(u => u.Id == p.PreparedByUserId).Select(u => u.Name).FirstOrDefault() ?? "Usuario no disponible", p.CreatedAt, kind, p.Id)).ToArrayAsync(ct));
        }
        return new(access.View, identity.Name, effective.Role, identity.Department, now, window, effective.Permissions, standards, stock, intermediate,
            expiry.Where(p => !access.Standards || p.Kind == "Standard").OrderBy(p => p.ExpirationDate).ThenBy(p => p.Id).Take(6).ToArray(), low.OrderBy(p => p.AvailabilityPercent).ThenBy(p => p.Id).Take(6).ToArray(),
            activity.OrderByDescending(a => a.At).ThenBy(a => a.RecordId).Take(6).ToArray());
    }
}

using Lims.Application.Alerts;
using Lims.Contracts.Alerts;
using Lims.Contracts.ReferencePreparations;
using Lims.Domain.ReferenceMaterials;
using Lims.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lims.Infrastructure.Alerts;

public sealed class AlertSourceReader(LimsDbContext db)
{
    public async Task<IReadOnlyList<AlertSource>> ReadAsync(AlertSettings settings, DateOnly today, CancellationToken ct)
    {
        var result = new List<AlertSource>();
        var scopes = settings.Scopes.ToDictionary(s => s.Module, s => s.DepartmentId);
        // The catalog has no department FK. Ownership must be explicitly configured, never inferred from the creator.
        var materials = await db.ReferenceMaterials.AsNoTracking().Include(m => m.Unit)
            .Include(m => m.CommercialRoles).Include(m => m.CertifiedComponents).ToArrayAsync(ct).ConfigureAwait(false);
        foreach (var m in materials)
        {
            var module = m.Presentation == "Solid" ? AlertModule.Standards : m.CommercialRoles.Aggregate(AlertModule.None,
                (mask, role) => mask | (role.Kind == "Stock" ? AlertModule.Stock : role.Kind == "Intermedia" ? AlertModule.Intermedia : AlertModule.None));
            if (module == AlertModule.None) module = AlertModule.Standards;
            var mapped = settings.Scopes.Where(s => module.HasFlag(s.Module)).ToArray();
            if (mapped.Length == 0 || mapped.Select(s => s.DepartmentId).Distinct().Count() != 1) continue;
            module = mapped.Aggregate(AlertModule.None, (mask, s) => mask | s.Module);
            var state = m.EffectiveStatus(today);
            result.Add(new(m.Id, "ReferenceMaterial", module, mapped[0].DepartmentId, m.Name,
                string.Join(" / ", m.CommercialRoles.OrderBy(r => r.Kind).Select(r => r.Code).DefaultIfEmpty(m.CatalogNumber ?? m.CasNumber ?? "Sin código")),
                m.Lot, m.Presentation == "Solid" ? "Estándar sólido" : "Comercial / " + m.Presentation,
                m.TotalQuantity > 0 ? m.RemainingPercentage : null, m.AvailableQuantity, m.Unit.Symbol,
                m.EffectiveExpirationDate ?? m.ExpirationDate,
                m.ReceivedDate <= today && state is ReferenceMaterialStatus.Active or ReferenceMaterialStatus.Depleted or ReferenceMaterialStatus.Expired,
                string.Join(' ', m.CertifiedComponents.Select(c => c.Name))));
        }
        var preparations = await db.ReferencePreparations.AsNoTracking().ToArrayAsync(ct).ConfigureAwait(false);
        foreach (var p in preparations)
        {
            var module = p.Kind switch { "Stock" => AlertModule.Stock, "Intermedia" => AlertModule.Intermedia, "Curva" => AlertModule.Curva, "AQS" => AlertModule.AQS, _ => AlertModule.None };
            if (!scopes.TryGetValue(module, out var department)) continue;
            // Curva/AQS intentionally persist available_volume=0: it is not a consumable balance.
            // That sentinel must never become an invented depletion alert.
            var balance = new StockAvailability(p.FinalVolume, p.FinalVolumeUnit, p.Kind is "Stock" or "Intermedia" ? p.AvailableVolume : null);
            result.Add(new(p.Id, "Preparation", module, department, p.Name, p.Code, p.SourceLot, p.Kind + " preparada",
                balance.RemainingPercent, balance.AvailableVolume ?? p.AvailableVolume, p.FinalVolumeUnit, p.ExpirationDate,
                p.PreparationDate <= today && p.Status is "Active" or "Depleted" or "Expired", p.Compound ?? p.SourceName));
        }
        return result;
    }
}

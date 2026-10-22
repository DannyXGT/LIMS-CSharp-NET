using Lims.Contracts.ReferencePreparations;
using Lims.Domain.ReferenceMaterials;
using Lims.Domain.ReferencePreparations;
using Microsoft.EntityFrameworkCore;

namespace Lims.Infrastructure.Persistence;

internal sealed record PreparationEntryKey(Guid Id, DateOnly Date, DateTimeOffset Created, bool Commercial);
internal sealed record PreparationEntryPage(IReadOnlyList<PreparationEntryKey> Keys, ReferenceCommercialRole[] Commercial, int Total);
internal static class CommercialPreparationQuery
{
    public static string Pattern(string value) => "%" + value.Trim().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
    public static async Task<PreparationEntryPage> PageAsync(LimsDbContext db, IQueryable<ReferencePreparation> internalQuery,
        string kind, string? search, PreparationListFilter? filter, DateOnly today, int page, int pageSize, CancellationToken ct)
    {
        var commercial = db.Set<ReferenceCommercialRole>().AsNoTracking().Where(r => r.Kind == kind);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = Pattern(search);
            commercial = commercial.Where(r => EF.Functions.ILike(r.Code, term, "\\") || EF.Functions.ILike(r.Material.Name, term, "\\") ||
                EF.Functions.ILike(r.Material.Lot, term, "\\") || EF.Functions.ILike(r.Material.Brand, term, "\\") ||
                r.Material.CertifiedComponents.Any(c => EF.Functions.ILike(c.Name, term, "\\") || (c.CasNumber != null && EF.Functions.ILike(c.CasNumber, term, "\\"))));
        }
        commercial = filter?.Status switch
        {
            "Active" => commercial.Where(r => r.Material.Status == ReferenceMaterialStatus.Active && (r.Material.EffectiveExpirationDate ?? r.Material.ExpirationDate) >= today),
            "Expired" => commercial.Where(r => r.Material.Status == ReferenceMaterialStatus.Active && (r.Material.EffectiveExpirationDate ?? r.Material.ExpirationDate) < today),
            "Archived" => commercial.Where(r => r.Material.Status == ReferenceMaterialStatus.Archived),
            _ => commercial
        };
        commercial = filter?.Availability switch
        {
            "Positive" => commercial.Where(r => r.Material.AvailableQuantity > 0),
            "Low" => commercial.Where(r => r.Material.AvailableQuantity > 0 && r.Material.AvailableQuantity <= r.Material.PresentationQuantity * 0.60m),
            "Depleted" => commercial.Where(r => r.Material.AvailableQuantity == 0),
            _ => commercial
        };
        if (!string.IsNullOrWhiteSpace(filter?.Method))
        {
            var term = Pattern(filter.Method);
            commercial = commercial.Where(r => EF.Functions.ILike(r.Material.Method.Name, term, "\\"));
        }
        var union = internalQuery.Select(p => new { p.Id, Date = p.PreparationDate, Created = p.CreatedAt, Commercial = false })
            .Concat(commercial.Select(r => new { Id = r.MaterialId, Date = r.Material.ReceivedDate, Created = r.RegisteredAt, Commercial = true }));
        var count = await union.CountAsync(ct).ConfigureAwait(false);
        var keys = await union.OrderByDescending(k => k.Date).ThenByDescending(k => k.Created).ThenBy(k => k.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(ct).ConfigureAwait(false);
        var ids = keys.Where(k => k.Commercial).Select(k => k.Id).ToArray();
        var roles = await db.Set<ReferenceCommercialRole>().AsNoTracking().Where(r => r.Kind == kind && ids.Contains(r.MaterialId))
            .Include(r => r.Material).ThenInclude(m => m.Method).Include(r => r.Material).ThenInclude(m => m.Unit)
            .Include(r => r.Material).ThenInclude(m => m.CertifiedComponents).ToArrayAsync(ct).ConfigureAwait(false);
        return new(keys.Select(k => new PreparationEntryKey(k.Id, k.Date, k.Created, k.Commercial)).ToArray(), roles, count);
    }
    public static StockSummary Stock(ReferenceCommercialRole role, string actor, DateOnly today)
    {
        var m = role.Material; var component = Lims.Application.ReferenceMaterials.CommercialMaterials.Certificate(m)!.ComponentsForUse().Single();
        return new(m.Id, role.Code, m.Name, "Comercial / comprado", m.Lot, null, component.CertifiedConcentration,
            component.ConcentrationUnit, m.TotalQuantity, m.Unit.Symbol, m.ReceivedDate, actor, m.EffectiveStatus(today).ToString(),
            null, "", m.EffectiveExpirationDate ?? m.ExpirationDate, m.AvailableQuantity, true);
    }
    public static IntermediateSummary Intermediate(ReferenceCommercialRole role, string actor, DateOnly today)
    {
        var m = role.Material;
        // List projection is descriptive. No preparation or dilution is created for the commercial bottle.
        var analytes = Lims.Application.ReferenceMaterials.CommercialMaterials.Certificate(m)!.ComponentsForUse().Select(c => new IntermediateAnalyte(c.Id, c.Name, c.CasNumber, c.Lot ?? m.Lot,
            c.CertifiedConcentration, c.ConcentrationUnit, m.Id, c.Id, c.ExpandedUncertainty)).ToArray();
        return new(m.Id, role.Code, m.Name, m.Method.Name, analytes.Length, m.TotalQuantity, m.Unit.Symbol, m.AvailableQuantity,
            analytes, m.ReceivedDate, m.EffectiveExpirationDate ?? m.ExpirationDate, actor, m.EffectiveStatus(today).ToString(), true);
    }
}

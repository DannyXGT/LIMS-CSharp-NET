using Lims.Application.ReferenceMaterials;
using Lims.Contracts.ReferenceMaterials;
using Lims.Contracts.ReferencePreparations;
using Lims.Domain.ReferenceMaterials;
using Lims.Domain.ReferencePreparations;
using Microsoft.EntityFrameworkCore;

namespace Lims.Infrastructure.Persistence;

/// <summary>Resolves both FK-backed source families; commercial bottles never become preparations.</summary>
internal static class PreparationSources
{
    public static IQueryable<ReferenceMaterial> Commercial(LimsDbContext db) => db.ReferenceMaterials
        .Include(m => m.Method).Include(m => m.Unit).Include(m => m.CertifiedComponents).Include(m => m.CommercialRoles)
        .Where(m => m.Presentation == MaterialPresentations.Solution || m.Presentation == MaterialPresentations.Mix);

    public static bool Eligible(ReferenceMaterial m, DateOnly today) => !m.OpeningRequired &&
        (!HistoricalEntryPolicy.EnforcePreparationDates || m.ReceivedDate <= today) &&
        m.EffectiveStatus(HistoricalEntryPolicy.EnforcePreparationDates ? today : m.EffectiveExpirationDate ?? m.ExpirationDate) == ReferenceMaterialStatus.Active && m.Method.IsActive;

    public static IntermediateSource? TrySource(ReferenceMaterial m)
    {
        try { return CommercialMaterials.Source(m); } catch (ArgumentException) { return null; }
    }

    public static void Debit(LimsDbContext db, ReferencePreparation destination, IntermediateSource snapshot, int position,
        decimal volume, string unit, ReferencePreparation[] preparations, ReferenceMaterial[] materials,
        int actor, DateTimeOffset now, DateOnly today)
    {
        ReferencePreparationComponent component;
        ReferenceMaterialMovement movement;
        var json = System.Text.Json.JsonSerializer.Serialize(snapshot);
        if (MaterialPresentations.IsCommercial(snapshot.Kind))
        {
            var source = materials.Single(m => m.Id == snapshot.Id);
            var debit = StockUnits.ConvertVolume(volume, unit, source.Unit.Symbol);
            component = new(destination.Id, source, position, volume, unit, json, now);
            movement = new(destination, source, component, debit, now);
            source.ConsumeCommercial(debit, actor, now, today, HistoricalEntryPolicy.EnforcePreparationDates);
        }
        else
        {
            var source = preparations.Single(p => p.Id == snapshot.Id);
            var debit = StockUnits.ConvertVolume(volume, unit, source.FinalVolumeUnit);
            component = new(destination.Id, source, position, volume, unit, json, now);
            movement = new(destination, source, component, debit, now);
            source.ConsumeVolume(debit, actor, now, today, HistoricalEntryPolicy.EnforcePreparationDates);
        }
        db.ReferencePreparationComponents.Add(component);
        db.ReferenceMaterialMovements.Add(movement);
    }
}

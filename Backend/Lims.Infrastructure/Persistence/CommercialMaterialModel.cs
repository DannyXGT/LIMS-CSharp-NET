using Lims.Domain.Identity;
using Lims.Domain.ReferenceMaterials;
using Microsoft.EntityFrameworkCore;

namespace Lims.Infrastructure.Persistence;

internal static class CommercialMaterialModel
{
    public static void Configure(ModelBuilder model)
    {
        var role = model.Entity<ReferenceCommercialRole>();
        role.ToTable("reference_commercial_roles", t => t.HasCheckConstraint("ck_commercial_role_kind", "kind IN ('Stock','Intermedia')"));
        role.HasKey(r => new { r.MaterialId, r.Kind });
        role.HasOne(r => r.Material).WithMany(m => m.CommercialRoles).HasForeignKey(r => r.MaterialId).OnDelete(DeleteBehavior.Restrict);
        role.HasOne<User>().WithMany().HasForeignKey(r => r.RegisteredByUserId).OnDelete(DeleteBehavior.Restrict);
        role.HasIndex(r => r.Code).IsUnique(); role.HasIndex(r => r.RequestId).IsUnique();
        role.Property(r => r.Kind).HasMaxLength(24); role.Property(r => r.Code).HasMaxLength(40); role.Property(r => r.RequestFingerprint).HasMaxLength(64);
        foreach (var p in role.Metadata.GetProperties()) role.Property(p.Name).HasColumnName(Snake(p.Name));
        var material = model.Entity<ReferenceMaterial>();
        material.Property(m => m.Presentation).HasColumnName("presentation").HasMaxLength(24).HasDefaultValue("Solid");
        material.Property(m => m.CommercialJson).HasColumnName("commercial_json").HasColumnType("jsonb");
        material.Property(m => m.OpenedDate).HasColumnName("opened_date");
        material.Property(m => m.EffectiveExpirationDate).HasColumnName("effective_expiration_date");
        material.Property(m => m.OpeningRequired).HasColumnName("opening_required");
        material.ToTable("reference_materials", t =>
        {
            t.HasCheckConstraint("ck_reference_materials_presentation", "presentation IN ('Solid','CommercialSolution','CommercialMix') AND (presentation = 'Solid' OR (commercial_json IS NOT NULL AND effective_expiration_date <= expiration_date))");
        });
        var component = model.Entity<CertifiedMaterialComponent>();
        component.ToTable("reference_certified_components", t => t.HasCheckConstraint("ck_certified_component_values",
            "certified_concentration > 0 AND (expanded_uncertainty IS NULL OR expanded_uncertainty >= 0) AND (purity_percent IS NULL OR (purity_percent > 0 AND purity_percent <= 100))"));
        component.HasKey(c => c.Id);
        component.Property(c => c.Id).ValueGeneratedNever();
        component.HasOne<ReferenceMaterial>().WithMany(m => m.CertifiedComponents).HasForeignKey(c => c.MaterialId).OnDelete(DeleteBehavior.Cascade);
        foreach (var p in component.Metadata.GetProperties())
        {
            component.Property(p.Name).HasColumnName(Snake(p.Name));
            if (p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)) component.Property(p.Name).HasColumnType("numeric");
        }
        var audit = model.Entity<ReferenceMaterialOpeningAudit>();
        audit.ToTable("reference_material_opening_history");
        audit.HasKey(a => a.Id); audit.Property(a => a.Id).ValueGeneratedNever();
        audit.HasOne<ReferenceMaterial>().WithMany(m => m.OpeningHistory).HasForeignKey(a => a.MaterialId).OnDelete(DeleteBehavior.Restrict);
        audit.HasOne<User>().WithMany().HasForeignKey(a => a.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        foreach (var p in audit.Metadata.GetProperties()) audit.Property(p.Name).HasColumnName(Snake(p.Name));
    }
    private static string Snake(string value) => string.Concat(value.Select((c, i) => (i > 0 && char.IsUpper(c) ? "_" : "") + char.ToLowerInvariant(c)));
}

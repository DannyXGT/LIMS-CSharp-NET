using Lims.Domain.ReferencePreparations;
using Lims.Domain.ReferenceMaterials;
using Microsoft.EntityFrameworkCore;

namespace Lims.Infrastructure.Persistence;

internal static class StockSolventModel
{
    public static void Configure(ModelBuilder model)
    {
        var catalog = model.Entity<ReferenceSolvent>();
        catalog.ToTable("reference_solvents", t => t.HasCheckConstraint("ck_reference_solvents_identity", "length(trim(name)) > 0 AND length(trim(abbreviation)) > 0 AND display_order >= 0"));
        catalog.HasKey(s => s.Id);
        catalog.Property(s => s.Id).HasColumnName("id").ValueGeneratedNever();
        catalog.Property(s => s.Name).HasColumnName("name").HasMaxLength(200);
        catalog.Property(s => s.Abbreviation).HasColumnName("abbreviation").HasMaxLength(32);
        catalog.Property(s => s.IsActive).HasColumnName("is_active");
        catalog.Property(s => s.DisplayOrder).HasColumnName("display_order");
        catalog.HasIndex(s => s.Abbreviation).IsUnique();
        catalog.HasData(
            new ReferenceSolvent(1, "Tetrahidrofurano", "THF", true, 0),
            new ReferenceSolvent(2, "Hexano", "Hxn", true, 1),
            new ReferenceSolvent(3, "Acetonitrilo", "ACN", true, 2),
            new ReferenceSolvent(4, "Metanol", "MeOH", true, 3),
            new ReferenceSolvent(5, "Tolueno", "Tolueno", true, 4),
            new ReferenceSolvent(6, "Éter metil tert-butílico", "MTBE", true, 5),
            new ReferenceSolvent(7, "Ciclohexano", "Ciclohexano", true, 6),
            new ReferenceSolvent(8, "Agua", "H2O", true, 7),
            new ReferenceSolvent(9, "Ácido nítrico", "HNO3", true, 8),
            new ReferenceSolvent(10, "Ácido clorhídrico", "HCl", true, 9),
            new ReferenceSolvent(11, "Acetona", "Acetona", true, 10),
            new ReferenceSolvent(12, "Dimetilsulfóxido", "DMSO", true, 11));
        var row = model.Entity<StockSolvent>();
        row.ToTable("stock_solvents", t =>
        {
            t.HasCheckConstraint("ck_stock_solvents_percent", "percent > 0 AND percent <= 100 AND position >= 0");
            t.HasCheckConstraint("ck_stock_solvents_owner", "(preparation_id IS NOT NULL AND material_id IS NULL) OR (preparation_id IS NULL AND material_id IS NOT NULL)");
        });
        row.HasKey(s => s.Id);
        row.Property(s => s.Id).HasColumnName("id").ValueGeneratedNever();
        row.Property(s => s.PreparationId).HasColumnName("preparation_id");
        row.Property(s => s.MaterialId).HasColumnName("material_id");
        row.Property(s => s.SolventId).HasColumnName("solvent_id");
        row.Property(s => s.Percent).HasColumnName("percent").HasPrecision(7, 4);
        row.Property(s => s.Position).HasColumnName("position");
        row.HasOne(s => s.Solvent).WithMany().HasForeignKey(s => s.SolventId).OnDelete(DeleteBehavior.Restrict);
        row.HasOne<ReferencePreparation>().WithMany(p => p.Solvents).HasForeignKey(s => s.PreparationId).OnDelete(DeleteBehavior.Restrict);
        row.HasOne<ReferenceMaterial>().WithMany().HasForeignKey(s => s.MaterialId).OnDelete(DeleteBehavior.Restrict);
        row.HasIndex(s => new { s.MaterialId, s.SolventId }).IsUnique();
        row.HasIndex(s => new { s.MaterialId, s.Position }).IsUnique();
        row.HasIndex(s => new { s.PreparationId, s.SolventId }).IsUnique();
        row.HasIndex(s => new { s.PreparationId, s.Position }).IsUnique();
    }
}

using System.Text;
using Lims.Domain.Identity;
using Lims.Domain.ReferenceMaterials;
using Lims.Domain.ReferencePreparations;
using Microsoft.EntityFrameworkCore;

namespace Lims.Infrastructure.Persistence;

internal static class StockModel
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.HasSequence<long>("reference_stock_code_sequence");
        var preparation = modelBuilder.Entity<ReferencePreparation>();
        preparation.ToTable("reference_preparations", table =>
        {
            table.HasCheckConstraint("ck_reference_preparations_kind_status", "kind = 'Stock' AND status = 'Active'");
            table.HasCheckConstraint("ck_reference_preparations_quantities", "target_concentration > 0 AND actual_concentration > 0 AND final_volume > 0 AND calculated_weight > 0 AND actual_weight > 0 AND source_total_quantity >= actual_weight");
            table.HasCheckConstraint("ck_reference_preparations_purity", "purity_percent_used > 0 AND purity_percent_used <= 100");
            table.HasCheckConstraint("ck_reference_preparations_units", "concentration_unit IN ('mg/L','g/L','µg/L','µg/mL') AND final_volume_unit IN ('mL','L') AND calculated_weight_unit = source_unit AND actual_weight_unit = source_unit AND actual_concentration_unit = concentration_unit AND source_unit IN ('g','mg','µg','μg','ug')");
            table.HasCheckConstraint("ck_reference_preparations_identity", "length(trim(code)) > 0 AND length(trim(name)) > 0 AND length(request_fingerprint) = 64");
            table.HasCheckConstraint("ck_reference_preparations_dates", "expiration_date >= preparation_date AND preparation_date <= source_expiration_date AND updated_at >= created_at");
        });
        preparation.HasKey(item => item.Id);
        preparation.Property(item => item.Id).ValueGeneratedNever();
        preparation.Property(item => item.Version).ValueGeneratedNever().IsConcurrencyToken();
        preparation.HasIndex(item => item.Code).IsUnique().HasDatabaseName("ux_reference_preparations_code");
        preparation.HasIndex(item => item.SourceMaterialId).HasDatabaseName("ix_reference_preparations_source");
        preparation.HasIndex(item => item.PreparationDate).HasDatabaseName("ix_reference_preparations_date");
        preparation.HasIndex(item => item.PreparedByUserId).HasDatabaseName("ix_reference_preparations_actor");
        preparation.HasOne(item => item.SourceMaterial).WithMany().HasForeignKey(item => item.SourceMaterialId).OnDelete(DeleteBehavior.Restrict);
        preparation.HasOne<User>().WithMany().HasForeignKey(item => item.PreparedByUserId).OnDelete(DeleteBehavior.Restrict);
        preparation.HasAlternateKey(item => new { item.Id, item.SourceMaterialId, item.PreparedByUserId, item.ActualWeight, item.SourceUnit })
            .HasName("ak_reference_preparations_consumption");
        foreach (var property in preparation.Metadata.GetProperties())
        {
            preparation.Property(property.Name).HasColumnName(Snake(property.Name));
            if (property.ClrType == typeof(decimal)) preparation.Property(property.Name).HasPrecision(18, 6);
            if (property.ClrType == typeof(string)) preparation.Property(property.Name).HasMaxLength(200);
        }
        preparation.Property(item => item.PurityPercentUsed).HasPrecision(7, 4);
        preparation.Property(item => item.CalculatedWeight).HasPrecision(40, 28);
        preparation.Property(item => item.ActualConcentration).HasPrecision(40, 28);
        preparation.Property(item => item.CalculatedWeightUnit).HasMaxLength(16);
        preparation.Property(item => item.ActualWeightUnit).HasMaxLength(16);
        preparation.Property(item => item.ActualConcentrationUnit).HasMaxLength(16);
        preparation.Property(item => item.Code).HasMaxLength(40);
        preparation.Property(item => item.RequestFingerprint).HasMaxLength(64);
        preparation.Property(item => item.SourceLot).HasMaxLength(120);
        preparation.Property(item => item.SourceCasNumber).HasMaxLength(80);
        preparation.Property(item => item.SourceCatalogNumber).HasMaxLength(80);
        preparation.Property(item => item.SourceBrand).HasMaxLength(120);
        preparation.Property(item => item.SourceUnit).HasMaxLength(16);
        preparation.Property(item => item.FinalVolumeUnit).HasMaxLength(16);
        preparation.Property(item => item.ConcentrationUnit).HasMaxLength(16);
        preparation.Property(item => item.Kind).HasMaxLength(24);
        preparation.Property(item => item.Status).HasMaxLength(24);
        preparation.Property(item => item.Formula).HasMaxLength(240);
        preparation.Property(item => item.Notes).HasMaxLength(2000);

        var movement = modelBuilder.Entity<ReferenceMaterialMovement>();
        movement.ToTable("reference_material_movements", table =>
        {
            table.HasCheckConstraint("ck_reference_material_movements_balance", "quantity > 0 AND balance_before >= quantity AND balance_after >= 0 AND balance_after = balance_before - quantity");
            table.HasCheckConstraint("ck_reference_material_movements_kind", "kind = 'StockConsumption'");
        });
        movement.HasKey(item => item.Id);
        movement.Property(item => item.Id).ValueGeneratedNever();
        movement.HasIndex(item => item.PreparationId).IsUnique().HasDatabaseName("ux_reference_material_movements_preparation");
        movement.HasIndex(item => new { item.SourceMaterialId, item.OccurredAt }).HasDatabaseName("ix_reference_material_movements_source_time");
        movement.HasOne(item => item.Preparation).WithMany()
            .HasForeignKey(item => new { item.PreparationId, item.SourceMaterialId, item.ActorUserId, item.Quantity, item.Unit })
            .HasPrincipalKey(item => new { item.Id, item.SourceMaterialId, item.PreparedByUserId, item.ActualWeight, item.SourceUnit })
            .OnDelete(DeleteBehavior.Restrict);
        movement.HasOne<ReferenceMaterial>().WithMany().HasForeignKey(item => item.SourceMaterialId).OnDelete(DeleteBehavior.Restrict);
        movement.HasOne<User>().WithMany().HasForeignKey(item => item.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        foreach (var property in movement.Metadata.GetProperties())
        {
            movement.Property(property.Name).HasColumnName(Snake(property.Name));
            if (property.ClrType == typeof(decimal)) movement.Property(property.Name).HasPrecision(18, 6);
        }
        movement.Property(item => item.Unit).HasMaxLength(16);
        movement.Property(item => item.Kind).HasMaxLength(24);
    }

    private static string Snake(string value)
    {
        var result = new StringBuilder();
        for (var index = 0; index < value.Length; index++)
        {
            if (index > 0 && char.IsUpper(value[index])) result.Append('_');
            result.Append(char.ToLowerInvariant(value[index]));
        }
        return result.ToString();
    }
}

using Lims.Domain.Alerts;
using Lims.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Lims.Infrastructure.Persistence;

internal static class AlertModel
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<MaterialAlert>(b =>
        {
            b.ToTable("material_alerts"); b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.SourceType).HasMaxLength(24);
            b.Property(x => x.Material).HasMaxLength(200); b.Property(x => x.SearchText).HasMaxLength(8000);
            b.Property(x => x.Code).HasMaxLength(160); b.Property(x => x.Lot).HasMaxLength(120);
            b.Property(x => x.MaterialType).HasMaxLength(80); b.Property(x => x.Category).HasMaxLength(24);
            b.Property(x => x.Condition).HasMaxLength(24); b.Property(x => x.Title).HasMaxLength(200);
            b.Property(x => x.Description).HasMaxLength(1200); b.Property(x => x.Recommendation).HasMaxLength(1200);
            b.Property(x => x.Unit).HasMaxLength(16); b.Property(x => x.Available).HasPrecision(28, 10);
            b.Property(x => x.AvailabilityPercent).HasPrecision(28, 10);
            b.Property(x => x.Revision).IsConcurrencyToken();
            b.HasIndex(x => new { x.SourceType, x.SourceId, x.Category }).IsUnique().HasFilter("\"ResolvedAt\" IS NULL");
            b.HasIndex(x => new { x.DepartmentId, x.ResolvedAt, x.Priority, x.DetectedAt });
            // Legacy departments are not migration-owned. Validate ownership against that catalog in AlertService.
        });
        model.Entity<AlertHistory>(b =>
        {
            b.ToTable("alert_history"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Action).HasMaxLength(40); b.Property(x => x.Condition).HasMaxLength(24); b.Property(x => x.Note).HasMaxLength(1000);
            b.HasOne<MaterialAlert>().WithMany().HasForeignKey(x => x.AlertId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.AlertId, x.At });
        });
        model.Entity<UserNotification>(b =>
        {
            b.ToTable("user_notifications"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Title).HasMaxLength(400); b.Property(x => x.Message).HasMaxLength(1200);
            b.HasOne(x => x.Alert).WithMany().HasForeignKey(x => x.AlertId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.AlertId, x.Revision, x.UserId }).IsUnique(); b.HasIndex(x => new { x.UserId, x.ReadAt, x.CreatedAt });
        });
        model.Entity<AlertConfiguration>(b => { b.ToTable("alert_configuration"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever(); b.Property(x => x.Version).IsConcurrencyToken(); });
        model.Entity<NotificationPreference>(b => { b.ToTable("notification_preferences"); b.HasKey(x => x.UserId); b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict); });
        model.Entity<EmailDelivery>(b =>
        {
            b.ToTable("alert_email_deliveries"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.DeduplicationKey).HasMaxLength(200); b.HasIndex(x => x.DeduplicationKey).IsUnique();
            b.Property(x => x.Recipient).HasMaxLength(254); b.Property(x => x.Kind).HasMaxLength(24); b.Property(x => x.Subject).HasMaxLength(400);
            b.Property(x => x.State).HasMaxLength(24); b.Property(x => x.LastError).HasMaxLength(400);
            b.HasIndex(x => new { x.State, x.NextAttemptAt }); b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<MaterialAlert>().WithMany().HasForeignKey(x => x.AlertId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<EmailAttempt>(b =>
        {
            b.ToTable("alert_email_attempts"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Outcome).HasMaxLength(24); b.Property(x => x.Error).HasMaxLength(400);
            b.HasOne<EmailDelivery>().WithMany().HasForeignKey(x => x.DeliveryId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.DeliveryId, x.Number }).IsUnique();
        });
        model.Entity<AlertConfigurationAudit>(b => { b.ToTable("alert_configuration_audit"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever(); b.Property(x => x.Action).HasMaxLength(40); });
    }
}

using Lims.Contracts.Permissions;
using Lims.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Lims.Infrastructure.Persistence;

internal static class PermissionModel
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<PermissionCode>(b =>
        {
            b.ToTable("permission_catalog"); b.HasKey(p => p.Code);
            b.Property(p => p.Code).HasColumnName("permission_code").HasMaxLength(120);
            b.HasData(PermissionCatalog.All.Select(p => new PermissionCode { Code = p.Code }));
        });
        model.Entity<UserPermissionSet>(b =>
        {
            b.ToTable("user_permission_sets"); b.HasKey(p => p.UserId);
            b.Property(p => p.UserId).HasColumnName("user_id").ValueGeneratedNever();
            b.Property(p => p.Version).HasColumnName("version").IsConcurrencyToken();
            b.HasOne<User>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<UserPermissionAssignment>(b =>
        {
            b.ToTable("user_permission_assignments"); b.HasKey(p => new { p.UserId, p.Code });
            b.Property(p => p.UserId).HasColumnName("user_id");
            b.Property(p => p.Code).HasColumnName("permission_code").HasMaxLength(120);
            b.Property(p => p.IsGranted).HasColumnName("is_granted");
            b.Property(p => p.ActorUserId).HasColumnName("actor_user_id");
            b.Property(p => p.ChangedAt).HasColumnName("changed_at");
            b.HasOne<UserPermissionSet>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<PermissionCode>().WithMany().HasForeignKey(p => p.Code).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<User>().WithMany().HasForeignKey(p => p.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<UserPermissionAudit>(b =>
        {
            b.ToTable("user_permission_audit"); b.HasKey(p => p.Id);
            b.Property(p => p.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(p => p.UserId).HasColumnName("user_id");
            b.Property(p => p.Code).HasColumnName("permission_code").HasMaxLength(120);
            b.Property(p => p.ActorUserId).HasColumnName("actor_user_id");
            b.Property(p => p.Before).HasColumnName("before_granted");
            b.Property(p => p.After).HasColumnName("after_granted");
            b.Property(p => p.At).HasColumnName("changed_at");
            b.HasOne<User>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<User>().WithMany().HasForeignKey(p => p.ActorUserId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<PermissionCode>().WithMany().HasForeignKey(p => p.Code).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(p => new { p.UserId, p.At });
        });
    }
}

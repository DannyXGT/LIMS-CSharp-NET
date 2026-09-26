using Lims.Domain.Authentication;
using Lims.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Lims.Infrastructure.Persistence;

public sealed class LimsDbContext(DbContextOptions<LimsDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<Department> Departments => Set<Department>();

    public DbSet<UserPermissionGrant> UserPermissionGrants => Set<UserPermissionGrant>();

    public DbSet<AuthSession> AuthSessions => Set<AuthSession>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        ConfigureLegacyIdentity(modelBuilder);
        ConfigureAuthentication(modelBuilder);
    }

    private static void ConfigureLegacyIdentity(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Role>(builder =>
        {
            builder.ToTable("roles", table => table.ExcludeFromMigrations());
            builder.HasKey(role => role.Id);
            builder.Property(role => role.Id).HasColumnName("id");
            builder.Property(role => role.Name).HasColumnName("nombre").HasMaxLength(50).IsRequired();
        });

        modelBuilder.Entity<Department>(builder =>
        {
            builder.ToTable("departamentos", table => table.ExcludeFromMigrations());
            builder.HasKey(department => department.Id);
            builder.Property(department => department.Id).HasColumnName("id");
            builder.Property(department => department.Name).HasColumnName("nombre").HasMaxLength(100).IsRequired();
        });

        modelBuilder.Entity<User>(builder =>
        {
            builder.ToTable("usuarios", table => table.ExcludeFromMigrations());
            builder.HasKey(user => user.Id);
            builder.Property(user => user.Id).HasColumnName("id");
            builder.Property(user => user.Name).HasColumnName("nombre").HasMaxLength(100).IsRequired();
            builder.Property(user => user.Email).HasColumnName("email").HasMaxLength(150).IsRequired();
            builder.Property(user => user.PasswordHash).HasColumnName("password_hash").IsRequired();
            builder.Property(user => user.RoleId).HasColumnName("id_rol");
            builder.Property(user => user.DepartmentId).HasColumnName("id_departamento");
            builder.Property(user => user.IsActive).HasColumnName("activo").IsRequired();
            builder.Property(user => user.CreatedAt).HasColumnName("fecha_creacion");

            builder.HasOne(user => user.Role)
                .WithMany()
                .HasForeignKey(user => user.RoleId)
                .OnDelete(DeleteBehavior.Restrict);
            builder.HasOne(user => user.Department)
                .WithMany()
                .HasForeignKey(user => user.DepartmentId)
                .OnDelete(DeleteBehavior.Restrict);
            builder.HasMany(user => user.Permissions)
                .WithOne()
                .HasForeignKey(permission => permission.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            builder.Navigation(user => user.Permissions)
                .UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<UserPermissionGrant>(builder =>
        {
            builder.ToTable("usuarios_permisos", table => table.ExcludeFromMigrations());
            builder.HasKey(permission => new { permission.UserId, permission.Key });
            builder.Property(permission => permission.UserId).HasColumnName("id_usuario");
            builder.Property(permission => permission.Key)
                .HasColumnName("permiso_key")
                .HasMaxLength(UserPermissionGrant.MaximumKeyLength)
                .IsRequired();
        });
    }

    private static void ConfigureAuthentication(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuthSession>(builder =>
        {
            builder.ToTable("auth_sessions");
            builder.HasKey(session => session.Id);
            builder.Property(session => session.Id).HasColumnName("id").ValueGeneratedNever();
            builder.Property(session => session.UserId).HasColumnName("user_id").IsRequired();
            builder.Property(session => session.TokenFamilyId).HasColumnName("token_family_id").IsRequired();
            builder.Property(session => session.CreatedAt).HasColumnName("created_at").IsRequired();
            builder.Property(session => session.LastSeenAt).HasColumnName("last_seen_at").IsRequired();
            builder.Property(session => session.ExpiresAt).HasColumnName("expires_at").IsRequired();
            builder.Property(session => session.RevokedAt).HasColumnName("revoked_at");
            builder.Property(session => session.RevocationReason)
                .HasColumnName("revocation_reason")
                .HasMaxLength(120);
            builder.Property(session => session.ClientName)
                .HasColumnName("client_name")
                .HasMaxLength(120);

            builder.HasIndex(session => session.UserId).HasDatabaseName("ix_auth_sessions_user_id");
            builder.HasIndex(session => session.TokenFamilyId)
                .IsUnique()
                .HasDatabaseName("ux_auth_sessions_token_family_id");
            builder.HasIndex(session => new { session.ExpiresAt, session.RevokedAt })
                .HasDatabaseName("ix_auth_sessions_expiry_revocation");
            builder.HasOne<User>()
                .WithMany()
                .HasForeignKey(session => session.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RefreshToken>(builder =>
        {
            builder.ToTable("auth_refresh_tokens");
            builder.HasKey(token => token.Id);
            builder.Property(token => token.Id).HasColumnName("id").ValueGeneratedNever();
            builder.Property(token => token.SessionId).HasColumnName("session_id").IsRequired();
            builder.Property(token => token.TokenHash)
                .HasColumnName("token_hash")
                .HasMaxLength(64)
                .IsFixedLength()
                .IsRequired();
            builder.Property(token => token.CreatedAt).HasColumnName("created_at").IsRequired();
            builder.Property(token => token.ExpiresAt).HasColumnName("expires_at").IsRequired();
            builder.Property(token => token.ConsumedAt).HasColumnName("consumed_at");
            builder.Property(token => token.ReplacedByTokenId).HasColumnName("replaced_by_token_id");
            builder.Property(token => token.RevokedAt).HasColumnName("revoked_at");

            builder.HasIndex(token => token.TokenHash)
                .IsUnique()
                .HasDatabaseName("ux_auth_refresh_tokens_hash");
            builder.HasIndex(token => token.SessionId)
                .HasDatabaseName("ix_auth_refresh_tokens_session_id");
            builder.HasOne<AuthSession>()
                .WithMany()
                .HasForeignKey(token => token.SessionId)
                .OnDelete(DeleteBehavior.Cascade);
            builder.HasOne<RefreshToken>()
                .WithMany()
                .HasForeignKey(token => token.ReplacedByTokenId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}

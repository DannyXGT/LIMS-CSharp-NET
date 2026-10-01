using Lims.Domain.Authentication;
using Lims.Domain.Identity;
using Lims.Domain.ReferenceMaterials;
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

    public DbSet<ReferenceMaterial> ReferenceMaterials => Set<ReferenceMaterial>();

    public DbSet<ReferenceMethod> ReferenceMethods => Set<ReferenceMethod>();

    public DbSet<ReferenceUnit> ReferenceUnits => Set<ReferenceUnit>();

    public DbSet<ReferenceLocation> ReferenceLocations => Set<ReferenceLocation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        ConfigureLegacyIdentity(modelBuilder);
        ConfigureAuthentication(modelBuilder);
        ConfigureReferenceMaterialCatalogs(modelBuilder);
        ConfigureReferenceMaterials(modelBuilder);
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
            builder.Property(user => user.CreatedAt)
                .HasColumnName("fecha_creacion")
                .HasColumnType("timestamp without time zone");

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

    private static void ConfigureReferenceMaterialCatalogs(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ReferenceMethod>(builder =>
        {
            builder.ToTable("reference_methods", table => table.HasCheckConstraint(
                "ck_reference_methods_name",
                "name = btrim(name) AND name <> ''"));
            builder.HasKey(method => method.Id);
            builder.Property(method => method.Id).HasColumnName("id").ValueGeneratedOnAdd();
            builder.Property(method => method.Name)
                .HasColumnName("name")
                .HasMaxLength(ReferenceMethod.NameMaximumLength)
                .IsRequired();
            builder.Property(method => method.IsActive).HasColumnName("is_active").IsRequired();
            builder.HasIndex(method => method.Name)
                .IsUnique()
                .HasDatabaseName("uq_reference_methods_name");
            builder.HasIndex(method => new { method.IsActive, method.Name })
                .HasDatabaseName("ix_reference_methods_active_name");
            builder.HasData(
                new ReferenceMethod(1, "Azodyes", true),
                new ReferenceMethod(2, "APEOs", true),
                new ReferenceMethod(3, "Disperse dyes", true),
                new ReferenceMethod(4, "Phtalatos", true),
                new ReferenceMethod(5, "PCP", true),
                new ReferenceMethod(6, "OPP", true),
                new ReferenceMethod(7, "Organotin", true),
                new ReferenceMethod(8, "PAHs", true),
                new ReferenceMethod(9, "SCCP/MCCP", true),
                new ReferenceMethod(10, "Retardantes de Flama", true),
                new ReferenceMethod(11, "DMFA/DMFU", true),
                new ReferenceMethod(12, "VOC", true),
                new ReferenceMethod(13, "AEEA", true),
                new ReferenceMethod(14, "Bisphenol", true),
                new ReferenceMethod(15, "Halogenated", true),
                new ReferenceMethod(16, "Tiourea", true),
                new ReferenceMethod(17, "PFC", true),
                new ReferenceMethod(18, "COC", true),
                new ReferenceMethod(19, "Cresoles", true),
                new ReferenceMethod(20, "UV", true),
                new ReferenceMethod(21, "Glicoles", true),
                new ReferenceMethod(22, "Metales", true),
                new ReferenceMethod(23, "AMB", true));
        });

        modelBuilder.Entity<ReferenceUnit>(builder =>
        {
            builder.ToTable("reference_units", table =>
            {
                table.HasCheckConstraint("ck_reference_units_name", "name = btrim(name) AND name <> ''");
                table.HasCheckConstraint("ck_reference_units_symbol", "symbol = btrim(symbol) AND symbol <> ''");
            });
            builder.HasKey(unit => unit.Id);
            builder.Property(unit => unit.Id).HasColumnName("id").ValueGeneratedOnAdd();
            builder.Property(unit => unit.Name)
                .HasColumnName("name")
                .HasMaxLength(ReferenceUnit.NameMaximumLength)
                .IsRequired();
            builder.Property(unit => unit.Symbol)
                .HasColumnName("symbol")
                .HasMaxLength(ReferenceUnit.SymbolMaximumLength)
                .IsRequired();
            builder.Property(unit => unit.IsActive).HasColumnName("is_active").IsRequired();
            builder.HasIndex(unit => unit.Name)
                .IsUnique()
                .HasDatabaseName("uq_reference_units_name");
            builder.HasIndex(unit => unit.Symbol)
                .IsUnique()
                .HasDatabaseName("uq_reference_units_symbol");
            builder.HasIndex(unit => new { unit.IsActive, unit.Name })
                .HasDatabaseName("ix_reference_units_active_name");
            builder.HasData(
                new ReferenceUnit(1, "Mililitro", "mL", true),
                new ReferenceUnit(2, "Gramo", "g", true),
                new ReferenceUnit(3, "Miligramo", "mg", true),
                new ReferenceUnit(4, "Microgramo", "µg", true));
        });

        modelBuilder.Entity<ReferenceLocation>(builder =>
        {
            builder.ToTable("reference_locations", table => table.HasCheckConstraint(
                "ck_reference_locations_name",
                "name = btrim(name) AND name <> ''"));
            builder.HasKey(location => location.Id);
            builder.Property(location => location.Id).HasColumnName("id").ValueGeneratedOnAdd();
            builder.Property(location => location.Name)
                .HasColumnName("name")
                .HasMaxLength(ReferenceLocation.NameMaximumLength)
                .IsRequired();
            builder.Property(location => location.IsActive).HasColumnName("is_active").IsRequired();
            builder.HasIndex(location => location.Name)
                .IsUnique()
                .HasDatabaseName("uq_reference_locations_name");
            builder.HasIndex(location => new { location.IsActive, location.Name })
                .HasDatabaseName("ix_reference_locations_active_name");
            builder.HasData(
                new ReferenceLocation(1, "Laboratorio", true),
                new ReferenceLocation(2, "Bodega", true));
        });
    }

    private static void ConfigureReferenceMaterials(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ReferenceMaterial>(builder =>
        {
            builder.ToTable("reference_materials", table =>
            {
                table.HasCheckConstraint(
                    "ck_reference_materials_purity",
                    "purity_percent > 0 AND purity_percent <= 100");
                table.HasCheckConstraint(
                    "ck_reference_materials_presentation_quantity",
                    "presentation_quantity > 0");
                table.HasCheckConstraint(
                    "ck_reference_materials_package_count",
                    "package_count > 0");
                table.HasCheckConstraint(
                    "ck_reference_materials_dates",
                    "expiration_date >= received_date");
                table.HasCheckConstraint(
                    "ck_reference_materials_available_quantity",
                    "available_quantity >= 0 AND available_quantity <= presentation_quantity * package_count");
                table.HasCheckConstraint(
                    "ck_reference_materials_status",
                    "status IN ('Active','Depleted','Expired','Blocked','Replaced','Archived','Retired')");
                table.HasCheckConstraint(
                    "ck_reference_materials_replacement",
                    "(status = 'Replaced' AND replaced_by_material_id IS NOT NULL) OR (status <> 'Replaced' AND replaced_by_material_id IS NULL)");
            });
            builder.HasKey(material => material.Id);
            builder.Property(material => material.Id).HasColumnName("id").ValueGeneratedNever();
            builder.Property(material => material.Name)
                .HasColumnName("name")
                .HasMaxLength(ReferenceMaterial.NameMaximumLength)
                .IsRequired();
            builder.Property(material => material.CasNumber)
                .HasColumnName("cas_number")
                .HasMaxLength(ReferenceMaterial.IdentifierMaximumLength);
            builder.Property(material => material.CatalogNumber)
                .HasColumnName("catalog_number")
                .HasMaxLength(ReferenceMaterial.IdentifierMaximumLength);
            builder.Property(material => material.MethodId)
                .HasColumnName("method_id")
                .IsRequired();
            builder.Property(material => material.PurityPercent)
                .HasColumnName("purity_percent")
                .HasPrecision(7, 4)
                .IsRequired();
            builder.Property(material => material.Lot)
                .HasColumnName("lot")
                .HasMaxLength(ReferenceMaterial.LotMaximumLength)
                .IsRequired();
            builder.Property(material => material.Brand)
                .HasColumnName("brand")
                .HasMaxLength(ReferenceMaterial.BrandMaximumLength)
                .IsRequired();
            builder.Property(material => material.ReceivedDate)
                .HasColumnName("received_date")
                .HasColumnType("date")
                .IsRequired();
            builder.Property(material => material.ExpirationDate)
                .HasColumnName("expiration_date")
                .HasColumnType("date")
                .IsRequired();
            builder.Property(material => material.PresentationQuantity)
                .HasColumnName("presentation_quantity")
                .HasPrecision(18, 6)
                .IsRequired();
            builder.Property(material => material.UnitId)
                .HasColumnName("unit_id")
                .IsRequired();
            builder.Property(material => material.PackageCount)
                .HasColumnName("package_count")
                .IsRequired();
            builder.Property(material => material.StorageTemperature)
                .HasColumnName("storage_temperature")
                .HasMaxLength(ReferenceMaterial.StorageTemperatureMaximumLength)
                .IsRequired();
            builder.Property(material => material.LocationId)
                .HasColumnName("location_id")
                .IsRequired();
            builder.Property(material => material.AvailableQuantity)
                .HasColumnName("available_quantity")
                .HasPrecision(18, 6)
                .IsRequired();
            builder.Property(material => material.Status)
                .HasColumnName("status")
                .HasConversion<string>()
                .HasMaxLength(24)
                .IsRequired();
            builder.Property(material => material.CreatedByUserId)
                .HasColumnName("created_by_user_id")
                .IsRequired();
            builder.Property(material => material.CreatedAt)
                .HasColumnName("created_at")
                .IsRequired();
            builder.Property(material => material.UpdatedByUserId)
                .HasColumnName("updated_by_user_id")
                .IsRequired();
            builder.Property(material => material.UpdatedAt)
                .HasColumnName("updated_at")
                .IsRequired();
            builder.Property(material => material.ArchivedByUserId)
                .HasColumnName("archived_by_user_id");
            builder.Property(material => material.ArchivedAt)
                .HasColumnName("archived_at");
            builder.Property(material => material.ArchiveReason)
                .HasColumnName("archive_reason")
                .HasMaxLength(ReferenceMaterial.ArchiveReasonMaximumLength);
            builder.Property(material => material.ReplacedByMaterialId)
                .HasColumnName("replaced_by_material_id");
            builder.Property(material => material.Version)
                .HasColumnName("version")
                .ValueGeneratedNever()
                .IsConcurrencyToken();
            builder.Ignore(material => material.TotalQuantity);
            builder.Ignore(material => material.RemainingPercentage);

            builder.HasIndex(material => material.Name)
                .HasDatabaseName("ix_reference_materials_name");
            builder.HasIndex(material => material.MethodId)
                .HasDatabaseName("ix_reference_materials_method_id");
            builder.HasIndex(material => material.UnitId)
                .HasDatabaseName("ix_reference_materials_unit_id");
            builder.HasIndex(material => material.LocationId)
                .HasDatabaseName("ix_reference_materials_location_id");
            builder.HasIndex(material => material.Status)
                .HasDatabaseName("ix_reference_materials_status");
            builder.HasIndex(material => material.ExpirationDate)
                .HasDatabaseName("ix_reference_materials_expiration_date");
            builder.HasIndex(material => new { material.CasNumber, material.CatalogNumber })
                .HasDatabaseName("ix_reference_materials_cas_catalog");
            builder.HasIndex(material => material.ReplacedByMaterialId)
                .HasDatabaseName("ix_reference_materials_replaced_by");

            builder.HasOne<User>()
                .WithMany()
                .HasForeignKey(material => material.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            builder.HasOne<User>()
                .WithMany()
                .HasForeignKey(material => material.UpdatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            builder.HasOne<User>()
                .WithMany()
                .HasForeignKey(material => material.ArchivedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            builder.HasOne<ReferenceMaterial>()
                .WithMany()
                .HasForeignKey(material => material.ReplacedByMaterialId)
                .OnDelete(DeleteBehavior.Restrict);
            builder.HasOne(material => material.Method)
                .WithMany()
                .HasForeignKey(material => material.MethodId)
                .OnDelete(DeleteBehavior.Restrict);
            builder.HasOne(material => material.Unit)
                .WithMany()
                .HasForeignKey(material => material.UnitId)
                .OnDelete(DeleteBehavior.Restrict);
            builder.HasOne(material => material.Location)
                .WithMany()
                .HasForeignKey(material => material.LocationId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}

using Lims.Domain.Authentication;
using Lims.Domain.Identity;
using Lims.Infrastructure.Persistence;
using Lims.Domain.ReferenceMaterials;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Lims.Infrastructure.Tests.Persistence;

public sealed class LimsDbContextModelTests
{
    [Fact]
    public void LegacyIdentityTablesAreMappedButExcludedFromMigrations()
    {
        using var context = CreateContext();

        AssertExcluded<User>(context, "usuarios");
        AssertExcluded<Role>(context, "roles");
        AssertExcluded<Department>(context, "departamentos");
        AssertExcluded<UserPermissionGrant>(context, "usuarios_permisos");
    }

    [Fact]
    public void LegacyUserCreationTimestampMatchesInterDbTimestampWithoutTimeZone()
    {
        using var context = CreateContext();

        var model = context.GetService<IDesignTimeModel>().Model;
        var user = Assert.IsAssignableFrom<IEntityType>(model.FindEntityType(typeof(User)));
        var createdAt = Assert.IsAssignableFrom<IProperty>(user.FindProperty(nameof(User.CreatedAt)));

        Assert.Equal(typeof(DateTime), createdAt.ClrType);
        Assert.Equal("timestamp without time zone", createdAt.GetColumnType());
    }

    [Fact]
    public void LoginIdentityGraphContainsNoDateTimeOffsetProperties()
    {
        using var context = CreateContext();

        var model = context.GetService<IDesignTimeModel>().Model;
        var loginEntityTypes = new[]
        {
            typeof(User),
            typeof(Role),
            typeof(Department),
            typeof(UserPermissionGrant),
        };

        var incompatibleProperties = loginEntityTypes
            .Select(type => Assert.IsAssignableFrom<IEntityType>(model.FindEntityType(type)))
            .SelectMany(entity => entity.GetProperties())
            .Where(property => Nullable.GetUnderlyingType(property.ClrType) == typeof(DateTimeOffset)
                || property.ClrType == typeof(DateTimeOffset))
            .Select(property => $"{property.DeclaringType.Name}.{property.Name}")
            .ToArray();

        Assert.Empty(incompatibleProperties);
    }

    [Fact]
    public void AuthenticationTablesAreOwnedByThisModelAndHaveRequiredUniqueIndexes()
    {
        using var context = CreateContext();

        var model = context.GetService<IDesignTimeModel>().Model;
        var session = model.FindEntityType(typeof(AuthSession));
        var refresh = model.FindEntityType(typeof(RefreshToken));

        Assert.NotNull(session);
        Assert.NotNull(refresh);
        Assert.Equal("auth_sessions", session.GetTableName());
        Assert.Equal("auth_refresh_tokens", refresh.GetTableName());
        Assert.False(session.IsTableExcludedFromMigrations());
        Assert.False(refresh.IsTableExcludedFromMigrations());
        Assert.Contains(session.GetIndexes(), index =>
            index.IsUnique && index.Properties.Single().Name == nameof(AuthSession.TokenFamilyId));
        Assert.Contains(refresh.GetIndexes(), index =>
            index.IsUnique && index.Properties.Single().Name == nameof(RefreshToken.TokenHash));
    }

    [Fact]
    public void ReferenceMaterialMappingUsesDeterministicDecimalPrecisionAndConcurrencyToken()
    {
        using var context = CreateContext();

        var model = context.GetService<IDesignTimeModel>().Model;
        var material = Assert.IsAssignableFrom<IEntityType>(model.FindEntityType(typeof(ReferenceMaterial)));
        var purity = Assert.IsAssignableFrom<IProperty>(material.FindProperty(nameof(ReferenceMaterial.PurityPercent)));
        var presentation = Assert.IsAssignableFrom<IProperty>(material.FindProperty(nameof(ReferenceMaterial.PresentationQuantity)));
        var available = Assert.IsAssignableFrom<IProperty>(material.FindProperty(nameof(ReferenceMaterial.AvailableQuantity)));
        var createdBy = Assert.IsAssignableFrom<IProperty>(material.FindProperty(nameof(ReferenceMaterial.CreatedByUserId)));
        var updatedBy = Assert.IsAssignableFrom<IProperty>(material.FindProperty(nameof(ReferenceMaterial.UpdatedByUserId)));
        var version = Assert.IsAssignableFrom<IProperty>(material.FindProperty(nameof(ReferenceMaterial.Version)));

        Assert.Equal("numeric(7,4)", purity.GetColumnType());
        Assert.Equal("numeric(18,6)", presentation.GetColumnType());
        Assert.Equal("numeric(18,6)", available.GetColumnType());
        Assert.False(createdBy.IsNullable);
        Assert.False(updatedBy.IsNullable);
        Assert.True(version.IsConcurrencyToken);
        Assert.Contains(material.GetCheckConstraints(), constraint => constraint.Name == "ck_reference_materials_dates");
        Assert.Contains(material.GetCheckConstraints(), constraint => constraint.Name == "ck_reference_materials_available_quantity");
    }

    [Fact]
    public void ReferenceMaterialCatalogsHaveExpectedTablesSeedsAndRestrictedRelationships()
    {
        using var context = CreateContext();

        var model = context.GetService<IDesignTimeModel>().Model;
        var material = Assert.IsAssignableFrom<IEntityType>(model.FindEntityType(typeof(ReferenceMaterial)));
        var method = Assert.IsAssignableFrom<IEntityType>(model.FindEntityType(typeof(ReferenceMethod)));
        var unit = Assert.IsAssignableFrom<IEntityType>(model.FindEntityType(typeof(ReferenceUnit)));
        var location = Assert.IsAssignableFrom<IEntityType>(model.FindEntityType(typeof(ReferenceLocation)));
        var storageTemperature = Assert.IsAssignableFrom<IProperty>(
            material.FindProperty(nameof(ReferenceMaterial.StorageTemperature)));

        Assert.Equal("reference_methods", method.GetTableName());
        Assert.Equal("reference_units", unit.GetTableName());
        Assert.Equal("reference_locations", location.GetTableName());
        Assert.Equal("character varying(160)", storageTemperature.GetColumnType());
        Assert.Equal(23, method.GetSeedData().Count());
        Assert.Equal(4, unit.GetSeedData().Count());
        Assert.Equal(2, location.GetSeedData().Count());

        AssertRequiredRestrictedForeignKey<ReferenceMethod>(material, nameof(ReferenceMaterial.MethodId));
        AssertRequiredRestrictedForeignKey<ReferenceUnit>(material, nameof(ReferenceMaterial.UnitId));
        AssertRequiredRestrictedForeignKey<ReferenceLocation>(material, nameof(ReferenceMaterial.LocationId));
    }

    private static LimsDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<LimsDbContext>()
            .UseNpgsql("Host=localhost;Database=not_used;Username=not_used")
            .Options;
        return new LimsDbContext(options);
    }

    private static void AssertExcluded<TEntity>(LimsDbContext context, string tableName)
        where TEntity : class
    {
        var model = context.GetService<IDesignTimeModel>().Model;
        var entity = model.FindEntityType(typeof(TEntity));

        Assert.NotNull(entity);
        Assert.Equal(tableName, entity.GetTableName());
        Assert.True(entity.IsTableExcludedFromMigrations());
    }

    private static void AssertRequiredRestrictedForeignKey<TPrincipal>(IEntityType material, string propertyName)
    {
        var property = Assert.IsAssignableFrom<IProperty>(material.FindProperty(propertyName));
        var foreignKey = Assert.Single(material.GetForeignKeys(), candidate => candidate.Properties.Contains(property));

        Assert.False(property.IsNullable);
        Assert.Equal(typeof(TPrincipal), foreignKey.PrincipalEntityType.ClrType);
        Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
    }
}

using Lims.Domain.Authentication;
using Lims.Domain.Identity;
using Lims.Infrastructure.Persistence;
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
}

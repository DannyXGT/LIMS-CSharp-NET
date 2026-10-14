using Lims.Application.Permissions;
using Lims.Contracts.Errors;
using Lims.Contracts.Permissions;
using Lims.Domain.Identity;
using Lims.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Lims.Infrastructure.Tests.Persistence;

public sealed class PermissionRepositoryTests
{
    [Theory]
    [InlineData("Laboratorio Analítico")]
    [InlineData("Laboratorio Analitico")]
    public async Task SharedDepartmentSpellingSupportsListStateSaveHistoryAndEffectivePermissions(string departmentName)
    {
        await using var f = await Fixture.Create(departmentName);
        var users = (await f.Repository.UsersAsync(1, null, default)).Value!;
        Assert.True(users.DepartmentExists);
        var user = Assert.Single(users.Items);
        Assert.Equal(2, user.Id);
        Assert.Equal(departmentName, user.Department);
        var state = (await f.Repository.GetAsync(1, user.Id, default)).Value!;
        var saved = await f.Repository.SaveAsync(1, user.Id, new([PermissionCatalog.StockView], state.Version), default);
        Assert.True(saved.IsSuccess, saved.Error?.Message);
        Assert.Equal([PermissionCatalog.StockView], (await f.Repository.EffectiveAsync(user.Id, default))!.Permissions);
        var history = (await f.Repository.HistoryAsync(1, user.Id, 1, default)).Value!;
        Assert.Equal(PermissionCatalog.All.Count, history.Total);

        // Moving out of either recognized spelling must still revoke effective access.
        await f.Db.Users.Where(u => u.Id == user.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.DepartmentId, (int?)2));
        Assert.Empty((await f.Repository.EffectiveAsync(user.Id, default))!.Permissions);
        Assert.Equal(ErrorCodes.Forbidden, (await f.Repository.GetAsync(1, user.Id, default)).Error?.Code);
        Assert.Equal(ErrorCodes.Forbidden, (await f.Repository.SaveAsync(1, user.Id, new([], saved.Value!.Version), default)).Error?.Code);
        Assert.Equal(ErrorCodes.Forbidden, (await f.Repository.HistoryAsync(1, user.Id, 1, default)).Error?.Code);
    }

    [Theory]
    [InlineData("Laboratorio Químico")]
    [InlineData("Laboratorio Analitico Externo")]
    public async Task OtherDepartmentNamesDoNotExpandPermissionScope(string departmentName)
    {
        await using var f = await Fixture.Create(departmentName);
        var users = (await f.Repository.UsersAsync(1, null, default)).Value!;
        Assert.False(users.DepartmentExists);
        Assert.Empty(users.Items);
        Assert.Equal(ErrorCodes.Forbidden, (await f.Repository.GetAsync(1, 2, default)).Error?.Code);
        Assert.Equal(ErrorCodes.Forbidden, (await f.Repository.SaveAsync(1, 2, new([], Guid.Empty), default)).Error?.Code);
        Assert.Equal(ErrorCodes.Forbidden, (await f.Repository.HistoryAsync(1, 2, 1, default)).Error?.Code);
    }

    [Fact]
    public async Task ScopeUsesUserDepartmentAndActiveStatusAndNeverPhysicalLocation()
    {
        await using var f = await Fixture.Create();
        var users = (await f.Repository.UsersAsync(1, "ANA", default)).Value!;
        Assert.True(users.DepartmentExists); Assert.Equal(2, Assert.Single(users.Items).Id);
        foreach (var id in new[] { 3, 4, 5, 999 })
        {
            Assert.Equal(ErrorCodes.Forbidden, (await f.Repository.GetAsync(1, id, default)).Error?.Code);
            Assert.Equal(ErrorCodes.Forbidden, (await f.Repository.SaveAsync(1, id, new([], Guid.Empty), default)).Error?.Code);
            Assert.Equal(ErrorCodes.Forbidden, (await f.Repository.HistoryAsync(1, id, 1, default)).Error?.Code);
        }
        Assert.Equal(ErrorCodes.Forbidden, (await f.Repository.UsersAsync(2, null, default)).Error?.Code);
        Assert.Equal(ErrorCodes.Forbidden, (await f.Repository.SaveAsync(2, 2, new([], Guid.Empty), default)).Error?.Code);
        Assert.Empty(await f.Db.Set<UserPermissionAudit>().ToArrayAsync());
    }
    [Fact]
    public async Task GrantsRevocationsAuditActorAndOptimisticConcurrencyArePersistent()
    {
        await using var f = await Fixture.Create();
        var initial = (await f.Repository.GetAsync(1, 2, default)).Value!;
        Assert.Empty(initial.Permissions); Assert.False(initial.IsConfigured);
        var saved = (await f.Repository.SaveAsync(1, 2, new([PermissionCatalog.StockView, PermissionCatalog.StockCreate], initial.Version), default)).Value!;
        Assert.True(saved.IsConfigured); Assert.Equal(2, saved.Permissions.Count);
        Assert.Contains(PermissionCatalog.StockCreate, (await f.Repository.EffectiveAsync(2, default))!.Permissions);
        var revoked = (await f.Repository.SaveAsync(1, 2, new([PermissionCatalog.StockView], saved.Version), default)).Value!;
        Assert.DoesNotContain(PermissionCatalog.StockCreate, (await f.Repository.EffectiveAsync(2, default))!.Permissions);
        var assignment = await f.Db.Set<UserPermissionAssignment>().SingleAsync(a => a.UserId == 2 && a.Code == PermissionCatalog.StockCreate);
        Assert.False(assignment.IsGranted); Assert.Equal(1, assignment.ActorUserId);
        var history = (await f.Repository.HistoryAsync(1, 2, 1, default)).Value!;
        Assert.Equal(10, history.Total);
        Assert.Contains(history.Items, h => h.Action == "Revocado" && h.Before && !h.After && h.Actor == "Administrador existente" && h.User == "Ana López");
        Assert.Equal(ErrorCodes.Conflict, (await f.Repository.SaveAsync(1, 2, new([], saved.Version), default)).Error?.Code);
        Assert.Equal(revoked.Version, (await f.Repository.GetAsync(1, 2, default)).Value!.Version);
    }
    [Fact]
    public async Task InvalidAndDuplicateCodesAreRejectedAndDatabaseForeignKeysRemainEnforced()
    {
        await using var f = await Fixture.Create();
        foreach (var codes in new string[][] { [PermissionCatalog.StockView, PermissionCatalog.StockView], ["stock.delete"], ["Otorgar Stock"] })
            Assert.Equal(ErrorCodes.ValidationError, (await f.Repository.SaveAsync(1, 2, new(codes, Guid.Empty), default)).Error?.Code);
        Assert.Empty(await f.Db.Set<UserPermissionSet>().ToArrayAsync());
        await f.Repository.SaveAsync(1, 2, new([], Guid.Empty), default);
        f.Db.Add(new UserPermissionAssignment { UserId = 2, Code = "unknown", ActorUserId = 1, ChangedAt = DateTimeOffset.UtcNow });
        await Assert.ThrowsAsync<DbUpdateException>(() => f.Db.SaveChangesAsync());
    }
    [Fact]
    public async Task TransitionPreservesExistingAccessUntilConfiguredWithoutCopyingLegacyRows()
    {
        await using var f = await Fixture.Create();
        f.Options.EnforceIndividualPermissions = false;
        Assert.Contains(PermissionCatalog.StockView, (await f.Repository.EffectiveAsync(2, default))!.Permissions);
        Assert.Empty((await f.Repository.GetAsync(1, 2, default)).Value!.Permissions);
        await f.Repository.SaveAsync(1, 2, new([], Guid.Empty), default);
        Assert.Empty((await f.Repository.EffectiveAsync(2, default))!.Permissions);
        Assert.Single(await f.Db.UserPermissionGrants.Where(g => g.UserId == 2).ToArrayAsync());
        Assert.Equal(PermissionCatalog.All.Count, (await f.Repository.EffectiveAsync(1, default))!.Permissions.Count);
        Assert.Null(await f.Repository.EffectiveAsync(4, default));
    }
    [Fact]
    public async Task MissingDepartmentProducesExplicitEmptyScope()
    {
        await using var f = await Fixture.Create();
        await f.Db.Users.Where(u => u.DepartmentId == 1).ExecuteUpdateAsync(s => s.SetProperty(u => u.DepartmentId, (int?)null));
        await f.Db.Departments.Where(d => d.Id == 1).ExecuteDeleteAsync();
        var result = (await f.Repository.UsersAsync(1, null, default)).Value!;
        Assert.False(result.DepartmentExists); Assert.Empty(result.Items);
    }
    private sealed class Fixture(SqliteConnection connection, LimsDbContext db) : IAsyncDisposable
    {
        public LimsDbContext Db { get; } = db;
        public PermissionOptions Options { get; } = new() { EnforceIndividualPermissions = true };
        public PermissionRepository Repository => new(Db, Options, TimeProvider.System);
        public static async Task<Fixture> Create(string departmentName = PermissionCatalog.DepartmentName)
        {
            var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<LimsDbContext>().UseSqlite(connection).ReplaceService<IModelCustomizer, PermissionTestModel>().Options;
            var db = new LimsDbContext(options); await db.Database.EnsureCreatedAsync();
            var admin = new Role(1, "Administrador"); var analyst = new Role(2, "Analista");
            var laboratory = new Department(1, departmentName); var other = new Department(2, "Laboratorio Químico");
            db.Users.AddRange(new User(1, "Administrador existente", "admin@intertek.com", "hash", true, admin, other),
                new User(2, "Ana López", "ana@intertek.com", "hash", true, analyst, laboratory, [new(2, "reference_materials.view")]),
                new User(3, "Usuario externo", "externo@intertek.com", "hash", true, analyst, other),
                new User(4, "Inactivo", "inactive@intertek.com", "hash", false, analyst, laboratory),
                new User(5, "Sin departamento", "none@intertek.com", "hash", true, analyst, null));
            await db.SaveChangesAsync(); db.ChangeTracker.Clear(); return new(connection, db);
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await connection.DisposeAsync(); }
    }
    private sealed class PermissionTestModel(ModelCustomizerDependencies dependencies) : ModelCustomizer(dependencies)
    {
        public override void Customize(ModelBuilder modelBuilder, DbContext context)
        {
            base.Customize(modelBuilder, context);
            foreach (var sequence in modelBuilder.Model.GetSequences().ToArray()) modelBuilder.Model.RemoveSequence(sequence.Name, sequence.Schema);
            foreach (var type in modelBuilder.Model.GetEntityTypes().ToArray())
                if (type.ClrType.Namespace != typeof(User).Namespace) modelBuilder.Ignore(type.ClrType);
            foreach (var type in modelBuilder.Model.GetEntityTypes()) type.SetIsTableExcludedFromMigrations(false);
            foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(t => t.GetProperties()).Where(p => p.ClrType == typeof(DateTimeOffset)))
                property.SetValueConverter(new DateTimeOffsetToBinaryConverter());
        }
    }
}

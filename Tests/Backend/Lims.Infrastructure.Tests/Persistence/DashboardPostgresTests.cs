using Lims.Application.Dashboard;
using Lims.Application.Permissions;
using Lims.Application.ReferencePreparations;
using Lims.Contracts.Permissions;
using Lims.Domain.Identity;
using Lims.Domain.ReferenceMaterials;
using Lims.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace Lims.Infrastructure.Tests.Persistence;
public sealed class DashboardPostgresTests
{
    [StockPostgresFact]
    public async Task AggregatesEffectiveStatesRanksAllFutureDatesAndEnforcesCurrentPermissions()
    {
        await using var fixture = await StockPostgresTests.Fixture.CreateAsync(100);
        await using var db = fixture.Context();
        // This fixture is guarded to 127.0.0.1:55439/lims_stock_test by its factory.
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE roles (id integer PRIMARY KEY, nombre varchar(50));
            CREATE TABLE departamentos (id integer PRIMARY KEY, nombre varchar(100));
            INSERT INTO roles VALUES (1,'Administrador'),(2,'Analista'),(3,'Comprador');
            INSERT INTO departamentos VALUES (7,'Laboratorio Analitico'),(8,'Compras de prueba');
            ALTER TABLE usuarios ADD COLUMN id_rol integer, ADD COLUMN id_departamento integer,
                ADD COLUMN email varchar(150) DEFAULT 'fixture@example.invalid', ADD COLUMN password_hash text DEFAULT 'fixture',
                ADD COLUMN fecha_creacion timestamp DEFAULT CURRENT_TIMESTAMP;
            UPDATE usuarios SET id_rol=1,id_departamento=7 WHERE id=42;
            CREATE TABLE usuarios_permisos (id_usuario integer, permiso_key varchar(120), PRIMARY KEY(id_usuario,permiso_key));
            """);
        var now = DateTimeOffset.UtcNow; var today = DateOnly.FromDateTime(now.ToOffset(TimeSpan.FromHours(-6)).DateTime);
        var method = await db.ReferenceMethods.FirstAsync(); var unit = await db.ReferenceUnits.SingleAsync(u => u.Symbol == "mg"); var location = await db.ReferenceLocations.FirstAsync();
        ReferenceMaterial Material(string name, int days) => new(Guid.NewGuid(),name,null,null,method,100,"LOTE-TEST","Prueba",today.AddDays(-20),today.AddDays(days),100,unit,1,"Ambiente",location,42,now);
        var expired=Material("Expirado",-1); var depleted=Material("Agotado y vencido",-1);
        var exhausted=Material("Agotado vigente",20); exhausted.Consume(100,42,now,today);
        var archived=Material("Archivado",1); archived.Archive("Prueba",42,now);
        var near=Material("Vence hoy",0); near.Consume(80,42,now,today);
        db.ReferenceMaterials.AddRange(expired,depleted,exhausted,archived,near);
        for (var i=1;i<=10;i++) db.ReferenceMaterials.Add(Material($"Futuro {i}", i*100));
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE reference_materials SET available_quantity=0 WHERE id={depleted.Id}");
        var created = await new StockService(new StockRepository(db), TimeProvider.System).CreateAsync(fixture.Request(50),42,default);
        Assert.True(created.IsSuccess,created.Error?.Message);
        var permissions = new PermissionRepository(db,new() { EnforceIndividualPermissions=true },TimeProvider.System);
        var repository = new DashboardRepository(db,permissions,new() { PurchasingDepartmentName="Compras de prueba" },TimeProvider.System);
        var admin=(await repository.GetAsync(42,default))!;
        Assert.Equal("Administrator",admin.View); Assert.Equal(16,admin.Standards!.Registered);
        Assert.Equal(1,admin.Standards.Expired); Assert.Equal(2,admin.Standards.Distribution.Single(s=>s.Status=="Depleted").Count);
        Assert.Equal(6,admin.Expirations.Count); Assert.Equal(near.Id,admin.Expirations[0].Id); Assert.Equal(0,admin.Expirations[0].DaysRemaining);
        Assert.Contains(admin.Expirations,r=>r.Id==exhausted.Id && r.AvailabilityPercent==0);
        Assert.True(admin.Expirations[^1].DaysRemaining>60); Assert.DoesNotContain(admin.Expirations,r=>r.Id==archived.Id);
        Assert.Equal(20,admin.LowAvailability.Single(r=>r.Id==near.Id).AvailabilityPercent);
        Assert.Equal(1,admin.Stock!.Available); Assert.Contains(admin.Activity,a=>a.Kind=="Stock" && a.RecordId==created.Value!.Preparation.Id);
        Assert.Equal(admin.Standards.Registered,admin.Standards.Distribution.Sum(s=>s.Count));
        await db.Database.ExecuteSqlRawAsync("UPDATE usuarios SET id_rol=2 WHERE id=42");
        db.Add(new UserPermissionSet { UserId=42,Version=Guid.NewGuid() });
        db.Add(new UserPermissionAssignment { UserId=42,Code=PermissionCatalog.StockView,IsGranted=true,ActorUserId=42,ChangedAt=now }); await db.SaveChangesAsync();
        var limited=(await repository.GetAsync(42,default))!;
        Assert.Equal("Laboratory",limited.View); Assert.Null(limited.Standards); Assert.Null(limited.Intermediate); Assert.NotNull(limited.Stock);
        Assert.All(limited.Expirations,r=>Assert.Equal("Stock",r.Kind)); Assert.All(limited.Activity,a=>Assert.Equal("Stock",a.Kind));
        await db.Database.ExecuteSqlRawAsync("UPDATE user_permission_assignments SET is_granted=false WHERE user_id=42");
        var revoked=(await repository.GetAsync(42,default))!;
        Assert.Null(revoked.Stock); Assert.Empty(revoked.Activity); Assert.Empty(revoked.Expirations);
        await db.Database.ExecuteSqlRawAsync("UPDATE usuarios SET id_rol=3,id_departamento=8 WHERE id=42");
        var purchasing=(await repository.GetAsync(42,default))!;
        Assert.Equal("Purchasing",purchasing.View); Assert.Null(purchasing.Standards); Assert.Empty(purchasing.Activity);
    }
}

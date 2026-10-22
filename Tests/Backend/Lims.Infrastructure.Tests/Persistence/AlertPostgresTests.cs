using System.Net.Mail;
using Lims.Application.Permissions;
using Lims.Contracts.Alerts;
using Lims.Contracts.Permissions;
using Lims.Contracts.ReferenceMaterials;
using Lims.Contracts.ReferencePreparations;
using Lims.Domain.ReferenceMaterials;
using Lims.Domain.ReferencePreparations;
using Lims.Domain.Alerts;
using Lims.Infrastructure.Alerts;
using Lims.Infrastructure.Persistence;
using Lims.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;

namespace Lims.Infrastructure.Tests.Persistence;

public sealed class AlertPostgresTests
{
    [Fact]
    public void MigrationOnlyAddsAlertTablesAndPermissions()
    {
        var operations = new AddAlertsAndNotifications().UpOperations;
        Assert.All(operations, o => Assert.True(o is CreateTableOperation or CreateIndexOperation or InsertDataOperation));
        Assert.Equal(8, operations.OfType<CreateTableOperation>().Count());
        Assert.Contains(operations.OfType<CreateIndexOperation>(), o => o.IsUnique && o.Filter == "\"ResolvedAt\" IS NULL");
        Assert.Contains(operations.OfType<InsertDataOperation>(), o => o.Table == "permission_catalog");
    }
    [StockPostgresFact]
    public async Task AllSevenWorkspacesReusePhysicalBalancesAndCurvaAqsNeverTreatSentinelZeroAsDepletion()
    {
        await using var f = await Fixture.CreateAsync(); await using var db = f.Db();
        var source = await db.ReferenceMaterials.Include(m => m.Method).Include(m => m.Unit).Include(m => m.Location).SingleAsync();
        var today = f.Clock.Today; var at = f.Clock.Now; var fingerprint = new string('a', 64);
        var stock = new ReferencePreparation(Guid.NewGuid(), "STOCK-ALERT-TEST", "Stock preparado de prueba", source,
            100, 100, "mg/L", 100, "mL", 1, 1, "Prueba aislada", today, 42, null, at, fingerprint, today.AddDays(7), null);
        stock.ConsumeVolume(92, 42, at, today);
        var intermediate = ReferencePreparation.Intermediate(Guid.NewGuid(), "INTER-ALERT-TEST", "Intermedia preparada de prueba", source.MethodId,
            source.Method.Name, 100, "mL", "[]", today, today.AddDays(7), 42, null, at, fingerprint, null);
        intermediate.ConsumeVolume(80, 42, at, today);
        var curve = ReferencePreparation.SolutionGroup(Guid.NewGuid(), "Curva", "CURVE-ALERT-TEST", "Curva de prueba", source.MethodId, source.Method.Name, 100,
            "{}", today, today.AddDays(7), 42, null, null, at, fingerprint);
        var aqs = ReferencePreparation.SolutionGroup(Guid.NewGuid(), "AQS", "AQS-ALERT-TEST", "AQS de prueba", source.MethodId, source.Method.Name, 100,
            "{}", today, today.AddDays(7), 42, null, null, at, fingerprint);
        db.AddRange(stock, intermediate, curve, aqs); await db.SaveChangesAsync();
        var certificate = new CommercialCertificate("CERT-TEST", "Acetonitrilo", null, null, null, null, today.AddDays(-1), null,
            [new(Guid.NewGuid(), "Compuesto certificado buscable", null, 1000, "µg/mL")], Solvents: [new(4, 100)], CompositionBasis: StockSolventComposition.Basis);
        var input = new CreateReferenceMaterialRequest("Frasco comercial de prueba", null, "COMM-TEST", source.MethodId, null, "COMM-LOT", "Marca ficticia",
            today.AddDays(-2), today.AddDays(7), 10, (await db.ReferenceUnits.SingleAsync(u => u.Symbol == "mL")).Id, 1, "Ambiente", source.LocationId, MaterialPresentations.Solution, certificate);
        var commercial = await new CommercialPreparationService(db, f.Clock).RegisterAsync("Stock", new(Guid.NewGuid(), null, null, input), 42, default);
        Assert.True(commercial.IsSuccess, commercial.Error?.Message); var bottleId = commercial.Value!.Material.Id;
        db.Add(new ReferenceCommercialRole(bottleId, "Intermedia", "COMM-INTER-TEST", Guid.NewGuid(), fingerprint, 42, at)); await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE reference_materials SET available_quantity={1m} WHERE id={bottleId}");
        await f.Engine(db).EvaluateAsync(default);
        var alerts = await db.Set<MaterialAlert>().AsNoTracking().ToArrayAsync();
        Assert.Equal("Critical", Assert.Single(alerts, a => a.SourceId == stock.Id && a.Category == "Availability").Condition);
        Assert.Equal("Low", Assert.Single(alerts, a => a.SourceId == intermediate.Id && a.Category == "Availability").Condition);
        Assert.Equal("Expiration", Assert.Single(alerts, a => a.SourceId == curve.Id).Category);
        Assert.Equal("Expiration", Assert.Single(alerts, a => a.SourceId == aqs.Id).Category);
        var bottle = Assert.Single(alerts, a => a.SourceId == bottleId && a.Category == "Availability");
        Assert.Equal(10, bottle.AvailabilityPercent); Assert.Equal((int)(AlertModule.Stock | AlertModule.Intermedia), bottle.Modules);
        Assert.DoesNotContain(alerts, a => a.SourceId == bottleId && a.Modules == (int)AlertModule.Standards);
        Assert.Single((await f.Service(db).ListAsync(42, new(Search: "Compuesto certificado buscable", Category: "Availability"), default)).Value!.Items);
    }
    [StockPostgresFact]
    public async Task EnginePersistsDeduplicatesEscalatesAndResolvesWithoutLosingHistory()
    {
        await using var f = await Fixture.CreateAsync(); await using var db = f.Db();
        var engine = f.Engine(db);
        await engine.EvaluateAsync(default); await engine.EvaluateAsync(default);
        var initial = Assert.Single(await db.Set<MaterialAlert>().ToArrayAsync()); Assert.Equal("Low", initial.Condition);
        var warningNotification = await db.Set<UserNotification>().Where(n => n.UserId == 84).Select(n => n.Id).SingleAsync();
        Assert.Equal(2, await db.Set<UserNotification>().CountAsync());
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE reference_materials SET available_quantity={8m} WHERE id={f.MaterialId}");
        await engine.EvaluateAsync(default);
        var escalated = Assert.Single(await db.Set<MaterialAlert>().ToArrayAsync()); Assert.Equal(initial.Id, escalated.Id); Assert.Equal("Critical", escalated.Condition);
        await f.Service(db).PreferencesAsync(84, new(true, true, false), default);
        Assert.False((await f.Service(db).ClaimWindowsAsync(84, warningNotification, default)).Value);
        var currentNotification = await db.Set<UserNotification>().Where(n => n.UserId == 84 && n.Priority == (int)AlertPriority.Critical).Select(n => n.Id).SingleAsync();
        Assert.True((await f.Service(db).ClaimWindowsAsync(84, currentNotification, default)).Value);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE reference_materials SET available_quantity={80m} WHERE id={f.MaterialId}");
        await engine.EvaluateAsync(default);
        await using var fresh = f.Db(); var resolved = Assert.Single(await fresh.Set<MaterialAlert>().ToArrayAsync());
        Assert.NotNull(resolved.ResolvedAt); Assert.Equal(3, await fresh.Set<AlertHistory>().CountAsync());
        await fresh.Database.ExecuteSqlInterpolatedAsync($"UPDATE reference_materials SET available_quantity={20m} WHERE id={f.MaterialId}");
        await f.Engine(fresh).EvaluateAsync(default);
        Assert.Equal(2, await fresh.Set<MaterialAlert>().CountAsync()); Assert.Single(await fresh.Set<MaterialAlert>().Where(a => a.ResolvedAt == null).ToArrayAsync());
    }
    [StockPostgresFact]
    public async Task ReadingIsPerUserAndDoesNotResolveOrAttendCondition()
    {
        await using var f = await Fixture.CreateAsync(); await using var db = f.Db(); await f.Engine(db).EvaluateAsync(default);
        var service = f.Service(db); var page = (await service.NotificationsAsync(84, default)).Value!;
        Assert.Equal(1, page.Unread); Assert.True((await service.ReadAsync(84, page.Items[0].Id, default)).Value);
        Assert.Equal(0, (await service.NotificationsAsync(84, default)).Value!.Unread);
        Assert.Equal(1, (await service.NotificationsAsync(42, default)).Value!.Unread);
        var list = (await service.ListAsync(84, new(), default)).Value!; Assert.Equal(1, list.Summary.Pending); Assert.Equal(1, list.Summary.Active);
        Assert.Equal(page.Items[0].Id, list.Items[0].NotificationId); Assert.NotNull(list.Items[0].ReadAt);
        var other = (await service.DetailAsync(42, list.Items[0].Id, default)).Value!.Alert;
        Assert.NotEqual(list.Items[0].NotificationId, other.NotificationId); Assert.Null(other.ReadAt);
        Assert.False((await service.AcknowledgeAsync(84, list.Items[0].Id, null, default)).IsSuccess);
        Assert.True((await service.AcknowledgeAsync(42, list.Items[0].Id, null, default)).IsSuccess);
        Assert.Equal(0, (await service.ListAsync(42, new(), default)).Value!.Summary.Pending);
        await f.Engine(db).EvaluateAsync(default); Assert.Equal(2, await db.Set<UserNotification>().CountAsync());
        Assert.Null((await service.DetailAsync(42, list.Items[0].Id, default)).Value!.Alert.ResolvedAt);
    }
    [StockPostgresFact]
    public async Task DepartmentAndModulePermissionsAreCheckedForReadsMutationsAndWindowsClaims()
    {
        await using var f = await Fixture.CreateAsync(); await using var db = f.Db(); await f.Engine(db).EvaluateAsync(default);
        var service = f.Service(db); var id = await db.Set<MaterialAlert>().Select(a => a.Id).SingleAsync();
        Assert.Empty((await service.ListAsync(85, new(), default)).Value!.Items);
        Assert.False((await service.DetailAsync(85, id, default)).IsSuccess);
        var n = (await service.NotificationsAsync(84, default)).Value!.Items.Single();
        Assert.False((await service.ReadAsync(85, n.Id, default)).Value);
        await service.PreferencesAsync(84, new(true, true, false), default);
        Assert.True((await service.ClaimWindowsAsync(84, n.Id, default)).Value);
        Assert.False((await service.ClaimWindowsAsync(84, n.Id, default)).Value);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM usuarios_permisos WHERE id_usuario=84 AND permiso_key='reference_materials.view'");
        Assert.Empty((await service.NotificationsAsync(84, default)).Value!.Items);
        Assert.False((await service.DetailAsync(84, id, default)).IsSuccess);
        Assert.False((await service.AdministrationAsync(84, default)).IsSuccess);
    }
    [StockPostgresFact]
    public async Task FiltersPaginationAndDashboardUsePersistedAuthorizedConditions()
    {
        await using var f = await Fixture.CreateAsync(); await using var db = f.Db();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE reference_materials SET expiration_date={f.Clock.Today.AddDays(7)} WHERE id={f.MaterialId}");
        await f.Engine(db).EvaluateAsync(default); var service = f.Service(db);
        var first = (await service.ListAsync(84, new(PageSize: 1), default)).Value!;
        var second = (await service.ListAsync(84, new(Page: 2, PageSize: 1), default)).Value!;
        Assert.Equal(2, first.Total); Assert.NotEqual(first.Items[0].Id, second.Items[0].Id);
        Assert.NotNull(first.Summary.Metrics); Assert.Equal(7, first.Summary.Metrics.NearestDays);
        Assert.Equal(f.MaterialId, (await service.DetailAsync(84, first.Summary.Metrics.NearestAlertId!.Value, default)).Value!.Alert.SourceId);
        Assert.NotEmpty(first.Summary.Metrics.NearestMaterial!);
        Assert.Single((await service.ListAsync(84, new(Search: "LOT-001", Category: "Upcoming", Module: AlertModule.Standards, From: f.Clock.Today, Until: f.Clock.Today), default)).Value!.Items);
        Assert.Empty((await service.ListAsync(84, new(Priority: AlertPriority.Critical), default)).Value!.Items);
        Assert.False((await service.ListAsync(84, new(PageSize: 101), default)).IsSuccess);
        var snapshot = await new AlertDashboard(db, f.Access(db), f.Clock).GetAsync(84, default);
        Assert.Equal(first.Summary.Active, snapshot!.Summary.Active); Assert.Equal(first.Items[0].SourceId, snapshot.LowAvailability.Single().Id);
    }
    [StockPostgresFact]
    public async Task MissingOwnershipDoesNotInventADepartmentOrRecipients()
    {
        await using var f = await Fixture.CreateAsync(); await using var db = f.Db();
        var service = f.Service(db); var settings = (await service.AdministrationAsync(42, default)).Value!.Settings;
        Assert.True((await service.ConfigureAsync(42, settings with { Scopes = [], Recipients = [] }, default)).IsSuccess);
        await f.Engine(db).EvaluateAsync(default); Assert.Empty(await db.Set<MaterialAlert>().ToArrayAsync());
        Assert.Empty(await db.Set<UserNotification>().ToArrayAsync());
    }
    [StockPostgresFact]
    public async Task SettingsUseConcurrencyAndValidateRealDepartmentAndRecipientPermissions()
    {
        await using var f = await Fixture.CreateAsync(); await using var db = f.Db(); var s = f.Service(db);
        var settings = (await s.AdministrationAsync(42, default)).Value!.Settings;
        Assert.False((await s.ConfigureAsync(42, settings with { Scopes = [new(AlertModule.Stock, 999999)] }, default)).IsSuccess);
        Assert.False((await s.ConfigureAsync(42, settings with { Recipients = [new(85, 7)] }, default)).IsSuccess);
        Assert.True((await s.ConfigureAsync(42, settings with { LowPercent = 30 }, default)).IsSuccess);
        Assert.False((await s.ConfigureAsync(42, settings with { LowPercent = 40 }, default)).IsSuccess);
        Assert.True(await db.Set<AlertConfigurationAudit>().CountAsync() >= 2);
    }
    [StockPostgresFact]
    public async Task MailOutboxPersistsRetriesAndOnlyMarksSentAfterTransportConfirms()
    {
        await using var f = await Fixture.CreateAsync(8); await using var db = f.Db(); await f.Engine(db).EvaluateAsync(default);
        Assert.Equal(2, await db.Set<EmailDelivery>().CountAsync()); // immediate + one daily for the configured analyst
        var transport = new TestTransport { Reject = true };
        var processor = new AlertDeliveryProcessor(db, f.Access(db), f.Mail, transport, f.Clock);
        Assert.Equal(0, await processor.ProcessAsync(default));
        Assert.All(await db.Set<EmailDelivery>().AsNoTracking().ToArrayAsync(), d => { Assert.Equal("Retry", d.State); Assert.Null(d.SentAt); });
        f.Clock.Now = f.Clock.Now.AddMinutes(10); transport.Reject = false;
        Assert.Equal(2, await processor.ProcessAsync(default)); Assert.Equal(0, await processor.ProcessAsync(default));
        await f.Engine(db).EvaluateAsync(default); Assert.Equal(2, await db.Set<EmailDelivery>().CountAsync());
        Assert.All(await db.Set<EmailDelivery>().AsNoTracking().ToArrayAsync(), d => { Assert.Equal("Sent", d.State); Assert.NotNull(d.SentAt); Assert.Equal(2, d.Attempts); });
        Assert.Equal(4, await db.Set<EmailAttempt>().CountAsync());
    }
    [StockPostgresFact]
    public async Task RevokedRecipientsCannotReceivePreviouslyQueuedMail()
    {
        await using var f = await Fixture.CreateAsync(8); await using var db = f.Db(); await f.Engine(db).EvaluateAsync(default);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM usuarios_permisos WHERE id_usuario=84 AND permiso_key='alerts.view'");
        var transport = new TestTransport(); Assert.Equal(0, await new AlertDeliveryProcessor(db, f.Access(db), f.Mail, transport, f.Clock).ProcessAsync(default));
        Assert.Equal(0, transport.Accepted); Assert.All(await db.Set<EmailDelivery>().ToArrayAsync(), d => Assert.Equal("Cancelled", d.State));
    }
    [StockPostgresFact]
    public async Task SchedulerLockSurvivesIisIndependentlyAndAmbiguousTransportIsNotAutomaticallyResent()
    {
        await using var f = await Fixture.CreateAsync(8); await using var db = f.Db(); var transport = new TestTransport { Ambiguous = true };
        var job = new AlertJob(db, f.Engine(db), new AlertDeliveryProcessor(db, f.Access(db), f.Mail, transport, f.Clock));
        await using (var held = new NpgsqlConnection(db.Database.GetConnectionString()))
        {
            await held.OpenAsync(); await using var cmd = new NpgsqlCommand("SELECT pg_advisory_lock(@key)", held); cmd.Parameters.AddWithValue("key", AlertData.JobLock); await cmd.ExecuteNonQueryAsync();
            Assert.False((await job.RunAsync(default)).Acquired);
        }
        Assert.True((await job.RunAsync(default)).Acquired); Assert.Equal(0, transport.Accepted);
        Assert.All(await db.Set<EmailDelivery>().AsNoTracking().ToArrayAsync(), d => Assert.Equal("Uncertain", d.State));
        transport.Ambiguous = false; await job.RunAsync(default); Assert.Equal(0, transport.Accepted);
    }
    private sealed class TestTransport : IAlertEmailTransport
    {
        public bool Reject { get; set; } public bool Ambiguous { get; set; } public int Accepted { get; private set; }
        public Task SendAsync(Guid id, string recipient, string subject, string html, CancellationToken ct)
        {
            if (Reject) throw new SmtpException(SmtpStatusCode.MailboxBusy);
            if (Ambiguous) throw new SmtpException(SmtpStatusCode.GeneralFailure);
            Assert.Contains("LIMS", html); Accepted++; return Task.CompletedTask;
        }
    }
    internal sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 21, 16, 0, 0, TimeSpan.Zero);
        public DateOnly Today => DateOnly.FromDateTime(Now.ToOffset(TimeSpan.FromHours(-6)).DateTime);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    internal sealed class Fixture(StockPostgresTests.Fixture stock) : IAsyncDisposable
    {
        public TestClock Clock { get; } = new();
        public Guid MaterialId => stock.Source.Id;
        public AlertMailOptions Mail { get; } = new() { Enabled = true, Host = "smtp.example.invalid", Sender = "lims@example.invalid", Username = "test-only", Password = "test-only" };
        public LimsDbContext Db() => stock.Context();
        public AlertAccess Access(LimsDbContext db) => new(db, new PermissionRepository(db, new PermissionOptions(), Clock));
        public AlertService Service(LimsDbContext db) => new(db, Access(db), Mail, Clock);
        public AlertEngine Engine(LimsDbContext db) => new(db, new AlertSourceReader(db), Access(db), Clock);
        public static async Task<Fixture> CreateAsync(decimal balance = 20)
        {
            var stock = await StockPostgresTests.Fixture.CreateAsync(100); var fixture = new Fixture(stock);
            await using var db = stock.Context();
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE roles (id integer PRIMARY KEY, nombre varchar(50));
                CREATE TABLE departamentos (id integer PRIMARY KEY, nombre varchar(100));
                INSERT INTO roles VALUES (1,'Administrador'),(2,'Analista');
                INSERT INTO departamentos VALUES (7,'Laboratorio Analitico'),(8,'Otro departamento de prueba');
                ALTER TABLE usuarios ADD COLUMN id_rol integer, ADD COLUMN id_departamento integer,
                    ADD COLUMN email varchar(150) DEFAULT 'fixture@example.invalid', ADD COLUMN password_hash text DEFAULT 'fixture',
                    ADD COLUMN fecha_creacion timestamp DEFAULT CURRENT_TIMESTAMP;
                UPDATE usuarios SET id_rol=1,id_departamento=7,email='admin@example.invalid' WHERE id=42;
                UPDATE usuarios SET id_rol=2,id_departamento=7,activo=true,email='analyst@example.invalid' WHERE id=84;
                INSERT INTO usuarios (id,nombre,activo,id_rol,id_departamento,email) VALUES (85,'Otro usuario ficticio',true,2,8,'other@example.invalid');
                CREATE TABLE usuarios_permisos (id_usuario integer, permiso_key varchar(120), PRIMARY KEY(id_usuario,permiso_key));
                INSERT INTO usuarios_permisos VALUES (84,'alerts.view'),(84,'reference_materials.view'),(85,'alerts.view'),(85,'reference_materials.view');
                """);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE reference_materials SET available_quantity={balance} WHERE id={stock.Source.Id}");
            var saved = await fixture.Service(db).ConfigureAsync(42, AlertSettings.Default with { Scopes = [new(AlertModule.Standards, 7), new(AlertModule.Stock, 7), new(AlertModule.Intermedia, 7), new(AlertModule.Curva, 7), new(AlertModule.AQS, 7)], Recipients = [new(84, 7)], EmailEnabled = true, DailyHour = 0 }, default);
            Assert.True(saved.IsSuccess, saved.Error?.Message); return fixture;
        }
        public ValueTask DisposeAsync() => stock.DisposeAsync();
    }
}

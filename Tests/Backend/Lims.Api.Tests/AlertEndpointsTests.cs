using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Lims.Application.Alerts;
using Lims.Application.Common;
using Lims.Contracts.Alerts;
using Lims.Contracts.Errors;
using Lims.Contracts.Permissions;
using Lims.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace Lims.Api.Tests;

public sealed class AlertEndpointsTests
{
    [Fact]
    public async Task EndpointsValidateLivePermissionsBindFiltersAndIgnoreInjectedActor()
    {
        await using var factory = new AuthenticationApiFactory(); var fake = new Fake();
        await using var app = factory.WithWebHostBuilder(b => b.ConfigureServices(s => { s.RemoveAll<IAlertService>(); s.AddSingleton<IAlertService>(fake); }));
        using var client = app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/alerts")).StatusCode);
        var claims = new[] { new Claim(JwtRegisteredClaimNames.Sub, AuthenticationApiFactory.UserId.ToString(CultureInfo.InvariantCulture)), new Claim(JwtAccessTokenService.SessionIdClaim, AuthenticationApiFactory.SessionId.ToString("D")), new Claim(ClaimTypes.Role, "Administrador") };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken("Lims.Api.Tests", "Lims.Api.Tests.Client", claims, expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: new(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(AuthenticationApiFactory.SigningKey)), SecurityAlgorithms.HmacSha256))));
        factory.SetIdentity("Analista", PermissionCatalog.AlertsView);
        var list = await client.GetAsync("/api/alerts?Page=2&PageSize=10&Priority=Critical&Module=Stock&From=2026-10-01&State=Pending&Search=LOTE&actor=999");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode); Assert.Equal(42, fake.Actor); Assert.Equal(2, fake.Filter!.Page); Assert.Equal(AlertPriority.Critical, fake.Filter.Priority); Assert.Equal(AlertModule.Stock, fake.Filter.Module); Assert.Equal(new DateOnly(2026, 10, 1), fake.Filter.From);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/alerts/administration")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/alerts/deliveries")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/alerts/{Guid.NewGuid()}/acknowledge", new AlertAction(Guid.Empty, null))).StatusCode);
        factory.SetIdentity("Analista"); Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/alerts")).StatusCode);
        factory.PermissionRepository.Current = null; Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/alerts/notifications")).StatusCode);
    }
    private sealed class Fake : IAlertService
    {
        public int Actor { get; private set; } public AlertFilter? Filter { get; private set; }
        private static Task<OperationResult<T>> Denied<T>() => Task.FromResult(OperationResult.Failure<T>(new(ErrorCodes.Forbidden, "Denied in fixture")));
        public Task<OperationResult<AlertPage>> ListAsync(int actor, AlertFilter filter, CancellationToken ct) { Actor = actor; Filter = filter; return Task.FromResult(OperationResult.Success(new AlertPage([], filter.Page, filter.PageSize, 0, new(0, 0, 0, null, 0, 0, 0, null, DateTimeOffset.UtcNow)))); }
        public Task<OperationResult<AlertDetail>> DetailAsync(int actor, Guid id, CancellationToken ct) => Denied<AlertDetail>();
        public Task<OperationResult<AlertDetail>> AcknowledgeAsync(int actor, Guid id, string? note, CancellationToken ct) => Denied<AlertDetail>();
        public Task<OperationResult<NotificationPage>> NotificationsAsync(int actor, CancellationToken ct) => Denied<NotificationPage>();
        public Task<OperationResult<bool>> ReadAsync(int actor, Guid? id, CancellationToken ct) => Denied<bool>();
        public Task<OperationResult<bool>> ClaimWindowsAsync(int actor, Guid id, CancellationToken ct) => Denied<bool>();
        public Task<OperationResult<NotificationPreferences>> PreferencesAsync(int actor, NotificationPreferences preferences, CancellationToken ct) => Denied<NotificationPreferences>();
        public Task<OperationResult<AlertAdministration>> AdministrationAsync(int actor, CancellationToken ct) => Denied<AlertAdministration>();
        public Task<OperationResult<AlertAdministration>> ConfigureAsync(int actor, AlertSettings settings, CancellationToken ct) => Denied<AlertAdministration>();
        public Task<OperationResult<DeliveryPage>> DeliveriesAsync(int actor, int page, CancellationToken ct) => Denied<DeliveryPage>();
        public Task<OperationResult<DeliveryItem>> TestEmailAsync(int actor, Guid id, CancellationToken ct) => Denied<DeliveryItem>();
    }
}

using Lims.Api.ReferenceMaterials;
using Lims.Application.Alerts;
using Lims.Contracts.Alerts;
using Lims.Contracts.Permissions;

namespace Lims.Api.Alerts;

internal static class AlertEndpoints
{
    public static IEndpointRouteBuilder MapAlertEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/alerts").WithTags("Alertas").RequireAuthorization(PermissionCatalog.AlertsView);
        group.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            return await next(context).ConfigureAwait(false);
        });
        group.MapPost("/evaluate", async (IAlertService s, HttpContext c, CancellationToken ct) =>
            ReferenceMaterialEndpoints.ToResult(c, await s.EvaluateAsync(Actor(c), ct).ConfigureAwait(false))).RequireAuthorization(PermissionCatalog.AlertsManage);
        group.MapGet("/evaluations", async (IAlertService s, HttpContext c, CancellationToken ct) =>
            ReferenceMaterialEndpoints.ToResult(c, await s.EvaluationsAsync(Actor(c), ct).ConfigureAwait(false))).RequireAuthorization(PermissionCatalog.AlertsManage);
        group.MapGet("", async ([AsParameters] AlertFilter filter, IAlertService service, HttpContext c, CancellationToken ct) =>
            ReferenceMaterialEndpoints.ToResult(c, await service.ListAsync(Actor(c), filter, ct).ConfigureAwait(false)));
        group.MapGet("/{id:guid}", async (Guid id, IAlertService s, HttpContext c, CancellationToken ct) =>
            ReferenceMaterialEndpoints.ToResult(c, await s.DetailAsync(Actor(c), id, ct).ConfigureAwait(false)));
        group.MapPost("/{id:guid}/acknowledge", async (Guid id, AlertAction request, IAlertService s, HttpContext c, CancellationToken ct) =>
            ReferenceMaterialEndpoints.ToResult(c, await s.AcknowledgeAsync(Actor(c), id, request.Note, ct).ConfigureAwait(false))).RequireAuthorization(PermissionCatalog.AlertsManage);
        group.MapGet("/notifications", async (IAlertService s, HttpContext c, CancellationToken ct) =>
            ReferenceMaterialEndpoints.ToResult(c, await s.NotificationsAsync(Actor(c), ct).ConfigureAwait(false)));
        group.MapPost("/notifications/{id:guid}/read", async (Guid id, IAlertService s, HttpContext c, CancellationToken ct) =>
            ReferenceMaterialEndpoints.ToResult(c, await s.ReadAsync(Actor(c), id, ct).ConfigureAwait(false)));
        group.MapPost("/notifications/read-all", async (IAlertService s, HttpContext c, CancellationToken ct) =>
            ReferenceMaterialEndpoints.ToResult(c, await s.ReadAsync(Actor(c), null, ct).ConfigureAwait(false)));
        group.MapPost("/notifications/{id:guid}/claim-windows", async (Guid id, IAlertService s, HttpContext c, CancellationToken ct) =>
            ReferenceMaterialEndpoints.ToResult(c, await s.ClaimWindowsAsync(Actor(c), id, ct).ConfigureAwait(false)));
        group.MapPut("/preferences", async (NotificationPreferences p, IAlertService s, HttpContext c, CancellationToken ct) =>
            ReferenceMaterialEndpoints.ToResult(c, await s.PreferencesAsync(Actor(c), p, ct).ConfigureAwait(false)));
        group.MapGet("/administration", async (IAlertService s, HttpContext c, CancellationToken ct) =>
            ReferenceMaterialEndpoints.ToResult(c, await s.AdministrationAsync(Actor(c), ct).ConfigureAwait(false))).RequireAuthorization(PermissionCatalog.NotificationsConfigure);
        group.MapPut("/administration", async (AlertSettings p, IAlertService s, HttpContext c, CancellationToken ct) =>
            ReferenceMaterialEndpoints.ToResult(c, await s.ConfigureAsync(Actor(c), p, ct).ConfigureAwait(false))).RequireAuthorization(PermissionCatalog.NotificationsConfigure);
        group.MapGet("/deliveries", async (int? page, IAlertService s, HttpContext c, CancellationToken ct) =>
            ReferenceMaterialEndpoints.ToResult(c, await s.DeliveriesAsync(Actor(c), page ?? 1, ct).ConfigureAwait(false))).RequireAuthorization(PermissionCatalog.DeliveryHistory);
        group.MapPost("/test-email", async (MailTestRequest p, IAlertService s, HttpContext c, CancellationToken ct) =>
            ReferenceMaterialEndpoints.ToResult(c, await s.TestEmailAsync(Actor(c), p.RequestId, ct).ConfigureAwait(false))).RequireAuthorization(PermissionCatalog.NotificationsConfigure);
        return endpoints;
    }
    private static int Actor(HttpContext c) => ReferenceMaterialEndpoints.TryGetActorUserId(c, out var id) ? id : 0;
}

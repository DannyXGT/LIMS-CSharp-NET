using Lims.Api.Authentication;
using Lims.Api.ReferenceMaterials;
using Lims.Application.Permissions;
using Lims.Contracts.Permissions;

namespace Lims.Api.Permissions;

internal static class PermissionEndpoints
{
    public static void MapPermissionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/api/administration/permissions").WithTags("Permissions").RequireAuthorization(LimsPolicies.RequireAdministrator);
        admin.MapGet("/catalog", () => Results.Ok(PermissionCatalog.All));
        admin.MapGet("/users", async (string? search, IPermissionRepository repository, HttpContext context, CancellationToken ct) =>
            ReferenceMaterialEndpoints.ToResult(context, await repository.UsersAsync(Actor(context), search, ct).ConfigureAwait(false)));
        admin.MapGet("/users/{id:int}", async (int id, IPermissionRepository repository, HttpContext context, CancellationToken ct) =>
            ReferenceMaterialEndpoints.ToResult(context, await repository.GetAsync(Actor(context), id, ct).ConfigureAwait(false)));
        admin.MapPut("/users/{id:int}", async (int id, SavePermissionsRequest request, IPermissionRepository repository, HttpContext context, CancellationToken ct) =>
            ReferenceMaterialEndpoints.ToResult(context, await repository.SaveAsync(Actor(context), id, request, ct).ConfigureAwait(false)));
        admin.MapGet("/users/{id:int}/history", async (int id, int? page, IPermissionRepository repository, HttpContext context, CancellationToken ct) =>
            ReferenceMaterialEndpoints.ToResult(context, await repository.HistoryAsync(Actor(context), id, page ?? 1, ct).ConfigureAwait(false)));
        endpoints.MapGet("/api/auth/permissions", async (IPermissionRepository repository, HttpContext context, CancellationToken ct) =>
        {
            var state = await repository.EffectiveAsync(Actor(context), ct).ConfigureAwait(false);
            return state is null ? ReferenceMaterialEndpoints.InvalidSession(context) : Results.Ok(state);
        }).RequireAuthorization();
    }
    private static int Actor(HttpContext context) => ReferenceMaterialEndpoints.TryGetActorUserId(context, out var actor) ? actor : 0;
}

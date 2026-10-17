using Lims.Application.ReferencePreparations;
using Lims.Contracts.ReferencePreparations;

namespace Lims.Api.ReferenceMaterials;

internal static class CommercialPreparationEndpoints
{
    public static void MapCommercial(this RouteGroupBuilder group, string kind, string viewPermission, string createPermission)
    {
        group.MapGet("/commercial/options", async (string? search, ICommercialPreparationService service, CancellationToken ct) =>
            Results.Ok(await service.OptionsAsync(kind, search, ct).ConfigureAwait(false))).RequireAuthorization(createPermission);
        group.MapGet("/commercial/{id:guid}", async (Guid id, ICommercialPreparationService service, HttpContext context, CancellationToken ct) =>
            ReferenceMaterialEndpoints.ToResult(context, await service.GetAsync(kind, id, ct).ConfigureAwait(false))).RequireAuthorization(viewPermission);
        group.MapPost("/commercial", async (RegisterCommercialPreparationRequest request, ICommercialPreparationService service, HttpContext context, CancellationToken ct) =>
        {
            if (!ReferenceMaterialEndpoints.TryGetActorUserId(context, out var actor)) return ReferenceMaterialEndpoints.InvalidSession(context);
            return ReferenceMaterialEndpoints.ToResult(context, await service.RegisterAsync(kind, request, actor, ct).ConfigureAwait(false));
        }).RequireAuthorization(createPermission);
    }
}

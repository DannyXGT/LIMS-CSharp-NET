using Lims.Application.ReferencePreparations;
using Lims.Contracts.Permissions;
using Lims.Contracts.ReferencePreparations;

namespace Lims.Api.ReferenceMaterials;

internal static class SolutionGroupEndpoints
{
    public static IEndpointRouteBuilder MapSolutionGroupEndpoints(this IEndpointRouteBuilder endpoints)
    {
        Map(endpoints, "curve", "Curva", PermissionCatalog.CurveView, PermissionCatalog.CurveCreate);
        Map(endpoints, "aqs", "AQS", PermissionCatalog.AqsView, PermissionCatalog.AqsCreate);
        return endpoints;
    }
    private static void Map(IEndpointRouteBuilder endpoints, string route, string kind, string view, string create)
    {
        var group = endpoints.MapGroup("/api/reference-materials/preparations/" + route).WithTags(kind);
        group.MapGet("/options", async (SolutionGroupService service, CancellationToken ct) => Results.Ok(await service.OptionsAsync(ct).ConfigureAwait(false))).RequireAuthorization(create);
        group.MapPost("/preview", async (SolutionCalculationRequest request, SolutionGroupService service, HttpContext context, CancellationToken ct) =>
            ReferenceMaterialEndpoints.ToResult(context, await service.PreviewAsync(request, ct).ConfigureAwait(false))).RequireAuthorization(create);
        group.MapGet("", async (string? search, string? status, string? method, int? page, int? pageSize, SolutionGroupService service, HttpContext context, CancellationToken ct) =>
            ReferenceMaterialEndpoints.ToResult(context, await service.ListAsync(kind, search, page ?? 1, pageSize ?? 25, ct, new(status, Method: method)).ConfigureAwait(false))).RequireAuthorization(view);
        group.MapGet("/{id:guid}", async (Guid id, SolutionGroupService service, HttpContext context, CancellationToken ct) =>
            ReferenceMaterialEndpoints.ToResult(context, await service.GetAsync(kind, id, ct).ConfigureAwait(false))).RequireAuthorization(view);
        group.MapPost("", async (CreateSolutionGroupRequest request, SolutionGroupService service, HttpContext context, CancellationToken ct) =>
        {
            if (!ReferenceMaterialEndpoints.TryGetActorUserId(context, out var actor) || actor <= 0) return ReferenceMaterialEndpoints.InvalidSession(context);
            var result = await service.CreateAsync(kind, request, actor, ct).ConfigureAwait(false);
            return result.IsSuccess ? Results.Created($"/api/reference-materials/preparations/{route}/{result.Value!.Preparation.Id:D}", result.Value) : ReferenceMaterialEndpoints.ToError(context, result.Error!);
        }).RequireAuthorization(create);
    }
}

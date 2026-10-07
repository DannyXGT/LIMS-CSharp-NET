using Lims.Application.ReferencePreparations;
using Lims.Contracts.ReferenceMaterials;
using Lims.Contracts.ReferencePreparations;

namespace Lims.Api.ReferenceMaterials;

internal static class StockEndpoints
{
    public static IEndpointRouteBuilder MapStockEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/reference-materials/preparations/stock").WithTags("Stock");
        group.MapGet("/options", (IStockService service) => Results.Ok(service.GetOptions()))
            .RequireAuthorization(ReferenceMaterialPermissions.Create);
        group.MapGet("/sources", async (string? search, int? page, int? pageSize, IStockService service, HttpContext context, CancellationToken cancellationToken) =>
            ReferenceMaterialEndpoints.ToResult(context, await service.SourcesAsync(search, page ?? 1, pageSize ?? 25, cancellationToken).ConfigureAwait(false)))
            .RequireAuthorization(ReferenceMaterialPermissions.Create);
        group.MapPost("/preview", async (StockCalculationRequest request, IStockService service, HttpContext context, CancellationToken cancellationToken) =>
            ReferenceMaterialEndpoints.ToResult(context, await service.PreviewAsync(request, cancellationToken).ConfigureAwait(false)))
            .RequireAuthorization(ReferenceMaterialPermissions.Create);
        group.MapGet("", async (string? search, int? page, int? pageSize, IStockService service, HttpContext context, CancellationToken cancellationToken) =>
            ReferenceMaterialEndpoints.ToResult(context, await service.ListAsync(search, page ?? 1, pageSize ?? 25, cancellationToken).ConfigureAwait(false)))
            .RequireAuthorization(ReferenceMaterialPermissions.View);
        group.MapGet("/{id:guid}", async (Guid id, IStockService service, HttpContext context, CancellationToken cancellationToken) =>
            ReferenceMaterialEndpoints.ToResult(context, await service.GetAsync(id, cancellationToken).ConfigureAwait(false)))
            .RequireAuthorization(ReferenceMaterialPermissions.View);
        group.MapPost("", CreateAsync).RequireAuthorization(ReferenceMaterialPermissions.Create);
        return endpoints;
    }

    private static async Task<IResult> CreateAsync(CreateStockRequest request, IStockService service, HttpContext context, CancellationToken cancellationToken)
    {
        if (!ReferenceMaterialEndpoints.TryGetActorUserId(context, out var actorUserId) || actorUserId <= 0)
            return ReferenceMaterialEndpoints.InvalidSession(context);
        var result = await service.CreateAsync(request, actorUserId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess
            ? Results.Created($"/api/reference-materials/preparations/stock/{result.Value!.Preparation.Id:D}", result.Value)
            : ReferenceMaterialEndpoints.ToError(context, result.Error!);
    }
}

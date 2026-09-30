using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using Lims.Application.Common;
using Lims.Application.ReferenceMaterials;
using Lims.Contracts.Errors;
using Lims.Contracts.ReferenceMaterials;

namespace Lims.Api.ReferenceMaterials;

internal static class ReferenceMaterialEndpoints
{
    public static IEndpointRouteBuilder MapReferenceMaterialEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/reference-materials").WithTags("Reference Materials");
        group.MapGet("", ListAsync).RequireAuthorization(ReferenceMaterialPermissions.View);
        group.MapGet("/{id:guid}", GetAsync).RequireAuthorization(ReferenceMaterialPermissions.View);
        group.MapPost("", CreateAsync).RequireAuthorization(ReferenceMaterialPermissions.Create);
        group.MapPut("/{id:guid}", UpdateAsync).RequireAuthorization(ReferenceMaterialPermissions.Edit);
        group.MapPost("/{id:guid}/archive", ArchiveAsync)
            .RequireAuthorization(ReferenceMaterialPermissions.Archive);
        group.MapPost("/{id:guid}/replacement", ReplaceAsync)
            .RequireAuthorization(
                ReferenceMaterialPermissions.Create,
                ReferenceMaterialPermissions.Archive);
        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        string? search,
        string? status,
        string? method,
        int? page,
        int? pageSize,
        IReferenceMaterialService service,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var result = await service.ListAsync(
                search,
                status,
                method,
                page ?? 1,
                pageSize ?? 25,
                cancellationToken)
            .ConfigureAwait(false);
        return ToResult(context, result);
    }

    private static async Task<IResult> GetAsync(
        Guid id,
        IReferenceMaterialService service,
        HttpContext context,
        CancellationToken cancellationToken) =>
        ToResult(context, await service.GetAsync(id, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> CreateAsync(
        CreateReferenceMaterialRequest request,
        IReferenceMaterialService service,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorUserId(context, out var actorUserId))
        {
            return InvalidSession(context);
        }

        var result = await service.CreateAsync(request, actorUserId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess
            ? Results.Created($"/api/reference-materials/{result.Value!.Id:D}", result.Value)
            : ToError(context, result.Error!);
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateReferenceMaterialRequest request,
        IReferenceMaterialService service,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorUserId(context, out var actorUserId))
        {
            return InvalidSession(context);
        }

        return ToResult(
            context,
            await service.UpdateAsync(id, request, actorUserId, cancellationToken).ConfigureAwait(false));
    }

    private static async Task<IResult> ArchiveAsync(
        Guid id,
        ArchiveReferenceMaterialRequest request,
        IReferenceMaterialService service,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorUserId(context, out var actorUserId))
        {
            return InvalidSession(context);
        }

        return ToResult(
            context,
            await service.ArchiveAsync(id, request, actorUserId, cancellationToken).ConfigureAwait(false));
    }

    private static async Task<IResult> ReplaceAsync(
        Guid id,
        ReplaceReferenceMaterialRequest request,
        IReferenceMaterialService service,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorUserId(context, out var actorUserId))
        {
            return InvalidSession(context);
        }

        var result = await service.ReplaceAsync(id, request, actorUserId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess
            ? Results.Created($"/api/reference-materials/{result.Value!.Id:D}", result.Value)
            : ToError(context, result.Error!);
    }

    private static IResult ToResult<T>(HttpContext context, OperationResult<T> result) =>
        result.IsSuccess ? Results.Ok(result.Value) : ToError(context, result.Error!);

    private static IResult ToError(HttpContext context, OperationError error)
    {
        var statusCode = error.Code switch
        {
            ErrorCodes.ValidationError => StatusCodes.Status400BadRequest,
            ErrorCodes.InvalidSession => StatusCodes.Status401Unauthorized,
            ErrorCodes.Forbidden => StatusCodes.Status403Forbidden,
            ErrorCodes.NotFound => StatusCodes.Status404NotFound,
            ErrorCodes.Conflict or ErrorCodes.InvalidState => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError,
        };
        return Results.Json(
            new ApiError(error.Code, error.Message, context.TraceIdentifier, error.ValidationErrors),
            statusCode: statusCode);
    }

    private static bool TryGetActorUserId(HttpContext context, out int userId) => int.TryParse(
        context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value,
        NumberStyles.None,
        CultureInfo.InvariantCulture,
        out userId);

    private static IResult InvalidSession(HttpContext context) => Results.Json(
        new ApiError(
            ErrorCodes.InvalidSession,
            "La sesión no es válida o expiró.",
            context.TraceIdentifier),
        statusCode: StatusCodes.Status401Unauthorized);
}

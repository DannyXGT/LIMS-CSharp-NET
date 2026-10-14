using System.IdentityModel.Tokens.Jwt;
using System.Globalization;
using System.Security.Claims;
using Lims.Application.Dashboard;
namespace Lims.Api.Dashboard;
internal static class DashboardEndpoints
{
    public static void MapDashboardEndpoints(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/dashboard", async (ClaimsPrincipal user, IDashboardRepository dashboard, HttpContext context, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            if (!int.TryParse(user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var id)) return Results.Unauthorized();
            var result = await dashboard.GetAsync(id, ct);
            return result is null ? Results.Unauthorized() : Results.Ok(result);
        }).RequireAuthorization();
}

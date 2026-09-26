using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Lims.Api.Health;

internal static class HealthResponseWriter
{
    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        return context.Response.WriteAsJsonAsync(new
        {
            status = report.Status.ToString().ToLowerInvariant(),
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString().ToLowerInvariant(),
                durationMilliseconds = Math.Round(entry.Value.Duration.TotalMilliseconds, 2),
            }),
            correlationId = context.TraceIdentifier,
        });
    }
}

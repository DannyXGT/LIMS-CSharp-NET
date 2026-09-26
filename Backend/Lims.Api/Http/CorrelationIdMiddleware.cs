using System.Diagnostics;

namespace Lims.Api.Http;

public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-ID";

    public async Task InvokeAsync(HttpContext context)
    {
        var incoming = context.Request.Headers[HeaderName].FirstOrDefault();
        var correlationId = IsSafe(incoming)
            ? incoming!
            : Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
        context.TraceIdentifier = correlationId;
        context.Response.Headers[HeaderName] = correlationId;
        Activity.Current?.SetTag("lims.correlation_id", correlationId);
        await next(context).ConfigureAwait(false);
    }

    private static bool IsSafe(string? value) =>
        value is { Length: > 0 and <= 64 } &&
        value.All(static character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
}

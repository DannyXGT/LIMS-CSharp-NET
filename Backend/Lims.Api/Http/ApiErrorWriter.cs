using Lims.Contracts.Errors;

namespace Lims.Api.Http;

internal static class ApiErrorWriter
{
    public static Task WriteAsync(
        HttpContext context,
        int statusCode,
        string code,
        string message,
        CancellationToken cancellationToken)
    {
        context.Response.StatusCode = statusCode;
        return context.Response.WriteAsJsonAsync(
            new ApiError(code, message, context.TraceIdentifier),
            cancellationToken);
    }
}

using Lims.Contracts.Errors;
using Microsoft.AspNetCore.Diagnostics;

namespace Lims.Api.Http;

internal static class ApiExceptionHandler
{
    public static async Task HandleAsync(HttpContext context)
    {
        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        ApiLog.UnexpectedFailure(
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("UnhandledException"),
            context.TraceIdentifier,
            exception);
        if (!context.Response.HasStarted)
        {
            await ApiErrorWriter.WriteAsync(
                context,
                StatusCodes.Status500InternalServerError,
                ErrorCodes.UnexpectedError,
                "Ocurrió un error inesperado. Use el identificador de soporte para solicitar ayuda.",
                context.RequestAborted).ConfigureAwait(false);
        }
    }
}

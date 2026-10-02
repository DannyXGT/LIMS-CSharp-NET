using Lims.Contracts.Errors;
using Microsoft.AspNetCore.Diagnostics;
using Npgsql;

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
            var postgres = FindException<PostgresException>(exception);
            if (postgres?.SqlState is "42P01" or "42703")
            {
                await ApiErrorWriter.WriteAsync(
                    context,
                    StatusCodes.Status503ServiceUnavailable,
                    ErrorCodes.DatabaseSchemaOutOfDate,
                    "El esquema de base de datos requiere actualización.",
                    context.RequestAborted).ConfigureAwait(false);
                return;
            }

            if (FindException<NpgsqlException>(exception) is not null)
            {
                await ApiErrorWriter.WriteAsync(
                    context,
                    StatusCodes.Status503ServiceUnavailable,
                    ErrorCodes.ServerUnavailable,
                    "El servidor de base de datos no está disponible.",
                    context.RequestAborted).ConfigureAwait(false);
                return;
            }

            await ApiErrorWriter.WriteAsync(
                context,
                StatusCodes.Status500InternalServerError,
                ErrorCodes.UnexpectedError,
                "Ocurrió un error inesperado. Use el identificador de soporte para solicitar ayuda.",
                context.RequestAborted).ConfigureAwait(false);
        }
    }

    private static TException? FindException<TException>(Exception? exception)
        where TException : Exception
    {
        while (exception is not null)
        {
            if (exception is TException match)
            {
                return match;
            }

            exception = exception.InnerException;
        }

        return null;
    }
}

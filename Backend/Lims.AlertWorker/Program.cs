using Lims.Application.Permissions;
using Lims.Infrastructure.Alerts;
using Lims.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using System.Text.Json;
using Npgsql;

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args.Where(a => a != "--evaluate-only").ToArray(), ContentRootPath = AppContext.BaseDirectory
});
try
{
    var connection = builder.Configuration.GetConnectionString("Lims");
    if (string.IsNullOrWhiteSpace(connection)) throw new InvalidOperationException("Configure ConnectionStrings__Lims para la cuenta de la tarea programada.");
    builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
    builder.Services.AddSingleton(builder.Configuration.GetSection("PermissionManagement").Get<PermissionOptions>() ?? new());
    builder.Services.AddDbContext<LimsDbContext>(o => o.UseNpgsql(connection, n => n.EnableRetryOnFailure(3)));
    builder.Services.AddScoped<IPermissionRepository, PermissionRepository>();
    builder.Services.AddLimsAlerts(builder.Configuration);
    using var host = builder.Build();
    using var scope = host.Services.CreateScope();
    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
    var result = await scope.ServiceProvider.GetRequiredService<AlertJob>().RunAsync(timeout.Token, sendEmail: !args.Contains("--evaluate-only", StringComparer.Ordinal));
    WriteEvent(result.Acquired ? "Completed" : "AlreadyRunning", result);
    return 0;
}
catch (Exception error) when (error is not OutOfMemoryException)
{
    // Provider messages can contain connection details. Keep unattended output free of secrets.
    WriteEvent("Failed", new { Type = error.GetType().Name, SqlState = (error as PostgresException)?.SqlState });
    return 1;
}

void WriteEvent(string state, object result)
{
    var line = JsonSerializer.Serialize(new { At = DateTimeOffset.UtcNow, State = state, Result = result });
    Console.WriteLine(line);
    try
    {
        var directory = builder.Configuration["Alerts:LogDirectory"] ?? Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(directory);
        File.AppendAllText(Path.Combine(directory, $"alerts-{DateTime.UtcNow:yyyyMMdd}.jsonl"), line + Environment.NewLine);
    }
    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
    { Console.Error.WriteLine("No se pudo escribir el registro local del ejecutor; revise Alerts:LogDirectory y sus permisos."); }
}

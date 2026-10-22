using Lims.Application.Permissions;
using Lims.Infrastructure.Alerts;
using Lims.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = args, ContentRootPath = AppContext.BaseDirectory });
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
try
{
    var result = await scope.ServiceProvider.GetRequiredService<AlertJob>().RunAsync(timeout.Token);
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result));
    return 0;
}
catch (Exception error) when (error is not OutOfMemoryException)
{
    // Provider messages can contain connection details. Keep unattended output free of secrets.
    Console.Error.WriteLine($"LIMS AlertWorker falló ({error.GetType().Name}). Las tareas persistentes se recuperarán en la siguiente ejecución.");
    return 1;
}

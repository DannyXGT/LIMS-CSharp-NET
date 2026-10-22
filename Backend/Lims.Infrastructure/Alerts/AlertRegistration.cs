using Lims.Application.Alerts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lims.Infrastructure.Alerts;

public static class AlertRegistration
{
    public static IServiceCollection AddLimsAlerts(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(configuration.GetSection("Alerts:Smtp").Get<AlertMailOptions>() ?? new AlertMailOptions());
        services.AddScoped<AlertAccess>(); services.AddScoped<IAlertService, AlertService>();
        services.AddScoped<AlertSourceReader>(); services.AddScoped<AlertEngine>();
        services.AddScoped<AlertDashboard>();
        services.AddScoped<IAlertEmailTransport, SmtpAlertEmailTransport>();
        services.AddScoped<AlertDeliveryProcessor>(); services.AddScoped<AlertJob>();
        return services;
    }
}

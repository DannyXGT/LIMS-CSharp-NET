using Lims.Desktop.Http;
using Lims.Desktop.Services;
using Lims.Desktop.ViewModels;
using Lims.Desktop.Views;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using OpenTelemetry.Trace;
using Serilog;

namespace Lims.Desktop;

public partial class App : Application
{
    private readonly IHost _host;
    private Window? _window;

    public App()
    {
        var uiCulture = CultureInfo.GetCultureInfo("es-GT");
        CultureInfo.DefaultThreadCurrentCulture = uiCulture;
        CultureInfo.DefaultThreadCurrentUICulture = uiCulture;
        Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = uiCulture.Name;
        InitializeComponent();
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ContentRootPath = AppContext.BaseDirectory,
        });
        var configuration = builder.Configuration.GetSection(DesktopOptions.ConfigurationSection);
        var timeoutValue = configuration[nameof(DesktopOptions.RequestTimeoutSeconds)];
        var options = new DesktopOptions
        {
            ApiBaseUrl = configuration[nameof(DesktopOptions.ApiBaseUrl)] ?? string.Empty,
            EnvironmentName = configuration[nameof(DesktopOptions.EnvironmentName)] ?? "Production",
            CorporateDomain = configuration[nameof(DesktopOptions.CorporateDomain)] ?? "intertek.com",
            RequestTimeoutSeconds = int.TryParse(
                timeoutValue,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var timeoutSeconds)
                ? timeoutSeconds
                : 30,
        };
        var baseAddress = options.GetValidatedBaseAddress();
        var timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);

        builder.Services.AddSerilog(new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.WithProperty("Application", "Lims.Desktop")
            .WriteTo.Debug(formatProvider: CultureInfo.InvariantCulture)
            .CreateLogger(), dispose: true);

        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<ISecureStorage, PasswordVaultSecureStorage>();
        builder.Services.AddSingleton<ISessionService, SessionService>();
        builder.Services.AddSingleton<INavigationService, NavigationService>();
        builder.Services.AddSingleton<IAuthenticationGateway, AuthenticationGateway>();
        builder.Services.AddSingleton<IAuthenticationApiClient, AuthenticationApiClient>();
        builder.Services.AddSingleton<IReferenceMaterialsApiClient, ReferenceMaterialsApiClient>();
        builder.Services.AddSingleton<ITokenRefreshCoordinator, TokenRefreshCoordinator>();
        builder.Services.AddTransient<CorrelationIdHandler>();
        builder.Services.AddTransient<AuthorizedApiHandler>();
        builder.Services.AddHttpClient(ApiClientNames.Anonymous, client =>
        {
            client.BaseAddress = baseAddress;
            client.Timeout = timeout;
        }).AddHttpMessageHandler<CorrelationIdHandler>();
        builder.Services.AddHttpClient(ApiClientNames.Authorized, client =>
        {
            client.BaseAddress = baseAddress;
            client.Timeout = timeout;
        }).AddHttpMessageHandler<AuthorizedApiHandler>();
        builder.Services.AddOpenTelemetry()
            .WithTracing(tracing => tracing
                .AddSource("Lims.Desktop")
                .AddHttpClientInstrumentation());

        builder.Services.AddSingleton<LoginViewModel>();
        builder.Services.AddSingleton<ShellViewModel>();
        builder.Services.AddSingleton<ReferenceMaterialsViewModel>();
        builder.Services.AddSingleton<LoginPage>();
        builder.Services.AddSingleton<ReferenceMaterialsPage>();
        builder.Services.AddSingleton<ShellPage>();
        builder.Services.AddSingleton<MainWindow>();
        _host = builder.Build();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        await _host.StartAsync().ConfigureAwait(true);
        _window = _host.Services.GetRequiredService<MainWindow>();
        _window.Activate();
    }
}

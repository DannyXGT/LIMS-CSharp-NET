using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Lims.Api.Authentication;
using Lims.Api.Health;
using Lims.Api.Http;
using Lims.Application.Authentication;
using Lims.Application.Authentication.Ports;
using Lims.Contracts.Errors;
using Lims.Infrastructure.Persistence;
using Lims.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, logger) => logger
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "Lims.Api")
        .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture));

    var connectionString = builder.Configuration.GetConnectionString("Lims");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        throw new InvalidOperationException(
            "ConnectionStrings:Lims is required. Configure it with User Secrets or an environment variable.");
    }

    var jwtOptions = builder.Configuration.GetSection(JwtOptions.ConfigurationSection)
        .Get<JwtOptions>() ?? new JwtOptions();
    jwtOptions.Validate();
    var corporateIdentity = builder.Configuration.GetSection(CorporateIdentityPolicy.ConfigurationSection)
        .Get<CorporateIdentityPolicy>() ?? new CorporateIdentityPolicy();
    corporateIdentity.Validate();
    var authenticationPolicy = builder.Configuration.GetSection(AuthenticationPolicy.ConfigurationSection)
        .Get<AuthenticationPolicy>() ?? new AuthenticationPolicy();
    authenticationPolicy.Validate();

    builder.Services.AddSingleton(jwtOptions);
    builder.Services.AddSingleton(corporateIdentity);
    builder.Services.AddSingleton(authenticationPolicy);
    builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
    builder.Services.AddSingleton<UserIdentifierNormalizer>();
    builder.Services.AddSingleton<LoginAttemptTracker>();
    builder.Services.AddDbContext<LimsDbContext>(options => options.UseNpgsql(
        connectionString,
        npgsql => npgsql.EnableRetryOnFailure(3)));
    builder.Services.AddScoped<IUserRepository, UserRepository>();
    builder.Services.AddScoped<IAuthenticationSessionStore, AuthenticationSessionStore>();
    builder.Services.AddSingleton<IPasswordHashService, Argon2PasswordHashService>();
    builder.Services.AddSingleton<IRefreshTokenService, RefreshTokenService>();
    builder.Services.AddSingleton<IAccessTokenService, JwtAccessTokenService>();
    builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = ClaimTypes.Name,
            RoleClaimType = ClaimTypes.Role,
        };
        options.Events = JwtSessionValidation.CreateEvents();
    });

    builder.Services.AddAuthorizationBuilder()
        .AddPolicy(LimsPolicies.RequireAdministrator, policy =>
            policy.RequireAuthenticatedUser().RequireRole("Administrador"))
        .AddPolicy(LimsPolicies.UsersManage, policy =>
            policy.RequireAuthenticatedUser().RequireClaim(JwtAccessTokenService.PermissionClaim, "users.manage"))
        .AddPolicy(LimsPolicies.ChemicalDepartment, policy =>
            policy.RequireAuthenticatedUser().RequireClaim("department", "Laboratorio Químico"));

    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.AddPolicy("login", httpContext => RateLimitPartition.GetSlidingWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            static _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 4,
                QueueLimit = 0,
                AutoReplenishment = true,
            }));
        options.AddPolicy("refresh", httpContext => RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            static _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true,
            }));
        options.OnRejected = (context, cancellationToken) => new ValueTask(ApiErrorWriter.WriteAsync(
            context.HttpContext,
            StatusCodes.Status429TooManyRequests,
            ErrorCodes.RateLimitExceeded,
            "Se alcanzó temporalmente el límite de solicitudes. Intente nuevamente más tarde.",
            cancellationToken));
    });

    builder.Services.AddHealthChecks().AddCheck<PostgreSqlHealthCheck>("postgresql", tags: ["ready"]);
    builder.Services.AddOpenApi();
    var telemetry = builder.Services.AddOpenTelemetry()
        .WithTracing(tracing => tracing
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddSource("Npgsql"))
        .WithMetrics(metrics => metrics
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation());
    if (builder.Configuration.GetValue<bool>("Observability:ConsoleExporter"))
    {
        telemetry.WithTracing(tracing => tracing.AddConsoleExporter())
            .WithMetrics(metrics => metrics.AddConsoleExporter());
    }

    var app = builder.Build();
    app.UseMiddleware<CorrelationIdMiddleware>();
    app.UseExceptionHandler(exceptionHandlerApp => exceptionHandlerApp.Run(ApiExceptionHandler.HandleAsync));
    app.UseSerilogRequestLogging(options =>
        options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms");
    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
    }

    app.UseHttpsRedirection();
    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapGet("/", () => Results.Ok(new { service = "Lims.Api", status = "running" }));
    app.MapAuthenticationEndpoints();
    app.MapHealthChecks("/health/live", new HealthCheckOptions
    {
        Predicate = static _ => false,
        ResponseWriter = HealthResponseWriter.WriteAsync,
    });
    app.MapHealthChecks("/health/ready", new HealthCheckOptions
    {
        Predicate = static check => check.Tags.Contains("ready"),
        ResponseWriter = HealthResponseWriter.WriteAsync,
    });
    Lims.Api.ApiLog.ApplicationStarted(app.Logger, app.Environment.EnvironmentName);
    app.Run();
}
catch (Exception exception)
{
    Log.Fatal(exception, "Application terminated unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program;

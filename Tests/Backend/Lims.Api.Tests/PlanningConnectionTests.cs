using Lims.Infrastructure.Persistence;
using Lims.Infrastructure.Planning;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Lims.Api.Tests;

public sealed class PlanningConnectionTests
{
    [Theory]
    [InlineData(null, "host_identity")]
    [InlineData("", "host_identity")]
    [InlineData("Host=localhost;Database=planning_only;Username=never_used", "planning_only")]
    public void PlanningConnectionDoesNotRedirectHostAuthentication(string? planning, string expected)
    {
        using var environment = new ConnectionEnvironment(planning);
        using var factory = new ConnectionFactory(planning);
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        Assert.Equal(expected, new NpgsqlConnectionStringBuilder(services.GetRequiredService<PlanningDatabase>().Source.ConnectionString).Database);
        Assert.Equal("host_identity", new NpgsqlConnectionStringBuilder(services.GetRequiredService<PlanningIdentityDatabase>().Source.ConnectionString).Database);
        Assert.Equal("host_identity", new NpgsqlConnectionStringBuilder(services.GetRequiredService<LimsDbContext>().Database.GetConnectionString()).Database);
    }

    // API tests run serially; startup reads these settings before WebApplicationFactory callbacks.
    private sealed class ConnectionEnvironment : IDisposable
    {
        private readonly string? _host = Environment.GetEnvironmentVariable("ConnectionStrings__Lims");
        private readonly string? _planning = Environment.GetEnvironmentVariable("ConnectionStrings__Planning");
        public ConnectionEnvironment(string? planning)
        {
            Environment.SetEnvironmentVariable("ConnectionStrings__Lims", "Host=localhost;Database=host_identity;Username=never_used");
            Environment.SetEnvironmentVariable("ConnectionStrings__Planning", planning);
        }
        public void Dispose()
        {
            Environment.SetEnvironmentVariable("ConnectionStrings__Lims", _host);
            Environment.SetEnvironmentVariable("ConnectionStrings__Planning", _planning);
        }
    }

    private sealed class ConnectionFactory(string? planning) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Lims"] = "Host=localhost;Database=host_identity;Username=never_used",
                    ["ConnectionStrings:Planning"] = planning,
                    ["Authentication:Jwt:Issuer"] = "Lims.Api.Tests",
                    ["Authentication:Jwt:Audience"] = "Lims.Api.Tests.Client",
                    ["Authentication:Jwt:SigningKey"] = AuthenticationApiFactory.SigningKey,
                    ["Observability:ConsoleExporter"] = "false",
                }));
        }
    }
}

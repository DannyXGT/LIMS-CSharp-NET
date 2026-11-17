using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Lims.Contracts.Authentication;
using Lims.Contracts.Planning;
using Lims.Infrastructure.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Lims.Api.Tests;

public sealed class PlanningAcceptanceFactAttribute : FactAttribute
{
    public PlanningAcceptanceFactAttribute(){if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("LIMS_PLANNING_ACCEPTANCE_RUNTIME")))Skip="Requiere copia local y archivos explícitos: scripts/test-planning-acceptance.ps1.";}
}
public sealed class PlanningAcceptanceTests
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web){WriteIndented=true};
    private sealed record Runtime(string Host,int Port,string Database,string Username,string Password,string SigningKey);
    private sealed record Account(int Id,string Email,string Password);
    private sealed class LocalEnvironment : IDisposable
    {
        private readonly string? _connection=Environment.GetEnvironmentVariable("ConnectionStrings__Lims");
        private readonly string? _key=Environment.GetEnvironmentVariable("Authentication__Jwt__SigningKey");
        public LocalEnvironment(string connection,string key){Environment.SetEnvironmentVariable("ConnectionStrings__Lims",connection);Environment.SetEnvironmentVariable("Authentication__Jwt__SigningKey",key);}
        public void Dispose(){Environment.SetEnvironmentVariable("ConnectionStrings__Lims",_connection);Environment.SetEnvironmentVariable("Authentication__Jwt__SigningKey",_key);}
    }
    private sealed class LocalApiFactory(string connection,string signingKey) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_,configuration)=>configuration.AddInMemoryCollection(new Dictionary<string,string?>{
                ["ConnectionStrings:Lims"]=connection,["Authentication:Jwt:SigningKey"]=signingKey,["Observability:ConsoleExporter"]="false"}));
        }
    }
    [PlanningAcceptanceFact]
    public async Task ExistingLoginImportsExplicitTipsFilesAndQueriesRealSourceInIsolatedDatabase()
    {
        var path=Environment.GetEnvironmentVariable("LIMS_PLANNING_ACCEPTANCE_RUNTIME")!;
        var runtime=JsonSerializer.Deserialize<Runtime>(await File.ReadAllTextAsync(path),Json)!;
        if(runtime.Host!="127.0.0.1"||runtime.Port!=55441||runtime.Database!="lims_planning_dev")throw new InvalidOperationException("Se rechaza un destino distinto de la copia local.");
        var connection=new NpgsqlConnectionStringBuilder{Host=runtime.Host,Port=runtime.Port,Database=runtime.Database,Username=runtime.Username,Password=runtime.Password}.ConnectionString;
        var accountPath=Path.Combine(Path.GetDirectoryName(path)!,"acceptance-account.json");
        Account account;
        if(File.Exists(accountPath))account=JsonSerializer.Deserialize<Account>(await File.ReadAllTextAsync(accountPath),Json)!;
        else
        {
            var email="planning.local."+Guid.NewGuid().ToString("N")+"@intertek.com";var password=Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            var hash=new Argon2PasswordHashService().Hash(password);
            await using var c=new NpgsqlConnection(connection);await c.OpenAsync();
            await using var cmd=new NpgsqlCommand("INSERT INTO usuarios(nombre,email,password_hash,id_rol,activo) SELECT 'VALIDACION LOCAL PLANIFICACION',@email,@hash,id,true FROM roles WHERE nombre='Administrador' RETURNING id",c);
            cmd.Parameters.AddWithValue("email",email);cmd.Parameters.AddWithValue("hash",hash);
            var id=(int)(await cmd.ExecuteScalarAsync()??throw new InvalidOperationException("Falta rol administrador en la copia."));account=new(id,email,password);
            await File.WriteAllTextAsync(accountPath,JsonSerializer.Serialize(account,Json));
        }
        using var configuration=new LocalEnvironment(connection,runtime.SigningKey);
        await using var factory=new LocalApiFactory(connection,runtime.SigningKey);
        using var client=factory.CreateClient(new(){BaseAddress=new Uri("https://localhost")});client.Timeout=TimeSpan.FromMinutes(5);
        Assert.Equal(HttpStatusCode.OK,(await client.GetAsync("/health/ready")).StatusCode);
        var login=await client.PostAsJsonAsync("/api/auth/login",new LoginRequest(account.Email,account.Password,"Planning acceptance local"));
        Assert.True(login.IsSuccessStatusCode,await login.Content.ReadAsStringAsync());var authenticated=(await login.Content.ReadFromJsonAsync<AuthenticationResponse>())!;
        client.DefaultRequestHeaders.Authorization=new("Bearer",authenticated.AccessToken);
        var access=(await client.GetFromJsonAsync<WorkspaceAccess>("/api/workspaces"))!;Assert.True(access.Planning);Assert.True(access.Laboratory);
        var legacy=(await client.GetFromJsonAsync<LegacyEquivalencePreview>("/api/planning/equivalences/legacy"))!;
        var mappings=await client.PostAsJsonAsync("/api/planning/equivalences/legacy",new LegacyEquivalenceConfirmation(legacy.Fingerprint,"Validación local: catálogo del respaldo, conflictos excluidos"));Assert.True(mappings.IsSuccessStatusCode,await mappings.Content.ReadAsStringAsync());
        var summaryPath=Environment.GetEnvironmentVariable("LIMS_PLANNING_SUMMARY")!;var reportPath=Environment.GetEnvironmentVariable("LIMS_PLANNING_REPORT")!;
        var summary=await File.ReadAllBytesAsync(summaryPath);var report=await File.ReadAllBytesAsync(reportPath);
        async Task<PlanningImportPreview> Preview()
        {
            using var form=new MultipartFormDataContent();form.Add(new ByteArrayContent(summary),"summary",Path.GetFileName(summaryPath));form.Add(new ByteArrayContent(report),"report",Path.GetFileName(reportPath));form.Add(new StringContent("Noon"),"window");form.Add(new StringContent("2026-11-13"),"day");
            var response=await client.PostAsync("/api/planning/imports/preview",form);Assert.True(response.IsSuccessStatusCode,await response.Content.ReadAsStringAsync());return (await response.Content.ReadFromJsonAsync<PlanningImportPreview>())!;
        }
        var watch=Stopwatch.StartNew();var preview=await Preview();
        await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(path)!,"tips-preview.json"),JsonSerializer.Serialize(preview,Json));
        Assert.DoesNotContain(preview.Issues,i=>i.Blocking);
        var confirm=await client.PostAsync($"/api/planning/imports/{preview.Id}/confirm",null);Assert.True(confirm.IsSuccessStatusCode,await confirm.Content.ReadAsStringAsync());
        var first=(await confirm.Content.ReadFromJsonAsync<PlanningImportResult>())!;
        var repeated=await client.PostAsync($"/api/planning/imports/{preview.Id}/confirm",null);Assert.True((await repeated.Content.ReadFromJsonAsync<PlanningImportResult>())!.AlreadyApplied);
        Assert.Equal("Applied",(await Preview()).Status);
        var page=(await client.GetFromJsonAsync<PlanningPage>("/api/planning/jobs?PageSize=100"))!;Assert.NotEmpty(page.Items);
        var sourceJob=page.Items[0];var dossier=(await client.GetFromJsonAsync<JobDossier>($"/api/planning/jobs/{sourceJob.EpisodeId}"))!;Assert.NotEmpty(dossier.Sources);
        var searched=(await client.GetFromJsonAsync<PlanningPage>("/api/planning/jobs?Search="+Uri.EscapeDataString(sourceJob.Job)))!;Assert.All(searched.Items,r=>Assert.Equal(sourceJob.Job,r.Job));
        var export=await client.GetAsync("/api/planning/export?Search="+Uri.EscapeDataString(sourceJob.Job));Assert.Equal(HttpStatusCode.OK,export.StatusCode);
        await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(path)!,"acceptance-api.json"),JsonSerializer.Serialize(new{Passed=true,Boundary="Authenticated real API + local restored PostgreSQL + explicit TIPS files; no production connection",SourceDate="2026-11-13",Database=runtime.Database,SourceJob=sourceJob.Job,EpisodeId=sourceJob.EpisodeId,QueryTotal=page.Total,first.NewJobs,first.NewTests,first.UpdatedJobs,first.LinkedManualTests,LegacyMappings=legacy.Items.Count,LegacyConflicts=legacy.Conflicts.Count,Warnings=preview.Issues.Count,SourceRows=dossier.Sources.Count,ElapsedSeconds=watch.Elapsed.TotalSeconds,SummarySha256=Convert.ToHexString(SHA256.HashData(summary)),ReportSha256=Convert.ToHexString(SHA256.HashData(report))},Json));
        var logout=await client.PostAsync("/api/auth/logout",null);Assert.True(logout.IsSuccessStatusCode);Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync("/api/workspaces")).StatusCode);
    }
}

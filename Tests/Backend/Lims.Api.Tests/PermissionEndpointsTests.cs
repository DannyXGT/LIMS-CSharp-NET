using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Lims.Contracts.Permissions;
using Lims.Application.Common;
using Lims.Application.ReferencePreparations;
using Lims.Contracts.ReferencePreparations;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Lims.Infrastructure.Security;
using Microsoft.IdentityModel.Tokens;

namespace Lims.Api.Tests;

public sealed class PermissionEndpointsTests : IDisposable
{
    private const string Path = "/api/administration/permissions";
    private readonly AuthenticationApiFactory _factory = new();
    private readonly HttpClient _client;
    private readonly WebApplicationFactory<Program> _webFactory;
    public PermissionEndpointsTests()
    {
        _webFactory = _factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        { s.RemoveAll<IIntermediateService>(); s.AddSingleton<IIntermediateService, TestIntermediateService>(); }));
        _client = _webFactory.CreateClient();
    }
    [Theory]
    [InlineData("/catalog")]
    [InlineData("/users")]
    [InlineData("/users/84")]
    [InlineData("/users/84/history")]
    public async Task OrdinaryUsersCannotAccessAnyAdministrativeReadEvenWithForgedRoleClaim(string path)
    {
        _factory.SetIdentity("Analista");
        _client.DefaultRequestHeaders.Authorization = Token("Administrador");
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.GetAsync(Path + path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.PutAsJsonAsync(Path + "/users/84", new SavePermissionsRequest([PermissionCatalog.StockView], Guid.Empty))).StatusCode);
    }
    [Fact]
    public async Task AdministratorReadsCatalogSavesWithAuthenticatedActorAndSeesHistory()
    {
        _factory.SetIdentity("Administrador"); _client.DefaultRequestHeaders.Authorization = Token("Analista");
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync(Path + "/users")).StatusCode);
        Assert.Equal(9, (await _client.GetFromJsonAsync<PermissionDefinition[]>(Path + "/catalog"))!.Length);
        var initial = await _client.GetFromJsonAsync<UserPermissionState>(Path + "/users/84");
        var response = await _client.PutAsJsonAsync(Path + "/users/84", new SavePermissionsRequest([PermissionCatalog.StockView], initial!.Version));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.Equal(AuthenticationApiFactory.UserId, _factory.PermissionRepository.LastActor);
        Assert.Single((await _client.GetFromJsonAsync<PermissionHistoryPage>(Path + "/users/84/history"))!.Items);
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.GetAsync(Path + "/users/999")).StatusCode);
    }
    [Theory]
    [InlineData("/api/reference-materials", "reference_materials.view")]
    [InlineData("/api/reference-materials/preparations/stock/options", "stock.create")]
    [InlineData("/api/reference-materials/preparations/intermediate/options", "intermedia.create")]
    public async Task OldJwtCannotRetainRevokedPermission(string path, string permission)
    {
        _factory.PermissionRepository.Current = new("Analista", [permission], true);
        _client.DefaultRequestHeaders.Authorization = Token("Administrador", permission);
        var allowed = await _client.GetAsync(path); Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        _factory.PermissionRepository.Current = new("Analista", [], true);
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.GetAsync(path)).StatusCode);
    }
    [Theory]
    [InlineData("/api/reference-materials", "POST")]
    [InlineData("/api/reference-materials/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "PUT")]
    [InlineData("/api/reference-materials/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/archive", "POST")]
    [InlineData("/api/reference-materials/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/replacement", "POST")]
    [InlineData("/api/reference-materials/preparations/stock", "POST")]
    [InlineData("/api/reference-materials/preparations/intermediate", "POST")]
    public async Task DirectWritesAreDeniedBeforePayloadBinding(string path, string method)
    {
        _factory.PermissionRepository.Current = new("Analista", [], true);
        _client.DefaultRequestHeaders.Authorization = Token("Administrador", PermissionCatalog.All.Select(p => p.Code).ToArray());
        using var request = new HttpRequestMessage(new HttpMethod(method), path) { Content = JsonContent.Create(new { }) };
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendAsync(request)).StatusCode);
    }
    [Fact]
    public async Task InactiveUserCannotUseAnOtherwiseValidSession()
    {
        _factory.PermissionRepository.Current = null; _client.DefaultRequestHeaders.Authorization = Token("Administrador");
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync(Path + "/catalog")).StatusCode);
    }
    private static AuthenticationHeaderValue Token(string role, params string[] permissions)
    {
        var claims = new List<Claim> { new(JwtRegisteredClaimNames.Sub, AuthenticationApiFactory.UserId.ToString(CultureInfo.InvariantCulture)), new(JwtAccessTokenService.SessionIdClaim, AuthenticationApiFactory.SessionId.ToString("D")), new(ClaimTypes.Role, role) };
        claims.AddRange(permissions.Select(p => new Claim(JwtAccessTokenService.PermissionClaim, p)));
        return new("Bearer", new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken("Lims.Api.Tests", "Lims.Api.Tests.Client", claims, expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: new(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(AuthenticationApiFactory.SigningKey)), SecurityAlgorithms.HmacSha256))));
    }
    private sealed class TestIntermediateService : IIntermediateService
    {
        public Task<OperationResult<IntermediateOptions>> OptionsAsync(CancellationToken cancellationToken) => Task.FromResult(OperationResult.Success(new IntermediateOptions([], [])));
        public Task<OperationResult<IntermediateCalculation>> PreviewAsync(IntermediateCalculationRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<OperationResult<IntermediateDetail>> CreateAsync(CreateIntermediateRequest request, int actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<OperationResult<IntermediatePage>> ListAsync(string? search, int page, int pageSize, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<OperationResult<IntermediateDetail>> GetAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
    public void Dispose() { _client.Dispose(); _webFactory.Dispose(); _factory.Dispose(); }
}

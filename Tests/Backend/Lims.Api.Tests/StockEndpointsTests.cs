using Lims.Contracts.ReferenceMaterials;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Lims.Application.Common;
using Lims.Application.ReferencePreparations;
using Lims.Contracts.Errors;
using Lims.Contracts.ReferencePreparations;
using Lims.Infrastructure.Security;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace Lims.Api.Tests;

public sealed class StockEndpointsTests : IDisposable
{
    private const string Path = "/api/reference-materials/preparations/stock";
    private readonly AuthenticationApiFactory _baseFactory = new();
    private readonly WebApplicationFactory<Program> _factory;
    private readonly FakeStockService _service = new();
    private readonly FakeCommercialService _commercial = new();
    private readonly HttpClient _client;
    public StockEndpointsTests()
    {
        _factory = _baseFactory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<ICommercialPreparationService>();
            services.AddSingleton<ICommercialPreparationService>(_commercial);
            services.RemoveAll<IStockService>();
            services.AddSingleton<IStockService>(_service);
        }));
        _client = _factory.CreateClient();
    }
    [Fact]
    public async Task AnonymousRequestsAreRejected()
    {
        using var response = await _client.GetAsync(Path);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
    [Fact]
    public async Task ViewerCanListAndOpenButCannotPrepare()
    {
        _client.DefaultRequestHeaders.Authorization = Bearer("Usuario", "reference_materials.view");
        using var list = await _client.GetAsync(Path + "?page=2&search=Stock");
        using var detail = await _client.GetAsync(Path + "/" + Guid.NewGuid());
        using var options = await _client.GetAsync(Path + "/options");
        using var preview = await _client.PostAsJsonAsync(Path + "/preview", Calculation);
        using var solvents = await _client.GetAsync(Path + "/solvents");
        Assert.Equal(HttpStatusCode.Forbidden, solvents.StatusCode);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(2, (await list.Content.ReadFromJsonAsync<StockPage>())!.Page);
        Assert.Equal(HttpStatusCode.NotFound, detail.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, options.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, preview.StatusCode);
    }
    [Fact]
    public async Task AdministratorCanReadOptionsAndSourcesWithoutExplicitPermissionRows()
    {
        _client.DefaultRequestHeaders.Authorization = Bearer("Administrador");
        using var options = await _client.GetAsync(Path + "/options");
        using var sources = await _client.GetAsync(Path + "/sources?search=CAS");
        Assert.Equal(HttpStatusCode.OK, options.StatusCode);
        Assert.Equal("mg/L", Assert.Single((await options.Content.ReadFromJsonAsync<StockOptions>())!.ConcentrationUnits));
        Assert.Equal(HttpStatusCode.OK, sources.StatusCode);
        using var solvents = await _client.GetAsync(Path + "/solvents?search=MeOH");
        Assert.Equal(HttpStatusCode.OK, solvents.StatusCode);
    }
    [Fact]
    public async Task PreviewCarriesParametersAndCreateUsesAuthenticatedActorAndCreatedLocation()
    {
        _client.DefaultRequestHeaders.Authorization = Bearer("Usuario", "reference_materials.view", "reference_materials.create");
        using var preview = await _client.PostAsJsonAsync(Path + "/preview", Calculation);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Equal(Calculation, _service.LastPreview);
        using var created = await _client.PostAsJsonAsync(Path, new CreateStockRequest(Guid.NewGuid(), "Stock", Calculation, new DateOnly(2026, 10, 6), null));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = (await created.Content.ReadFromJsonAsync<StockDetail>())!;
        Assert.Equal(AuthenticationApiFactory.UserId, _service.ActorUserId);
        Assert.Equal(Path + "/" + body.Preparation.Id.ToString("D"), created.Headers.Location!.ToString());
    }
    [Theory]
    [InlineData(ErrorCodes.ValidationError, HttpStatusCode.BadRequest)]
    [InlineData(ErrorCodes.Conflict, HttpStatusCode.Conflict)]
    [InlineData(ErrorCodes.InvalidState, HttpStatusCode.Conflict)]
    public async Task MapsDomainFailuresToApiErrors(string code, HttpStatusCode status)
    {
        _client.DefaultRequestHeaders.Authorization = Bearer("Administrador");
        _service.FailureCode = code;
        using var response = await _client.PostAsJsonAsync(Path + "/preview", Calculation);
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, (await response.Content.ReadFromJsonAsync<ApiError>())?.Code);
    }
    [Theory]
    [InlineData("stock", "stock.create")]
    [InlineData("intermediate", "intermedia.create")]
    public async Task CommercialRegistrationUsesModulePermissionAndAuthenticatedActor(string module, string permission)
    {
        var route = "/api/reference-materials/preparations/" + module + "/commercial";
        var request = new RegisterCommercialPreparationRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null);
        using (var anonymous = await _client.PostAsJsonAsync(route, request)) Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        _client.DefaultRequestHeaders.Authorization = Bearer("Usuario", "reference_materials.view");
        using (var viewer = await _client.PostAsJsonAsync(route, request)) Assert.Equal(HttpStatusCode.Forbidden, viewer.StatusCode);
        _client.DefaultRequestHeaders.Authorization = Bearer("Usuario", permission);
        using var options = await _client.GetAsync(route + "/options");
        Assert.Equal(HttpStatusCode.OK, options.StatusCode);
        using var registered = await _client.PostAsJsonAsync(route, request);
        Assert.Equal(HttpStatusCode.NotFound, registered.StatusCode); // fake result proves authorized dispatch
        Assert.Equal(AuthenticationApiFactory.UserId, _commercial.Actor);
        Assert.Equal(module == "stock" ? "Stock" : "Intermedia", _commercial.Kind);
    }
    private sealed class FakeCommercialService : ICommercialPreparationService
    {
        public int Actor { get; private set; }
        public string? Kind { get; private set; }
        public Task<CommercialPreparationOptions> OptionsAsync(string kind, string? search, CancellationToken ct) => Task.FromResult(new CommercialPreparationOptions([], [], [], []));
        public Task<OperationResult<CommercialPreparationDetail>> GetAsync(string kind, Guid id, CancellationToken ct) => Task.FromResult(OperationResult.Failure<CommercialPreparationDetail>(new(ErrorCodes.NotFound, "Prueba")));
        public Task<OperationResult<CommercialPreparationDetail>> RegisterAsync(string kind, RegisterCommercialPreparationRequest request, int actor, CancellationToken ct)
        { Actor = actor; Kind = kind; return GetAsync(kind, request.ExistingMaterialId!.Value, ct); }
    }
    public void Dispose() { _client.Dispose(); _factory.Dispose(); _baseFactory.Dispose(); }
    private static readonly StockCalculationRequest Calculation = new(Guid.NewGuid(), Guid.NewGuid(), 100, "mg/L", 1000, "mL");
    private AuthenticationHeaderValue Bearer(string role, params string[] permissions)
    {
        _baseFactory.SetIdentity(role, permissions);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, AuthenticationApiFactory.UserId.ToString(CultureInfo.InvariantCulture)),
            new(JwtAccessTokenService.SessionIdClaim, AuthenticationApiFactory.SessionId.ToString("D")),
            new(ClaimTypes.Role, role),
        };
        claims.AddRange(permissions.Select(permission => new Claim(JwtAccessTokenService.PermissionClaim, permission)));
        var token = new JwtSecurityToken("Lims.Api.Tests", "Lims.Api.Tests.Client", claims, expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(AuthenticationApiFactory.SigningKey)), SecurityAlgorithms.HmacSha256));
        return new("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
    }
    private sealed class FakeStockService : IStockService
    {
        public int ActorUserId { get; private set; }
        public StockCalculationRequest? LastPreview { get; private set; }
        public string? FailureCode { get; set; }
        public Task<OperationResult<IReadOnlyList<SolventOption>>> SolventsAsync(string? search, CancellationToken cancellationToken) => Task.FromResult(OperationResult.Success<IReadOnlyList<SolventOption>>([]));
        public StockOptions GetOptions() => new(["mg/L"], ["mL"]);
        public Task<OperationResult<StockSourcePage>> SourcesAsync(string? search, int page, int pageSize, CancellationToken cancellationToken) => Task.FromResult(OperationResult.Success(new StockSourcePage([], page, pageSize, 0)));
        public Task<OperationResult<StockCalculation>> PreviewAsync(StockCalculationRequest request, CancellationToken cancellationToken)
        {
            LastPreview = request;
            return Task.FromResult(FailureCode is { } code ? OperationResult.Failure<StockCalculation>(new(code, "Revise los datos.")) : OperationResult.Success(Result));
        }
        public Task<OperationResult<StockDetail>> CreateAsync(CreateStockRequest request, int actorUserId, CancellationToken cancellationToken)
        {
            ActorUserId = actorUserId;
            return Task.FromResult(OperationResult.Success(new StockDetail(new StockSummary(request.RequestId, "STK-20261006-001", request.Name,
                "Estándar", "Lote", 100, 100, "mg/L", 1000, "mL", request.PreparationDate, "Test User", "Active"),
                new(Calculation.SourceMaterialId, "Estándar", null, null, "Lote", "Marca", "Ensayo", 100, 100, 100, "mg", new DateOnly(2027, 12, 31), "Laboratorio", Calculation.SourceVersion),
                Result, new(Guid.NewGuid(), 100, "mg", 100, 0, "Test User", DateTimeOffset.UtcNow), request.Notes, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, Guid.NewGuid())));
        }
        public Task<OperationResult<StockPage>> ListAsync(string? search, int page, int pageSize, CancellationToken cancellationToken, PreparationListFilter? filter = null) => Task.FromResult(OperationResult.Success(new StockPage([], page, pageSize, 0)));
        public Task<OperationResult<StockDetail>> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(OperationResult.Failure<StockDetail>(new(ErrorCodes.NotFound, "No existe.")));
        private static readonly StockCalculation Result = new(100, 100, "mg", 100, 0, 0, true, 100, 100, 100, "mg/L", 1000, "mL", StockCalculator.MassFormula);
    }
}

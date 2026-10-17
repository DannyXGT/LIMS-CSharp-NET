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

public sealed class SolutionGroupEndpointsTests : IDisposable
{
    private string Path => "/api/reference-materials/preparations/" + _route;
    private string _route = "curve";
    private readonly AuthenticationApiFactory _base = new();
    private readonly WebApplicationFactory<Program> _factory;
    private readonly Repository _repository = new();
    private readonly HttpClient _client;
    public SolutionGroupEndpointsTests()
    {
        _factory = _base.WithWebHostBuilder(b => b.ConfigureServices(s => { s.RemoveAll<ISolutionGroupRepository>(); s.AddSingleton<ISolutionGroupRepository>(_repository); }));
        _client = _factory.CreateClient();
    }
    [Theory]
    [InlineData("curve", "curva.view")]
    [InlineData("aqs", "aqs.view")]
    public async Task AnonymousAndViewerCannotCreateButViewerCanReadListAndDetail(string route, string view)
    {
        _route = route;
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync(Path)).StatusCode);
        _client.DefaultRequestHeaders.Authorization = Bearer("Usuario", view);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync(Path + "?page=2")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync(Path + "/" + Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.GetAsync(Path + "/options")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.PostAsJsonAsync(Path, Request())).StatusCode);
    }
    [Theory]
    [InlineData("curve")]
    [InlineData("aqs")]
    public async Task AdministratorOptionsPreviewAndCreateUseRealCalculatorAndAuthenticatedActor(string route)
    {
        _route = route;
        _client.DefaultRequestHeaders.Authorization = Bearer("Administrador");
        var options = (await _client.GetFromJsonAsync<IntermediateOptions>(Path + "/options"))!;
        Assert.Single(options.Methods); Assert.Single(options.Sources);
        var request = Request();
        using var preview = await _client.PostAsJsonAsync(Path + "/preview", request.Calculation);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Equal(25, (await preview.Content.ReadFromJsonAsync<SolutionCalculation>())!.Parts[0].Results[0].Concentration);
        using var created = await _client.PostAsJsonAsync(Path, request);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(AuthenticationApiFactory.UserId, _repository.Actor);
        Assert.Equal(Path + "/" + request.RequestId.ToString("D"), created.Headers.Location!.ToString());
    }
    [Theory]
    [InlineData("curve", "curva.view", "curva.create")]
    [InlineData("aqs", "aqs.view", "aqs.create")]
    public async Task ExplicitIndividualCreateWorksAndLegacyStandardAccessGrantsNothingNew(string route, string view, string create)
    {
        _route = route;
        _client.DefaultRequestHeaders.Authorization = Bearer("Analista", "reference_materials.view", "reference_materials.create");
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.GetAsync(Path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.PostAsJsonAsync(Path, Request())).StatusCode);
        _client.DefaultRequestHeaders.Authorization = Bearer("Analista", view, create);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync(Path)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await _client.PostAsJsonAsync(Path, Request())).StatusCode);
        _base.SetIdentity("Analista");
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.GetAsync(Path)).StatusCode);
    }
    [Fact]
    public async Task InvalidDatesAndFormulaUnitsReturnHumanValidationErrors()
    {
        _client.DefaultRequestHeaders.Authorization = Bearer("Administrador"); var request = Request();
        using var invalidDate = await _client.PostAsJsonAsync(Path, request with { PreparationDate = request.PreparationDate.AddDays(1) });
        Assert.Equal(HttpStatusCode.BadRequest, invalidDate.StatusCode);
        using var invalidUnit = await _client.PostAsJsonAsync(Path + "/preview", request.Calculation with { Parts = [request.Calculation.Parts[0] with { FinalVolumeUnit = "g" }] });
        Assert.Equal(HttpStatusCode.BadRequest, invalidUnit.StatusCode);
        Assert.Equal(ErrorCodes.ValidationError, (await invalidUnit.Content.ReadFromJsonAsync<ApiError>())!.Code);
    }
    private CreateSolutionGroupRequest Request()
    {
        var source = _repository.Source; var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-6)).DateTime);
        return new(Guid.NewGuid(), "CURVA-TEST", null, new(1, [new(source.Id, source.Version, 25, "mL", 100, "mL")]), today, today.AddDays(30), null);
    }
    private AuthenticationHeaderValue Bearer(string role, params string[] permissions)
    {
        _base.SetIdentity(role, permissions);
        var claims = new List<Claim> { new(JwtRegisteredClaimNames.Sub, AuthenticationApiFactory.UserId.ToString(System.Globalization.CultureInfo.InvariantCulture)), new(JwtAccessTokenService.SessionIdClaim, AuthenticationApiFactory.SessionId.ToString("D")), new(ClaimTypes.Role, role) };
        claims.AddRange(permissions.Select(p => new Claim(JwtAccessTokenService.PermissionClaim, p)));
        var token = new JwtSecurityToken("Lims.Api.Tests", "Lims.Api.Tests.Client", claims, expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(AuthenticationApiFactory.SigningKey)), SecurityAlgorithms.HmacSha256));
        return new("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
    }
    private sealed class Repository : ISolutionGroupRepository
    {
        public int Actor { get; private set; }
        public IntermediateSource Source { get; } = new(Guid.NewGuid(), "Intermedia", "INT-TEST", "Analito", 1, "Ensayo", 100, 100, "mL", new(2026, 1, 1), new(2099, 1, 1), Guid.NewGuid(), [new(Guid.NewGuid(), "Analito", null, "LOTE", 100, "mg/L")]);
        public Task<IntermediateOptions> OptionsAsync(DateOnly today, CancellationToken cancellationToken) => Task.FromResult(new IntermediateOptions([new(1, "Ensayo")], [Source]));
        public Task<OperationResult<SolutionGroupDetail>> CreateAsync(string kind, CreateSolutionGroupRequest request, int actor, string fingerprint, DateTimeOffset now, CancellationToken cancellationToken)
        {
            Actor = actor; var calculation = SolutionGroupCalculator.Calculate(request.Calculation, [Source]);
            return Task.FromResult(OperationResult.Success(new SolutionGroupDetail(new(request.RequestId, kind, request.Code, request.Name ?? request.Code, "Ensayo", 1, Source.Code, request.PreparationDate, request.ExpirationDate, "Test User", "Active"), calculation, [], null, null, now, Guid.NewGuid())));
        }
        public Task<SolutionGroupPage> ListAsync(string kind, string? search, int page, int pageSize, DateOnly today, CancellationToken cancellationToken, PreparationListFilter? filter = null) => Task.FromResult(new SolutionGroupPage([], page, pageSize, 0));
        public Task<SolutionGroupDetail?> GetAsync(string kind, Guid id, DateOnly today, CancellationToken cancellationToken) => Task.FromResult<SolutionGroupDetail?>(null);
    }
    public void Dispose() { _client.Dispose(); _factory.Dispose(); _base.Dispose(); }
}

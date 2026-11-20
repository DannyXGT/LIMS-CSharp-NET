using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Lims.Contracts.Planning;
using Lims.Infrastructure.Security;
using Microsoft.IdentityModel.Tokens;

namespace Lims.Api.Tests;

public sealed class PlanningAuthorizationTests : IDisposable
{
    private readonly AuthenticationApiFactory _factory=new();
    private readonly HttpClient _client;
    public PlanningAuthorizationTests(){_client=_factory.CreateClient(new(){BaseAddress=new Uri("https://localhost")});}
    [Theory]
    [InlineData("/api/workspaces")]
    [InlineData("/api/planning/jobs")]
    [InlineData("/api/planning/report-list")]
    [InlineData("/api/planning/imports")]
    [InlineData("/api/planning/reviews")]
    [InlineData("/api/planning/reviews/workspace")]
    [InlineData("/api/planning/reviews/export")]
    [InlineData("/api/planning/imports/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/inspection")]
    [InlineData("/api/planning/export")]
    public async Task AllReadsRequireExistingAuthenticatedSession(string path)=>Assert.Equal(HttpStatusCode.Unauthorized,(await _client.GetAsync(path)).StatusCode);
    [Theory]
    [InlineData("/api/planning/jobs/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/wip","PUT")]
    [InlineData("/api/planning/jobs/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/control","PUT")]
    [InlineData("/api/planning/jobs/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/manual-tests","POST")]
    [InlineData("/api/planning/imports/preview","POST")]
    [InlineData("/api/planning/imports/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/confirm","POST")]
    [InlineData("/api/planning/incidents/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa","PUT")]
    [InlineData("/api/planning/equivalences","PUT")]
    [InlineData("/api/planning/source-resolution","POST")]
    [InlineData("/api/planning/notes","POST")]
    [InlineData("/api/planning/report-list/check","POST")]
    [InlineData("/api/planning/report-list/correction","POST")]
    [InlineData("/api/planning/report-list/keep","PUT")]
    [InlineData("/api/planning/report-list/scan","POST")]
    [InlineData("/api/planning/reviews/decision","PUT")]
    [InlineData("/api/planning/access/users/42","PUT")]
    public async Task ReadPermissionDoesNotAuthorizeDirectWritesEvenWithOldAdministratorToken(string path,string method)
    {
        _factory.SetIdentity("Analista",PlanningPermissions.View);_client.DefaultRequestHeaders.Authorization=Token();
        using var request=new HttpRequestMessage(new HttpMethod(method),path){Content=JsonContent.Create(new{})};
        Assert.Equal(HttpStatusCode.Forbidden,(await _client.SendAsync(request)).StatusCode);
    }
    [Fact]
    public async Task WorkspaceListUsesCurrentPermissionsAndDoesNotCreateAnotherSession()
    {
        _factory.SetIdentity("Analista",PlanningPermissions.View);_client.DefaultRequestHeaders.Authorization=Token();
        var initial=(await _client.GetFromJsonAsync<WorkspaceAccess>("/api/workspaces"))!;Assert.True(initial.Planning);Assert.False(initial.Laboratory);Assert.False(initial.Administration);
        _factory.SetIdentity("Analista");var revoked=(await _client.GetFromJsonAsync<WorkspaceAccess>("/api/workspaces"))!;Assert.False(revoked.Planning);
        Assert.Equal(HttpStatusCode.Forbidden,(await _client.GetAsync("/api/planning/jobs")).StatusCode);
    }
    private static AuthenticationHeaderValue Token()
    {
        var claims=new[]{new Claim(JwtRegisteredClaimNames.Sub,AuthenticationApiFactory.UserId.ToString(CultureInfo.InvariantCulture)),new Claim("sid",AuthenticationApiFactory.SessionId.ToString("D")),new Claim(ClaimTypes.Role,"Administrador"),new Claim("permission",PlanningPermissions.Import)};
        var token=new JwtSecurityToken("Lims.Api.Tests","Lims.Api.Tests.Client",claims,expires:DateTime.UtcNow.AddMinutes(5),signingCredentials:new(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(AuthenticationApiFactory.SigningKey)),SecurityAlgorithms.HmacSha256));
        return new("Bearer",new JwtSecurityTokenHandler().WriteToken(token));
    }
    public void Dispose(){_client.Dispose();_factory.Dispose();}
}

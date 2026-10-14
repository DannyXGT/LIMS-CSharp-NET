using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Lims.Application.Dashboard;
using Lims.Contracts.Dashboard;
using Lims.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
namespace Lims.Api.Tests;
public sealed class DashboardEndpointsTests
{
    [Fact]
    public async Task DashboardRequiresLiveSessionAndUsesOnlyAuthenticatedUser()
    {
        await using var factory=new AuthenticationApiFactory(); var fake=new DashboardFake();
        await using var web=factory.WithWebHostBuilder(b=>b.ConfigureServices(s=>{s.RemoveAll<IDashboardRepository>();s.AddSingleton<IDashboardRepository>(fake);}));
        using var client=web.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync("/api/dashboard")).StatusCode);
        var claims=new[]{new Claim(JwtRegisteredClaimNames.Sub,AuthenticationApiFactory.UserId.ToString(CultureInfo.InvariantCulture)),new Claim(JwtAccessTokenService.SessionIdClaim,AuthenticationApiFactory.SessionId.ToString("D")),new Claim(ClaimTypes.Role,"Administrador")};
        client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken("Lims.Api.Tests","Lims.Api.Tests.Client",claims,expires:DateTime.UtcNow.AddMinutes(5),signingCredentials:new(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(AuthenticationApiFactory.SigningKey)),SecurityAlgorithms.HmacSha256))));
        factory.SetIdentity("Analista");
        var response=await client.GetAsync("/api/dashboard?userId=999&role=Administrador");
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.Equal(AuthenticationApiFactory.UserId,fake.UserId);
        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.NotNull(await response.Content.ReadFromJsonAsync<DashboardResponse>());
        factory.PermissionRepository.Current=null;
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync("/api/dashboard")).StatusCode);
    }
    private sealed class DashboardFake:IDashboardRepository
    {
        public int UserId {get;private set;}
        public Task<DashboardResponse?> GetAsync(int userId,CancellationToken ct)
        {UserId=userId;return Task.FromResult<DashboardResponse?>(new("Restricted","Test","Analista",null,DateTimeOffset.UtcNow,null,[],null,null,null,[],[],[]));}
    }
}

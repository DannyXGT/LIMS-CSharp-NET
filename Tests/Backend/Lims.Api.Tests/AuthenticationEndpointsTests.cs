using System.IdentityModel.Tokens.Jwt;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Lims.Api.Authentication;
using Lims.Contracts.Authentication;
using Lims.Contracts.Errors;
using Lims.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Lims.Api.Tests;

public sealed class AuthenticationEndpointsTests : IClassFixture<AuthenticationApiFactory>
{
    private readonly AuthenticationApiFactory _factory;
    private readonly HttpClient _client;

    public AuthenticationEndpointsTests(AuthenticationApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new() { AllowAutoRedirect = false });
    }

    [Fact]
    public async Task LiveHealthReturnsHealthyWithoutDatabaseDependency()
    {
        using var response = await _client.GetAsync(new Uri("/health/live", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("X-Correlation-ID"));
    }

    [Fact]
    public async Task MeWithoutBearerTokenReturnsStableUnauthorizedError()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Add("X-Correlation-ID", "test-correlation-401");

        using var response = await _client.SendAsync(request);
        var error = await response.Content.ReadFromJsonAsync<ApiError>();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotNull(error);
        Assert.Equal(ErrorCodes.InvalidSession, error.Code);
        Assert.Equal("test-correlation-401", error.CorrelationId);
    }

    [Fact]
    public async Task LoginSuccessReturnsProfileAndTokens()
    {
        using var response = await _client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest("valid.user", "correct-password", "tests"));
        var authentication = await response.Content.ReadFromJsonAsync<AuthenticationResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(authentication);
        Assert.Equal(AuthenticationApiFactory.UserId, authentication.Profile.Id);
        Assert.Equal("Administrador", authentication.Profile.Role);
        Assert.False(string.IsNullOrWhiteSpace(authentication.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(authentication.RefreshToken));
    }

    [Fact]
    public async Task InvalidLoginReturnsGenericCredentialError()
    {
        using var response = await _client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest("missing.user", "wrong-password", "tests"));
        var error = await response.Content.ReadFromJsonAsync<ApiError>();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotNull(error);
        Assert.Equal(ErrorCodes.InvalidCredentials, error.Code);
        Assert.DoesNotContain("missing.user", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IdentifierCooldownReturnsTooManyRequestsAfterRepeatedFailures()
    {
        const string identifier = "rate-limited.user";
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var failed = await _client.PostAsJsonAsync(
                "/api/auth/login",
                new LoginRequest(identifier, "wrong-password", "tests"));
            Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
        }

        using var limited = await _client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(identifier, "wrong-password", "tests"));
        var error = await limited.Content.ReadFromJsonAsync<ApiError>();

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.NotNull(error);
        Assert.Equal(ErrorCodes.RateLimitExceeded, error.Code);
    }

    [Fact]
    public async Task MeWithActiveServerSessionReturnsAuthoritativeProfile()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CreateAccessToken());

        using var response = await _client.SendAsync(request);
        var profile = await response.Content.ReadFromJsonAsync<UserProfile>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(profile);
        Assert.Equal(AuthenticationApiFactory.UserId, profile.Id);
        Assert.Contains("users.manage", profile.Permissions);
    }

    [Fact]
    public async Task AuthorizationPoliciesKeepRoleDepartmentAndPermissionSeparate()
    {
        using var scope = _factory.Services.CreateScope();
        var authorization = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Role, "Administrador"),
            new Claim("department", "Laboratorio Químico"),
            new Claim(JwtAccessTokenService.PermissionClaim, "users.manage"),
        ], "tests"));

        Assert.True((await authorization.AuthorizeAsync(principal, null, LimsPolicies.RequireAdministrator)).Succeeded);
        Assert.True((await authorization.AuthorizeAsync(principal, null, LimsPolicies.ChemicalDepartment)).Succeeded);
        Assert.True((await authorization.AuthorizeAsync(principal, null, LimsPolicies.UsersManage)).Succeeded);

        var ordinaryUser = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, "Usuario")],
            "tests"));
        Assert.False((await authorization.AuthorizeAsync(
            ordinaryUser,
            null,
            LimsPolicies.RequireAdministrator)).Succeeded);
    }

    private static string CreateAccessToken()
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(AuthenticationApiFactory.SigningKey));
        var token = new JwtSecurityToken(
            "Lims.Api.Tests",
            "Lims.Api.Tests.Client",
            [
                new Claim(
                    JwtRegisteredClaimNames.Sub,
                    AuthenticationApiFactory.UserId.ToString(CultureInfo.InvariantCulture)),
                new Claim(JwtAccessTokenService.SessionIdClaim, AuthenticationApiFactory.SessionId.ToString("D")),
                new Claim(ClaimTypes.Name, "Test User"),
                new Claim(ClaimTypes.Role, "Administrador"),
            ],
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

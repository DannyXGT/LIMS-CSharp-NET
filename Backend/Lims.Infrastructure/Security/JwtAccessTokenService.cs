using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Lims.Application.Authentication.Ports;
using Lims.Application.Authentication.Security;
using Lims.Domain.Authentication;
using Lims.Domain.Identity;
using Microsoft.IdentityModel.Tokens;

namespace Lims.Infrastructure.Security;

public sealed class JwtAccessTokenService : IAccessTokenService
{
    public const string SessionIdClaim = "sid";
    public const string PermissionClaim = "permission";

    private readonly JwtOptions _options;
    private readonly SigningCredentials _signingCredentials;

    public JwtAccessTokenService(JwtOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _options = options;
        _signingCredentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)),
            SecurityAlgorithms.HmacSha256);
    }

    public IssuedAccessToken Issue(
        User user,
        AuthSession session,
        DateTimeOffset issuedAt,
        TimeSpan lifetime)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(session);

        var expiresAt = issuedAt.Add(lifetime);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString(CultureInfo.InvariantCulture)),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("D")),
            new(SessionIdClaim, session.Id.ToString("D")),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(ClaimTypes.Name, user.Name),
            new(ClaimTypes.Role, user.Role?.Name ?? string.Empty),
        };

        if (user.Department is not null)
        {
            claims.Add(new Claim("department", user.Department.Name));
        }

        claims.AddRange(user.Permissions.Select(permission => new Claim(PermissionClaim, permission.Key)));

        var token = new JwtSecurityToken(
            _options.Issuer,
            _options.Audience,
            claims,
            issuedAt.UtcDateTime,
            expiresAt.UtcDateTime,
            _signingCredentials);
        return new IssuedAccessToken(new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}

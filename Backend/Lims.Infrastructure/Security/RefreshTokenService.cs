using System.Security.Cryptography;
using System.Text;
using Lims.Application.Authentication.Ports;
using Lims.Application.Authentication.Security;

namespace Lims.Infrastructure.Security;

public sealed class RefreshTokenService : IRefreshTokenService
{
    public GeneratedRefreshToken Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var token = Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return new GeneratedRefreshToken(token, Hash(token));
    }

    public string Hash(string token)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
}

using Lims.Application.Authentication.Security;

namespace Lims.Application.Authentication.Ports;

public interface IRefreshTokenService
{
    GeneratedRefreshToken Generate();

    string Hash(string token);
}

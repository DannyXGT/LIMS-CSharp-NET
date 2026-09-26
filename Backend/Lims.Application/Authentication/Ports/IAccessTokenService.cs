using Lims.Application.Authentication.Security;
using Lims.Domain.Authentication;
using Lims.Domain.Identity;

namespace Lims.Application.Authentication.Ports;

public interface IAccessTokenService
{
    IssuedAccessToken Issue(
        User user,
        AuthSession session,
        DateTimeOffset issuedAt,
        TimeSpan lifetime);
}

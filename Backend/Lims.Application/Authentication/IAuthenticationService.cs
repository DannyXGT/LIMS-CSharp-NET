using Lims.Application.Common;
using Lims.Contracts.Authentication;

namespace Lims.Application.Authentication;

public interface IAuthenticationService
{
    Task<OperationResult<AuthenticationResponse>> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken);

    Task<OperationResult<AuthenticationResponse>> RefreshAsync(
        RefreshRequest request,
        CancellationToken cancellationToken);

    Task<OperationResult<LogoutResponse>> LogoutAsync(
        Guid sessionId,
        CancellationToken cancellationToken);

    Task<OperationResult<UserProfile>> GetCurrentUserAsync(
        Guid sessionId,
        int userId,
        CancellationToken cancellationToken);
}

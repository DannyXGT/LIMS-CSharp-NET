using Lims.Contracts.Authentication;

namespace Lims.Desktop.Http;

internal interface IAuthenticationApiClient
{
    Task<ApiCallResult<AuthenticationResponse>> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken);

    Task<ApiCallResult<AuthenticationResponse>> RefreshAsync(
        RefreshRequest request,
        CancellationToken cancellationToken);

    Task<bool> LogoutAsync(CancellationToken cancellationToken);
}

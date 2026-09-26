namespace Lims.Desktop.Services;

public interface IAuthenticationGateway
{
    Task<AuthenticationOutcome> SignInAsync(
        string identifier,
        string password,
        CancellationToken cancellationToken);

    Task<LogoutOutcome> LogoutAsync(CancellationToken cancellationToken);
}

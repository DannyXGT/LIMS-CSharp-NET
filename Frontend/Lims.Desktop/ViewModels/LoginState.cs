namespace Lims.Desktop.ViewModels;

public enum LoginState
{
    Idle,
    Authenticating,
    InvalidCredentials,
    NetworkUnavailable,
    Timeout,
    ServerUnavailable,
    UnexpectedError,
    Authenticated,
}

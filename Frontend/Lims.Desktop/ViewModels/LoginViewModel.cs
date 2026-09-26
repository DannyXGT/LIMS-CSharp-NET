using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lims.Desktop.Services;

namespace Lims.Desktop.ViewModels;

public sealed partial class LoginViewModel(
    IAuthenticationGateway authentication,
    INavigationService navigation,
    ShellViewModel shell,
    DesktopOptions options) : ObservableObject
{
    private int _signInInProgress;
    private CancellationTokenSource? _signInCancellation;

    [ObservableProperty]
    public partial string Identifier { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Password { get; set; } = string.Empty;

    [ObservableProperty]
    public partial LoginState State { get; set; }

    [ObservableProperty]
    public partial string Message { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SupportId { get; set; } = string.Empty;

    public string CorporateDomain => string.Concat("@", options.CorporateDomain);

    public string EnvironmentLabel => options.EnvironmentName switch
    {
        "Production" => "Producción",
        "Development" => "Desarrollo",
        "Testing" => "Pruebas",
        _ => options.EnvironmentName,
    };

    public string VersionLabel { get; } = GetVersionLabel();

    public bool IsBusy => State == LoginState.Authenticating;

    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);

    public string ActionLabel => IsBusy ? "Iniciando sesión..." : "Iniciar sesión";

    public bool CanSignIn =>
        !IsBusy && Volatile.Read(ref _signInInProgress) == 0 &&
        !string.IsNullOrWhiteSpace(Identifier) &&
        !string.IsNullOrEmpty(Password);

    [RelayCommand(AllowConcurrentExecutions = false, CanExecute = nameof(CanSignIn))]
    private async Task SignInAsync()
    {
        if (Interlocked.CompareExchange(ref _signInInProgress, 1, 0) != 0)
        {
            return;
        }

        try
        {
            using var cancellation = new CancellationTokenSource();
            _signInCancellation = cancellation;
            State = LoginState.Authenticating;
            Message = string.Empty;
            SupportId = string.Empty;
            try
            {
                var outcome = await authentication.SignInAsync(Identifier, Password, cancellation.Token);
                State = outcome.Kind switch
                {
                    AuthenticationOutcomeKind.Authenticated => LoginState.Authenticated,
                    AuthenticationOutcomeKind.InvalidCredentials => LoginState.InvalidCredentials,
                    AuthenticationOutcomeKind.NetworkUnavailable => LoginState.NetworkUnavailable,
                    AuthenticationOutcomeKind.Timeout => LoginState.Timeout,
                    AuthenticationOutcomeKind.ServerUnavailable => LoginState.ServerUnavailable,
                    _ => LoginState.UnexpectedError,
                };
                Message = MessageFor(State);
                SupportId = string.IsNullOrWhiteSpace(outcome.CorrelationId)
                    ? string.Empty
                    : string.Concat("ID de soporte: ", outcome.CorrelationId);

                if (State == LoginState.Authenticated)
                {
                    Password = string.Empty;
                    shell.RefreshProfile();
                    navigation.ShowAuthenticatedShell();
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                State = LoginState.Idle;
            }
        }
        finally
        {
            _signInCancellation = null;
            Interlocked.Exchange(ref _signInInProgress, 0);
            OnPropertyChanged(nameof(CanSignIn));
            SignInCommand.NotifyCanExecuteChanged();
        }
    }

    public void CancelPendingSignIn() => _signInCancellation?.Cancel();

    partial void OnIdentifierChanged(string value)
    {
        OnPropertyChanged(nameof(CanSignIn));
        SignInCommand.NotifyCanExecuteChanged();
    }

    partial void OnPasswordChanged(string value)
    {
        OnPropertyChanged(nameof(CanSignIn));
        SignInCommand.NotifyCanExecuteChanged();
    }

    partial void OnMessageChanged(string value) => OnPropertyChanged(nameof(HasMessage));

    partial void OnStateChanged(LoginState value)
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(ActionLabel));
        OnPropertyChanged(nameof(CanSignIn));
        SignInCommand.NotifyCanExecuteChanged();
    }

    private static string MessageFor(LoginState state) => state switch
    {
        LoginState.InvalidCredentials => "El usuario o la contraseña no son válidos.",
        LoginState.NetworkUnavailable => "No se puede conectar con el servidor LIMS.",
        LoginState.Timeout => "El servidor tardó demasiado en responder. Puede intentarlo nuevamente.",
        LoginState.ServerUnavailable => "El servicio LIMS no está disponible temporalmente.",
        LoginState.UnexpectedError => "Ocurrió un error inesperado. Intente nuevamente.",
        _ => string.Empty,
    };

    private static string GetVersionLabel()
    {
        var version = typeof(LoginViewModel).Assembly.GetName().Version;
        return version is null
            ? "v1.0.0"
            : string.Concat("v", version.Major, ".", version.Minor, ".", version.Build);
    }
}

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lims.Desktop.Services;

namespace Lims.Desktop.ViewModels;

public sealed partial class ShellViewModel(
    ISessionService session,
    IAuthenticationGateway authentication,
    INavigationService navigation) : ObservableObject
{
    [ObservableProperty]
    public partial string DisplayName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Role { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Department { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SessionMessage { get; set; } = "Sesión activa";

    public void RefreshProfile()
    {
        var profile = session.Profile;
        DisplayName = profile?.Name ?? string.Empty;
        Role = profile?.Role ?? string.Empty;
        Department = profile?.Department ?? "Sin departamento";
        SessionMessage = "Sesión activa";
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task LogoutAsync(CancellationToken cancellationToken)
    {
        var outcome = await authentication.LogoutAsync(cancellationToken);
        DisplayName = string.Empty;
        Role = string.Empty;
        Department = string.Empty;
        SessionMessage = outcome.RemoteRevoked
            ? "Sesión cerrada"
            : "Sesión local cerrada; la revocación remota no pudo confirmarse.";
        navigation.ShowLogin();
    }
}

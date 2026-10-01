using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lims.Desktop.Services;
using Lims.Contracts.ReferenceMaterials;

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

    public string Initials
    {
        get
        {
            var parts = DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return string.Concat(parts.Take(2).Select(part => char.ToUpperInvariant(part[0])));
        }
    }

    public bool CanViewReferenceMaterials =>
        string.Equals(session.Profile?.Role, "Administrador", StringComparison.OrdinalIgnoreCase) ||
        session.Profile?.Permissions.Contains(ReferenceMaterialPermissions.View, StringComparer.Ordinal) == true;

    public void RefreshProfile()
    {
        var profile = session.Profile;
        DisplayName = profile?.Name ?? string.Empty;
        Role = profile?.Role ?? string.Empty;
        Department = profile?.Department ?? "Sin departamento";
        SessionMessage = "Sesión activa";
        OnPropertyChanged(nameof(Initials));
        OnPropertyChanged(nameof(CanViewReferenceMaterials));
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

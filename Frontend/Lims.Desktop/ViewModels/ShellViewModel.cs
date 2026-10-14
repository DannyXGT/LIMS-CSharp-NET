using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lims.Desktop.Services;
using Lims.Contracts.ReferenceMaterials;
using Lims.Contracts.Permissions;
using Lims.Desktop.Http;

namespace Lims.Desktop.ViewModels;

public sealed partial class ShellViewModel(
    ISessionService session,
    IAuthenticationGateway authentication,
    INavigationService navigation,
    IPermissionsApiClient? permissionsApi = null) : ObservableObject
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

    private bool _accessVerified = true;
    public bool HasAccessWarning => !_accessVerified;

    public bool CanViewReferenceMaterials => _accessVerified && (
        string.Equals(session.Profile?.Role, "Administrador", StringComparison.OrdinalIgnoreCase) ||
        session.Profile?.Permissions.Contains(ReferenceMaterialPermissions.View, StringComparer.Ordinal) == true);

    public bool IsAdministrator => session.Profile?.Role == "Administrador";
    public bool CanViewStock => Has(PermissionCatalog.StockView);
    public bool CanViewIntermediate => Has(PermissionCatalog.IntermediateView);
    public bool CanViewPreparations => CanViewStock || CanViewIntermediate;
    private bool Has(string code) => _accessVerified && (IsAdministrator || session.Profile?.Permissions.Contains(code, StringComparer.Ordinal) == true);
    public async Task<bool> RefreshAccessAsync()
    {
        if (permissionsApi is null) { RefreshProfile(); return true; }
        var response = await permissionsApi.EffectiveAsync(CancellationToken.None);
        if (response.IsSuccess && response.Value is { } current)
        { _accessVerified = true; session.UpdateAuthorization(current.Role, current.Permissions); RefreshProfile(); return true; }
        // Service failures do not change identity. Operational access needs a fresh server response.
        _accessVerified = false;
        RefreshProfile();
        SessionMessage = response.StatusCode is 401 or 403
            ? "No se pudo validar la sesión. Inicie sesión nuevamente."
            : "No se pudo verificar el acceso con el servidor. Reintente para recuperar los módulos.";
        return false;
    }

    public void RefreshProfile()
    {
        var profile = session.Profile;
        DisplayName = profile?.Name ?? string.Empty;
        Role = profile?.Role ?? string.Empty;
        Department = profile?.Department ?? "Sin departamento";
        SessionMessage = "Sesión activa";
        OnPropertyChanged(nameof(HasAccessWarning));
        OnPropertyChanged(nameof(Initials));
        OnPropertyChanged(nameof(CanViewReferenceMaterials));
        OnPropertyChanged(nameof(IsAdministrator));
        OnPropertyChanged(nameof(CanViewStock));
        OnPropertyChanged(nameof(CanViewIntermediate));
        OnPropertyChanged(nameof(CanViewPreparations));
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

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Lims.Contracts.Permissions;
using Lims.Desktop.Http;
using Lims.Desktop.Services;

namespace Lims.Desktop.ViewModels;

public sealed partial class PermissionChoice(PermissionDefinition definition, bool granted) : ObservableObject
{
    public PermissionDefinition Definition { get; } = definition;
    [ObservableProperty] public partial bool IsGranted { get; set; } = granted;
}
public sealed record PermissionGroup(string Name, IReadOnlyList<PermissionChoice> Items);
public sealed record PermissionHistoryRow(string Permission, string Actor, string Action, string Change, string At);

public sealed partial class PermissionsViewModel(IPermissionsApiClient api, ISessionService session) : ObservableObject
{
    private UserPermissionState? _saved;
    private int _revision;
    private IReadOnlyList<PermissionDefinition> _catalog = [];
    public ObservableCollection<PermissionUser> Users { get; } = [];
    public ObservableCollection<PermissionGroup> Groups { get; } = [];
    public ObservableCollection<PermissionHistoryRow> History { get; } = [];
    [ObservableProperty] public partial PermissionUser? SelectedUser { get; set; }
    [ObservableProperty] public partial string Search { get; set; } = string.Empty;
    [ObservableProperty] public partial string Message { get; set; } = string.Empty;
    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);
    partial void OnMessageChanged(string value) => OnPropertyChanged(nameof(HasMessage));
    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial bool HasPendingChanges { get; set; }
    [ObservableProperty] public partial bool IsLoaded { get; set; }
    [ObservableProperty] public partial int HistoryPage { get; set; } = 1;
    [ObservableProperty] public partial bool HasMoreHistory { get; set; }
    public bool IsAdministrator => session.Profile?.Role == "Administrador";
    public bool CanSave => IsAdministrator && IsLoaded && (HasPendingChanges || _saved?.IsConfigured == false) && !IsBusy;
    public bool CanEdit => IsAdministrator && IsLoaded && !IsBusy;
    public string ChangeStatus => HasPendingChanges ? "Cambios pendientes de guardar" : !IsLoaded ? "Seleccione un usuario" : _saved?.IsConfigured == false ? "Sin matriz individual configurada" : "Permisos guardados";
    public string AccessNote => SelectedUser is null ? "Seleccione un usuario para consultar y modificar sus permisos." : SelectedUser.Role == "Administrador"
        ? "Este usuario conserva acceso completo por su rol Administrador. La matriz muestra únicamente sus asignaciones individuales."
        : _saved?.IsConfigured == false ? "Este usuario aún no tiene una matriz individual configurada. Guardar activará sus permisos individuales, incluso si deja todas las casillas vacías." : "Solo las casillas guardadas conceden acceso individual.";
    partial void OnIsBusyChanged(bool value) => Notify();
    partial void OnHasPendingChangesChanged(bool value) => Notify();
    partial void OnIsLoadedChanged(bool value) => Notify();
    private void Notify() { OnPropertyChanged(nameof(CanSave)); OnPropertyChanged(nameof(CanEdit)); OnPropertyChanged(nameof(ChangeStatus)); OnPropertyChanged(nameof(AccessNote)); }
    public async Task LoadUsersAsync(CancellationToken ct = default)
    {
        if (!IsAdministrator) { RevokeAccess(); return; }
        IsBusy = true;
        try
        {
            var catalog = await api.CatalogAsync(ct);
            if (!catalog.IsSuccess || catalog.Value is null) { Message = catalog.Error?.Message ?? "No se pudo consultar el catálogo."; return; }
            _catalog = catalog.Value;
            var response = await api.UsersAsync(Search, ct);
            if (!IsAdministrator) { RevokeAccess(); return; }
            if (!response.IsSuccess || response.Value is null) { Message = response.Error?.Message ?? "No se pudo consultar los usuarios."; return; }
            Users.Clear(); foreach (var u in response.Value.Items) Users.Add(u);
            Message = !response.Value.DepartmentExists ? "No existe el departamento Laboratorio Analítico. No se pueden administrar usuarios hasta verificar su relación en la base de datos."
                : Users.Count == 0 ? "No hay usuarios activos elegibles para esta búsqueda." : string.Empty;
        }
        finally { IsBusy = false; }
    }
    public async Task<bool> SelectAsync(PermissionUser user, bool discardPending = false, CancellationToken ct = default)
    {
        if (IsBusy || HasPendingChanges && !discardPending) return false;
        if (!IsAdministrator) { Clear(); Message = "Acceso denegado."; return false; }
        Clear(); var revision = ++_revision; SelectedUser = user; IsBusy = true;
        try
        {
            var response = await api.GetAsync(user.Id, ct);
            if (revision != _revision) return false;
            if (!response.IsSuccess || response.Value is null) { Message = response.Error?.Message ?? "No se pudieron consultar los permisos."; return false; }
            Apply(response.Value); Message = string.Empty; await LoadHistoryAsync(ct); return true;
        }
        finally { IsBusy = false; }
    }
    private void Apply(UserPermissionState state)
    {
        _saved = state; SelectedUser = state.User; Groups.Clear();
        foreach (var group in _catalog.GroupBy(p => p.Module))
        {
            var choices = group.Select(p => new PermissionChoice(p, state.Permissions.Contains(p.Code, StringComparer.Ordinal))).ToArray();
            foreach (var choice in choices) choice.PropertyChanged += (_, _) => Recalculate();
            Groups.Add(new(group.Key, choices));
        }
        IsLoaded = true; Recalculate(); Notify();
    }
    private string[] Selection() => Groups.SelectMany(g => g.Items).Where(p => p.IsGranted).Select(p => p.Definition.Code).ToArray();
    private void Recalculate() => HasPendingChanges = _saved is not null && !_saved.Permissions.ToHashSet(StringComparer.Ordinal).SetEquals(Selection());
    public async Task<bool> SaveAsync(CancellationToken ct = default)
    {
        if (!CanSave || _saved is null) return false;
        var revision = _revision;
        IsBusy = true;
        try
        {
            var response = await api.SaveAsync(_saved.User.Id, new(Selection(), _saved.Version), ct);
            if (revision != _revision || !IsAdministrator) return false;
            if (!response.IsSuccess || response.Value is null) { Message = response.Error?.Message ?? "No se pudo guardar. Los cambios siguen pendientes."; return false; }
            Apply(response.Value); Message = "Cambios guardados y confirmados por el servidor."; HistoryPage = 1; await LoadHistoryAsync(ct); return true;
        }
        finally { IsBusy = false; }
    }
    public async Task LoadHistoryAsync(CancellationToken ct = default)
    {
        if (!IsAdministrator || SelectedUser is null) return;
        var user = SelectedUser.Id; var page = HistoryPage;
        var response = await api.HistoryAsync(user, page, ct);
        if (!IsAdministrator || SelectedUser?.Id != user || HistoryPage != page) return;
        if (!response.IsSuccess || response.Value is null) { Message = response.Error?.Message ?? "No se pudo consultar el historial."; return; }
        History.Clear(); foreach (var entry in response.Value.Items)
            History.Add(new(_catalog.FirstOrDefault(p => p.Code == entry.Permission)?.Name ?? "Permiso", entry.Actor, entry.Action,
                $"{(entry.Before ? "Permitido" : "Sin asignación")} → {(entry.After ? "Permitido" : "Sin asignación")}", entry.At.ToLocalTime().ToString("g", System.Globalization.CultureInfo.CurrentCulture)));
        HasMoreHistory = HistoryPage * 30 < response.Value.Total;
    }
    public void Clear()
    {
        _revision++;
        _saved = null; SelectedUser = null; Groups.Clear(); History.Clear(); IsLoaded = false; HasPendingChanges = false; HistoryPage = 1; HasMoreHistory = false;
    }
    public void RevokeAccess()
    {
        Clear(); Users.Clear(); Message = "Acceso denegado. Solo los administradores pueden gestionar permisos.";
    }
}

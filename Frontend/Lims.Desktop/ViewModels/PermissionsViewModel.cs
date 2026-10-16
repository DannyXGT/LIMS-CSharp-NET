using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Lims.Contracts.Permissions;
using Lims.Desktop.Http;
using Lims.Desktop.Services;

namespace Lims.Desktop.ViewModels;

public sealed partial class PermissionChoice(PermissionDefinition definition, bool granted) : ObservableObject
{
    private readonly bool _savedGranted = granted;
    public PermissionDefinition Definition { get; } = definition;
    public string ActionName => Definition.Code.Split('.').Last() switch
    {
        "view" => "Consultar", "create" => "Registrar", "edit" => "Editar", "archive" => "Archivar", "replace" => "Reemplazar", _ => Definition.Name
    };
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChanged))]
    public partial bool IsGranted { get; set; } = granted;
    // Presentation only. Apply recreates choices from the server after save/discard.
    public bool IsChanged => IsGranted != _savedGranted;
}
public sealed class PermissionGroup(string name, IReadOnlyList<PermissionChoice> items) : ObservableObject
{
    public string Name { get; } = name;
    public IReadOnlyList<PermissionChoice> Items { get; } = items;
    public string Summary => $"{Items.Count(p => p.IsGranted)} de {Items.Count} seleccionados";
    public Lims.DesignSystem.Controls.LimsIconKind Icon => Name == "Estándares" ? Lims.DesignSystem.Controls.LimsIconKind.Standards : Lims.DesignSystem.Controls.LimsIconKind.Preparations;
    public void Refresh() => OnPropertyChanged(nameof(Summary));
}
public sealed partial class PermissionUserRow(PermissionUser user) : ObservableObject
{
    public PermissionUser User { get; } = user;
    public string Initials => string.Concat(User.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(s => s[0])).ToUpperInvariant();
    [ObservableProperty] public partial string Summary { get; set; } = user.Role == "Administrador" ? "Acceso por rol" : "Permisos por consultar";
    public override string ToString() => User.Name;
}
public sealed record PermissionHistoryRow(string Module, string Permission, string Actor, string Action, string Change, string At)
{
    public string Tone => Action == "Otorgado" ? "Success" : Action == "Revocado" ? "Warning" : "Neutral";
}

public sealed partial class PermissionsViewModel(IPermissionsApiClient api, ISessionService session) : ObservableObject
{
    private static readonly string[] SummaryProperties = [nameof(CanSave), nameof(CanEdit), nameof(ChangeStatus), nameof(AccessNote), nameof(HasSelection), nameof(IsRoleAdministrator), nameof(IsConfigured), nameof(SelectedCount), nameof(SavedCount), nameof(AccessibleModules), nameof(SelectedInitials), nameof(SavedSummary), nameof(LocalSummary), nameof(ModuleSummary), nameof(ConfigurationLabel), nameof(ConfigurationTone), nameof(CanConfigureEmpty), nameof(RequiresEmptyConfirmation), nameof(SaveLabel)];
    private UserPermissionState? _saved;
    private int _revision;
    private IReadOnlyList<PermissionDefinition> _catalog = [];
    public ObservableCollection<PermissionUser> Users { get; } = [];
    public ObservableCollection<PermissionUserRow> UserRows { get; } = [];
    public ObservableCollection<PermissionGroup> Groups { get; } = [];
    public ObservableCollection<PermissionHistoryRow> History { get; } = [];
    [ObservableProperty] public partial PermissionUser? SelectedUser { get; set; }
    [ObservableProperty] public partial string Search { get; set; } = string.Empty;
    [ObservableProperty] public partial string Message { get; set; } = string.Empty;
    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);
    partial void OnMessageChanged(string value) => OnPropertyChanged(nameof(HasMessage));
    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial bool IsSaving { get; set; }
    [ObservableProperty] public partial bool IsLoadingUser { get; set; }
    [ObservableProperty] public partial bool IsLoadingHistory { get; set; }
    [ObservableProperty] public partial bool MessageIsSuccess { get; set; }
    [ObservableProperty] public partial bool HasPendingChanges { get; set; }
    [ObservableProperty] public partial bool IsLoaded { get; set; }
    [ObservableProperty] public partial int HistoryPage { get; set; } = 1;
    [ObservableProperty] public partial bool HasMoreHistory { get; set; }
    [ObservableProperty] public partial int HistoryTotal { get; set; }
    public bool IsAdministrator => session.Profile?.Role == "Administrador";
    public bool CanSave => IsAdministrator && IsLoaded && HasPendingChanges && !IsBusy;
    public bool CanEdit => IsAdministrator && IsLoaded && !IsBusy;
    public bool HasSelection => SelectedUser is not null;
    public bool IsRoleAdministrator => SelectedUser?.Role == "Administrador";
    public bool IsConfigured => _saved?.IsConfigured == true;
    public int SelectedCount => Selection().Length;
    public int SavedCount => _saved?.Permissions.Count ?? 0;
    public int AccessibleModules => IsRoleAdministrator ? _catalog.Select(p => p.Module).Distinct().Count() : _catalog.Where(p => p.Code.EndsWith(".view", StringComparison.Ordinal) && (_saved?.Permissions.Contains(p.Code) ?? false)).Select(p => p.Module).Distinct().Count();
    public string SelectedInitials => SelectedUser is null ? string.Empty : string.Concat(SelectedUser.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(s => s[0])).ToUpperInvariant();
    public string SavedSummary => !IsLoaded ? "Por consultar" : !IsConfigured ? "Sin configuración individual" : $"{SavedCount} permisos guardados";
    public string LocalSummary => $"{SelectedCount} seleccionados";
    public string ModuleSummary => !IsLoaded ? "Por consultar" : IsRoleAdministrator ? $"{AccessibleModules} módulos · acceso por rol" : !IsConfigured ? "Sin configuración individual" : $"{AccessibleModules} módulos con consulta";
    public string ConfigurationLabel => !IsLoaded ? "Por consultar" : IsRoleAdministrator ? "Administrador · acceso por rol" : !IsConfigured ? "Sin configuración individual" : SavedCount == 0 ? "Configurado · sin permisos" : "Configuración individual";
    public string ConfigurationTone => IsRoleAdministrator ? "Information" : !IsConfigured ? "Neutral" : SavedCount == 0 ? "Warning" : "Success";
    public bool CanConfigureEmpty => CanEdit && !IsConfigured && !HasPendingChanges && SelectedCount == 0 && !IsRoleAdministrator;
    public bool RequiresEmptyConfirmation => CanSave && SelectedCount == 0 && !IsRoleAdministrator;
    public bool HasPreviousHistory => HistoryPage > 1 && !IsLoadingHistory;
    public bool IsHistoryEmpty => !IsLoadingHistory && History.Count == 0;
    public string HistorySummary => $"{HistoryTotal} cambios · Página {HistoryPage}";
    public string SaveLabel => IsSaving ? "Guardando…" : "Guardar cambios";
    public string ChangeStatus => HasPendingChanges ? "Cambios sin guardar" : !IsLoaded ? "Seleccione un usuario" : !IsConfigured ? "Sin configuración individual" : "Sin cambios pendientes";
    public string AccessNote => SelectedUser is null ? "Seleccione un usuario para consultar y modificar sus permisos." : SelectedUser.Role == "Administrador"
        ? "El rol Administrador mantiene el acceso completo, incluso con cero asignaciones individuales."
        : !IsConfigured ? "Aún no tiene configuración individual. Guardar una selección activará esa configuración; una selección vacía requiere confirmación."
        : "Solo los permisos confirmados por el servidor conceden acceso. Las selecciones locales no cambian el acceso hasta guardar.";
    partial void OnSelectedUserChanged(PermissionUser? value) => Notify();
    partial void OnIsBusyChanged(bool value) => Notify();
    partial void OnIsSavingChanged(bool value) => Notify();
    partial void OnIsLoadingHistoryChanged(bool value) => NotifyHistory();
    partial void OnHistoryPageChanged(int value) => NotifyHistory();
    partial void OnHistoryTotalChanged(int value) => NotifyHistory();
    partial void OnHasPendingChangesChanged(bool value) => Notify();
    partial void OnIsLoadedChanged(bool value) => Notify();
    private void Notify()
    {
        foreach (var property in SummaryProperties) OnPropertyChanged(property);
    }
    private void NotifyHistory() { OnPropertyChanged(nameof(HasPreviousHistory)); OnPropertyChanged(nameof(IsHistoryEmpty)); OnPropertyChanged(nameof(HistorySummary)); }
    public async Task LoadUsersAsync(CancellationToken ct = default)
    {
        if (!IsAdministrator) { RevokeAccess(); return; }
        IsBusy = true; MessageIsSuccess = false;
        try
        {
            var catalog = await api.CatalogAsync(ct);
            if (!catalog.IsSuccess || catalog.Value is null) { Message = catalog.Error?.Message ?? "No se pudo consultar el catálogo."; return; }
            _catalog = catalog.Value;
            var response = await api.UsersAsync(Search, ct);
            if (!IsAdministrator) { RevokeAccess(); return; }
            if (!response.IsSuccess || response.Value is null) { Message = response.Error?.Message ?? "No se pudo consultar los usuarios."; return; }
            var summaries = UserRows.ToDictionary(r => r.User.Id, r => r.Summary);
            Users.Clear(); UserRows.Clear(); foreach (var u in response.Value.Items) { Users.Add(u); UserRows.Add(new(u) { Summary = summaries.GetValueOrDefault(u.Id, u.Role == "Administrador" ? "Acceso por rol" : "Permisos por consultar") }); }
            OnPropertyChanged(nameof(UserCountLabel));
            MessageIsSuccess = false;
            Message = !response.Value.DepartmentExists ? "No existe el departamento Laboratorio Analítico. No se pueden administrar usuarios hasta verificar su relación en la base de datos."
                : Users.Count == 0 ? "No hay usuarios activos elegibles para esta búsqueda." : string.Empty;
        }
        finally { IsBusy = false; }
    }
    public async Task<bool> SelectAsync(PermissionUser user, bool discardPending = false, CancellationToken ct = default)
    {
        if (IsBusy || HasPendingChanges && !discardPending) return false;
        if (!IsAdministrator) { Clear(); Message = "Acceso denegado."; return false; }
        Clear(); Message = string.Empty; var revision = ++_revision; SelectedUser = user; IsBusy = true; IsLoadingUser = true;
        try
        {
            var response = await api.GetAsync(user.Id, ct);
            if (revision != _revision) return false;
            if (!response.IsSuccess || response.Value is null) { Message = response.Error?.Message ?? "No se pudieron consultar los permisos."; return false; }
            Apply(response.Value); IsLoadingUser = false; Message = string.Empty; await LoadHistoryAsync(ct); return true;
        }
        finally { IsLoadingUser = false; IsBusy = false; }
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
        var row = UserRows.FirstOrDefault(r => r.User.Id == state.User.Id);
        if (row is not null) row.Summary = state.User.Role == "Administrador" ? "Acceso por rol" : !state.IsConfigured ? "Sin configuración individual" : $"{state.Permissions.Count} permisos guardados";
        IsLoaded = true; Recalculate(); Notify();
    }
    public string UserCountLabel => $"{Users.Count} usuarios activos";
    private string[] Selection() => Groups.SelectMany(g => g.Items).Where(p => p.IsGranted).Select(p => p.Definition.Code).ToArray();
    private void Recalculate()
    {
        HasPendingChanges = _saved is not null && !_saved.Permissions.ToHashSet(StringComparer.Ordinal).SetEquals(Selection());
        foreach (var group in Groups) group.Refresh();
        Notify();
    }
    public void DiscardChanges()
    {
        if (!CanEdit || _saved is null) return;
        Apply(_saved); MessageIsSuccess = false; Message = string.Empty;
    }
    public async Task<bool> SaveAsync(CancellationToken ct = default)
    {
        if (!CanSave) return false;
        return await SaveCoreAsync(ct);
    }
    // Invoked only after the view confirms the explicit activation of an empty matrix.
    public async Task<bool> ConfigureEmptyAsync(CancellationToken ct = default)
    {
        if (!CanConfigureEmpty) return false;
        return await SaveCoreAsync(ct);
    }
    private async Task<bool> SaveCoreAsync(CancellationToken ct)
    {
        if (_saved is null) return false;
        var revision = _revision;
        IsBusy = true; IsSaving = true; MessageIsSuccess = false; Message = string.Empty;
        try
        {
            var response = await api.SaveAsync(_saved.User.Id, new(Selection(), _saved.Version), ct);
            if (revision != _revision || !IsAdministrator) return false;
            if (!response.IsSuccess || response.Value is null) { Message = response.Error?.Message ?? "No se pudo guardar. Los cambios siguen pendientes."; return false; }
            Apply(response.Value); MessageIsSuccess = true; Message = "Cambios guardados y confirmados por el servidor."; HistoryPage = 1; await LoadHistoryAsync(ct); return true;
        }
        finally { IsSaving = false; IsBusy = false; }
    }
    public async Task LoadHistoryAsync(CancellationToken ct = default)
    {
        if (!IsAdministrator || SelectedUser is null) return;
        var user = SelectedUser.Id; var page = HistoryPage; var revision = _revision;
        IsLoadingHistory = true;
        try
        {
        var response = await api.HistoryAsync(user, page, ct);
        if (!IsAdministrator || revision != _revision || SelectedUser?.Id != user || HistoryPage != page) return;
        if (!response.IsSuccess || response.Value is null) { MessageIsSuccess = false; Message = response.Error?.Message ?? "No se pudo consultar el historial."; return; }
        History.Clear(); foreach (var entry in response.Value.Items)
            History.Add(new(_catalog.FirstOrDefault(p => p.Code == entry.Permission)?.Module ?? "Permisos", _catalog.FirstOrDefault(p => p.Code == entry.Permission)?.Name ?? "Permiso", entry.Actor, entry.Action,
                $"{(entry.Before ? "Permitido" : "Sin asignación")} → {(entry.After ? "Permitido" : "Sin asignación")}", entry.At.ToLocalTime().ToString("g", System.Globalization.CultureInfo.CurrentCulture)));
        HistoryTotal = response.Value.Total;
        HasMoreHistory = HistoryPage * 30 < response.Value.Total;
        }
        finally { if (revision == _revision) { IsLoadingHistory = false; NotifyHistory(); } }
    }
    public void Clear()
    {
        _revision++;
        _saved = null; SelectedUser = null; Groups.Clear(); History.Clear(); IsLoaded = false; HasPendingChanges = false; HistoryPage = 1; HasMoreHistory = false; HistoryTotal = 0; IsLoadingHistory = false; MessageIsSuccess = false;
    }
    public void RevokeAccess()
    {
        Clear(); Users.Clear(); UserRows.Clear(); OnPropertyChanged(nameof(UserCountLabel)); Message = "Acceso denegado. Solo los administradores pueden gestionar permisos.";
    }
}

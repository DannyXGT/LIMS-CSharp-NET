using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Lims.Contracts.Alerts;
using Lims.Contracts.Permissions;
using Lims.Desktop.Http;
using Lims.Desktop.Services;

namespace Lims.Desktop.ViewModels;

public enum AlertScreenState { Loading, Unavailable, EnginePending, Empty, FilteredEmpty, Ready }

public sealed class AlertRow(AlertItem item)
{
    public AlertItem Item { get; } = item;
    public string Title => Item.Title;
    public string Material => Item.Material;
    public string Subtitle => $"{Item.Code} · Lote {Item.Lot}";
    public string Module => ModuleLabel(Item.Modules);
    public string Priority => PriorityLabel(Item.Priority);
    public Lims.DesignSystem.Controls.LimsIconKind Icon => Item.Category == "Expiration" ? Lims.DesignSystem.Controls.LimsIconKind.Calendar : Lims.DesignSystem.Controls.LimsIconKind.Alerts;
    public bool IsCritical => Item.Priority == AlertPriority.Critical;
    public string Tone => Item.Priority switch { AlertPriority.Critical or AlertPriority.High => "Danger", AlertPriority.Warning => "Warning", _ => "Information" };
    public string Status => Item.ResolvedAt is not null ? "Resuelta" : Item.AcknowledgedAt is not null ? "Atendida · activa" : "Pendiente";
    public string StatusTone => Item.ResolvedAt is not null ? "Success" : Item.AcknowledgedAt is not null ? "Neutral" : "Information";
    public string Metric => Item.Category == "Availability" ? $"{Item.AvailabilityPercent:0.##} % disponible" : $"{Item.Expiration:dd MMM yyyy}";
    public string ReadLabel => Item.NotificationId is null ? "Sin aviso personal" : Item.ReadAt is null ? "No leída" : "Leída";
    public bool IsUnread => Item.NotificationId is not null && Item.ReadAt is null;
    public string Detected => Item.DetectedAt.ToOffset(TimeSpan.FromHours(-6)).ToString("dd MMM · HH:mm", System.Globalization.CultureInfo.GetCultureInfo("es-GT"));
    public static string ModuleLabel(AlertModule modules) => string.Join(" / ", Enum.GetValues<AlertModule>().Where(m => m != AlertModule.None && modules.HasFlag(m)).Select(m => m == AlertModule.Standards ? "Estándares" : m.ToString()));
    public static string PriorityLabel(AlertPriority p) => p switch { AlertPriority.Critical => "Crítica", AlertPriority.High => "Alta", AlertPriority.Warning => "Advertencia", _ => "Informativa" };
    public override string ToString() => $"{Priority}: {Material}, {Title}, {Module}, {Status}, {ReadLabel}";
}

public sealed partial class AlertsViewModel(IAlertsApiClient api, ISessionService session) : ObservableObject
{
    private int _revision, _detailRevision;
    private CancellationTokenSource? _loadCancellation, _detailCancellation;
    private AlertFilter? _loadingFilter;
    private Task? _loadingTask;
    public ObservableCollection<AlertRow> Items { get; } = [];
    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial bool IsDetailBusy { get; set; }
    [ObservableProperty] public partial bool IsActionBusy { get; set; }
    [ObservableProperty] public partial string Message { get; set; } = "";
    [ObservableProperty] public partial int FailureStatus { get; set; }
    [ObservableProperty] public partial AlertPage? Data { get; set; }
    [ObservableProperty] public partial AlertDetail? Detail { get; set; }
    public AlertFilter Filter { get; set; } = new();
    public event EventHandler? NotificationsChanged;
    public bool Has(string code) => session.Profile?.Role == "Administrador" || session.Profile?.Permissions.Contains(code, StringComparer.Ordinal) == true;
    public bool CanManage => Has(PermissionCatalog.AlertsManage);
    public bool CanConfigure => session.Profile?.Role == "Administrador" && Has(PermissionCatalog.NotificationsConfigure);
    public bool CanViewHistory => Has(PermissionCatalog.DeliveryHistory);
    public AlertScreenState State => IsBusy ? AlertScreenState.Loading : Data is null ? AlertScreenState.Unavailable :
        Data.Summary.LastEvaluation is null ? AlertScreenState.EnginePending : Data.Total > 0 ? AlertScreenState.Ready :
        Data.Summary.Active == 0 && !HasFilters ? AlertScreenState.Empty : AlertScreenState.FilteredEmpty;
    public bool HasFilters => !string.IsNullOrWhiteSpace(Filter.Search) || !string.IsNullOrWhiteSpace(Filter.Category) || Filter.Priority is not null ||
        Filter.Module is not null || Filter.State is not (null or "" or "Active") || Filter.From is not null || Filter.Until is not null;
    public bool Synchronized => !IsBusy && Data?.Summary.LastEvaluation is not null;
    public string ServiceLabel => State switch { AlertScreenState.Loading => "Sincronizando", AlertScreenState.Unavailable => FailureStatus == 404 ? "API pendiente de despliegue" : "Servicio no disponible", AlertScreenState.EnginePending => "Motor sin evaluación", _ => "Servicio conectado" };
    public string UpdateDisplay => Data?.Summary.LastEvaluation is { } at ? "Última evaluación · " + at.ToOffset(TimeSpan.FromHours(-6)).ToString("dd/MM/yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture) :
        Data is not null ? "Primera evaluación no registrada" : "Sin sincronizar con el servidor";

    public void Clear()
    {
        _revision++; _detailRevision++; _loadCancellation?.Cancel(); _detailCancellation?.Cancel();
        Items.Clear(); Data = null; Detail = null; Message = ""; FailureStatus = 0; IsBusy = IsDetailBusy = IsActionBusy = false;
    }
    public Task LoadAsync(CancellationToken ct = default)
    {
        if (IsBusy && _loadingFilter == Filter && _loadingTask is { IsCompleted: false }) return _loadingTask;
        _loadingFilter = Filter; return _loadingTask = LoadCoreAsync(Filter, ct);
    }
    private async Task LoadCoreAsync(AlertFilter filter, CancellationToken ct)
    {
        _loadCancellation?.Cancel(); _loadCancellation?.Dispose();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct); _loadCancellation = cancellation;
        var revision = ++_revision; _detailRevision++; _detailCancellation?.Cancel(); IsDetailBusy = false;
        var user = session.Profile?.Id; IsBusy = true; Message = ""; FailureStatus = 0;
        Data = null; Items.Clear(); Detail = null;
        try
        {
            var result = await api.ListAsync(filter, cancellation.Token);
            if (revision != _revision || user != session.Profile?.Id || cancellation.IsCancellationRequested) return;
            if (result.IsSuccess && result.Value is { Summary: not null, Items: not null } page)
            { Data = page; foreach (var row in page.Items) Items.Add(new(row)); }
            else { FailureStatus = result.StatusCode; Message = result.Error?.Message ?? "No se pudieron consultar las alertas. Revise el estado del servidor."; }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (HttpRequestException) { if (revision == _revision) Message = "No se pudo conectar con el servidor de Alertas."; }
        finally { if (revision == _revision) IsBusy = false; }
    }
    public async Task OpenAsync(Guid id, CancellationToken ct = default)
    {
        _detailCancellation?.Cancel(); _detailCancellation?.Dispose();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct); _detailCancellation = cancellation;
        var revision = ++_detailRevision; var user = session.Profile?.Id; IsDetailBusy = true; Detail = null;
        try
        {
            var response = await api.DetailAsync(id, cancellation.Token);
            if (revision != _detailRevision || user != session.Profile?.Id || cancellation.IsCancellationRequested) return;
            Detail = response.IsSuccess ? response.Value : null;
            if (!response.IsSuccess) Message = response.Error?.Message ?? "La alerta ya no está disponible en su ámbito.";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        finally { if (revision == _detailRevision) IsDetailBusy = false; }
    }
    public Task AcknowledgeAsync(CancellationToken ct = default) => ActAsync(false, ct);
    public Task MarkReadAsync(CancellationToken ct = default) => ActAsync(true, ct);
    private async Task ActAsync(bool read, CancellationToken ct)
    {
        if (IsActionBusy || Detail is not { } detail || (!read && !CanManage) || (read && (detail.Alert.NotificationId is null || detail.Alert.ReadAt is not null))) return;
        var user = session.Profile?.Id; var id = detail.Alert.Id; IsActionBusy = true;
        try
        {
            bool success; string? error;
            if (read) { var result = await api.ReadAsync(detail.Alert.NotificationId, ct); success = result.IsSuccess && result.Value; error = result.Error?.Message; }
            else { var result = await api.AcknowledgeAsync(id, ct); success = result.IsSuccess; error = result.Error?.Message; }
            if (user != session.Profile?.Id) return;
            if (success) { await LoadAsync(ct); await OpenAsync(id, ct); if (read) NotificationsChanged?.Invoke(this, EventArgs.Empty); }
            else Message = error ?? "No se pudo completar la acción. Reintente.";
        }
        finally { IsActionBusy = false; }
    }
}

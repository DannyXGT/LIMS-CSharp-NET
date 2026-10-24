using Lims.Contracts.Alerts;
using Lims.Contracts.Permissions;
using Lims.Desktop.Http;
using Lims.Desktop.Services;
using Lims.Desktop.ViewModels;
using Lims.DesignSystem.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Lims.Desktop.Views;

public sealed class AlertsWorkspace
{
    private readonly IAlertsApiClient _api;
    private readonly ISessionService _session;
    private readonly WindowsAlertNotifications _windows;
    private readonly IReferenceMaterialsApiClient _references;
    private readonly IStockApiClient _stock;
    private readonly IIntermediateApiClient _intermediate;
    private readonly ICommercialPreparationApiClient _commercial;
    private readonly ISolutionGroupApiClient _groups;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly TextBlock _badge = new() { FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
    private readonly LimsIcon _syncIndicator = new() { Icon = LimsIconKind.Error, Size = 12, Visibility = Visibility.Collapsed };
    private readonly StackPanel _flyoutContent = new() { Width = 430, Spacing = 10 };
    private readonly Flyout _flyout = new() { Placement = FlyoutPlacementMode.BottomEdgeAlignedRight };
    private bool _polling, _running, _dialog;
    private Task? _pollTask;
    private int? _user;
    private int _epoch;
    private string? _authorization;
    private NotificationPage? _notifications;
    private string? _syncError;
    private bool _unreadOnly;
    private (Guid AlertId, Guid NotificationId, int UserId)? _pendingActivation;
    private readonly HashSet<Guid> _shown = [];
    public AlertsWorkspace(IAlertsApiClient api, ISessionService session, WindowsAlertNotifications windows,
        IReferenceMaterialsApiClient references, IStockApiClient stock, IIntermediateApiClient intermediate,
        ICommercialPreparationApiClient commercial, ISolutionGroupApiClient groups)
    {
        _api = api; _session = session; _windows = windows; _references = references; _stock = stock; _intermediate = intermediate; _commercial = commercial; _groups = groups;
        Page = new(new AlertsViewModel(api, session));
        Page.Configure += async (_, _) => await ConfigureAsync(); Page.DeliveryHistory += async (_, _) => await HistoryAsync();
        Page.EngineHistory += async (_, _) => await EngineHistoryAsync();
        Page.OpenMaterial += async (_, alert) => await OpenMaterialAsync(alert);
        Page.ViewModel.NotificationsChanged += async (_, _) => await PollAsync();
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 }; content.Children.Add(new Lims.DesignSystem.Controls.LimsIcon { Icon = Lims.DesignSystem.Controls.LimsIconKind.Notifications, Size = 18 }); content.Children.Add(_badge);
        content.Children.Add(_syncIndicator);
        Bell = new Button { Content = content, MinWidth = 38, MinHeight = 32, Style = (Style)Application.Current.Resources["LimsSecondaryButtonStyle"] };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(Bell, "Notificaciones");
        var flyoutStyle = new Style(typeof(FlyoutPresenter));
        flyoutStyle.Setters.Add(new Setter(FrameworkElement.MaxWidthProperty, 500d));
        _flyout.FlyoutPresenterStyle = flyoutStyle;
        ToolTipService.SetToolTip(Bell, "Abrir notificaciones"); _flyout.Content = _flyoutContent;
        Bell.Flyout = _flyout; Bell.Click += async (_, _) => { await PollAsync(); RenderFlyout(); };
        _timer.Tick += async (_, _) => await PollAsync();
        _windows.OpenRequested += async (_, route) => { _pendingActivation = route; await OpenPendingAsync(); };
    }
    public AlertsPage Page { get; }
    public Button Bell { get; }
    public event EventHandler<Guid?>? NavigateRequested;
    public void RefreshAuthorization(string signature, bool allowed)
    {
        if (_authorization != signature || !allowed)
        {
            _epoch++; _authorization = signature; _notifications = null; _badge.Text = ""; _badge.Visibility = Visibility.Collapsed; _syncError = null;
            _flyout.Hide(); _flyoutContent.Children.Clear(); Page.ViewModel.Clear();
        }
        Bell.Visibility = allowed ? Visibility.Visible : Visibility.Collapsed;
    }
    public void InitializeWindow(Window window) => _windows.Initialize(window);
    public async Task StartAsync() { _running = true; _timer.Start(); await PollAsync(); }
    public void Stop()
    {
        _running = false; _timer.Stop(); _epoch++; _user = null; _notifications = null; _badge.Text = ""; _shown.Clear(); _flyout.Hide(); _flyoutContent.Children.Clear(); Page.ViewModel.Clear();
    }
    public Task PollAsync() => _pollTask is { IsCompleted: false } ? _pollTask : _pollTask = PollCoreAsync();
    private async Task PollCoreAsync()
    {
        if (_polling || !_running) return;
        var profile = _session.Profile;
        if (profile is null || profile.Role != "Administrador" && !profile.Permissions.Contains(PermissionCatalog.AlertsView, StringComparer.Ordinal))
        { _notifications = null; _badge.Text = ""; _flyoutContent.Children.Clear(); Page.ViewModel.Clear(); Bell.Visibility = Visibility.Collapsed; return; }
        Bell.Visibility = Visibility.Visible;
        if (_user != profile.Id) { _epoch++; _user = profile.Id; _shown.Clear(); _notifications = null; Page.ViewModel.Clear(); }
        var epoch = _epoch; _polling = true;
        try
        {
            var result = await _api.NotificationsAsync(CancellationToken.None);
            if (!_running || epoch != _epoch || _session.Profile?.Id != profile.Id) return;
            _notifications = result.IsSuccess ? result.Value : null;
            _syncError = result.IsSuccess ? null : result.Error?.Message ?? $"No se pudo sincronizar (HTTP {result.StatusCode}).";
            _badge.Text = _notifications is { Unread: > 0 } unread ? unread.Unread.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
            _badge.Visibility = _notifications is { Unread: > 0 } ? Visibility.Visible : Visibility.Collapsed;
            _syncIndicator.Visibility = _syncError is null ? Visibility.Collapsed : Visibility.Visible;
            _syncIndicator.IsDanger = _syncError is not null;
            ToolTipService.SetToolTip(Bell, _syncError is null ? "Abrir notificaciones" : "Notificaciones sin sincronizar · " + _syncError);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(Bell, _notifications is { } n ? $"Notificaciones, {n.Unread} sin leer" : "Notificaciones, error de sincronización");
            RenderFlyout();
            if (_notifications is { WindowsEnabled: true, Preferences.WindowsEnabled: true } data && _windows.Available)
            {
                var item = data.Items.FirstOrDefault(n => n.ReadAt is null && !n.WindowsClaimed && !_shown.Contains(n.Id) && n.CreatedAt >= data.ServerTime.AddHours(-1) && (!data.Preferences.OnlyCriticalWindows || n.Priority == AlertPriority.Critical));
                if (item is not null)
                {
                    var claimed = await _api.ClaimWindowsAsync(item.Id, CancellationToken.None);
                    if (epoch == _epoch && _session.Profile?.Id == profile.Id && claimed.IsSuccess)
                    {
                        _shown.Add(item.Id); // A denied/superseded claim must not starve newer candidates on every poll.
                        if (claimed.Value) _windows.Show(item, profile.Id);
                    }
                }
            }
            await OpenPendingAsync();
        }
        finally { _polling = false; }
    }
    private async Task OpenPendingAsync()
    {
        if (!_running || _pendingActivation is not { } route || _session.Profile?.Id != route.UserId) return;
        _pendingActivation = null;
        var read = await _api.ReadAsync(route.NotificationId, CancellationToken.None);
        if (read.IsSuccess && read.Value) NavigateRequested?.Invoke(this, route.AlertId);
    }
    private void RenderFlyout()
    {
        _flyoutContent.Children.Clear();
        _flyoutContent.Width = Math.Min(430, Math.Max(280, (Bell.XamlRoot?.Size.Width ?? 530) - 80));
        var header = new Grid(); header.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        header.Children.Add(AlertUi.Text("Notificaciones", 19, true));
        var badge = new StatusBadge { Text = _notifications is null ? "Sin sincronizar" : "Conectado", Tone = _notifications is null ? "Warning" : "Success", VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(badge, 1); header.Children.Add(badge); _flyoutContent.Children.Add(header);
        if (_notifications is not { } data)
        { _flyoutContent.Children.Add(AlertUi.Text(_syncError ?? "Sincronización pendiente. No se conoce el contador.")); _flyoutContent.Children.Add(AlertUi.Button("Reintentar sincronización", PollAsync, icon: LimsIconKind.Refresh)); AddFooter(); return; }
        var tabs = new LimsTabStrip(); tabs.Items.Add(new() { Label = "Todas" }); tabs.Items.Add(new() { Label = "No leídas", Count = data.Unread }); tabs.SelectedIndex = _unreadOnly ? 1 : 0;
        tabs.SelectionChanged += (_, _) => { _unreadOnly = tabs.SelectedIndex == 1; RenderFlyout(); }; _flyoutContent.Children.Add(tabs);
        var rows = new StackPanel { Spacing = 8 };
        _flyoutContent.Children.Add(new ScrollViewer { Content = rows, MaxHeight = Math.Max(120, Math.Min(340, (Bell.XamlRoot?.Size.Height ?? 650) - 310)), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        var visible = data.Items.Where(n => !_unreadOnly || n.ReadAt is null).Take(30).ToArray();
        if (visible.Length == 0) rows.Children.Add(AlertUi.Text(_unreadOnly ? "No tiene notificaciones sin leer." : "No tiene notificaciones en su ámbito."));
        foreach (var n in visible)
        {
            var row = new Grid { ColumnSpacing = 10, Padding = new Thickness(0, 6, 0, 6) };
            row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            row.Children.Add(new LimsIcon { Icon = n.Priority == AlertPriority.Critical ? LimsIconKind.Error : LimsIconKind.Notifications, Size = 18, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 3, 0, 0) });
            var line = new StackPanel { Spacing = 4 }; Grid.SetColumn(line, 1); row.Children.Add(line);
            var title = AlertUi.Text(n.Title, 12, n.ReadAt is null); title.MaxLines = 2; title.TextTrimming = TextTrimming.CharacterEllipsis; line.Children.Add(title);
            line.Children.Add(AlertUi.Text($"{AlertRow.PriorityLabel(n.Priority)} · {n.CreatedAt.ToOffset(TimeSpan.FromHours(-6)):dd/MM HH:mm} · {(n.ReadAt is null ? "No leída" : "Leída")}", 10));
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 }; Grid.SetColumn(actions, 2); row.Children.Add(actions);
            actions.Children.Add(IconButton("Abrir alerta de " + n.Title, async () => { if (n.ReadAt is null && !await ReadNotificationAsync(n.Id, false)) return; _flyout.Hide(); NavigateRequested?.Invoke(this, n.AlertId); }, LimsIconKind.ViewDetail));
            rows.Children.Add(row); rows.Children.Add(AlertUi.Rule());
        }
        if (data.Unread > 0) _flyoutContent.Children.Add(AlertUi.Button("Marcar todas como leídas", async () => { await ReadNotificationAsync(null); }, icon: LimsIconKind.Check));
        _flyoutContent.Children.Add(AlertUi.Text("La lectura no resuelve la condición del material.", 10)); AddFooter();
    }
    private void AddFooter()
    {
        _flyoutContent.Children.Add(AlertUi.Rule());
        var footer = new Grid { ColumnSpacing = 8 }; footer.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        footer.Children.Add(AlertUi.Button("Ver todas las alertas", () => { _flyout.Hide(); NavigateRequested?.Invoke(this, null); return Task.CompletedTask; }, true, LimsIconKind.Alerts));
        var preferences = IconButton("Mis preferencias", PreferencesAsync, LimsIconKind.Administration); preferences.IsEnabled = _notifications is not null; Grid.SetColumn(preferences, 1); footer.Children.Add(preferences); _flyoutContent.Children.Add(footer);
    }
    private static Button IconButton(string title, Func<Task> action, LimsIconKind icon)
    { var button = AlertUi.Button(title, action, icon: icon); button.Style = (Style)Application.Current.Resources["LimsActionIconStyle"]; LimsAction.SetLabelVisibility(button, Visibility.Collapsed); return button; }
    private async Task<bool> ReadNotificationAsync(Guid? id, bool refreshPage = true)
    {
        var result = await _api.ReadAsync(id, CancellationToken.None);
        if (!result.IsSuccess || !result.Value) { _syncError = result.Error?.Message ?? "No se pudo registrar la lectura."; _notifications = null; RenderFlyout(); return false; }
        if (_pollTask is { IsCompleted: false }) await _pollTask;
        await PollAsync(); if (refreshPage && Page.IsLoaded) await Page.LoadAsync(); return true;
    }
    private async Task PreferencesAsync()
    {
        if (_dialog || _notifications is not { } data) return; _flyout.Hide(); _dialog = true;
        try
        {
            var panel = new StackPanel { Spacing = 16 };
            var windows = new ToggleSwitch { Header = "Avisos nativos de Windows", IsOn = data.Preferences.WindowsEnabled };
            var critical = new ToggleSwitch { Header = "Solo avisos críticos en Windows", IsOn = data.Preferences.OnlyCriticalWindows };
            var email = new ToggleSwitch { Header = "Recibir resumen diario por correo", IsOn = data.Preferences.EmailEnabled };
            panel.Children.Add(windows); panel.Children.Add(critical); panel.Children.Add(email);
            panel.Children.Add(AlertUi.Text(_windows.Status, 12)); panel.Children.Add(AlertUi.Text("Los avisos nativos se generan con el cliente abierto. Los correos críticos obligatorios y la configuración de otros usuarios permanecen bajo administración.", 12));
            var dialog = new ContentDialog { Title = "Mis preferencias de notificación", Content = panel, PrimaryButtonText = "Guardar", CloseButtonText = "Cerrar", XamlRoot = Bell.XamlRoot, RequestedTheme = Bell.ActualTheme };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                var r = await _api.PreferencesAsync(new(windows.IsOn, email.IsOn, critical.IsOn), CancellationToken.None);
                if (!r.IsSuccess) Page.ViewModel.Message = r.Error?.Message ?? "No se guardaron las preferencias.";
                await PollAsync();
            }
        }
        finally { _dialog = false; }
    }
    private async Task EngineHistoryAsync()
    {
        if (_dialog || !Page.ViewModel.CanEvaluate) return; _dialog = true;
        try
        {
            var response = await _api.EvaluationsAsync(CancellationToken.None);
            var rows = new StackPanel { Spacing = 12 };
            if (!response.IsSuccess) rows.Children.Add(AlertUi.Text(response.Error?.Message ?? "No se pudo consultar el historial del motor."));
            else if (response.Value is not { Count: > 0 }) rows.Children.Add(AlertUi.Text("No hay ejecuciones registradas."));
            else foreach (var run in response.Value)
            {
                var state = run.State switch { "Succeeded" => "Completada", "Running" => "En ejecución", "Evaluated" => "Evaluada; procesando canales", "Interrupted" => "Interrumpida", "DeliveryFailed" => "Evaluada; fallo en canales", _ => "Fallida" };
                rows.Children.Add(AlertUi.Text($"{run.StartedAt.ToOffset(TimeSpan.FromHours(-6)):dd/MM/yyyy HH:mm} · {state}", 13, true));
                rows.Children.Add(AlertUi.Text($"{(run.ActorUserId is null ? "Programada" : "Manual")} · {run.Evaluated} materiales · {run.Active} alertas activas · {run.Changed} cambios · {run.Sent} correos confirmados", 12));
                if (run.Error is { } error) rows.Children.Add(AlertUi.Text(error, 12));
                rows.Children.Add(AlertUi.Rule());
            }
            await new ContentDialog { Title = "Historial del motor", Content = new ScrollViewer { Content = rows, MaxHeight = Math.Max(160, Math.Min(520, (Bell.XamlRoot?.Size.Height ?? 650) - 220)) }, CloseButtonText = "Cerrar", XamlRoot = Bell.XamlRoot, RequestedTheme = Bell.ActualTheme }.ShowAsync();
        }
        finally { _dialog = false; }
    }
    private async Task ConfigureAsync()
    {
        if (_dialog) return; _dialog = true;
        try
        {
            var response = await _api.AdministrationAsync(CancellationToken.None);
            if (response.IsSuccess && response.Value is { } data)
            {
                var dialog = new AlertConfigurationDialog(_api, data) { XamlRoot = Page.XamlRoot, RequestedTheme = Page.ActualTheme };
                dialog.Resources["ContentDialogMaxWidth"] = 1000d;
                await dialog.ShowAsync(); if (dialog.Saved) { await Page.LoadAsync(); await PollAsync(); }
            }
            else Page.ViewModel.Message = response.Error?.Message ?? "No se pudo abrir la configuración.";
        }
        finally { _dialog = false; }
    }
    private async Task HistoryAsync()
    {
        if (_dialog) return; _dialog = true;
        try
        {
            var panel = new Grid { RowSpacing = 12, Width = Math.Min(760, Page.XamlRoot.Size.Width - 100), Height = Math.Max(200, Math.Min(540, Page.XamlRoot.Size.Height - 210)) }; var page = 1;
            panel.RowDefinitions.Add(new() { Height = GridLength.Auto }); panel.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); panel.RowDefinitions.Add(new() { Height = GridLength.Auto });
            var rows = new StackPanel { Spacing = 12 }; var status = AlertUi.Text(""); panel.Children.Add(status);
            var scroll = new ScrollViewer { Content = rows, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; Grid.SetRow(scroll, 1); panel.Children.Add(scroll);
            var controls = new Grid { ColumnSpacing = 8 }; controls.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); controls.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); controls.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); Grid.SetRow(controls, 2); panel.Children.Add(controls);
            var pageText = AlertUi.Text("", 12); pageText.VerticalAlignment = VerticalAlignment.Center; controls.Children.Add(pageText);
            Button previous = null!, next = null!;
            async Task Load()
            {
                previous.IsEnabled = next.IsEnabled = false; status.Text = "Consultando entregas…";
                var result = await _api.DeliveriesAsync(page, CancellationToken.None); rows.Children.Clear();
                if (result.Value is not { } data || !result.IsSuccess) { status.Text = result.Error?.Message ?? "No se pudo consultar el historial."; return; }
                status.Text = data.MailState switch { "NotConfigured" => "SMTP sin configurar · No hay transporte disponible para nuevas entregas.", "Disabled" => "SMTP deshabilitado en el servidor.", "Configured" => "SMTP configurado · El estado de cada entrega confirma su resultado.", _ => "Estado SMTP no informado por esta versión del servidor." };
                pageText.Text = $"{data.Total} entregas · Página {data.Page} de {Math.Max(1, (int)Math.Ceiling(data.Total / 30d))}";
                if (data.Items.Count == 0) rows.Children.Add(AlertUi.Text("No hay entregas registradas."));
                foreach (var d in data.Items)
                {
                    var line = new StackPanel { Spacing = 5 };
                    var kind = d.Kind switch { "Test" => "Prueba de correo", "Daily" => "Resumen diario", "Immediate" => "Alerta crítica", _ => "Notificación" };
                    var state = d.State switch { "Pending" => "Pendiente", "Sending" => "Procesando", "Sent" when d.SentAt is not null => "Enviado · SMTP confirmado", "Sent" => "Confirmación no disponible", "Retry" => "Reintento programado", "Failed" => "Fallido", "Uncertain" => "Entrega sin confirmar", "Cancelled" => "Cancelado", _ => "Estado desconocido" };
                    line.Children.Add(new StatusBadge { Text = state, Tone = d.State == "Sent" && d.SentAt is not null ? "Success" : d.State is "Failed" or "Uncertain" ? "Danger" : "Neutral", HorizontalAlignment = HorizontalAlignment.Left });
                    line.Children.Add(AlertUi.Text($"{kind} · {d.Recipient}", 13, true));
                    line.Children.Add(AlertUi.Text($"Creado {d.CreatedAt.ToOffset(TimeSpan.FromHours(-6)):dd/MM/yyyy HH:mm} · {d.Attempts} intentos" + (d.SentAt is { } sent ? $" · Confirmado {sent.ToOffset(TimeSpan.FromHours(-6)):dd/MM HH:mm}" : "") + (d.State == "Retry" ? $" · Próximo {d.NextAttemptAt.ToOffset(TimeSpan.FromHours(-6)):dd/MM HH:mm}" : ""), 11));
                    if (d.LastError is { Length: > 0 } error) line.Children.Add(AlertUi.Text(error, 12));
                    rows.Children.Add(line); rows.Children.Add(AlertUi.Rule());
                }
                next.IsEnabled = page * 30 < data.Total; previous.IsEnabled = page > 1;
            }
            // Variables are initialized before the first load uses them.
            previous = AlertUi.Button("Anterior", async () => { page--; await Load(); }); next = AlertUi.Button("Siguiente", async () => { page++; await Load(); });
            Grid.SetColumn(previous, 1); Grid.SetColumn(next, 2); controls.Children.Add(previous); controls.Children.Add(next); await Load();
            var dialog = new ContentDialog { Title = "Historial de envíos", Content = panel, CloseButtonText = "Cerrar", XamlRoot = Page.XamlRoot, RequestedTheme = Page.ActualTheme }; dialog.Resources["ContentDialogMaxWidth"] = 900d;
            await dialog.ShowAsync();
        }
        finally { _dialog = false; }
    }
    private async Task OpenMaterialAsync(AlertItem alert)
    {
        if (_dialog) return; _dialog = true;
        try
        {
            // Recheck the alert's scope with the backend before accessing a source endpoint.
            var authorized = await _api.DetailAsync(alert.Id, CancellationToken.None);
            if (!authorized.IsSuccess || authorized.Value is null) { Page.ViewModel.Message = authorized.Error?.Message ?? "Ya no tiene acceso a esta alerta."; return; }
            alert = authorized.Value.Alert;
            var module = Enum.GetValues<AlertModule>().FirstOrDefault(m => m != AlertModule.None && alert.Modules.HasFlag(m) && Page.ViewModel.Has(m switch { AlertModule.Standards => "reference_materials.view", AlertModule.Stock => PermissionCatalog.StockView, AlertModule.Intermedia => PermissionCatalog.IntermediateView, AlertModule.Curva => PermissionCatalog.CurveView, _ => PermissionCatalog.AqsView }));
            if (module == AlertModule.None) return;
            ContentDialog? dialog = null;
            if (alert.SourceType == "ReferenceMaterial" && module != AlertModule.Standards)
            {
                var result = await _commercial.GetAsync(module.ToString(), alert.SourceId, CancellationToken.None);
                if (result.IsSuccess && result.Value is { } d) { var commercial = new CommercialPreparationDetailDialog(d) { XamlRoot = Page.XamlRoot, RequestedTheme = Page.ActualTheme }; await commercial.ShowAsync(); return; }
                Page.ViewModel.Message = result.Error?.Message ?? "No se pudo consultar el frasco comercial."; return;
            }
            if (module == AlertModule.Standards)
            {
                var result = await _references.GetAsync(alert.SourceId, CancellationToken.None);
                if (result.IsSuccess && result.Value is { } d)
                {
                    var vm = new ReferenceMaterialsViewModel(_references, _session) { SelectedDetail = d };
                    var detail = new ReferenceMaterialDetailDialog(vm) { RequestedTheme = Page.ActualTheme };
                    // This entry opens the existing real record for consultation. Administrative edits belong to its workspace.
                    foreach (var name in new[] { "EditButton", "ReplaceButton", "ArchiveButton" }) ((Button)detail.FindName(name)).Visibility = Visibility.Collapsed;
                    await detail.ShowAsync(Page.XamlRoot); return;
                }
                Page.ViewModel.Message = result.Error?.Message ?? "No se pudo abrir el estándar."; return;
            }
            if (module == AlertModule.Stock) { var r = await _stock.GetAsync(alert.SourceId, CancellationToken.None); if (r.IsSuccess && r.Value is { } d) dialog = new StockDetailDialog(d); else Page.ViewModel.Message = r.Error?.Message ?? "No se pudo abrir Stock."; }
            else if (module == AlertModule.Intermedia) { var r = await _intermediate.GetAsync(alert.SourceId, CancellationToken.None); if (r.IsSuccess && r.Value is { } d) dialog = new IntermediateDetailDialog(d); else Page.ViewModel.Message = r.Error?.Message ?? "No se pudo abrir Intermedia."; }
            else { var r = await _groups.GetAsync(module.ToString(), alert.SourceId, CancellationToken.None); if (r.IsSuccess && r.Value is { } d) dialog = new SolutionGroupDetailDialog(d); else Page.ViewModel.Message = r.Error?.Message ?? "No se pudo abrir la preparación."; }
            if (dialog is not null) { dialog.XamlRoot = Page.XamlRoot; dialog.RequestedTheme = Page.ActualTheme; await dialog.ShowAsync(); }
        }
        finally { _dialog = false; }
    }
}

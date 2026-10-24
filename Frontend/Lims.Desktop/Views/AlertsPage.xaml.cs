using Lims.Contracts.Alerts;
using Lims.DesignSystem.Controls;
using Lims.DesignSystem.Presentation;
using Lims.Desktop.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Lims.Desktop.Views;

public sealed partial class AlertsPage : Page
{
    private readonly DispatcherTimer _search = new() { Interval = TimeSpan.FromMilliseconds(320) };
    private bool _initializing = true, _renderQueued, _compact;
    private AlertDetail? _renderedDetail;
    private string? _detailSignature;
    public AlertsPage(AlertsViewModel viewModel)
    {
        ViewModel = viewModel; InitializeComponent();
        CategoryBox.SelectedIndex = PriorityBox.SelectedIndex = ModuleBox.SelectedIndex = StateBox.SelectedIndex = SortBox.SelectedIndex = 0;
        _initializing = false;
        _search.Tick += async (_, _) => { _search.Stop(); await ApplyAsync(); };
        Unloaded += (_, _) => { _search.Stop(); ViewModel.Clear(); };
        ViewModel.PropertyChanged += (_, _) => QueueRender();
        Loaded += (_, _) => { Render(); Motion.Enter(HeaderGrid, y: 3); };
        foreach (var card in new[] { CriticalCard, UpcomingCard, ExpiredCard, PendingCard }) Motion.SetIsEnabled(card, true);
        ActualThemeChanged += (_, _) => { _detailSignature = null; QueueRender(); };
    }
    public AlertsViewModel ViewModel { get; }
    public event EventHandler<AlertItem>? OpenMaterial;
    public event EventHandler? Configure;
    public event EventHandler? DeliveryHistory;
    public async Task LoadAsync() { await ViewModel.LoadAsync(); Render(); }
    public async Task OpenAsync(Guid id) { await ViewModel.OpenAsync(id); Render(); ShowDetail(); }
    private static string? SelectedTag(ComboBox combo) => (combo.SelectedItem as ComboBoxItem)?.Tag as string;
    private async Task ApplyAsync(int page = 1)
    {
        if (_initializing) return;
        _search.Stop(); HideCompactDetail();
        ViewModel.Filter = new(SearchBox.Text, SelectedTag(CategoryBox), Enum.TryParse<AlertPriority>(SelectedTag(PriorityBox), out var priority) ? priority : null,
            Enum.TryParse<AlertModule>(SelectedTag(ModuleBox), out var module) ? module : null, SelectedTag(StateBox),
            FromDate.Date is { } from ? DateOnly.FromDateTime(from.DateTime) : null, UntilDate.Date is { } until ? DateOnly.FromDateTime(until.DateTime) : null, SelectedTag(SortBox), page, ViewModel.Filter.PageSize);
        await LoadAsync();
    }
    private void QueueRender()
    {
        if (_renderQueued) return; _renderQueued = true;
        DispatcherQueue.TryEnqueue(() => { _renderQueued = false; Render(); });
    }
    private void Render()
    {
        if (CriticalCount is null) return;
        var summary = ViewModel.Data?.Summary; var synced = ViewModel.Synchronized;
        ActionMessage.IsOpen = ViewModel.Data is not null && ViewModel.Message.Length > 0;
        ActionMessage.Message = ViewModel.Message; ActionMessage.Visibility = ActionMessage.IsOpen ? Visibility.Visible : Visibility.Collapsed;
        ActionMessage.Severity = ViewModel.IsMessageInformational ? InfoBarSeverity.Informational : InfoBarSeverity.Error;
        var screen = ViewModel.State;
        ServiceBadge.Text = screen switch
        {
            AlertScreenState.Loading => "Sincronizando",
            AlertScreenState.Unavailable => ViewModel.FailureStatus >= 500 ? "Error" : "No disponible",
            AlertScreenState.EnginePending => "Pendiente de evaluación",
            _ => "Activo"
        };
        var stateBrush = HeaderStateBrush(screen switch
        {
            AlertScreenState.Loading => "LimsInformationBrush",
            AlertScreenState.Unavailable => "LimsDangerBrush",
            AlertScreenState.EnginePending => "LimsWarningBrush",
            _ => "LimsSuccessBrush"
        });
        ServiceBadge.Foreground = EngineStateIcon.Foreground = stateBrush;
        EngineStateIcon.Icon = screen switch { AlertScreenState.Unavailable => LimsIconKind.Error, AlertScreenState.Loading => LimsIconKind.Refresh,
            AlertScreenState.EnginePending => LimsIconKind.Scheduling, _ => LimsIconKind.Check };
        HeaderEvaluation.Text = summary?.LastEvaluation is { } evaluated
            ? "Última · " + evaluated.ToOffset(TimeSpan.FromHours(-6)).ToString("dd/MM/yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture)
            : screen == AlertScreenState.Loading ? "Consultando última evaluación" : summary is not null ? "Sin evaluación registrada" : "Última evaluación no disponible";
        ToolTipService.SetToolTip(HeaderEvaluation, ViewModel.UpdateDisplay);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(HeaderEvaluation, summary?.LastEvaluation is not null ? ViewModel.UpdateDisplay : HeaderEvaluation.Text);
        ToolTipService.SetToolTip(ServiceBadge, screen == AlertScreenState.Unavailable ? ViewModel.Message : ViewModel.UpdateDisplay);
        var unsynced = "—";
        var values = new[] { summary?.Critical, summary?.Upcoming, summary?.Expired, summary?.Pending };
        var textBlocks = new[] { CriticalCount, UpcomingCount, ExpiredCount, PendingCount };
        for (var i = 0; i < textBlocks.Length; i++) { textBlocks[i].Text = synced ? values[i]?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "No disponible" : unsynced; textBlocks[i].FontSize = synced ? ActualHeight < 650 ? 29 : 34 : 17; }
        CriticalUnit.Visibility = ExpiredUnit.Visibility = PendingUnit.Visibility = synced ? Visibility.Visible : Visibility.Collapsed;
        var metrics = summary?.Metrics;
        CriticalBreakdown.Text = !synced ? "Condiciones aún sin sincronizar" : metrics is { } m ? $"{m.Depleted} agotados · {m.CriticalAvailability} disponibilidad crítica" : "Condiciones críticas del ámbito autorizado";
        NearestDays.Text = !synced ? "" : metrics?.NearestDays is { } days ? days == 0 ? "vence hoy" : $"en {days} días" : "";
        NearestText.Text = !synced ? "Fecha prioritaria sin confirmar" : summary?.NearestExpiration is { } date ? $"{date:dd/MM/yyyy} · {metrics?.NearestMaterial ?? "Próximo vencimiento"}" : "Sin vencimientos próximos";
        ToolTipService.SetToolTip(UpcomingCard, synced ? NearestText.Text : "Consultar próximos vencimientos al sincronizar");
        ExpiredPendingText.Text = !synced ? "Atención pendiente sin confirmar" : metrics is { } em ? $"{em.ExpiredPending} pendientes de atención · Ver listado" : "Consultar materiales afectados";
        UnreadText.Text = synced ? $"{summary!.Unread} avisos sin leer · lectura independiente" : "Lecturas aún sin sincronizar";
        foreach (var card in new[] { CriticalCard, UpcomingCard, ExpiredCard, PendingCard }) card.IsEnabled = synced;
        RefreshButton.IsEnabled = !ViewModel.IsBusy;
        EvaluateButton.Visibility = EngineHistoryButton.Visibility = ViewModel.CanEvaluate ? Visibility.Visible : Visibility.Collapsed;
        EngineActionsButton.Visibility = ViewModel.CanEvaluate ? Visibility.Visible : Visibility.Collapsed;
        EvaluateButton.IsEnabled = !ViewModel.IsActionBusy && !ViewModel.IsBusy && ViewModel.Data is not null;
        SettingsButton.Visibility = ViewModel.CanConfigure ? Visibility.Visible : Visibility.Collapsed;
        HistoryButton.Visibility = ViewModel.CanViewHistory ? Visibility.Visible : Visibility.Collapsed;
        ListCountBadge.Text = summary is null ? "Sin sincronizar" : summary.LastEvaluation is null ? "Sin evaluación" : $"{ViewModel.Data!.Total} resultados";
        ListCountBadge.Tone = synced ? "Information" : "Neutral";
        QuickTabs.Items[0].Count = synced ? summary!.Active : null;
        QuickTabs.Items[1].Count = synced ? summary!.Critical : null;
        QuickTabs.Items[2].Count = synced ? summary!.Upcoming : null;
        PageText.Text = ViewModel.Data is { } data && synced ? $"{data.Total} resultados · Página {data.Page} de {Math.Max(1, (int)Math.Ceiling(data.Total / (double)data.PageSize))}" : ViewModel.UpdateDisplay;
        ToolTipService.SetToolTip(PageText, ViewModel.UpdateDisplay);
        PreviousButton.IsEnabled = !ViewModel.IsBusy && synced && ViewModel.Filter.Page > 1;
        NextButton.IsEnabled = !ViewModel.IsBusy && synced && ViewModel.Data is { } p && p.Page * p.PageSize < p.Total;
        RenderState(); RenderDetail();
    }
    private Brush HeaderStateBrush(string key)
    {
        foreach (var dictionary in Application.Current.Resources.MergedDictionaries)
            if (dictionary.ThemeDictionaries.TryGetValue(ActualTheme.ToString(), out var theme) && theme is ResourceDictionary palette &&
                palette.TryGetValue(key, out var value) && value is Brush brush) return brush;
        return (Brush)Application.Current.Resources[key];
    }
    private void RenderState()
    {
        var state = ViewModel.State; var ready = state == AlertScreenState.Ready;
        AlertsList.Visibility = ready ? Visibility.Visible : Visibility.Collapsed;
        StatePanel.Visibility = ready ? Visibility.Collapsed : Visibility.Visible;
        LoadingRing.IsActive = state == AlertScreenState.Loading; LoadingRing.Visibility = LoadingRing.IsActive ? Visibility.Visible : Visibility.Collapsed;
        StateIconTile.Visibility = LoadingRing.IsActive ? Visibility.Collapsed : Visibility.Visible;
        StateAction.Visibility = state is AlertScreenState.Unavailable or AlertScreenState.FilteredEmpty ? Visibility.Visible : Visibility.Collapsed;
        StateTechnical.Text = ViewModel.UpdateDisplay;
        switch (state)
        {
            case AlertScreenState.Loading:
                StateTitle.Text = "Sincronizando alertas"; StateDescription.Text = "Consultando condiciones y permisos en el servidor…"; break;
            case AlertScreenState.Unavailable:
                StateIcon.Icon = LimsIconKind.Error; StateIcon.IsDanger = true;
                StateTitle.Text = "No se pudieron consultar las alertas";
                StateDescription.Text = ViewModel.Message.Length > 0 ? ViewModel.Message : "El servicio todavía no se ha sincronizado.";
                StateTechnical.Text = ViewModel.FailureStatus > 0 ? $"Respuesta HTTP {ViewModel.FailureStatus} · Estado del motor no verificable" : "Conectividad sin confirmar · Estado del motor no verificable";
                StateAction.Content = "Reintentar"; LimsAction.SetIcon(StateAction, LimsIconKind.Refresh); break;
            case AlertScreenState.EnginePending:
                StateIcon.Icon = LimsIconKind.Scheduling; StateIcon.IsDanger = false;
                StateTitle.Text = "Monitoreo pendiente de primera evaluación";
                StateDescription.Text = "El monitoreo automático aún no ha realizado su primera evaluación.";
                StateTechnical.Text = "La API respondió. El administrador debe verificar la ejecución programada en el servidor."; break;
            case AlertScreenState.Empty:
                StateIcon.Icon = LimsIconKind.Quality; StateIcon.IsDanger = false;
                StateTitle.Text = "Sin alertas activas"; StateDescription.Text = "No hay condiciones que requieran atención en su ámbito autorizado."; break;
            case AlertScreenState.FilteredEmpty:
                StateIcon.Icon = LimsIconKind.Search; StateIcon.IsDanger = false;
                StateTitle.Text = "Sin coincidencias con estos filtros"; StateDescription.Text = "Pruebe otro material, módulo o período de consulta.";
                StateAction.Content = "Limpiar filtros"; LimsAction.SetIcon(StateAction, LimsIconKind.ClearFilters); break;
        }
        MonitorSurface.VerticalAlignment = ready || state == AlertScreenState.Loading ? VerticalAlignment.Stretch : VerticalAlignment.Top;
        MonitorGrid.RowDefinitions[3].Height = ready || state == AlertScreenState.Loading ? new(1, GridUnitType.Star) : GridLength.Auto;
        if (!ready) { HideCompactDetail(); DetailSurface.Visibility = Visibility.Collapsed; MainGrid.ColumnDefinitions[1].Width = new(0); }
        else { MainGrid.ColumnDefinitions[1].Width = _compact ? new(0) : new(1, GridUnitType.Star); DetailSurface.Visibility = _compact ? Visibility.Collapsed : Visibility.Visible; }
    }
    private void RenderDetail()
    {
        var detail = ViewModel.Detail;
        var signature = $"{detail?.Alert.Id}|{detail?.Alert.Revision}|{detail?.Alert.ReadAt}|{detail?.Alert.AcknowledgedAt}|{ViewModel.IsDetailBusy}|{ViewModel.IsActionBusy}|{ActualTheme}";
        if (_detailSignature == signature) return; _detailSignature = signature;
        var changed = _renderedDetail?.Alert.Id != detail?.Alert.Id; _renderedDetail = detail;
        DetailPanel.Children.Clear();
        if (ViewModel.IsDetailBusy) { DetailPanel.Children.Add(new ProgressRing { IsActive = true, Width = 24, Height = 24 }); DetailPanel.Children.Add(AlertUi.Text("Consultando detalle…", 13)); return; }
        if (detail is null)
        {
            DetailPanel.Children.Add(new LimsIcon { Icon = LimsIconKind.ViewDetail, Size = 24, HorizontalAlignment = HorizontalAlignment.Left });
            DetailPanel.Children.Add(AlertUi.Text("Detalle de la incidencia", 16, true));
            DetailPanel.Children.Add(AlertUi.Text("Seleccione una fila del monitor para consultar el material, la condición y las acciones disponibles.", 12));
            DetailSurface.VerticalAlignment = VerticalAlignment.Top; return;
        }
        DetailSurface.VerticalAlignment = VerticalAlignment.Stretch;
        var a = detail.Alert;
        DetailPanel.Children.Add(AlertUi.Text("DETALLE DE LA INCIDENCIA", 10, true));
        DetailPanel.Children.Add(AlertUi.Text(a.Material, 19, true));
        var badges = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        badges.Children.Add(new StatusBadge { Text = AlertRow.PriorityLabel(a.Priority), Tone = a.Priority >= AlertPriority.High ? "Danger" : "Warning" });
        badges.Children.Add(new StatusBadge { Text = a.ResolvedAt is not null ? "Resuelta" : a.AcknowledgedAt is not null ? "Atendida · activa" : "Pendiente", Tone = a.ResolvedAt is not null ? "Success" : "Information" }); DetailPanel.Children.Add(badges);
        DetailPanel.Children.Add(AlertUi.Text($"{AlertRow.ModuleLabel(a.Modules)} · {a.MaterialType}\nCódigo {a.Code} · Lote {a.Lot}", 11));
        DetailPanel.Children.Add(AlertUi.Rule());
        DetailPanel.Children.Add(AlertUi.Text(a.Title, 14, true));
        DetailPanel.Children.Add(AlertUi.Text(a.Description, 12));
        if (a.Category == "Availability" && a.AvailabilityPercent is { } percent)
        {
            DetailPanel.Children.Add(AlertUi.Text($"{a.Available:0.######} {a.Unit} disponibles · {percent:0.##} %", 18, true));
            DetailPanel.Children.Add(new ProgressBar { Minimum = 0, Maximum = 100, Value = (double)percent, Height = 4 });
        }
        else DetailPanel.Children.Add(AlertUi.Text($"Vencimiento · {a.Expiration:dd/MM/yyyy}", 17, true));
        DetailPanel.Children.Add(AlertUi.Text($"Detectada · {a.DetectedAt.ToOffset(TimeSpan.FromHours(-6)):dd/MM/yyyy HH:mm}", 11));
        DetailPanel.Children.Add(AlertUi.Text("ACCIÓN RECOMENDADA", 10, true)); DetailPanel.Children.Add(AlertUi.Text(a.Recommendation, 12));
        var open = AlertUi.Button("Ver material", () => { OpenMaterial?.Invoke(this, a); return Task.CompletedTask; }, true, LimsIconKind.ViewDetail); DetailPanel.Children.Add(open);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        if (a.NotificationId is not null && a.ReadAt is null) actions.Children.Add(AlertUi.Button("Marcar como leído", async () => { await ViewModel.MarkReadAsync(); ShowDetail(); }, icon: LimsIconKind.Check));
        if (a.ResolvedAt is null && a.AcknowledgedAt is null && ViewModel.CanManage) actions.Children.Add(AlertUi.Button("Atender", async () => { await ViewModel.AcknowledgeAsync(); ShowDetail(); }, icon: LimsIconKind.Quality));
        if (actions.Children.Count > 0) DetailPanel.Children.Add(actions);
        foreach (var button in actions.Children.OfType<Button>()) button.IsEnabled = !ViewModel.IsActionBusy;
        DetailPanel.Children.Add(AlertUi.Text(a.NotificationId is null ? "Sin notificación personal asociada." : a.ReadAt is not null ? "Aviso leído · la atención es independiente." : "Aviso sin leer · leer no resuelve la condición.", 10));
        DetailPanel.Children.Add(AlertUi.Rule()); DetailPanel.Children.Add(AlertUi.Text("HISTORIAL RECIENTE", 10, true));
        foreach (var h in detail.History.Take(5)) DetailPanel.Children.Add(AlertUi.Text($"{h.At.ToOffset(TimeSpan.FromHours(-6)):dd/MM HH:mm} · {h.Action}\n{(h.ActorUserId is { } actor ? $"Usuario {actor}" : "Motor automático")}" + (h.Note is { Length: > 0 } note ? " · " + note : ""), 10));
        if (changed && IsLoaded) Motion.Enter(DetailPanel, x: 4, milliseconds: 140);
    }
    private void ShowDetail()
    {
        if (!_compact || ViewModel.Detail is null) return;
        DetailScroll.Content = null; CompactDetailContent.Content = DetailPanel;
        CompactDetailHost.Visibility = Visibility.Visible; MonitorSurface.Visibility = Visibility.Collapsed;
        Motion.Enter(CompactDetailHost, x: 5, milliseconds: 150);
    }
    private void HideCompactDetail()
    {
        if (CompactDetailContent is null) return;
        CompactDetailContent.Content = null; DetailScroll.Content = DetailPanel;
        CompactDetailHost.Visibility = Visibility.Collapsed; MonitorSurface.Visibility = Visibility.Visible;
    }
    private async void OnRefresh(object sender, RoutedEventArgs e) => await LoadAsync();
    private void OnSearchChanged(object sender, TextChangedEventArgs e) { if (_initializing) return; _search.Stop(); _search.Start(); }
    private async void OnFilterChanged(object sender, SelectionChangedEventArgs e) { if (!_initializing && IsLoaded) await ApplyAsync(); }
    private async void OnDateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args) { if (!_initializing && IsLoaded) await ApplyAsync(); }
    private async void OnAlertSelected(object sender, SelectionChangedEventArgs e) { if (AlertsList.SelectedItem is AlertRow row) await OpenAsync(row.Item.Id); }
    private async void OnPrevious(object sender, RoutedEventArgs e) { if (PreviousButton.IsEnabled) await ApplyAsync(ViewModel.Filter.Page - 1); }
    private async void OnNext(object sender, RoutedEventArgs e) { if (NextButton.IsEnabled) await ApplyAsync(ViewModel.Filter.Page + 1); }
    private async void OnEvaluate(object sender, RoutedEventArgs e) { EngineActionsButton.Flyout.Hide(); await ViewModel.EvaluateAsync(); }
    public event EventHandler? EngineHistory;
    private void OnEngineHistory(object sender, RoutedEventArgs e) { EngineActionsButton.Flyout.Hide(); EngineHistory?.Invoke(this, EventArgs.Empty); }
    private void OnSettings(object sender, RoutedEventArgs e) => Configure?.Invoke(this, EventArgs.Empty);
    private void OnHistory(object sender, RoutedEventArgs e) => DeliveryHistory?.Invoke(this, EventArgs.Empty);
    private void OnBackToMonitor(object sender, RoutedEventArgs e) => HideCompactDetail();
    private async void OnStateAction(object sender, RoutedEventArgs e) { if (ViewModel.State == AlertScreenState.FilteredEmpty) await ClearAsync(); else await LoadAsync(); }
    private async void OnClear(object sender, RoutedEventArgs e) => await ClearAsync();
    public async Task ClearAsync()
    {
        _initializing = true; SearchBox.Text = ""; FromDate.Date = UntilDate.Date = null;
        CategoryBox.SelectedIndex = PriorityBox.SelectedIndex = ModuleBox.SelectedIndex = StateBox.SelectedIndex = SortBox.SelectedIndex = QuickTabs.SelectedIndex = 0;
        _initializing = false; AdvancedButton.Flyout.Hide(); await ApplyAsync();
    }
    private async void OnQuickTabChanged(object? sender, EventArgs e) { if (!_initializing && IsLoaded) await SetQuickAsync(QuickTabs.SelectedIndex); }
    public async Task SetQuickAsync(int index)
    {
        _initializing = true; QuickTabs.SelectedIndex = index; CategoryBox.SelectedIndex = PriorityBox.SelectedIndex = StateBox.SelectedIndex = 0;
        if (index == 1) PriorityBox.SelectedIndex = 1; else if (index == 2) CategoryBox.SelectedIndex = 3; else if (index == 3) CategoryBox.SelectedIndex = 1; else if (index == 4) StateBox.SelectedIndex = 2;
        _initializing = false; await ApplyAsync();
    }
    private async void OnCritical(object sender, RoutedEventArgs e) => await SetQuickAsync(1);
    private async void OnUpcoming(object sender, RoutedEventArgs e) => await SetQuickAsync(2);
    private async void OnExpired(object sender, RoutedEventArgs e) { _initializing = true; PriorityBox.SelectedIndex = StateBox.SelectedIndex = 0; CategoryBox.SelectedIndex = 4; _initializing = false; await ApplyAsync(); }
    private async void OnPending(object sender, RoutedEventArgs e) { _initializing = true; PriorityBox.SelectedIndex = CategoryBox.SelectedIndex = 0; StateBox.SelectedIndex = 1; _initializing = false; await ApplyAsync(); }
    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (MainGrid is null) return;
        var compact = e.NewSize.Width < 1120;
        if (_compact != compact) { _compact = compact; HideCompactDetail(); }
        MainGrid.ColumnDefinitions[1].Width = compact ? new(0) : new(1, GridUnitType.Star);
        DetailSurface.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        DetailScroll.MaxHeight = Math.Max(130, e.NewSize.Height - MetricsGrid.ActualHeight - HeaderGrid.ActualHeight - 60);
        var twoCards = e.NewSize.Width < 800;
        var shortHeight = e.NewSize.Height < 650;
        AlertsRoot.RowSpacing = shortHeight ? 8 : 12;
        MetricsGrid.RowSpacing = twoCards ? 8 : 0;
        MonitorHeader.Padding = shortHeight ? new(12, 8, 12, 0) : new(16, 13, 16, 5);
        var cards = new[] { CriticalCard, UpcomingCard, ExpiredCard, PendingCard };
        for (var i = 0; i < cards.Length; i++)
        {
            Grid.SetColumn(cards[i], twoCards ? i % 2 : i); Grid.SetRow(cards[i], twoCards ? i / 2 : 0); cards[i].Padding = shortHeight ? new(12, 8, 12, 8) : new(14, 12, 14, 12);
            var content = (Grid)cards[i].Content; content.RowSpacing = shortHeight ? 3 : 8;
            foreach (var tile in content.Children.OfType<Border>()) tile.Width = tile.Height = shortHeight ? 24 : 34;
        }
        MetricsGrid.ColumnDefinitions[2].Width = MetricsGrid.ColumnDefinitions[3].Width = twoCards ? new(0) : new(1, GridUnitType.Star);
        var wrapFilters = e.NewSize.Width < 650;
        FilterGrid.RowSpacing = wrapFilters ? 8 : 0;
        FilterGrid.Padding = shortHeight ? new(12, 7, 12, 7) : new(16, 10, 16, 10);
        Grid.SetColumn(PrimaryFilters, wrapFilters ? 0 : 1); Grid.SetRow(PrimaryFilters, wrapFilters ? 1 : 0);
        var wrapHeader = e.NewSize.Width < 800;
        Grid.SetColumn(HeaderOperations, wrapHeader ? 0 : 1);
        Grid.SetRow(HeaderOperations, wrapHeader ? 1 : 0);
        Grid.SetColumnSpan(HeaderOperations, wrapHeader ? 2 : 1);
        Grid.SetColumnSpan(HeaderIdentity, wrapHeader ? 2 : 1);
        HeaderOperations.HorizontalAlignment = wrapHeader ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        HeaderOperations.Margin = wrapHeader ? new(0, 12, 0, 0) : new(0);
        HeaderGrid.Padding = e.NewSize.Width < 650 ? new(16, 14, 16, 14) : new(20, 16, 20, 16);
        HeaderTitle.FontSize = e.NewSize.Width < 1000 ? 20 : 22;
        LimsAction.SetLabelVisibility(RefreshButton, e.NewSize.Width < 1120 ? Visibility.Collapsed : Visibility.Visible);
        Render();
    }
}

internal static class AlertUi
{
    public static TextBlock Text(string text, double size = 13, bool bold = false) => new() { Text = text, FontSize = size, FontWeight = bold ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal, TextWrapping = TextWrapping.Wrap };
    public static Border Rule() => new() { Height = 1, Background = (Brush)Application.Current.Resources["LimsBorderBrush"] };
    public static Button Button(string title, Func<Task> action, bool primary = false, LimsIconKind? icon = null)
    {
        var button = new Button { Content = title, Style = (Style)Application.Current.Resources[primary ? "LimsPrimaryActionStyle" : "LimsUtilityActionStyle"], HorizontalAlignment = HorizontalAlignment.Stretch };
        LimsAction.SetIcon(button, icon ?? LimsIconKind.ViewDetail); ToolTipService.SetToolTip(button, title);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, title);
        button.Click += async (_, _) => { button.IsEnabled = false; try { await action(); } finally { button.IsEnabled = true; } };
        return button;
    }
}

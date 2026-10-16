using System.Globalization;
using Lims.Contracts.Dashboard;
using Lims.Desktop.Controls;
using Lims.Desktop.Presentation;
using Lims.Desktop.ViewModels;
using Lims.DesignSystem.Controls;
using Lims.DesignSystem.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.UI.ViewManagement;
using static Lims.Desktop.Controls.DashboardComponents;

namespace Lims.Desktop.Views;
public sealed record DashboardRoute(string Module, string? Filter = null, string? Search = null, Guid? RecordId = null);

public sealed partial class DashboardPage : Page
{
    public DashboardViewModel ViewModel { get; }
    public event EventHandler<DashboardRoute>? Navigate;
    private readonly List<FrameworkElement> _metrics = [];
    private readonly DispatcherTimer _appearanceTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly AccessibilitySettings _accessibility = new();
    private bool _highContrast;
    private int _layoutTier = -1;
    public DashboardPage(DashboardViewModel viewModel)
    {
        ViewModel = viewModel; InitializeComponent();
        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(DashboardViewModel.Data) or nameof(DashboardViewModel.Error) or nameof(DashboardViewModel.IsBusy)) Render();
        };
        SizeChanged += (_, _) =>
        {
            var tier = ActualWidth >= 980 ? 3 : ActualWidth >= 880 ? 2 : ActualWidth >= 700 ? 1 : 0;
            if (tier != _layoutTier) { _layoutTier = tier; Render(); } else Layout();
        };
        ActualThemeChanged += (_, _) => Render();
        _appearanceTimer.Tick += (_, _) =>
        {
            // Some desktop environments do not expose the HighContrastChanged event.
            // XAML resources update natively; refresh the code-created chart brushes too.
            if (_highContrast != _accessibility.HighContrast) { _highContrast = _accessibility.HighContrast; Render(); }
        };
        Loaded += (_, _) => _appearanceTimer.Start();
        Unloaded += (_, _) => { _appearanceTimer.Stop(); ViewModel.Clear(); };
    }
    public Task LoadAsync() => ViewModel.LoadAsync();
    private async void OnRetry(object sender, RoutedEventArgs e) { if (!ViewModel.IsBusy) await LoadAsync(); }
    private void Go(string module, string? filter = null, string? search = null, Guid? id = null) => Navigate?.Invoke(this, new(module, filter, search, id));

    private void Render()
    {
        DashboardComponents.Theme = ActualTheme;
        RetryButton.IsEnabled = !ViewModel.IsBusy;
        ErrorBar.IsOpen = ViewModel.Error.Length != 0; ErrorBar.Message = ViewModel.Error;
        ErrorBar.Visibility = ErrorBar.IsOpen ? Visibility.Visible : Visibility.Collapsed;
        // Keep the previous visual composition while the authorized refresh is in flight.
        // Clear() still removes it immediately when authorization is revoked or the page is left.
        if (ViewModel.IsBusy) return;
        _metrics.Clear(); MetricsGrid.Children.Clear(); OverviewGrid.Children.Clear(); AnalyticsGrid.Children.Clear();
        OperationsGrid.Children.Clear(); EmptyPanel.Children.Clear();
        EmptyPanel.Visibility = Visibility.Collapsed;
        if (ViewModel.Data is not {} data) return;
        if (data.View == "Purchasing")
        { Pending("Abastecimiento", "El servicio de compras todavía no está conectado. Las solicitudes y recepciones se mostrarán cuando exista una fuente real."); return; }
        if (data.Standards is null && data.Stock is null && data.Intermediate is null)
        { Pending("Sin operaciones autorizadas", "Su cuenta no tiene permisos de consulta de materiales. Consulte a su administrador."); return; }
        RenderMetrics(data);
        if (data.Standards is {} standards)
        {
            OverviewGrid.Children.Add(AnalyticPanel("Estado general de estándares", "Estados efectivos del catálogo completo",
                Donut(DashboardAnalytics.States(standards), "Total", ActualWidth >= 980 ? 154 : 140), () => Go("standards")));
        }
        OverviewGrid.Children.Add(AnalyticPanel("Vencimientos prioritarios", "Los seis vencimientos futuros más cercanos",
            ExpirationTable(data.Expirations), data.Standards is not null ? () => Go("standards") : null, "Ver todos"));
        var statistics = data.MethodStatistics;
        var scope = statistics?.Scope == "Standards" ? "estándares" : statistics?.Scope == "Stock" ? "Stock" : statistics?.Scope == "Intermedia" ? "intermedias" : "preparaciones";
        var unavailable = statistics is null ? "Estadísticas del catálogo no disponibles" : null;
        AnalyticsGrid.Children.Add(AnalyticPanel("Disponibilidad por método", unavailable ?? $"Promedio simple (%) · {statistics!.AvailabilityTotal} {scope} utilizables",
            VerticalBars(DashboardAnalytics.MethodAvailability(data), unavailable)));
        AnalyticsGrid.Children.Add(AnalyticPanel("Distribución por método", unavailable ?? $"Catálogo autorizado · {statistics!.Total} {scope}",
            HorizontalBars(DashboardAnalytics.Methods(data), unavailable)));
        if (data.Standards is {} availability)
            AnalyticsGrid.Children.Add(AnalyticPanel("Resumen de disponibilidad", "Estándares · distribución sin duplicar materiales",
                Donut(DashboardAnalytics.Availability(availability), "Total", ActualWidth >= 980 ? 126 : 140), () => Go("standards")));
        OperationsGrid.Children.Add(AnalyticPanel("Materiales con disponibilidad baja", "Hasta el 60 % · umbral operativo actual",
            LowTable(data.LowAvailability)));
        OperationsGrid.Children.Add(AlertPanel(data));
        Layout();
        if (!ViewModel.IsBusy) { Motion.Enter(MetricsGrid); Motion.Enter(OverviewGrid); Motion.Enter(AnalyticsGrid); }
    }

    private void RenderMetrics(DashboardResponse data)
    {
        if (data.Standards is {} standards)
        {
            _metrics.Add(Metric("Estándares registrados", Number(standards.Registered), $"{Number(standards.Active)} activos y disponibles", LimsIconKind.Standards, "Information",
                () => Go("standards"), Ratio(standards.Active, standards.Registered), "Activos / registrados"));
            var critical = standards.Critical + (data.Stock?.Critical ?? 0) + (data.Intermediate?.Critical ?? 0);
            var usable = standards.Active + (data.Stock?.Available ?? 0) + (data.Intermediate?.Available ?? 0);
            _metrics.Add(Metric("Disponibilidad crítica", Number(critical), "Materiales utilizables · menos del 25 %", LimsIconKind.Alerts, critical > 0 ? "Danger" : "Success",
                percent: Ratio(critical, usable), detail: "Críticos / utilizables autorizados"));
            NextExpiry(data);
            _metrics.Add(Metric("Estándares vencidos", Number(standards.Expired), "Fuera de vigencia", LimsIconKind.Archive, standards.Expired > 0 ? "Danger" : "Success",
                () => Go("standards", "Expired"), Ratio(standards.Expired, standards.Registered), "Vencidos / registrados"));
        }
        else
        {
            NextExpiry(data);
            var low = (data.Stock?.Low ?? 0) + (data.Intermediate?.Low ?? 0);
            _metrics.Add(Metric("Disponibilidad baja", Number(low), "Materiales utilizables con hasta el 60 %", LimsIconKind.Availability, low > 0 ? "Warning" : "Success"));
            if (data.Stock is {} stock) _metrics.Add(Metric("Stock disponible", Number(stock.Available), "Preparaciones aptas para uso", LimsIconKind.Preparations, "Information", () => Go("stock")));
            if (data.Intermediate is {} intermediate) _metrics.Add(Metric("Intermedias disponibles", Number(intermediate.Available), "Preparaciones aptas para uso", LimsIconKind.Preparations, "Success", () => Go("intermediate")));
        }
    }
    private void NextExpiry(DashboardResponse data)
    {
        var next = data.Expirations.Count == 0 ? null : data.Expirations[0];
        _metrics.Add(Metric("Próximo vencimiento", next is null ? "—" : next.DaysRemaining == 0 ? "Hoy" : $"{next.DaysRemaining} días",
            next?.Name ?? "Sin vencimientos futuros", LimsIconKind.Calendar, next is null ? "Neutral" : IsSoon(data, next) ? "Warning" : "Information",
            next is null ? null : () => Go(Module(next.Kind), search: next.Name, id: next.Id),
            next?.AvailabilityPercent is {} p ? (double)p : null, next is null ? "Consulta actual" : $"Lote {next.Lot} · disponibilidad"));
    }
    private static bool IsSoon(DashboardResponse data, DashboardMaterial material) =>
        material.DaysRemaining == 0 || data.ExpirationWindowDays is {} window && material.DaysRemaining <= window;
    private static string Number(int value) => value.ToString("N0", CultureInfo.GetCultureInfo("es-GT"));
    private static double? Ratio(int value, int total) => total == 0 ? null : value * 100d / total;
    private static string Module(string kind) => kind switch { "Stock" => "stock", "Intermedia" => "intermediate", _ => "standards" };
    private void Pending(string title, string description)
    { EmptyPanel.Children.Add(AnalyticPanel(title, "", Text(description, 12, "LimsTextSecondaryBrush"))); EmptyPanel.Visibility = Visibility.Visible; }
    private Button MaterialLink(DashboardMaterial material) => Action(material.Name, LimsIconKind.ViewDetail, () => Go(Module(material.Kind), search: material.Name, id: material.Id));
    private static Grid Balance(DashboardMaterial row)
    {
        var balance = new Grid { ColumnSpacing = 5 };
        balance.ColumnDefinitions.Add(new() { Width = new(33) }); balance.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        balance.Children.Add(Text(row.PercentDisplay, 9, ToneBrush(row.AvailabilityTone)));
        if (row.AvailabilityPercent.HasValue)
        {
            var bar = Pill((double)row.AvailabilityPercent.Value, ToneBrush(row.AvailabilityTone), 7);
            bar.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(bar, 1);
            AutomationProperties.SetName(bar, $"Disponibilidad de {row.Name}: {row.PercentDisplay}"); balance.Children.Add(bar);
        }
        return balance;
    }
    private UIElement ExpirationTable(IReadOnlyList<DashboardMaterial> materials)
    {
        if (materials.Count == 0) return EmptyStatus("Sin vencimientos futuros", "Los materiales vigentes aparecerán aquí por fecha de vencimiento.", LimsIconKind.Calendar);
        if (ActualWidth < 700)
        {
            var cards = new StackPanel { Spacing = 8 };
            foreach (var row in materials)
            {
                var card = new StackPanel { Spacing = 3 }; card.Children.Add(MaterialLink(row));
                card.Children.Add(Text($"{row.Lot} · {row.Method} · {row.ExpirationDisplay} · {row.DaysDisplay}", 10, "LimsTextSecondaryBrush"));
                card.Children.Add(Balance(row)); cards.Children.Add(card);
            }
            return cards;
        }
        return DataTable(["Nombre", "Lote", "Método", "Vence", "Días", "Disponible"], [-1.4, -0.8, -0.85, 62, 30, 86], materials.Select(row => new UIElement[]
        { MaterialName(row), Text(row.Lot, 10), Text(row.Method, 10, "LimsTextSecondaryBrush"), Text(row.ExpirationDisplay, 10), Text(row.DaysRemaining.ToString(CultureInfo.CurrentCulture), 10, row.DaysRemaining == 0 ? "LimsWarningBrush" : "LimsAccentBrush"), Balance(row) }),
            index => OpenMaterial(materials[index]));
    }
    private UIElement LowTable(IReadOnlyList<DashboardMaterial> materials)
    {
        if (materials.Count == 0) return EmptyStatus("Sin materiales con disponibilidad baja", "Los saldos consultados no requieren reposición por este indicador.", LimsIconKind.Availability);
        return DataTable(["Nombre", "Lote", "Disponible", "Estado"], [-1.5, -.8, 110, 64], materials.Select(row => new UIElement[]
        { MaterialName(row), Text(row.Lot, 10, "LimsTextSecondaryBrush"), Balance(row), Badge(row.AvailabilityPercent < 25 ? "Crítico" : "Bajo", row.AvailabilityTone) }), index => OpenMaterial(materials[index]));
    }
    private void OpenMaterial(DashboardMaterial row) => Go(Module(row.Kind), search: row.Name, id: row.Id);
    private static Grid MaterialName(DashboardMaterial row)
    {
        var line = new Grid();
        var label = Text(row.Name, 10); label.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        label.TextWrapping = TextWrapping.NoWrap; label.TextTrimming = TextTrimming.CharacterEllipsis;
        line.Children.Add(label); ToolTipService.SetToolTip(line, $"Abrir {row.Name} · {row.Lot}"); AutomationProperties.SetName(line, $"Abrir {row.Name}"); return line;
    }
    private Border AlertPanel(DashboardResponse data)
    {
        var content = new StackPanel { Spacing = 3 };
        var conditions = data.LowAvailability.Select(m => (Material: m, Label: m.AvailabilityPercent < 25 ? "Disponibilidad crítica" : "Disponibilidad baja", Tone: m.AvailabilityTone, Date: m.PercentDisplay))
            .Concat(data.Expirations.Where(m => IsSoon(data, m)).Select(m => (Material: m, Label: m.DaysRemaining == 0 ? "Vence hoy" : "Vencimiento próximo", Tone: "Warning", Date: m.ExpirationDisplay))).ToArray();
        if (conditions.Length > 0)
        {
            content.Children.Add(DataTable(["Alerta", "Material", "Detalle"], [-1, -1.5, 100], conditions.Select(condition => new UIElement[]
            { Text(condition.Label, 10, ToneBrush(condition.Tone)), MaterialName(condition.Material), Badge(condition.Date, condition.Tone) }),
                index => OpenMaterial(conditions[index].Material)));
        }
        if (data.Standards is { Expired: > 0 } standards)
        {
            var summary = new Grid { ColumnSpacing = 10 };
            summary.ColumnDefinitions.Add(new() { Width = new(36) }); summary.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); summary.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            summary.Children.Add(IconTile(LimsIconKind.Archive, "LimsDangerBrush", 36));
            var labels = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
            var title = Text($"{Number(standards.Expired)} estándares vencidos", 12, "LimsDangerBrush"); title.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            labels.Children.Add(title); labels.Children.Add(Text("Fuera de vigencia · revisar el catálogo", 10, "LimsTextSecondaryBrush"));
            Grid.SetColumn(labels, 1); summary.Children.Add(labels);
            var expired = Link("Revisar", () => Go("standards", "Expired"), $"{Number(standards.Expired)} estándares vencidos · Revisar");
            Grid.SetColumn(expired, 2); summary.Children.Add(expired);
            var notice = Surface(summary, 10); notice.Background = BrushResource("LimsSurfaceSecondaryBrush"); notice.BorderThickness = new(2, 0, 0, 0); notice.BorderBrush = BrushResource("LimsDangerBrush"); notice.CornerRadius = new(6);
            content.Children.Add(notice);
        }
        if (data.Stock is { Expired: > 0 } stockExpired) content.Children.Add(Text($"Stock vencido: {Number(stockExpired.Expired)}", 10, "LimsDangerBrush"));
        if (data.Intermediate is { Expired: > 0 } intermediateExpired) content.Children.Add(Text($"Intermedias vencidas: {Number(intermediateExpired.Expired)}", 10, "LimsDangerBrush"));
        if (conditions.Length == 0 && (data.Standards?.Expired ?? 0) + (data.Stock?.Expired ?? 0) + (data.Intermediate?.Expired ?? 0) == 0)
            content.Children.Add(EmptyStatus("Sin alarmas en los datos consultados", "Disponibilidad y vigencias sin condiciones de alerta.", LimsIconKind.Availability));
        return AnalyticPanel("Alarmas y notificaciones", "Condiciones actuales · consulta del laboratorio", content);
    }
    private static Grid EmptyStatus(string title, string caption, LimsIconKind icon)
    {
        var root = new Grid { ColumnSpacing = 12, Padding = new Thickness(10), Background = BrushResource("LimsSurfaceSecondaryBrush"), CornerRadius = new(6) };
        root.ColumnDefinitions.Add(new() { Width = new(36) }); root.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        root.Children.Add(IconTile(icon, "LimsSuccessBrush", 36));
        var body = new StackPanel { Spacing = 3 }; body.Children.Add(Text(title, 12)); body.Children.Add(Text(caption, 10, "LimsTextSecondaryBrush")); Grid.SetColumn(body, 1); root.Children.Add(body); return root;
    }
    private void Layout()
    {
        Arrange(MetricsGrid, _metrics, ActualWidth >= 700 ? 4 : ActualWidth >= 500 ? 2 : 1);
        var overview = OverviewGrid.Children.Cast<FrameworkElement>().ToArray();
        Arrange(OverviewGrid, overview, ActualWidth >= 700 && overview.Length > 1 ? 2 : 1);
        if (ActualWidth >= 700 && overview.Length > 1) { OverviewGrid.ColumnDefinitions[0].Width = new(ActualWidth >= 980 ? ActualWidth * .40 : 340); OverviewGrid.ColumnDefinitions[1].Width = new(1, GridUnitType.Star); }
        Arrange(AnalyticsGrid, AnalyticsGrid.Children.Cast<FrameworkElement>().ToArray(), ActualWidth >= 980 ? 3 : ActualWidth >= 700 ? 2 : 1);
        if (ActualWidth is >= 700 and < 980 && AnalyticsGrid.Children.Count == 3)
            Grid.SetColumnSpan((FrameworkElement)AnalyticsGrid.Children[2], 2);
        Arrange(OperationsGrid, OperationsGrid.Children.Cast<FrameworkElement>().ToArray(), ActualWidth >= 700 ? 2 : 1);
    }
    private static void Arrange(Grid grid, IReadOnlyList<FrameworkElement> elements, int columns)
    {
        grid.Children.Clear(); grid.ColumnDefinitions.Clear(); grid.RowDefinitions.Clear();
        for (var i = 0; i < columns; i++) grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        for (var i = 0; i < (elements.Count + columns - 1) / columns; i++) grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        for (var i = 0; i < elements.Count; i++) { elements[i].VerticalAlignment = VerticalAlignment.Stretch; Grid.SetColumnSpan(elements[i], 1); Grid.SetColumn(elements[i], i % columns); Grid.SetRow(elements[i], i / columns); grid.Children.Add(elements[i]); }
    }
}

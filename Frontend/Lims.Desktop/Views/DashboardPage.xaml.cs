using System.Globalization;
using Lims.Contracts.Dashboard;
using Lims.Contracts.Permissions;
using Lims.Contracts.ReferenceMaterials;
using Lims.Desktop.Controls;
using Lims.Desktop.Presentation;
using Lims.Desktop.ViewModels;
using Lims.DesignSystem.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using static Lims.Desktop.Controls.DashboardComponents;
namespace Lims.Desktop.Views;
public sealed record DashboardRoute(string Module, string? Filter = null, string? Search = null, Guid? RecordId = null);
public sealed partial class DashboardPage : Page
{
    public DashboardViewModel ViewModel { get; }
    public event EventHandler<DashboardRoute>? Navigate;
    private static readonly double[] ExpirationWidths = [-1, 76, 92, 84, 70, 108];
    private static readonly string[] ExpirationLabels = ["Nombre", "Lote", "Método", "Vencimiento", "Días restantes", "Disponibilidad"];
    private readonly List<FrameworkElement> _metrics = [];
    private int _layoutTier = -1;
        public DashboardPage(DashboardViewModel viewModel)
    {
        ViewModel = viewModel; InitializeComponent();
        ViewModel.PropertyChanged += (_, args) => { if (args.PropertyName is nameof(DashboardViewModel.Data) or nameof(DashboardViewModel.Error) or nameof(DashboardViewModel.IsBusy)) Render(); };
        SizeChanged += (_, _) => { var tier = ActualWidth >= 1100 ? 2 : ActualWidth >= 690 ? 1 : 0; if (tier != _layoutTier) { _layoutTier = tier; Render(); } else Layout(); };
        ActualThemeChanged += (_, _) => Render();
        Unloaded += (_, _) => ViewModel.Clear();
    }
    public Task LoadAsync() => ViewModel.LoadAsync();
    private async void OnRefresh(object sender, RoutedEventArgs e) => await LoadAsync();
    private void Go(string module, string? filter = null, string? search = null, Guid? id = null) => Navigate?.Invoke(this, new(module, filter, search, id));
    private void Render()
    {
        DashboardComponents.Theme = ActualTheme;
        LoadingIndicator.Visibility = ViewModel.IsBusy ? Visibility.Visible : Visibility.Collapsed;
        RefreshButton.IsEnabled = !ViewModel.IsBusy;
        ErrorBar.IsOpen = ViewModel.Error.Length != 0; ErrorBar.Message = ViewModel.Error;
        _metrics.Clear(); MetricsGrid.Children.Clear(); OverviewGrid.Children.Clear(); OperationsGrid.Children.Clear(); QuickPanel.Children.Clear(); EmptyPanel.Children.Clear();
        EmptyPanel.Visibility = Visibility.Collapsed; Timestamp.Text = string.Empty;
        if (ViewModel.Data is not {} data) { Welcome.Text = "Su laboratorio, de un vistazo"; Identity.Text = ViewModel.IsBusy ? "Consultando información autorizada…" : "Actualice para consultar la información del laboratorio."; return; }
        Welcome.Text = $"Bienvenido, {data.Name}";
        var viewLabel = data.View switch { "Administrator" => "Gestión general", "Laboratory" => "Operación de laboratorio", "Purchasing" => "Abastecimiento", _ => "Inicio" };
        Identity.Text = $"{viewLabel} · {data.Role} · {data.Department ?? "Sin departamento"}";
        Timestamp.Text = $"Última consulta: {ReferenceMaterialPresentation.LocalTimestamp(data.AsOf)} · Fechas del laboratorio (Guatemala).";
        if (data.View == "Purchasing")
        {
            Pending("Compras", "El seguimiento de abastecimiento estará disponible cuando se conecten sus servicios.", ["Solicitudes pendientes", "Materiales para reposición", "Pedidos y recepciones", "Compras recientes", "Alertas de abastecimiento"]);
            return;
        }
        if (data.Standards is null && data.Stock is null && data.Intermediate is null)
        { Pending("Sin operaciones autorizadas", "Su cuenta no tiene permisos de consulta para los módulos de materiales. Consulte a su administrador.", []); return; }
        if (data.View == "Administrator" && data.Standards is {} standards)
        {
            AddMetric("Estándares registrados", standards.Registered.ToString(CultureInfo.GetCultureInfo("es-GT")), $"{standards.Active} activos y disponibles para uso", LimsIconKind.Standards, "Information", () => Go("standards"));
            AddMetric("Disponibilidad crítica", (standards.Critical + (data.Stock?.Critical ?? 0) + (data.Intermediate?.Critical ?? 0)).ToString(CultureInfo.GetCultureInfo("es-GT")), "Materiales utilizables con menos del 25 %", LimsIconKind.Availability, "Danger");
            NextExpiry(data);
            AddMetric("Estándares vencidos", standards.Expired.ToString(CultureInfo.GetCultureInfo("es-GT")), "Estado efectivo; consultar registros", LimsIconKind.Calendar, "Danger", () => Go("standards", "Expired"));
        }
        else
        {
            NextExpiry(data);
            AddMetric("Disponibilidad baja", ((data.Standards?.Low ?? 0) + (data.Stock?.Low ?? 0) + (data.Intermediate?.Low ?? 0)).ToString(CultureInfo.GetCultureInfo("es-GT")), "Materiales utilizables con hasta el 60 %", LimsIconKind.Availability, "Warning");
            if (data.Stock is {} stock) AddMetric("Stock disponible", stock.Available.ToString(CultureInfo.GetCultureInfo("es-GT")), "Preparaciones aptas para uso", LimsIconKind.Preparations, "Information", () => Go("stock"));
            if (data.Intermediate is {} intermediate) AddMetric("Intermedias disponibles", intermediate.Available.ToString(CultureInfo.GetCultureInfo("es-GT")), "Preparaciones aptas para uso", LimsIconKind.Preparations, "Information", () => Go("intermediate"));
        }
        if (data.Standards is {} distribution)
        {
            var panel = Section("Estándares por estado", $"{distribution.Registered} registrados · estados calculados a la fecha");
            foreach (var state in distribution.Distribution)
            {
                var line = new Grid { ColumnSpacing = 12 }; line.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); line.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                line.Children.Add(Text(state.Label)); var count = Text(state.Count.ToString(CultureInfo.GetCultureInfo("es-GT")), 14, ToneBrush(state.Tone)); Grid.SetColumn(count, 1); line.Children.Add(count); panel.Children.Add(line);
                var bar = new ProgressBar { Maximum = Math.Max(1, distribution.Registered), Value = state.Count, Height = 4, Foreground = BrushResource(ToneBrush(state.Tone)) };
                AutomationProperties.SetName(bar, $"{state.Label}: {state.Count} de {distribution.Registered}"); panel.Children.Add(bar);
            }
            if (distribution.Registered == 0) panel.Children.Add(Text("Todavía no hay estándares registrados.", 13, "LimsTextSecondaryBrush"));
            OverviewGrid.Children.Add(Surface(panel));
        }
        var expiryPanel = Section("Vencimientos prioritarios", "Los seis vencimientos más cercanos · fecha y disponibilidad independientes");
        if (data.Expirations.Count == 0) expiryPanel.Children.Add(Text("No hay registros vigentes con vencimientos futuros.", 13, "LimsTextSecondaryBrush"));
        if (data.Expirations.Count != 0) expiryPanel.Children.Add(ExpirationTable(data.Expirations));
        if (ActualWidth < 1100) OverviewGrid.Children.Insert(0, Surface(expiryPanel)); else OverviewGrid.Children.Add(Surface(expiryPanel));
        var lowPanel = Section("Materiales con disponibilidad baja", "Hasta el 60 % del saldo inicial · ordenados de menor a mayor");
        if (data.LowAvailability.Count == 0) lowPanel.Children.Add(Text("No hay materiales utilizables con disponibilidad baja.", 13, "LimsTextSecondaryBrush"));
        foreach (var row in data.LowAvailability) lowPanel.Children.Add(MaterialRow(row, false));
        OperationsGrid.Children.Add(Surface(lowPanel));
        var activity = Section("Actividad reciente", "Operaciones registradas en los módulos autorizados");
        if (data.Activity.Count == 0) activity.Children.Add(Text("Todavía no hay operaciones registradas.", 13, "LimsTextSecondaryBrush"));
        foreach (var item in data.Activity)
        {
            var row = new StackPanel { Spacing = 4 };
            row.Children.Add(Text(item.Action, 12, "LimsTextSecondaryBrush"));
            row.Children.Add(Action(item.Material, LimsIconKind.ViewDetail, () => Go(Module(item.Kind), search: item.Material, id: item.RecordId)));
            row.Children.Add(Text($"{item.User} · {item.DateDisplay}", 12, "LimsTextSecondaryBrush")); activity.Children.Add(row);
        }
        OperationsGrid.Children.Add(Surface(activity));
        QuickPanel.Children.Add(Text("Accesos rápidos", 16));
        var quick = new Grid { ColumnSpacing = 10, RowSpacing = 10 };
        var actions = new List<Button>();
        if (data.Standards != null) actions.Add(Action("Estándares", LimsIconKind.Standards, () => Go("standards")));
        if (data.Stock != null) actions.Add(Action("Stock", LimsIconKind.Preparations, () => Go("stock")));
        if (data.Intermediate != null) actions.Add(Action("Intermedia", LimsIconKind.Preparations, () => Go("intermediate")));
        if (data.View == "Administrator") actions.Add(Action("Gestión de permisos", LimsIconKind.Administration, () => Go("permissions")));
        // Two columns leave room for translated labels and keyboard focus at 125% DPI.
        Arrange(quick, actions.Cast<FrameworkElement>().ToArray(), 2); QuickPanel.Children.Add(quick);
        Layout();
        if (!ViewModel.IsBusy) { for (var i = 0; i < _metrics.Count; i++) DashboardMotion.Enter(_metrics[i], i * 30); DashboardMotion.Enter(OverviewGrid); DashboardMotion.Enter(OperationsGrid); }
    }
    private void NextExpiry(DashboardResponse data)
    {
        var next = (data.Expirations.Count == 0 ? null : data.Expirations[0]);
        AddMetric("Próximo vencimiento", next is null ? "—" : next.DaysRemaining == 0 ? "Hoy" : $"{next.DaysRemaining} días", next?.Name ?? "Sin vencimientos futuros", LimsIconKind.Calendar, next is null ? "Neutral" : "Warning");
    }
    private void AddMetric(string title, string value, string caption, LimsIconKind icon, string tone, Action? action = null) => _metrics.Add(Metric(title, value, caption, icon, tone, action));
    private void Pending(string title, string description, string[] features)
    {
        var section = Section(title, description);
        foreach (var feature in features) section.Children.Add(Text($"{feature} · Pendiente de conexión", 14, "LimsTextSecondaryBrush"));
        EmptyPanel.Children.Add(Surface(section, 24)); EmptyPanel.Visibility = Visibility.Visible;
    }
    private static string Module(string kind) => kind switch { "Stock" => "stock", "Intermedia" => "intermediate", _ => "standards" };
    private StackPanel ExpirationTable(IReadOnlyList<DashboardMaterial> rows)
    {
        var panelWidth = ActualWidth >= 1100 ? ActualWidth - 352 : ActualWidth - 40;
        if (panelWidth < 650)
        {
            var cards = new StackPanel { Spacing = 4 };
            foreach (var row in rows) cards.Children.Add(MaterialRow(row, true));
            return cards;
        }
        var table = new StackPanel { Spacing = 8 };
        Grid Line()
        {
            var grid = new Grid { ColumnSpacing = 8, Padding = new Thickness(0, 8, 0, 8) };
            foreach (var width in ExpirationWidths)
                grid.ColumnDefinitions.Add(new() { Width = width < 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(width) });
            return grid;
        }
        var header = Line();
        for (var i = 0; i < ExpirationLabels.Length; i++) { var label = Text(ExpirationLabels[i], 11, "LimsTextSecondaryBrush"); Grid.SetColumn(label, i); header.Children.Add(label); }
        table.Children.Add(header);
        foreach (var row in rows)
        {
            var line = Line();
            line.BorderBrush = BrushResource("LimsBorderBrush"); line.BorderThickness = new Thickness(0, 0, 0, 1);
            var name = new StackPanel { Spacing = 4 };
            name.Children.Add(Action(row.Name, LimsIconKind.ViewDetail, () => Go(Module(row.Kind), search: row.Name, id: row.Id)));
            name.Children.Add(Text(row.Kind == "Standard" ? "Estándar" : row.Kind, 11, "LimsTextSecondaryBrush"));
            line.Children.Add(name);
            var values = new[] { row.Lot, row.Method, row.ExpirationDisplay, row.DaysDisplay };
            for (var i = 0; i < values.Length; i++) { var text = Text(values[i], 12); Grid.SetColumn(text, i + 1); line.Children.Add(text); }
            var balance = new StackPanel { Spacing = 6 };
            balance.Children.Add(Text(row.PercentDisplay, 13, ToneBrush(row.AvailabilityTone)));
            if (row.AvailabilityPercent.HasValue)
            {
                var bar = new LimsAvailabilityBar { Value = row.Progress, Tone = row.AvailabilityTone, Height = 6, Foreground = BrushResource(ToneBrush(row.AvailabilityTone)) };
                AutomationProperties.SetName(bar, $"Disponibilidad de {row.Name}: {row.PercentDisplay}");
                balance.Children.Add(bar);
            }
            Grid.SetColumn(balance, 5); line.Children.Add(balance); table.Children.Add(line);
        }
        return table;
    }
    private Grid MaterialRow(DashboardMaterial row, bool expiry)
    {
        // Shared row: compact table at large widths, wrapping cells at smaller widths.
        var line = new Grid { ColumnSpacing = 14, RowSpacing = 5, Padding = new Thickness(0, 8, 0, 8), BorderBrush = BrushResource("LimsBorderBrush"), BorderThickness = new Thickness(0, 0, 0, 1) };
        line.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); line.ColumnDefinitions.Add(new() { Width = new(132) });
        line.RowDefinitions.Add(new() { Height = GridLength.Auto }); line.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var info = new StackPanel { Spacing = 5 };
        info.Children.Add(Action(row.Name, LimsIconKind.ViewDetail, () => Go(Module(row.Kind), search: row.Name, id: row.Id)));
        info.Children.Add(Text($"{(row.Kind == "Standard" ? "Estándar" : row.Kind)} · Lote: {row.Lot} · Método: {row.Method}", 12, "LimsTextSecondaryBrush"));
        info.Children.Add(Text($"Vence: {row.ExpirationDisplay} · {row.DaysDisplay}", 12, expiry ? "LimsTextPrimaryBrush" : "LimsTextSecondaryBrush"));
        line.Children.Add(info);
        var balance = new StackPanel { Spacing = 7, VerticalAlignment = VerticalAlignment.Center };
        balance.Children.Add(Text("Disponible", 11, "LimsTextSecondaryBrush")); balance.Children.Add(Text(row.PercentDisplay, 15, ToneBrush(row.AvailabilityTone)));
        if (row.AvailabilityPercent.HasValue)
        {
            var bar = new LimsAvailabilityBar { Value = row.Progress, Tone = row.AvailabilityTone, Height = 6, Foreground = BrushResource(ToneBrush(row.AvailabilityTone)) };
            AutomationProperties.SetName(bar, $"Disponibilidad de {row.Name}: {row.PercentDisplay}"); balance.Children.Add(bar);
        }
        Grid.SetColumn(balance, 1); line.Children.Add(balance);
        return line;
    }
    private void Layout()
    {
        Arrange(MetricsGrid, _metrics, ActualWidth >= 720 ? 4 : 2);
        var wide = ActualWidth >= 1100; 
        var overview = OverviewGrid.Children.Cast<FrameworkElement>().ToArray();
        Arrange(OverviewGrid, overview, wide && overview.Length > 1 ? 2 : 1);
        if (wide && overview.Length > 1) { OverviewGrid.ColumnDefinitions[0].Width = new GridLength(300); OverviewGrid.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star); }
        Arrange(OperationsGrid, OperationsGrid.Children.Cast<FrameworkElement>().ToArray(), ActualWidth >= 850 ? 2 : 1);
    }
    private static void Arrange(Grid grid, IReadOnlyList<FrameworkElement> elements, int columns)
    {
        grid.Children.Clear(); grid.ColumnDefinitions.Clear(); grid.RowDefinitions.Clear();
        for (var i = 0; i < columns; i++) grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        for (var i = 0; i < (elements.Count + columns - 1) / columns; i++) grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        for (var i = 0; i < elements.Count; i++) { Grid.SetColumn(elements[i], i % columns); Grid.SetRow(elements[i], i / columns); grid.Children.Add(elements[i]); }
    }
}

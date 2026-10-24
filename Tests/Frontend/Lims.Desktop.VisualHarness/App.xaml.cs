using System.Globalization;
using System.Text.Json;
using Lims.Desktop.ViewModels;
using Lims.Desktop.Views;
using Lims.DesignSystem.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace Lims.Desktop.VisualHarness;

public partial class App : Microsoft.UI.Xaml.Application
{
    private static readonly JsonSerializerOptions ReportOptions = new() { WriteIndented = true };
    private static readonly string[] PopupFields = ["MethodBox", "UnitCombo", "StorageLocationBox", "ReceivedDatePicker", "ExpirationDatePicker"];
    private static readonly string[] StockPopupFields = ["SourceList", "ConcentrationUnitSelect", "PreparationDatePicker", "ExpirationDatePicker"];
    private static readonly string[] DetailSectionNames = ["HeaderGrid", "KpiGrid", "GeneralSection", "AuditSection", "AvailabilityCard", "ExpiryCard", "StateCard"];
    private static readonly string[] PermissionElementNames = ["UsersPanel", "UserSearch", "UsersList", "DetailPanel", "UserHeader", "PermissionTabs", "TabContent", "PermissionScroll", "HistoryView", "WorkspaceFooter", "SaveButton", "DiscardButton", "ReloadButton"];
    private readonly FixtureDashboardApi _dashboardApi = new();
    private DashboardPage? _dashboardPage;
    private static readonly string[] DashboardElementNames = ["DashboardHeader", "DashboardTitle", "MetricsGrid", "OverviewGrid", "AnalyticsGrid", "OperationsGrid"];
    private Window? _window;
    private ShellPage? _shell;
    private AlertsWorkspace? _alertsWorkspace;
    private FixtureAlertsApi? _alertsApi;
    private readonly Lims.Desktop.Services.WindowsAlertNotifications _alertWindows = new();
    private ReferenceMaterialsPage? _page;
    private Border? _standardsTableForComparison;
    private TextBlock? _standardsTitleForComparison;
    private FixtureCommercialPreparationApi? _commercialApi;
    private StockPage? _stockPage;
    private IntermediatePage? _intermediatePage;
    private SolutionGroupWorkspace? _solutionGroups;
    private FixtureSolutionGroupApi? _solutionApi;
    private PermissionsPage? _permissionsPage;
    private readonly FixturePermissionsApi _permissionsApi = new();
    private double _fourTabWidth;
    private readonly FixtureIntermediateApi _intermediateApi = new();
    private readonly FixtureStockApi _stockApi = new();
    private readonly FixtureApi _api = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private string _lastCommand = string.Empty;
    private ReferenceMaterialEditorDialog? _editor;
    private LimsDatePicker? _edgePicker;
    private FrameworkElement? _iconGallery;
    private readonly string _directory = Environment.GetEnvironmentVariable("LIMS_VISUAL_DIRECTORY") ?? Path.GetTempPath();

    public App()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("es-GT");
        Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = "es-GT";
        UnhandledException += (_, e) => File.WriteAllText(Path.Combine(_directory, "crash.txt"), e.Exception.ToString());
        try { InitializeComponent(); }
        catch (Exception exception)
        {
            File.WriteAllText(Path.Combine(_directory, "crash.txt"), exception.ToString());
            throw;
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var session = new FixtureSession();
        var shellModel = new ShellViewModel(session, new FixtureAuthentication(), new FixtureNavigation(), _permissionsApi);
        shellModel.RefreshProfile();
        _page = new ReferenceMaterialsPage(new ReferenceMaterialsViewModel(_api, session));
        _commercialApi = new FixtureCommercialPreparationApi(_api, _intermediateApi);
        _stockApi.CommercialRows = () => _commercialApi.Stocks;
        _intermediateApi.CommercialRows = () => _commercialApi.Intermediates;
        _stockPage = new StockPage(new StockViewModel(_stockApi, session), _commercialApi);
        _intermediatePage = new IntermediatePage(new IntermediateViewModel(_intermediateApi, session), commercialApi: _commercialApi);
        _permissionsPage = new PermissionsPage(new PermissionsViewModel(_permissionsApi, session));
        _dashboardPage = new DashboardPage(new DashboardViewModel(_dashboardApi));
        _solutionApi = new FixtureSolutionGroupApi(_intermediateApi);
        _solutionGroups = new SolutionGroupWorkspace(_solutionApi, session);
        _alertsApi = new FixtureAlertsApi(_api.AlertVisualMaterials);
        _alertsWorkspace = new AlertsWorkspace(_alertsApi, session, _alertWindows, _api, _stockApi, _intermediateApi, _commercialApi, _solutionApi);
        _shell = new ShellPage(shellModel, _page, _stockPage, _intermediatePage, _permissionsPage, _dashboardPage, _api, _solutionGroups, _alertsWorkspace) { RequestedTheme = ElementTheme.Dark };
        var root = new Grid { RequestedTheme = ElementTheme.Dark, Style = (Style)Resources["FixtureRootStyle"] };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var title = new Grid { Padding = new Thickness(16, 0, 142, 0), ColumnSpacing = 16, Background = (Brush)Resources["LimsBackgroundBrush"] };
        title.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        title.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        title.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        brand.Children.Add(new Image { Width = 22, Height = 22, Source = new BitmapImage(new Uri("ms-appx:///Assets/Branding/LIMS.png")) });
        brand.Children.Add(new TextBlock { Text = "LIMS", FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        brand.Children.Add(new Border { Width = 1, Height = 18, Background = (Brush)Resources["LimsBorderBrush"] });
        brand.Children.Add(new TextBlock { Text = "Gestión de laboratorio", FontSize = 12, Foreground = (Brush)Resources["LimsTextSecondaryBrush"], VerticalAlignment = VerticalAlignment.Center });
        title.Children.Add(brand);
        var bellHost = new ContentControl { Content = _alertsWorkspace.Bell, VerticalAlignment = VerticalAlignment.Center };
        title.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); Grid.SetColumn(bellHost, 3); title.Children.Add(bellHost);
        var fixtureLabel = new TextBlock { Text = PersistentPreparationValidation.Enabled ? "BD AISLADA · DATOS DE PRUEBA" : "DATOS FICTICIOS", FontSize = 9, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(fixtureLabel, 1); title.Children.Add(fixtureLabel);
        var systemSearch = new AutoSuggestBox { Width = 280, FontSize = 12, MinHeight = 30, VerticalAlignment = VerticalAlignment.Center, PlaceholderText = "Buscar en el sistema...", QueryIcon = new SymbolIcon(Symbol.Find) };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(systemSearch, "SystemSearch");
        _shell.AttachSystemSearch(systemSearch); Grid.SetColumn(systemSearch, 2); title.Children.Add(systemSearch);
        root.Children.Add(title);
        Grid.SetRow(_shell, 1);
        root.Children.Add(_shell);
        _window = new Window { Content = root, Title = "LIMS UX V2 — fixtures" };
        _window.ExtendsContentIntoTitleBar = true;
        _window.SetTitleBar(brand);
        _window.AppWindow.MoveAndResize(new RectInt32(0, 0, 1920, 1080));
        _window.Activate();
        File.WriteAllText(Path.Combine(_directory, "window.json"), JsonSerializer.Serialize(new { hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window).ToInt64() }));
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private async void OnTick(object? sender, object e)
    {
        var path = Path.Combine(_directory, "command.json");
        if (!File.Exists(path) || _window is null || _shell is null || _page is null) return;
        string command;
        try { command = File.ReadAllText(path); } catch (IOException) { return; }
        if (command == _lastCommand) return;
        _lastCommand = command;
        try
        {
            using var document = JsonDocument.Parse(command);
            var data = document.RootElement;
            var action = data.GetProperty("action").GetString();
            var navigation = (NavigationView)_shell.FindName("MainNavigation");
            switch (action)
            {
                case "motionRegression": await ValidateMotionRegressionAsync(); break;
                case "alerts":
                    _alertsApi!.Fail = data.TryGetProperty("error", out var alertError) && alertError.GetBoolean();
                    _alertsApi.Empty = data.TryGetProperty("empty", out var alertEmpty) && alertEmpty.GetBoolean();
                    _alertsApi.FailureStatus = data.TryGetProperty("status", out var alertStatus) ? alertStatus.GetInt32() : 0;
                    _alertsApi.EnginePending = data.TryGetProperty("pending", out var alertPending) && alertPending.GetBoolean();
                    _alertsApi.DelayMs = data.TryGetProperty("delay", out var alertDelay) ? alertDelay.GetInt32() : 0;
                    _alertsWorkspace!.Page.ViewModel.Filter = new(PageSize: data.TryGetProperty("pageSize", out var alertPageSize) ? alertPageSize.GetInt32() : 30);
                    if (_alertsApi.DelayMs > 0) _ = _shell.NavigateAlertsAsync(); else await _shell.NavigateAlertsAsync();
                    await _alertsWorkspace.PollAsync(); break;
                case "alertQuick": await _alertsWorkspace!.Page.SetQuickAsync(data.GetProperty("index").GetInt32()); break;
                case "alertSelect":
                    var alertList = (ListView)_alertsWorkspace!.Page.FindName("AlertsList");
                    alertList.SelectedIndex = data.TryGetProperty("index", out var alertIndex) ? alertIndex.GetInt32() : 0; break;
                case "alertBell": _alertsWorkspace!.Bell.Flyout.ShowAt(_alertsWorkspace.Bell); break;
                case "alertNative":
                    _alertWindows.Initialize(_window);
                    var nativeItem = (await _alertsApi!.NotificationsAsync(default)).Value!.Items[0] with { Title = "PRUEBA AISLADA · Alertas", Message = "Aviso de prueba WinUI. No corresponde a inventario productivo." };
                    var accepted = _alertWindows.Show(nativeItem, 1);
                    File.WriteAllText(Path.Combine(_directory, "windows-notification.json"), JsonSerializer.Serialize(new { fixtureOnly = true, accepted, status = _alertWindows.Status, clientOpen = true, closedClientGeneration = false }, ReportOptions)); break;
                case "alertReport":
                    var vm = _alertsWorkspace!.Page.ViewModel;
                    File.WriteAllText(Path.Combine(_directory, "alerts-report.json"), JsonSerializer.Serialize(new { fixtureOnly = true, state = vm.State.ToString(), vm.FailureStatus, vm.IsBusy, count = vm.Items.Count, summary = vm.Data?.Summary, filter = vm.Filter, error = vm.Message, selected = vm.Detail?.Alert, readCalls = _alertsApi!.ReadCalls, _alertsApi.ListCalls, _alertsApi.AcknowledgeCalls, scale = _alertsWorkspace.Page.XamlRoot.RasterizationScale, width = _alertsWorkspace.Page.ActualWidth, height = _alertsWorkspace.Page.ActualHeight, monitor = GroupBounds((FrameworkElement)_alertsWorkspace.Page.FindName("MonitorSurface"), _alertsWorkspace.Page.XamlRoot.Content), detail = GroupBounds((FrameworkElement)_alertsWorkspace.Page.FindName("DetailSurface"), _alertsWorkspace.Page.XamlRoot.Content), compact = GroupBounds((FrameworkElement)_alertsWorkspace.Page.FindName("CompactDetailHost"), _alertsWorkspace.Page.XamlRoot.Content) }, ReportOptions)); break;
                case var actionsCommand when actionsCommand?.StartsWith("actions", StringComparison.Ordinal) == true:
                    await HandleActionsCommandAsync(actionsCommand, data); break;
                case var solventAction when solventAction?.StartsWith("solvent", StringComparison.Ordinal) == true:
                    await HandleSolventCommandAsync(solventAction, data); break;
                case var formAction when formAction?.StartsWith("form", StringComparison.Ordinal) == true:
                    HandleFormCommand(formAction, data); break;
                case var mixAction when mixAction?.StartsWith("mix", StringComparison.Ordinal) == true:
                    await HandleMixCommandAsync(mixAction, data); break;
                case "group":
                    navigation.SelectedItem = _shell.FindName("PreparationsItem");
                    ((LimsTabStrip)((PreparationsPage)((ContentControl)_shell.FindName("PreparationsHost")).Content).FindName("PreparationTabs")).SelectedIndex = data.GetProperty("kind").GetString() == "Curva" ? 2 : 3;
                    break;
                case "groupEditor": _ = GroupPage(data).OpenCreateAsync(); break;
                case "groupScenario":
                    var groupVm = GroupPage(data).ViewModel;
                    groupVm.Method = groupVm.Methods[0]; groupVm.Code = data.GetProperty("code").GetString()!;
                    groupVm.Name = "Grupo multianalito · validación visual";
                    foreach (var row in groupVm.Components.ToArray()) groupVm.RemoveComponent(row);
                    foreach (var volume in data.GetProperty("volumes").EnumerateArray())
                    {
                        var index = groupVm.Components.Count;
                        var row = groupVm.AddComponent(); row.Source = row.Sources[data.TryGetProperty("sourceIndices", out var sourceIndices) ? sourceIndices[index].GetInt32() : 0];
                        row.Volume = volume.GetDecimal().ToString(CultureInfo.InvariantCulture);
                        row.FinalVolume = data.TryGetProperty("finalVolumes", out var finals) ? finals[index].GetDecimal().ToString(CultureInfo.InvariantCulture) : "100";
                    }
                    if (data.TryGetProperty("internalIndex", out var internalIndex)) groupVm.Components[internalIndex.GetInt32()].IsInternalStandard = true;
                    if (data.TryGetProperty("selectedIndex", out var selectedIndex)) groupVm.SelectedComponent = groupVm.Components[selectedIndex.GetInt32()];
                    break;
                case "groupEdit":
                    var edited = GroupPage(data).ViewModel.Components[data.GetProperty("index").GetInt32()];
                    if (data.TryGetProperty("volume", out var editedVolume)) edited.Volume = editedVolume.GetString()!;
                    if (data.TryGetProperty("finalVolume", out var editedFinal)) edited.FinalVolume = editedFinal.GetString()!;
                    break;
                case "groupDetailSelect": ((ListView)GroupPage(data).DetailDialog!.FindName("SolutionsList")).SelectedIndex = data.GetProperty("index").GetInt32(); break;
                case "groupDetailScroll": ((ScrollViewer)GroupPage(data).DetailDialog!.FindName("DetailScroll")).ChangeView(null, data.GetProperty("offset").GetDouble(), null, true); break;
                case "groupScroll": ((ScrollViewer)GroupPage(data).Editor!.FindName("EditorScroll")).ChangeView(null, data.GetProperty("offset").GetDouble(), null, true); break;
                case "groupSave": await GroupPage(data).ViewModel.SaveAsync(default); break;
                case "groupClose": GroupPage(data).Editor?.Hide(); break;
                case "groupDetail": GroupPage(data).ViewModel.SelectedPreparation = GroupPage(data).ViewModel.Items[0]; _ = GroupPage(data).OpenDetailAsync(); break;
                case "groupScene": _solutionApi!.Scene = data.GetProperty("scene").GetString()!; _ = GroupPage(data).LoadAsync(); break;
                case "groupReport":
                    var gp = GroupPage(data); var ge = gp.Editor;
                    File.WriteAllText(Path.Combine(_directory, "group-report.json"), JsonSerializer.Serialize(new {
                        fixtureOnly = true, interDbModified = false, gp.ViewModel.Kind, gp.ViewModel.CanSave, gp.ViewModel.Validation,
                        gp.ViewModel.VisibleValidation,
                        rows = gp.ViewModel.Components.Select(r => new { r.Title, sourceId = r.Source?.Id, r.SourceBalance, r.FieldError, r.VolumePreview, r.Preview }).ToArray(),
                        gp.ViewModel.Message, gp.ViewModel.Created, gp.ViewModel.Calculation, gp.ViewModel.Page, gp.ViewModel.TotalCount,
                        sources = _intermediateApi.Sources.Where(s => s.Kind == "Intermedia").ToArray(), _solutionApi!.CreateCalls,
                        scale = gp.XamlRoot.RasterizationScale, width = gp.XamlRoot.Size.Width, height = gp.XamlRoot.Size.Height,
                        theme = gp.ActualTheme.ToString(), editorWidth = ge?.ActualWidth, editorHeight = ge?.ActualHeight,
                        saveBounds = ge is null ? null : GroupBounds((FrameworkElement)ge.FindName("SaveButton"), gp.XamlRoot.Content),
                        resultBounds = ge is null ? null : GroupBounds((FrameworkElement)ge.FindName("ResultPanel"), gp.XamlRoot.Content),
                        editorScroll = ge is null ? null : new { ((ScrollViewer)ge.FindName("EditorScroll")).ScrollableHeight, ((ScrollViewer)ge.FindName("EditorScroll")).VerticalOffset }
                    }, ReportOptions)); break;
                case "dashboard":
                    _dashboardApi.ReferenceRows = _api.ConfigureDashboardFixtures();
                    _dashboardApi.Scenario = data.GetProperty("scenario").GetString()!;
                    _permissionsApi.Current = _dashboardApi.Scenario == "Limited" ? new("Analista", [Lims.Contracts.Permissions.PermissionCatalog.StockView], true) :
                        _dashboardApi.Scenario is "Laboratory" or "Purchasing" or "Restricted" ? new("Analista", _dashboardApi.Scenario == "Laboratory" ? Lims.Contracts.Permissions.PermissionCatalog.All.Select(p => p.Code).ToArray() : [], true) : new("Administrador", [], true);
                    await _shell.RefreshAccessAsync();
                    navigation.SelectedItem = _shell.FindName("HomeItem");
                    await _dashboardPage!.LoadAsync();
                    break;
                case "dashboardReport":
                    var ds = (ScrollViewer)_dashboardPage!.FindName("DashboardScroll");
                    File.WriteAllText(Path.Combine(_directory, "dashboard-layout.json"), JsonSerializer.Serialize(new {
                        fixtureOnly = true, _dashboardApi.Scenario, data = _dashboardPage.ViewModel.Data, _dashboardPage.ViewModel.Error,
                        scale = _dashboardPage.XamlRoot.RasterizationScale, theme = _dashboardPage.ActualTheme.ToString(),
                        _dashboardPage.ViewModel.IsBusy, retryEnabled = ((Button)_dashboardPage.FindName("RetryButton")).IsEnabled,
                        headerTitle = ((TextBlock)_dashboardPage.FindName("DashboardTitle")).Text,
                        shellHeaderVisible = navigation.Header is not null,
                        metricHeights = ((Grid)_dashboardPage.FindName("MetricsGrid")).Children.Cast<FrameworkElement>().Select(c => c.ActualHeight).ToArray(),
                        analyticsPanelCount = ((Grid)_dashboardPage.FindName("AnalyticsGrid")).Children.Count,
                        categoryCounts = _dashboardPage.ViewModel.Data is {} chartData ? Lims.Desktop.Presentation.DashboardAnalytics.MethodAvailability(chartData) : [],
                        width = _dashboardPage.ActualWidth, height = _dashboardPage.ActualHeight, ds.ScrollableHeight, ds.VerticalOffset,
                        scrollWidth = ds.ActualWidth, extentWidth = ds.ExtentWidth, animationsEnabled = Lims.DesignSystem.Presentation.Motion.Enabled,
                        module = ((NavigationViewItem)navigation.SelectedItem).Tag,
                        filter = _page.ViewModel.StatusFilter,
                        _shell.LastDashboardRoute, _shell.DashboardRouteStage,
                        standardIds = _page.ViewModel.Items.Select(r => r.Id).ToArray(),
                        detailOpen = _page.DetailDialog is not null,
                        standardsLoaded = _page.IsLoaded, standardsBusy = _page.ViewModel.IsBusy, standardMessage = _page.ViewModel.Message,
                        selectedStandard = _page.ViewModel.SelectedMaterial?.Id, loadedStandard = _page.ViewModel.SelectedDetail?.Id,
                        preparationTab = ((LimsTabStrip)((PreparationsPage)((ContentControl)_shell.FindName("PreparationsHost")).Content).FindName("PreparationTabs")).SelectedIndex,
                        elements = DashboardElementNames.Select(n => {
                            var el = (FrameworkElement)_dashboardPage.FindName(n); var point = el.TransformToVisual(_dashboardPage).TransformPoint(new Windows.Foundation.Point());
                            return new { name=n, x=point.X,y=point.Y,width=el.ActualWidth,height=el.ActualHeight, visibility=el.Visibility.ToString() };
                        }).ToArray()
                    }, ReportOptions)); break;
                case "standardsDesignReference":
                    _standardsTableForComparison = (Border)_page.FindName("TableBorder");
                    _standardsTitleForComparison = VisualDescendants<TextBlock>((Grid)navigation.Header).Single(t => t.Name == "SectionTitle");
                    break;
                case "dashboardDesignReport":
                    var dashboardCards = ((Grid)_dashboardPage!.FindName("MetricsGrid")).Children.OfType<Border>().ToArray();
                    var standardsTable = _standardsTableForComparison!;
                    var dashboardTexts = VisualDescendants<TextBlock>(_dashboardPage).Where(t => t.ActualWidth > 0 && t.Text.Any(char.IsLetterOrDigit)).ToArray();
                    File.WriteAllText(Path.Combine(_directory, "dashboard-design.json"), JsonSerializer.Serialize(new {
                        fixtureOnly = true, dashboardTheme = _dashboardPage.ActualTheme.ToString(), standardsTheme = _page.ActualTheme.ToString(),
                        dashboardSurface = (dashboardCards[0].Background as SolidColorBrush)?.Color.ToString(CultureInfo.InvariantCulture),
                        standardsSurface = (standardsTable.Background as SolidColorBrush)?.Color.ToString(CultureInfo.InvariantCulture),
                        dashboardBorder = (dashboardCards[0].BorderBrush as SolidColorBrush)?.Color.ToString(CultureInfo.InvariantCulture),
                        standardsBorder = (standardsTable.BorderBrush as SolidColorBrush)?.Color.ToString(CultureInfo.InvariantCulture),
                        dashboardRadius = dashboardCards[0].CornerRadius.TopLeft, standardsRadius = standardsTable.CornerRadius.TopLeft,
                        smallestText = dashboardTexts.Min(t => t.FontSize),
                        dashboardTitleSize = ((TextBlock)_dashboardPage.FindName("DashboardTitle")).FontSize,
                        standardsTitleSize = _standardsTitleForComparison!.FontSize,
                        rows = VisualDescendants<ListViewItem>(_dashboardPage).Select(Microsoft.UI.Xaml.Automation.AutomationProperties.GetName).ToArray(),
                    }, ReportOptions)); break;
                case "dashboardScroll": ((ScrollViewer)_dashboardPage!.FindName("DashboardScroll")).ChangeView(null, data.GetProperty("offset").GetDouble(), null, true); break;
                case "permissions": navigation.SelectedItem = _shell.FindName("PermissionsItem"); break;
                case "permissionActivate": _window.AppWindow.Show(true); _window.Activate(); break;
                case "permissionSelect":
                    var selectedPermissionUser = _permissionsPage!.ViewModel.Users[data.GetProperty("index").GetInt32()];
                    await _permissionsPage.ViewModel.SelectAsync(selectedPermissionUser, true);
                    ((ListView)_permissionsPage.FindName("UsersList")).SelectedItem = _permissionsPage.ViewModel.UserRows.Single(r => r.User.Id == selectedPermissionUser.Id);
                    break;
                case "permissionToggle":
                    var permissionChoice = _permissionsPage!.ViewModel.Groups.SelectMany(g => g.Items).Single(c => c.Definition.Code == data.GetProperty("code").GetString());
                    permissionChoice.IsGranted = !permissionChoice.IsGranted;
                    break;
                case "permissionSave": await _permissionsPage!.ViewModel.SaveAsync(); break;
                case "permissionTab": ((LimsTabStrip)_permissionsPage!.FindName("PermissionTabs")).SelectedIndex = data.GetProperty("index").GetInt32(); break;
                case "permissionReport":
                    var permissionModel = _permissionsPage!.ViewModel;
                    var permissionElements = PermissionElementNames.Select(name =>
                    {
                        var element = (FrameworkElement)_permissionsPage.FindName(name);
                        var position = element.TransformToVisual(_permissionsPage).TransformPoint(new Windows.Foundation.Point());
                        return new { name, x = position.X, y = position.Y, width = element.ActualWidth, height = element.ActualHeight, visibility = element.Visibility.ToString() };
                    }).ToArray();
                    File.WriteAllText(Path.Combine(_directory, "permission-layout.json"), JsonSerializer.Serialize(new { fixtureOnly = true, animationsEnabled = Lims.DesignSystem.Presentation.Motion.Enabled, width = _permissionsPage.ActualWidth, height = _permissionsPage.ActualHeight, selected = permissionModel.SelectedUser?.Name, permissionModel.CanSave, permissionModel.HasPendingChanges, permissionModel.SelectedCount, permissionModel.SavedCount, permissionModel.IsConfigured, permissionModel.IsSaving, permissionModel.HistoryTotal, permissionModel.HistoryPage, _permissionsApi.SaveCalls, elements = permissionElements }, ReportOptions));
                    break;
                case "permissionFailSave": _permissionsApi.FailSave = data.GetProperty("fail").GetBoolean(); break;
                case "permissionScroll": ((ScrollViewer)_permissionsPage!.FindName("PermissionScroll")).ChangeView(null, data.GetProperty("offset").GetDouble(), null, true); break;
                case "permissionCommon":
                    _permissionsApi.Current = new("Analista", [], true);
                    await _shell.ViewModel.RefreshAccessAsync();
                    navigation.SelectedItem = _shell.FindName("HomeItem");
                    navigation.SelectedItem = _shell.FindName("PermissionsItem");
                    break;
                case "tabSelect":
                    ((LimsTabStrip)((PreparationsPage)((ContentControl)_shell.FindName("PreparationsHost")).Content).FindName("PreparationTabs")).SelectedIndex = data.GetProperty("index").GetInt32();
                    break;
                case "tabFocus":
                    var focusTabs = (LimsTabStrip)((PreparationsPage)((ContentControl)_shell.FindName("PreparationsHost")).Content).FindName("PreparationTabs");
                    focusTabs.SelectedItem!.Focus(FocusState.Keyboard);
                    break;
                case "tabCapacity":
                    var capacityPage = (PreparationsPage)((ContentControl)_shell.FindName("PreparationsHost")).Content;
                    var capacityTabs = (LimsTabStrip)capacityPage.FindName("PreparationTabs");
                    // Measure a four-item DesignSystem sample only; these are not module routes.
                    var capacityProbe = new LimsTabStrip { Width = capacityTabs.ActualWidth, Opacity = 0, IsHitTestVisible = false };
                    foreach (var label in new[] { "Stock", "Intermedia", "Curvas", "AQS" }) capacityProbe.Items.Add(new LimsTabItem { Label = label });
                    var preparationRoot = (Grid)capacityPage.Content;
                    preparationRoot.Children.Add(capacityProbe);
                    capacityProbe.UpdateLayout();
                    _fourTabWidth = capacityProbe.Items.Sum(tab => tab.ActualWidth);
                    preparationRoot.Children.Remove(capacityProbe);
                    break;
                case "tabReport":
                    var preparations = (PreparationsPage)((ContentControl)_shell.FindName("PreparationsHost")).Content;
                    var tabs = (LimsTabStrip)preparations.FindName("PreparationTabs");
                    var tabPeer = Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement(tabs);
                    var selection = (Microsoft.UI.Xaml.Automation.Provider.ISelectionProvider)tabPeer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Selection);
                    var indicator = (Border)((Grid)tabs.Content).Children[1];
                    double ContentX(FrameworkElement element) => element.TransformToVisual(_shell).TransformPoint(new Windows.Foundation.Point()).X;
                    double ContentY(FrameworkElement element) => element.TransformToVisual(_shell).TransformPoint(new Windows.Foundation.Point()).Y;
                    var currentPreparationPage = tabs.SelectedIndex == 0 ? (Page)_stockPage! : _intermediatePage!;
                    var tableSurface = ((Grid)currentPreparationPage.Content).Children.OfType<Border>().Single();
                    var headerAction = (Button)_shell.FindName("PreparationsNewButton");
                    var tabResults = tabs.Items.Select(tab => {
                        var peer = Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement(tab);
                        var pattern = (Microsoft.UI.Xaml.Automation.Provider.ISelectionItemProvider)peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.SelectionItem);
                        var label = (TextBlock)((StackPanel)((Grid)tab.Content).Children[0]).Children[1];
                        return new { Name = peer.GetName(), Type = peer.GetAutomationControlType().ToString(), pattern.IsSelected, tab.IsTabStop, Focus = tab.FocusState.ToString(), tab.ActualWidth, tab.ActualHeight,
                            LabelX = ContentX(label), LabelWidth = label.ActualWidth, HitX = ContentX(tab), HitY = ContentY(tab),
                            Foreground = ((Microsoft.UI.Xaml.Media.SolidColorBrush)label.Foreground).Color.ToString(CultureInfo.InvariantCulture), Weight = label.FontWeight.Weight };
                    }).ToArray();
                    File.WriteAllText(Path.Combine(_directory, "tabs.json"), JsonSerializer.Serialize(new {
                        FixtureOnly = true, WindowWidth = _window.AppWindow.Size.Width, WindowHeight = _window.AppWindow.Size.Height, Theme = tabs.ActualTheme.ToString(), tabs.SelectedIndex,
                        Type = tabPeer.GetAutomationControlType().ToString(), selection.IsSelectionRequired, selection.CanSelectMultiple,
                        SelectionCount = selection.GetSelection().Length, Tabs = tabResults,
                        Description = ((TextBlock)preparations.FindName("PreparationDescription")).Text,
                        Action = ((TextBlock)_shell.FindName("PreparationsNewLabel")).Text,
                        ContentMatchesSelection = ((ContentControl)preparations.FindName("PreparationHost")).Content == (tabs.SelectedIndex == 0 ? (object)_stockPage! : _intermediatePage!),
                        StripWidth = tabs.ActualWidth, StripHeight = tabs.ActualHeight, RasterizationScale = tabs.XamlRoot.RasterizationScale,
                        Guide = new {
                            TitleX = ContentX((TextBlock)_shell.FindName("SectionTitle")),
                            SubtitleX = ContentX((TextBlock)_shell.FindName("SectionSubtitle")),
                            StockX = tabResults[0].LabelX,
                            DescriptionX = ContentX((TextBlock)preparations.FindName("PreparationDescription")),
                            SearchX = ContentX((TextBox)currentPreparationPage.FindName("SearchBox")),
                            TableX = ContentX(tableSurface)
                        },
                        HeaderActionBottom = ContentY(headerAction) + headerAction.ActualHeight,
                        StripY = ContentY(tabs), SeparatorY = ContentY((Border)((Grid)tabs.Content).Children[2]),
                        FourTabWidth = _fourTabWidth, IndicatorHeight = indicator.ActualHeight, IndicatorBaseWidth = indicator.ActualWidth,
                        IndicatorColor = ((Microsoft.UI.Xaml.Media.SolidColorBrush)indicator.Background).Color.ToString(CultureInfo.InvariantCulture),
                        Background = ((Microsoft.UI.Xaml.Media.SolidColorBrush)_stockPage!.Background).Color.ToString(CultureInfo.InvariantCulture),
                    }, ReportOptions));
                    break;
                case "intermediate":
                    navigation.SelectedItem = _shell.FindName("PreparationsItem");
                    await ((PreparationsPage)((ContentControl)_shell.FindName("PreparationsHost")).Content).ShowIntermediateAsync();
                    break;
                case "intEditor": _ = _intermediatePage!.OpenCreateAsync(); break;
                case "intScenario":
                    var intVm = _intermediatePage!.ViewModel;
                    intVm.SelectDilution(null);
                    intVm.Method = intVm.Methods[0]; intVm.Name = "Intermedia de aminas · validación"; intVm.FinalVolume = "100";
                    intVm.ExpirationDate = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-6)).AddDays(30);
                    ((LimsDatePicker)_intermediatePage.Editor!.FindName("ExpirationPicker")).Date = intVm.ExpirationDate;
                    var intVolumes = data.GetProperty("volumes").EnumerateArray().Select(v => v.GetDecimal()).ToArray();
                    for (var intIndex = 0; intIndex < intVolumes.Length; intIndex++)
                    {
                        var intRow = intVm.AddComponent(); intRow.Source = _intermediateApi.Sources[intIndex];
                        intRow.Volume = intVolumes[intIndex].ToString(CultureInfo.InvariantCulture);
                    }
                    break;
                case "intText": ((TextBox)_intermediatePage!.Editor!.FindName(data.GetProperty("field").GetString()!)).Text = data.GetProperty("text").GetString()!; break;
                case "intDate": ((LimsDatePicker)_intermediatePage!.Editor!.FindName(data.GetProperty("field").GetString()!)).Date = DateTimeOffset.Parse(data.GetProperty("text").GetString()!, CultureInfo.InvariantCulture); break;
                case "intScroll": ((ScrollViewer)(_intermediatePage!.IsCommercialWorkspaceOpen ? _intermediatePage.CommercialWorkspace!.FindName("CaptureScroll") : _intermediatePage.Editor!.FindName("EditorScroll"))).ChangeView(null, data.GetProperty("offset").GetDouble(), null, true); break;
                case "intClose": _intermediatePage!.Editor?.Hide(); break;
                case "intPopup": ((LimsPopupField)_intermediatePage!.Editor!.FindName(data.GetProperty("field").GetString()!)).OpenPopup(); break;
                case "intDetail": ((ListView)_intermediatePage!.FindName("PreparationsList")).SelectedIndex = 0; _ = _intermediatePage.OpenDetailAsync(); break;
                case "intReport":
                    File.WriteAllText(System.IO.Path.Combine(_directory, "intermediate-report.json"), JsonSerializer.Serialize(new {
                        FixtureOnly = true, _intermediateApi.CreateCalls, _intermediatePage!.ViewModel.Created,
                        _intermediatePage.ViewModel.CanSave, _intermediatePage.ViewModel.Calculation,
                        _intermediatePage.ViewModel.OccupiedPercent, _intermediatePage.ViewModel.VolumeError,
                        _intermediatePage.ViewModel.Message,
                        EditorWidth = (_intermediatePage.Editor?.FindName("EditorRoot") as FrameworkElement)?.ActualWidth,
                        EditorHeight = (_intermediatePage.Editor?.FindName("EditorRoot") as FrameworkElement)?.ActualHeight,
                        ResultStacked = _intermediatePage.Editor?.FindName("ResultPanel") is FrameworkElement intResult && Grid.GetRow(intResult) == 1,
                        Sources = _intermediateApi.Sources.Select(src => new { src.Code, src.AvailableVolume }),
                    }, ReportOptions)); break;
                case "preparations": navigation.SelectedItem = _shell.FindName("PreparationsItem"); break;
                case "preparationScenario":
                    _stockApi.Scene = data.GetProperty("scene").GetString()!;
                    _intermediateApi.Scene = _stockApi.Scene;
                    if (_stockApi.Scene == "Loading") { _ = _stockPage!.LoadAsync(); _ = _intermediatePage!.LoadAsync(); }
                    else { await _stockPage!.LoadAsync(); await _intermediatePage!.LoadAsync(); }
                    break;
                case "preparationSelection":
                    ((ListView)_stockPage!.FindName("PreparationsList")).SelectedIndex = data.GetProperty("index").GetInt32();
                    break;
                case "preparationReload": await _stockPage!.LoadAsync(); break;
                case "preparationCloseDetail": _stockPage!.DetailSheet?.Hide(); break;
                case "preparationListReport":
                    var stockView = _stockPage!.ViewModel;
                    var intermediateView = _intermediatePage!.ViewModel;
                    File.WriteAllText(Path.Combine(_directory, "preparation-list.json"), JsonSerializer.Serialize(new {
                        FixtureOnly = true, Count = stockView.Items.Count,
                        SelectedId = stockView.SelectedPreparation?.Id,
                        CanOpen = stockView.OpenSelectedPreparationCommand.CanExecute(null),
                        ButtonEnabled = ((Button)_stockPage.FindName("ViewPreparationButton")).IsEnabled,
                        EmptyVisible = ((StackPanel)_stockPage.FindName("EmptyState")).Visibility.ToString(),
                        stockView.HasSearchCriteria, stockView.EmptyTitle,
                        stockView.IsLoadingList, stockView.HasListMessage, stockView.ListMessage,
                        stockView.HasResults, stockView.ShowSelectionAction, stockView.PageDisplay,
                        Scale = _stockPage.XamlRoot.RasterizationScale,
                        PageWidth = _stockPage.ActualWidth, PageHeight = _stockPage.ActualHeight,
                        ToolbarWidth = ((Grid)_stockPage.FindName("FilterGrid")).ActualWidth,
                        FilterRow = Grid.GetRow((StackPanel)_stockPage.FindName("Filters")),
                        EmptyHeight = ((StackPanel)_stockPage.FindName("EmptyState")).ActualHeight,
                        Availability = stockView.Items.Select(p => new { p.Code, p.Availability.AvailableDisplay, p.Availability.PercentDisplay, p.StatusLabel, p.InventoryLabel }),
                        DetailId = _stockPage.DetailSheet?.Detail.Preparation.Id,
                        IntermediateSelectedId = intermediateView.SelectedPreparation?.Id,
                        IntermediateCanOpen = intermediateView.OpenSelectedPreparationCommand.CanExecute(null)
                    }, ReportOptions));
                    break;
                case "stockMixture":
                    var mixtureVm = _stockPage!.ViewModel;
                    foreach (var row in mixtureVm.SolventRows.ToArray()) mixtureVm.RemoveSolvent(row);
                    foreach (var row in data.GetProperty("rows").EnumerateArray())
                    {
                        mixtureVm.AddSolvent();
                        var rowPanel = ((SolventCompositionEditor)_stockPage.Editor!.FindName("CompositionEditor")).RowsPanel;
                        var rowGrid = (Grid)rowPanel.Children[^1];
                        ((LimsSearchSelect)rowGrid.Children[0]).SelectedItem = mixtureVm.SolventCatalog.Single(s => s.Id == row.GetProperty("id").GetInt32());
                        ((TextBox)rowGrid.Children[1]).Text = row.GetProperty("percent").GetString()!;
                    }
                    break;
                case "stockSave": await _stockPage!.ViewModel.SaveAsync(CancellationToken.None); break;
                case "stockEditor": _ = _stockPage!.OpenCreateAsync(); break;
                case "stockSelect": ((LimsSearchSelect)_stockPage!.Editor!.FindName("SourceList")).SelectedIndex = data.GetProperty("index").GetInt32(); break;
                case "stockText": ((TextBox)_stockPage!.Editor!.FindName(data.GetProperty("field").GetString()!)).Text = data.GetProperty("text").GetString()!; break;
                case "stockDate": ((LimsDatePicker)_stockPage!.Editor!.FindName(data.GetProperty("field").GetString()!)).Date = DateTimeOffset.Parse(data.GetProperty("text").GetString()!, System.Globalization.CultureInfo.InvariantCulture); break;
                case "stockScroll": ((ScrollViewer)_stockPage!.Editor!.FindName("StepScroll")).ChangeView(null, data.GetProperty("offset").GetDouble(), null, true); break;
                case "stockFocusList":
                    var stockList = (ListView)_stockPage!.FindName("PreparationsList"); stockList.SelectedIndex = 0; stockList.Focus(FocusState.Keyboard); break;
                case "stockClose": _stockPage!.Editor?.Hide(); break;
                case "stockDetail": ((ListView)_stockPage!.FindName("PreparationsList")).SelectedIndex = 0; _ = _stockPage.OpenDetailAsync(); break;
                case "stockReport":
                    File.WriteAllText(Path.Combine(_directory, "interaction.json"), JsonSerializer.Serialize(new
                    {
                        FixtureOnly = true, _stockApi.CreateCalls, _stockApi.Source.AvailableQuantity,
                        _stockPage!.ViewModel.Created,
                        CanSave = _stockPage.ViewModel.CanSave,
                        _stockPage.ViewModel.Concentration, _stockPage.ViewModel.FinalVolume, _stockPage.ViewModel.Calculation,
                        Message = _stockPage.ViewModel.Message,
                    }, ReportOptions));
                    break;
                case "size": if (_window.AppWindow.Presenter is OverlappedPresenter sizePresenter) sizePresenter.Restore(); _window.AppWindow.MoveAndResize(new RectInt32(0, 0, data.GetProperty("width").GetInt32(), data.GetProperty("height").GetInt32())); break;
                case "themeReport":
                    object Bounds(FrameworkElement element)
                    {
                        var pt = element.TransformToVisual(element.XamlRoot.Content).TransformPoint(new Windows.Foundation.Point());
                        return new { x = pt.X, y = pt.Y, width = element.ActualWidth, height = element.ActualHeight };
                    }
                    var stockEditor = _stockPage!.Editor;
                    var osBackground = new Windows.UI.ViewManagement.UISettings().GetColorValue(Windows.UI.ViewManagement.UIColorType.Background);
                    File.WriteAllText(Path.Combine(_directory, "theme-layout.json"), JsonSerializer.Serialize(new
                    {
                        fixtureOnly = true, requestedTheme = RequestedTheme.ToString(), actualTheme = _shell.ActualTheme.ToString(),
                        osBackground = osBackground.ToString(CultureInfo.InvariantCulture),
                        scale = _shell.XamlRoot.RasterizationScale, rootSize = _shell.XamlRoot.Size,
                        content = Bounds((FrameworkElement)_window.Content),
                        text = ((Microsoft.UI.Xaml.Media.SolidColorBrush)Resources["LimsTextPrimaryBrush"]).Color.ToString(CultureInfo.InvariantCulture),
                        surface = ((Microsoft.UI.Xaml.Media.SolidColorBrush)Resources["LimsSurfaceBrush"]).Color.ToString(CultureInfo.InvariantCulture),
                        paneOpen = navigation.IsPaneOpen,
                        dialog = stockEditor is null ? null : new
                        {
                            theme = stockEditor.ActualTheme.ToString(), root = Bounds((FrameworkElement)stockEditor.FindName("EditorRoot")),
                            cancel = Bounds((FrameworkElement)stockEditor.FindName("CancelButton")), save = Bounds((FrameworkElement)stockEditor.FindName("SaveButton")),
                            scroll = Bounds((FrameworkElement)stockEditor.FindName("StepScroll")),
                            scrollableHeight = ((ScrollViewer)stockEditor.FindName("StepScroll")).ScrollableHeight,
                            popup = StockPopupFields
                                .Select(n => (LimsPopupField)stockEditor.FindName(n)).Where(f => f.IsPopupOpen)
                                .Select(f => new { f.Name, f.Placement }).ToArray()
                        }
                    }, ReportOptions));
                    break;
                case "standards": navigation.SelectedItem = _shell.FindName("StandardsItem"); break;
                case "home": navigation.SelectedItem = _shell.FindName("HomeItem"); break;
                case "icons":
                    var iconRoot = (Grid)_window.Content;
                    if (_iconGallery is not null) { iconRoot.Children.Remove(_iconGallery); _iconGallery = null; }
                    if (data.GetProperty("open").GetBoolean())
                    {
                        _iconGallery = IconValidation.CreateGallery();
                        Grid.SetRow(_iconGallery, 1);
                        iconRoot.Children.Add(_iconGallery);
                    }
                    break;
                case "iconChecks":
                    var iconChecks = await IconValidation.CheckAsync(_iconGallery!, (NavigationViewItem)_shell.FindName("HomeItem"), (NavigationViewItem)_shell.FindName("StandardsItem"));
                    File.WriteAllText(Path.Combine(_directory, "icon-checks.json"), JsonSerializer.Serialize(iconChecks, ReportOptions));
                    break;
                case "pane": navigation.IsPaneOpen = data.GetProperty("open").GetBoolean(); break;
                case "select": ((ListView)_page.FindName("MaterialsList")).SelectedIndex = data.GetProperty("index").GetInt32(); break;
                case "availability":
                    var fixtureId = _page.ViewModel.SelectedDetail!.Id;
                    if (_page.DetailDialog is { } availabilitySheet) await availabilitySheet.CloseAsync();
                    var fixtureQuantity = data.GetProperty("quantity");
                    var fixtureDetail = _api.SetAvailability(fixtureId, fixtureQuantity.ValueKind == JsonValueKind.Null ? null : fixtureQuantity.GetDecimal(), data.GetProperty("status").GetString()!);
                    await _page.ViewModel.SelectAsync(FixtureApi.Summary(fixtureDetail), CancellationToken.None);
                    _ = _page.OpenSelectedDetailAsync();
                    break;
                case "nextAvailability": _api.NextAvailableQuantity = data.GetProperty("quantity").GetDecimal(); break;
                case "detail": _ = _page.OpenSelectedDetailAsync(); break;
                case "closeDetail": if (_page.DetailDialog is { } openDetail) await openDetail.CloseAsync(); break;
                case "detailScroll": _page.DetailDialog?.ScrollTo(data.GetProperty("offset").GetDouble()); break;
                case "focus": if (_editor?.FindName(data.GetProperty("field").GetString()!) is Control focusTarget) focusTarget.Focus(FocusState.Keyboard); break;
                case "enabled": if (_editor?.FindName(data.GetProperty("field").GetString()!) is Control enabledTarget) enabledTarget.IsEnabled = data.GetProperty("enabled").GetBoolean(); break;
                case "text": if (_editor?.FindName(data.GetProperty("field").GetString()!) is TextBox textTarget) textTarget.Text = data.GetProperty("text").GetString()!; break;
                case "entryCommercial":
                    var entryDialog = data.GetProperty("kind").GetString() == "Stock" ? (ContentDialog)_stockPage!.Editor! : _intermediatePage!.Editor!;
                    ((LimsSelect)entryDialog.FindName("EntryTypeSelect")).SelectedIndex = 1;
                    break;
                case "entryFill":
                    if (data.GetProperty("kind").GetString() == "Intermedia")
                    {
                        var mixEntry = _intermediatePage!.CommercialWorkspace!;
                        foreach (var field in data.GetProperty("fields").EnumerateObject()) ((TextBox)mixEntry.FindName(field.Name)).Text = field.Value.GetString()!;
                        ((LimsSearchSelect)mixEntry.FindName("MethodSelect")).SelectedIndex = 0;
                        ((LimsSelect)mixEntry.FindName("LocationSelect")).SelectedIndex = 0;
                        ((LimsDatePicker)mixEntry.FindName("ExpirationPicker")).Date = DateTimeOffset.Now.AddYears(1);
                        var certificate = JsonSerializer.Deserialize<Lims.Contracts.ReferenceMaterials.CommercialCertificate>(data.GetProperty("certificate").GetRawText())!;
                        foreach (var component in certificate.Components) mixEntry.ViewModel.Rows.Add(CommercialComponentRow.From(component));
                        break;
                    }
                    var fillDialog = data.GetProperty("kind").GetString() == "Stock" ? (ContentDialog)_stockPage!.Editor! : _intermediatePage!.Editor!;
                    var entry = (CommercialPreparationEditor)fillDialog.FindName("CommercialEntry");
                    foreach (var field in data.GetProperty("fields").EnumerateObject()) ((TextBox)entry.FindName(field.Name)).Text = field.Value.GetString()!;
                    ((LimsSelect)entry.FindName("MethodSelect")).SelectedIndex = 0;
                    ((LimsSelect)entry.FindName("LocationSelect")).SelectedIndex = 0;
                    ((LimsDatePicker)entry.FindName("ExpirationPicker")).Date = DateTimeOffset.Now.AddYears(1);
                    ((CommercialCertificateEditor)entry.FindName("ComponentsEditor")).SetData(JsonSerializer.Deserialize<Lims.Contracts.ReferenceMaterials.CommercialCertificate>(data.GetProperty("certificate").GetRawText())!);
                    break;
                case "entryGeometry":
                    if (data.GetProperty("kind").GetString() == "Intermedia")
                    {
                        var mix = _intermediatePage!.CommercialWorkspace!;
                        var save = (Button)mix.FindName("SaveButton");
                        var p = save.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point());
                        File.WriteAllText(Path.Combine(_directory, data.GetProperty("name").GetString()! + ".json"), JsonSerializer.Serialize(new {
                            fixtureOnly = true, mix.ViewModel.IsReady, internalFormCollapsed = _intermediatePage.Editor is null,
                            footerVisible = p.Y >= 0 && p.Y + save.ActualHeight <= mix.XamlRoot.Size.Height,
                            request = mix.ViewModel.ReadRequest()
                        }, ReportOptions)); break;
                    }
                    var geometryDialog = data.GetProperty("kind").GetString() == "Stock" ? (ContentDialog)_stockPage!.Editor! : _intermediatePage!.Editor!;
                    var geometryEntry = (CommercialPreparationEditor)geometryDialog.FindName("CommercialEntry");
                    var footer = (Button)geometryDialog.FindName("SaveButton");
                    var footerPosition = footer.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(0, 0));
                    File.WriteAllText(Path.Combine(_directory, data.GetProperty("name").GetString()! + ".json"), JsonSerializer.Serialize(new {
                        fixtureOnly = true, geometryEntry.IsReady, rootWidth = geometryDialog.XamlRoot.Size.Width, rootHeight = geometryDialog.XamlRoot.Size.Height,
                        footerTop = footerPosition.Y, footerBottom = footerPosition.Y + footer.ActualHeight,
                        footerVisible = footerPosition.Y >= 0 && footerPosition.Y + footer.ActualHeight <= geometryDialog.XamlRoot.Size.Height,
                        internalFormCollapsed = ((FrameworkElement)geometryDialog.FindName("FormSurface")).Visibility == Visibility.Collapsed,
                        resultText = ((TextBlock)geometryEntry.FindName("ResultConcentrations")).Text,
                        resultBalance = ((TextBlock)geometryEntry.FindName("BalanceText")).Text,
                        request = geometryEntry.ReadRequest()
                    }, ReportOptions));
                    break;
                case "entryReport":
                    File.WriteAllText(Path.Combine(_directory, "commercial-entry-report.json"), JsonSerializer.Serialize(new { fixtureOnly = true, _commercialApi!.LastRequest, _commercialApi.Roles }, ReportOptions));
                    break;
                case "entryCloseDetail": _stockPage!.CommercialDetail?.Hide(); _intermediatePage!.CommercialDetail?.Hide(); break;
                case "standardEntryCatalogs":
                    navigation.SelectedItem = _shell.FindName("StandardsItem");
                    await _page.ViewModel.LoadCatalogsAsync();
                    break;
                case "standardEntryReport":
                    File.WriteAllText(Path.Combine(_directory, "standard-entry-report.json"), JsonSerializer.Serialize(new {
                        fixtureOnly = true,
                        presentationSelectorRemoved = _editor!.FindName("PresentationSelect") is null,
                        commercialEditorRemoved = _editor.FindName("CommercialEditor") is null,
                        request = _editor.CreateRequest()
                    }, ReportOptions));
                    break;
                case "commercialReport":
                    File.WriteAllText(Path.Combine(_directory, "commercial-report.json"), JsonSerializer.Serialize(new { fixtureOnly = true, request = _editor!.CreateRequest() }, ReportOptions));
                    break;
                case "commercialIntermediate":
                    var commercialVm = _intermediatePage!.ViewModel;
                    commercialVm.SourceType = "Mix comercial";
                    commercialVm.Method = commercialVm.Methods[0]; commercialVm.Name = "Mix comercial · dilución de prueba";
                    commercialVm.FinalVolume = "10"; commercialVm.ExpirationDate = DateTimeOffset.Now.AddDays(30);
                    ((LimsDatePicker)_intermediatePage.Editor!.FindName("ExpirationPicker")).Date = commercialVm.ExpirationDate;
                    var commercialBottleId = Guid.NewGuid();
                    var commercialSource = new Lims.Contracts.ReferencePreparations.IntermediateSource(commercialBottleId, "CommercialMix", "MIX-PRUEBA", "Mix comercial de prueba", 1,
                        commercialVm.Method.Name, 10, 10, "mL", new(2026, 1, 1), new(2027, 6, 1), Guid.NewGuid(),
                        [new(Guid.NewGuid(), "Analito A", null, "LOTE-MIX", 50, "µg/mL", commercialBottleId), new(Guid.NewGuid(), "Analito B", null, "LOTE-MIX", 49.9m, "µg/mL", commercialBottleId), new(Guid.NewGuid(), "Analito C", null, "LOTE-MIX", 50.1m, "µg/mL", commercialBottleId)]);
                    _intermediateApi.Sources.Add(commercialSource); await commercialVm.LoadOptionsAsync(default);
                    var commercialRow = commercialVm.AddComponent(); commercialRow.Source = commercialSource; commercialRow.Volume = "1";
                    break;
                case "presentationChecks":
                    File.WriteAllText(Path.Combine(_directory, "presentation.json"), JsonSerializer.Serialize(StandardsPresentationChecks.Report(_editor!), ReportOptions));
                    break;
                case "light": ((FrameworkElement)_window.Content).RequestedTheme = ElementTheme.Light; _shell.RequestedTheme = ElementTheme.Light; break;
                case "dark": ((FrameworkElement)_window.Content).RequestedTheme = ElementTheme.Dark; _shell.RequestedTheme = ElementTheme.Dark; break;
                case "editor":
                    if (_editor is not null) break;
                    _editor = new ReferenceMaterialEditorDialog(_page.ViewModel.Methods, _page.ViewModel.Units, _page.ViewModel.Locations) { XamlRoot = _shell.XamlRoot, RequestedTheme = _shell.RequestedTheme };
                    _editor.SaveAsync = async () =>
                    {
                        var saved = await _page.ViewModel.CreateAsync(_editor.CreateRequest(), CancellationToken.None);
                        return saved ? null : _page.ViewModel.LastSaveFailure;
                    };
                    _editor.Closed += (_, _) => _editor = null;
                    try { _ = _editor.ShowAsync(); }
                    catch { _editor = null; throw; }
                    break;
                case "combo": if (_editor is not null) ((LimsSelect)_editor.FindName("MethodBox")).IsDropDownOpen = data.GetProperty("open").GetBoolean(); break;
                case "calendar": if (_editor is not null) ((LimsDatePicker)_editor.FindName("ExpirationDatePicker")).IsCalendarOpen = data.GetProperty("open").GetBoolean(); break;
                case "popup":
                    var target = (LimsPopupField)(data.TryGetProperty("page", out var pageTarget) && pageTarget.GetBoolean()
                        ? _page.FindName(data.GetProperty("field").GetString()!) : _editor!.FindName(data.GetProperty("field").GetString()!));
                    if (data.GetProperty("open").GetBoolean()) target.OpenPopup(); else target.ClosePopup(true);
                    break;
                case "editorScroll":
                    if (_editor is not null) ((ScrollViewer)_editor.FindName("EditorScrollViewer")).ChangeView(null, data.GetProperty("offset").GetDouble(), null, true);
                    break;
                case "edge":
                    var fixtureRoot = (Grid)_window.Content;
                    if (_edgePicker is not null) { _edgePicker.CloseImmediately(); fixtureRoot.Children.Remove(_edgePicker); _edgePicker = null; }
                    if (data.GetProperty("open").GetBoolean())
                    {
                        _edgePicker = new LimsDatePicker { Width = 180, HorizontalAlignment = HorizontalAlignment.Right,
                            VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(8), Date = DateTimeOffset.Now };
                        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_edgePicker, "Fecha de prueba junto a bordes");
                        Grid.SetRow(_edgePicker, 1);
                        fixtureRoot.Children.Add(_edgePicker);
                        await Task.Delay(150);
                        _edgePicker.OpenPopup();
                    }
                    break;
                case "checks":
                    var checks = await PopupInteractionChecks.RunAsync(_editor!, _shell.XamlRoot);
                    File.WriteAllText(Path.Combine(_directory, "interaction-checks.json"), JsonSerializer.Serialize(checks, ReportOptions));
                    break;
                case "delay": _api.DelayMilliseconds = data.GetProperty("milliseconds").GetInt32(); break;
                case "fill":
                    if (_editor is not null)
                    {
                        foreach (var field in new[] { "NameBox", "LotBox", "BrandBox", "PresentationBox", "StorageTemperatureBox" })
                            ((TextBox)_editor.FindName(field)).Text = field == "PresentationBox" ? "100" : "Fixture";
                        ((LimsSelect)_editor.FindName("StorageLocationBox")).SelectedIndex = 0;
                    }
                    break;
                case "reload": await _page.LoadAsync(); break;
                case "availabilityRows":
                    if (data.TryGetProperty("offset", out var rowOffset))
                        StandardsAvailabilityChecks.Scroll(_page).ChangeView(null, rowOffset.GetDouble(), null, true);
                    else if (data.TryGetProperty("pageDelta", out var pageDelta))
                    {
                        if (pageDelta.GetInt32() > 0) await _page.ViewModel.GoToNextPageAsync(CancellationToken.None);
                        else await _page.ViewModel.GoToPreviousPageAsync(CancellationToken.None);
                    }
                    else
                        File.WriteAllText(Path.Combine(_directory, "availability-rows.json"), JsonSerializer.Serialize(StandardsAvailabilityChecks.Report(_page), ReportOptions));
                    break;
                case "report":
                    var surface = _editor?.FindName("EditorSurface") as FrameworkElement;
                    var scroll = _editor?.FindName("EditorScrollViewer") as ScrollViewer;
                    var list = (ListView)_page.FindName("MaterialsList");
                    var presenter = FindPresenter((DependencyObject)_shell.FindName("StandardsItem"));
                    var detailSurface = _page.DetailDialog?.FindName("DetailSurface") as FrameworkElement;
                    var detailOrigin = detailSurface?.TransformToVisual(_page.DetailDialog).TransformPoint(new Windows.Foundation.Point(0, 0));
                    File.WriteAllText(Path.Combine(_directory, "layout.json"), JsonSerializer.Serialize(new {
                        windowWidth = _window.AppWindow.Size.Width, windowHeight = _window.AppWindow.Size.Height,
                        pageWidth = _page.ActualWidth, pageHeight = _page.ActualHeight,
                        editorWidth = surface?.ActualWidth, editorHeight = surface?.ActualHeight,
                        editorViewport = scroll?.ViewportHeight, editorExtent = scroll?.ExtentHeight,
                        sidebarOpen = navigation.IsPaneOpen, items = _page.ViewModel.Items.Count,
                        sidebarIcons = IconValidation.SidebarBoxes(_shell),
                        selectedName = _page.ViewModel.SelectedDetail?.NameDisplay, saveCalls = _api.SaveCalls,
                        detailWidth = (_page.DetailDialog?.FindName("DetailSurface") as FrameworkElement)?.ActualWidth,
                        detailHeight = (_page.DetailDialog?.FindName("DetailSurface") as FrameworkElement)?.ActualHeight,
                        detailViewport = (_page.DetailDialog?.FindName("DetailScroll") as ScrollViewer)?.ViewportHeight,
                        detailExtent = (_page.DetailDialog?.FindName("DetailScroll") as ScrollViewer)?.ExtentHeight,
                        detailOpen = _page.DetailDialog is not null,
                        clientWidth = _shell.XamlRoot.Size.Width, clientHeight = _shell.XamlRoot.Size.Height,
                        rasterizationScale = _shell.XamlRoot.RasterizationScale,
                        detailX = detailOrigin?.X, detailY = detailOrigin?.Y,
                        summaryHeight = (_page.DetailDialog?.FindName("KpiGrid") as FrameworkElement)?.ActualHeight,
                        generalHeight = (_page.DetailDialog?.FindName("GeneralSection") as FrameworkElement)?.ActualHeight,
                        storageHeight = (_page.DetailDialog?.FindName("RightWorkspace") as FrameworkElement)?.ActualHeight,
                        auditHeight = (_page.DetailDialog?.FindName("AuditSection") as FrameworkElement)?.ActualHeight,
                        headerHeight = (_page.DetailDialog?.FindName("HeaderGrid") as FrameworkElement)?.ActualHeight,
                        bodyStacked = _page.DetailDialog?.FindName("RightWorkspace") is FrameworkElement storageSection && Grid.GetRow(storageSection) > 0,
                        availability = AvailabilityReport(_page.DetailDialog),
                        detailSections = DetailSectionNames
                            .Select(name => {
                                var section = _page.DetailDialog?.FindName(name) as FrameworkElement;
                                var origin = section?.TransformToVisual(_page.DetailDialog).TransformPoint(new Windows.Foundation.Point(0, 0));
                                return new { name, x = origin?.X, y = origin?.Y, width = section?.ActualWidth, height = section?.ActualHeight };
                            }),
                        customSidebarPresenter = presenter?.GetType().Name,
                        popups = PopupFields
                            .Select(name => _editor?.FindName(name) as LimsPopupField).OfType<LimsPopupField>()
                            .Append((LimsPopupField)_page.FindName("MethodBox"))
                            .Select(field => new { field.Name, field.IsPopupOpen, field.Placement,
                                selectedId = (field as LimsSelect)?.SelectedValue, date = (field as LimsDatePicker)?.Date }),
                        focusedType = (Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(_shell.XamlRoot) as DependencyObject)?.GetType().Name
                    }, ReportOptions));
                    break;
                case "exit": _window.Close(); break;
            }
            File.WriteAllText(Path.Combine(_directory, "ack.json"), command);
        }
        catch (Exception exception) { File.WriteAllText(Path.Combine(_directory, "error.txt"), exception.ToString()); }
    }

    private static object? AvailabilityReport(ReferenceMaterialDetailDialog? sheet)
    {
        if (sheet is null) return null;
        var progress = (ProgressBar)sheet.FindName("AvailabilityProgress");
        var track = FindNamedElement(progress, "ProgressBarTrack");
        var indicator = FindNamedElement(progress, "DeterminateProgressBarIndicator");
        return new
        {
            quantity = ((TextBlock)sheet.FindName("AvailabilityQuantity")).Text,
            qualifier = ((TextBlock)sheet.FindName("AvailabilityQualifier")).Text,
            totals = ((TextBlock)sheet.FindName("AvailabilityTotals")).Text,
            presentation = ((TextBlock)sheet.FindName("AvailabilityPresentation")).Text,
            percent = ((TextBlock)sheet.FindName("AvailabilityPercent")).Text,
            value = progress.Value, height = progress.ActualHeight,
            color = (progress.Foreground as Microsoft.UI.Xaml.Media.SolidColorBrush)?.Color.ToString(CultureInfo.InvariantCulture),
            meterVisible = ((FrameworkElement)sheet.FindName("AvailabilityMeter")).Visibility == Visibility.Visible,
            iconVisible = ((FrameworkElement)sheet.FindName("AvailabilityVial")).Visibility == Visibility.Visible,
            trackHeight = track?.ActualHeight, trackWidth = track?.ActualWidth, indicatorWidth = indicator?.ActualWidth,
        };
    }

    private SolutionGroupPage GroupPage(JsonElement data) => data.GetProperty("kind").GetString() == "Curva" ? _solutionGroups!.Curve : _solutionGroups!.Aqs;
    private static object GroupBounds(FrameworkElement element, UIElement root)
    {
        var point = element.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
        return new { x = point.X, y = point.Y, width = element.ActualWidth, height = element.ActualHeight, visible = element.Visibility == Visibility.Visible };
    }
    private static IEnumerable<T> VisualDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var descendant in VisualDescendants<T>(child)) yield return descendant;
        }
    }

    private static FrameworkElement? FindNamedElement(DependencyObject parent, string name)
    {
        for (var index = 0; index < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, index);
            if (child is FrameworkElement element && element.Name == name) return element;
            if (FindNamedElement(child, name) is { } descendant) return descendant;
        }
        return null;
    }

    private static DependencyObject? FindPresenter(DependencyObject root)
    {
        for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
            if (child is Microsoft.UI.Xaml.Controls.Primitives.NavigationViewItemPresenter) return child;
            var found = FindPresenter(child);
            if (found is not null) return found;
        }
        return null;
    }
}

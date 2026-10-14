using System.Globalization;
using System.Text.Json;
using Lims.Desktop.ViewModels;
using Lims.Desktop.Views;
using Lims.DesignSystem.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace Lims.Desktop.VisualHarness;

public partial class App : Microsoft.UI.Xaml.Application
{
    private static readonly JsonSerializerOptions ReportOptions = new() { WriteIndented = true };
    private static readonly string[] PopupFields = ["MethodBox", "UnitCombo", "StorageLocationBox", "ReceivedDatePicker", "ExpirationDatePicker"];
    private static readonly string[] DetailSectionNames = ["HeaderGrid", "KpiGrid", "GeneralSection", "AuditSection", "AvailabilityCard", "ExpiryCard", "StateCard"];
    private static readonly string[] PermissionElementNames = ["UsersPanel", "UserSearch", "UsersList", "DetailPanel", "UserHeader", "PermissionTabs", "TabContent", "PermissionScroll", "HistoryView", "WorkspaceFooter", "SaveButton", "DiscardButton", "ReloadButton"];
    private readonly FixtureDashboardApi _dashboardApi = new();
    private DashboardPage? _dashboardPage;
    private static readonly string[] DashboardElementNames = ["MetricsGrid", "OverviewGrid", "OperationsGrid", "QuickPanel", "Timestamp"];
    private Window? _window;
    private ShellPage? _shell;
    private ReferenceMaterialsPage? _page;
    private StockPage? _stockPage;
    private IntermediatePage? _intermediatePage;
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
        _stockPage = new StockPage(new StockViewModel(_stockApi, session));
        _intermediatePage = new IntermediatePage(new IntermediateViewModel(_intermediateApi, session));
        _permissionsPage = new PermissionsPage(new PermissionsViewModel(_permissionsApi, session));
        _dashboardPage = new DashboardPage(new DashboardViewModel(_dashboardApi));
        _shell = new ShellPage(shellModel, _page, _stockPage, _intermediatePage, _permissionsPage, _dashboardPage, _api) { RequestedTheme = ElementTheme.Dark };
        var root = new Grid { RequestedTheme = ElementTheme.Dark, Style = (Style)Resources["FixtureRootStyle"] };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var title = new TextBlock { Text = "LIMS · VALIDACIÓN VISUAL · DATOS FICTICIOS", Margin = new Thickness(16, 10, 0, 0) };
        root.Children.Add(title);
        Grid.SetRow(_shell, 1);
        root.Children.Add(_shell);
        _window = new Window { Content = root, Title = "LIMS UX V2 — fixtures" };
        _window.ExtendsContentIntoTitleBar = true;
        _window.SetTitleBar(title);
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
                            return new { name=n, x=point.X,y=point.Y,width=el.ActualWidth,height=el.ActualHeight };
                        }).ToArray()
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
                case "intScroll": ((ScrollViewer)_intermediatePage!.Editor!.FindName("EditorScroll")).ChangeView(null, data.GetProperty("offset").GetDouble(), null, true); break;
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
                        DetailId = _stockPage.DetailSheet?.Detail.Preparation.Id,
                        IntermediateSelectedId = intermediateView.SelectedPreparation?.Id,
                        IntermediateCanOpen = intermediateView.OpenSelectedPreparationCommand.CanExecute(null)
                    }, ReportOptions));
                    break;
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
                case "size": _window.AppWindow.MoveAndResize(new RectInt32(0, 0, data.GetProperty("width").GetInt32(), data.GetProperty("height").GetInt32())); break;
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

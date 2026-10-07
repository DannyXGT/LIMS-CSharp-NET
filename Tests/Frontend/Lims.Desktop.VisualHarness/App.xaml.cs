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
    private Window? _window;
    private ShellPage? _shell;
    private ReferenceMaterialsPage? _page;
    private StockPage? _stockPage;
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
        var shellModel = new ShellViewModel(session, new FixtureAuthentication(), new FixtureNavigation());
        shellModel.RefreshProfile();
        _page = new ReferenceMaterialsPage(new ReferenceMaterialsViewModel(_api, session));
        _stockPage = new StockPage(new StockViewModel(_stockApi, session));
        _shell = new ShellPage(shellModel, _page, _stockPage) { RequestedTheme = ElementTheme.Dark };
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
                case "preparations": navigation.SelectedItem = _shell.FindName("PreparationsItem"); break;
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

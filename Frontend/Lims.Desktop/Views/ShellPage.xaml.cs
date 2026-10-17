using Lims.Desktop.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Lims.DesignSystem.Presentation;
using Lims.Desktop.Presentation;

namespace Lims.Desktop.Views;

public sealed partial class ShellPage : Page
{
    private readonly ReferenceMaterialsPage _referenceMaterialsPage;
    private readonly PreparationsPage _preparationsPage;
    private readonly PermissionsPage? _permissionsPage;
    private readonly DashboardPage? _dashboard;
    private readonly object _moduleHeader;
    private readonly Lims.Desktop.Http.IReferenceMaterialsApiClient? _dashboardReferences;
    private readonly StockPage _stockPage;
    private readonly IntermediatePage _intermediatePage;
    private string? _dashboardAuthorization;
    private readonly DispatcherTimer _accessTimer = new() { Interval = TimeSpan.FromSeconds(15) };
    private Task? _accessRefresh;
    private int _navigationRevision;
    private bool _restoringNavigation;
    private string? _dashboardNavigationTag;
    internal DashboardRoute? LastDashboardRoute { get; private set; }
    internal string DashboardRouteStage { get; private set; } = string.Empty;

    public ShellPage(ShellViewModel viewModel, ReferenceMaterialsPage referenceMaterialsPage, StockPage stockPage, IntermediatePage intermediatePage, PermissionsPage? permissionsPage = null, DashboardPage? dashboard = null, Lims.Desktop.Http.IReferenceMaterialsApiClient? dashboardReferences = null, SolutionGroupWorkspace? solutionGroups = null)
    {
        ViewModel = viewModel;
        _referenceMaterialsPage = referenceMaterialsPage;
        _permissionsPage = permissionsPage;
        _dashboard = dashboard; _dashboardReferences = dashboardReferences; _stockPage = stockPage; _intermediatePage = intermediatePage;
        _preparationsPage = new PreparationsPage(stockPage, intermediatePage, solutionGroups?.Curve, solutionGroups?.Aqs);
        InitializeComponent();
        _moduleHeader = MainNavigation.Header;
        MainNavigation.Header = null;
        MainNavigation.AlwaysShowHeader = false;
        _preparationsPage.SelectionChanged += (_, _) => UpdatePreparationAction();
        StandardsHost.Content = referenceMaterialsPage;
        PreparationsHost.Content = _preparationsPage;
        PermissionsHost.Content = permissionsPage;
        HomePanel.Content = dashboard;
        if (dashboard is not null) dashboard.Navigate += async (_, route) => await NavigateDashboardAsync(route);
        MainNavigation.SelectedItem = HomeItem;
        _accessTimer.Tick += async (_, _) => await RefreshAccessAsync();
        Loaded += async (_, _) => { await RefreshAccessAsync(); _accessTimer.Start(); };
        Unloaded += (_, _) => _accessTimer.Stop();
    }

    public ShellViewModel ViewModel { get; }
    public void AttachSystemSearch(AutoSuggestBox search)
    {
        search.TextChanged += OnSystemSearchChanged;
        search.QuerySubmitted += OnSystemSearchSubmitted;
    }

    private void OnNavigationItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer?.Tag is string) MainNavigation.IsPaneOpen = false;
    }

    private async void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_restoringNavigation) return;
        if (args.SelectedItemContainer?.Tag is not string tag)
        {
            return;
        }
        if (_dashboardNavigationTag == tag) { _dashboardNavigationTag = null; return; }
        var revision = ++_navigationRevision;
        if (_permissionsPage is not null && PermissionsHost.Visibility == Visibility.Visible && tag != "permissions")
        {
            if (!await _permissionsPage.ConfirmDiscardAsync()) { _restoringNavigation = true; MainNavigation.SelectedItem = PermissionsItem; _restoringNavigation = false; return; }
            _permissionsPage.ViewModel.Clear();
        }
        await RefreshAccessAsync();
        if (revision != _navigationRevision) return;

        HideAllSections();
        StandardsNewButton.Visibility = Visibility.Collapsed;
        PreparationsNewButton.Visibility = Visibility.Collapsed;
        switch (tag)
        {
            case "home":
                StandardsNewButton.Visibility = Visibility.Collapsed;
                Show(HomePanel, "Inicio", "Panel principal");
                if (_dashboard is not null) await _dashboard.LoadAsync();
                break;
            case "standards":
                if (!ViewModel.CanViewReferenceMaterials) { Show(DeniedPanel, "Acceso denegado", "Estándares"); break; }
                Show(StandardsHost, "Estándares", "Materiales de Referencia");
                StandardsNewButton.IsEnabled = _referenceMaterialsPage.ViewModel.CanCreate;
                StandardsNewButton.Visibility = Visibility.Visible;
                _ = _referenceMaterialsPage.LoadAsync();
                break;
            case "preparations":
                if (!ViewModel.CanViewPreparations) { Show(DeniedPanel, "Acceso denegado", "Preparaciones"); break; }
                Show(PreparationsHost, "Preparaciones", "Materiales de Referencia");
                UpdatePreparationAction();
                PreparationsNewButton.Visibility = _preparationsPage.CanCreate ? Visibility.Visible : Visibility.Collapsed;
                _ = _preparationsPage.LoadAsync();
                break;
            case "permissions":
                if (!ViewModel.IsAdministrator || _permissionsPage is null) { Show(DeniedPanel, "Acceso denegado", "Administración"); break; }
                Show(PermissionsHost, "Gestión de permisos", "Administración");
                await _permissionsPage.LoadAsync();
                break;
        }
    }

    private void HideAllSections()
    {
        Lims.DesignSystem.Controls.LimsPopupField.CloseForRoot(XamlRoot);
        Motion.ConfigureVisibility(HomePanel);
        Motion.ConfigureVisibility(StandardsHost);
        Motion.ConfigureVisibility(PreparationsHost);
        HomePanel.Visibility = Visibility.Collapsed;
        StandardsHost.Visibility = Visibility.Collapsed;
        PreparationsHost.Visibility = Visibility.Collapsed;
        PermissionsHost.Visibility = Visibility.Collapsed;
        DeniedPanel.Visibility = Visibility.Collapsed;
    }

    public Task RefreshAccessAsync() => _accessRefresh is { IsCompleted: false } ? _accessRefresh : _accessRefresh = RefreshAccessCoreAsync();

    private async Task RefreshAccessCoreAsync()
    {
        var verified = await ViewModel.RefreshAccessAsync();
        var authorization = ViewModel.AuthorizationSignature;
        if (!verified) _dashboard?.ViewModel.Clear();
        else if (_dashboardAuthorization != authorization)
        {
            _dashboard?.ViewModel.Clear();
            _dashboardAuthorization = authorization;
            if (_dashboard is not null && HomePanel.Visibility == Visibility.Visible) await _dashboard.LoadAsync();
        }
        StandardsItem.Visibility = ViewModel.CanViewReferenceMaterials ? Visibility.Visible : Visibility.Collapsed;
        MaterialsGroup.Visibility = MainNavigation.IsPaneOpen && (ViewModel.CanViewReferenceMaterials || ViewModel.CanViewPreparations) ? Visibility.Visible : Visibility.Collapsed;
        PreparationsItem.IsEnabled = ViewModel.CanViewPreparations;
        PreparationsItem.Visibility = ViewModel.CanViewPreparations ? Visibility.Visible : Visibility.Collapsed;
        PermissionsItem.Visibility = ViewModel.IsAdministrator ? Visibility.Visible : Visibility.Collapsed;
        if (!ViewModel.IsAdministrator) _permissionsPage?.ViewModel.RevokeAccess();
        AdministrationGroup.Visibility = ViewModel.IsAdministrator && MainNavigation.IsPaneOpen ? Visibility.Visible : Visibility.Collapsed;
        _preparationsPage.RefreshAccess(ViewModel.CanViewStock, ViewModel.CanViewIntermediate, ViewModel.CanViewCurve, ViewModel.CanViewAqs);
        _referenceMaterialsPage.ViewModel.RefreshPermissions();
        StandardsNewButton.IsEnabled = _referenceMaterialsPage.ViewModel.CanCreate;
        var tag = (MainNavigation.SelectedItem as NavigationViewItem)?.Tag as string;
        if (tag == "standards" && !ViewModel.CanViewReferenceMaterials || tag == "preparations" && !ViewModel.CanViewPreparations || tag == "permissions" && !ViewModel.IsAdministrator)
        { HideAllSections(); StandardsNewButton.Visibility = Visibility.Collapsed; PreparationsNewButton.Visibility = Visibility.Collapsed; Show(DeniedPanel, "Acceso denegado", "Sus permisos han cambiado."); }
    }

    private void Show(FrameworkElement element, string title, string subtitle)
    {
        var home = element == HomePanel;
        MainNavigation.Header = home ? null : _moduleHeader;
        MainNavigation.AlwaysShowHeader = !home;
        element.Visibility = Visibility.Visible;
        SectionTitle.Text = title;
        SectionSubtitle.Text = subtitle;
        Motion.Enter(element, y: 5, milliseconds: 150);
    }

    private void OnNavigationLoaded(object sender, RoutedEventArgs e)
    {
        MainNavigation.IsPaneOpen = false;
        Motion.ConfigureVisibility(HomePanel);
        Motion.ConfigureVisibility(StandardsHost);
        UpdatePane(MainNavigation.IsPaneOpen);
    }
    private void OnPaneOpening(NavigationView sender, object args) => UpdatePane(true);
    private void OnPaneClosing(NavigationView sender, NavigationViewPaneClosingEventArgs args) => UpdatePane(false);

    private void UpdatePane(bool expanded)
    {
        foreach (var item in new[] { HomeItem, StandardsItem, PreparationsItem, PermissionsItem })
            ToolTipService.SetToolTip(item, expanded ? null : item.Content);
        var visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        BrandLabels.Visibility = visibility;
        MaterialsGroup.Visibility = expanded && (ViewModel.CanViewReferenceMaterials || ViewModel.CanViewPreparations) ? Visibility.Visible : Visibility.Collapsed;
        AdministrationGroup.Visibility = expanded && ViewModel.IsAdministrator ? Visibility.Visible : Visibility.Collapsed;
        UserLabels.Visibility = visibility;
        LogoutButton.Visibility = visibility;
        if (expanded)
        {
            Motion.Enter(BrandLabels, x: -3, milliseconds: 180);
            Motion.Enter(UserLabels, x: -3, milliseconds: 180);
        }
    }


    private async Task NavigateDashboardAsync(DashboardRoute route)
    {
        if (_permissionsPage is not null && PermissionsHost.Visibility == Visibility.Visible && route.Module != "permissions")
        {
            if (!await _permissionsPage.ConfirmDiscardAsync())
            { _restoringNavigation = true; MainNavigation.SelectedItem = PermissionsItem; _restoringNavigation = false; return; }
            _permissionsPage.ViewModel.Clear();
        }
        LastDashboardRoute = route; DashboardRouteStage = "authorizing";
        await RefreshAccessAsync();
        if (route.Module == "permissions") { if (ViewModel.IsAdministrator) MainNavigation.SelectedItem = PermissionsItem; return; }
        if (route.Module == "standards" && !ViewModel.CanViewReferenceMaterials || route.Module == "stock" && !ViewModel.CanViewStock || route.Module == "intermediate" && !ViewModel.CanViewIntermediate) return;
        var revision = ++_navigationRevision; DashboardRouteStage = "navigating";
        HideAllSections(); StandardsNewButton.Visibility = Visibility.Collapsed; PreparationsNewButton.Visibility = Visibility.Collapsed;
        // NavigationView may raise SelectionChanged after SelectedItem returns; consume the matching event.
        var target = route.Module == "standards" ? StandardsItem : PreparationsItem;
        if (!ReferenceEquals(MainNavigation.SelectedItem, target))
        {
            _dashboardNavigationTag = target.Tag as string;
            MainNavigation.SelectedItem = target;
        }
        if (route.Module == "standards")
        {
            DashboardRouteStage = "waiting for standards";
            var model = _referenceMaterialsPage.ViewModel;
            Show(StandardsHost, "Estándares", "Materiales de Referencia");
            // First-load handlers own catalog/list initialization. Wait asynchronously before applying the route.
            var deadline = DateTimeOffset.UtcNow.AddSeconds(8);
            while (!_referenceMaterialsPage.IsLoaded || model.IsBusy || model.IsCatalogsLoading)
            {
                if (revision != _navigationRevision) return;
                if (DateTimeOffset.UtcNow >= deadline) { model.Message = "No se pudo preparar la pantalla. Reintente."; return; }
                await Task.Delay(16);
            }
            DashboardRouteStage = "applying filters";
            model.StatusFilter = route.Filter; model.SearchText = route.Search ?? string.Empty;
            var statusCombo = (ComboBox)_referenceMaterialsPage.FindName("StatusCombo");
            var selectedStatus = statusCombo.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (item.Tag as string ?? string.Empty) == (route.Filter ?? string.Empty));
            model.MethodFilter = null;
            ((Lims.DesignSystem.Controls.LimsSearchSelect)_referenceMaterialsPage.FindName("MethodBox")).SelectedItem = null;
            StandardsNewButton.Visibility = Visibility.Visible; StandardsNewButton.IsEnabled = model.CanCreate;
            if (!ReferenceEquals(statusCombo.SelectedItem, selectedStatus))
            {
                // The existing filter handler starts its own request. Await that request instead of issuing another.
                var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var started = false;
                System.ComponentModel.PropertyChangedEventHandler loaded = (_, args) =>
                {
                    if (args.PropertyName != nameof(ReferenceMaterialsViewModel.IsBusy)) return;
                    if (model.IsBusy) started = true;
                    else if (started) completion.TrySetResult();
                };
                model.PropertyChanged += loaded;
                try
                {
                    statusCombo.SelectedItem = selectedStatus;
                    await completion.Task.WaitAsync(TimeSpan.FromSeconds(35));
                }
                catch (TimeoutException) { model.Message = "No se pudo consultar el filtro. Reintente."; return; }
                finally { model.PropertyChanged -= loaded; }
            }
            else await model.ApplyFiltersAsync(CancellationToken.None);
            if (revision != _navigationRevision) { DashboardRouteStage = "navigation changed"; return; }
            DashboardRouteStage = "list loaded";
            if (route.RecordId is {} id)
            {
                DashboardRouteStage = "resolving record";
                var row = model.Items.FirstOrDefault(r => r.Id == id);
                if (row is null && _dashboardReferences is not null)
                {
                    var response = await _dashboardReferences.GetAsync(id, CancellationToken.None);
                    if (response.IsSuccess && response.Value is {} d)
                    {
                        row = new Lims.Contracts.ReferenceMaterials.ReferenceMaterialSummary(d.Id, d.Name, d.CasNumber, d.CatalogNumber, d.Method, d.Lot, d.Brand, d.PurityPercent, d.ExpirationDate, d.Status, d.TotalQuantity, d.Unit, d.AvailableQuantity, d.Version);
                        model.Items.Add(row);
                    }
                    else model.Message = response.Error?.Message ?? "No se pudo abrir el registro solicitado.";
                }
                if (row is not null) { DashboardRouteStage = "opening record"; ((ListView)_referenceMaterialsPage.FindName("MaterialsList")).SelectedItem = row; await _referenceMaterialsPage.OpenSelectedDetailAsync(); DashboardRouteStage = "record returned"; }
            }
        }
        else
        {
            var tabs = (Lims.DesignSystem.Controls.LimsTabStrip)_preparationsPage.FindName("PreparationTabs");
            tabs.SelectedIndex = route.Module == "intermediate" ? 1 : 0;
            Show(PreparationsHost, "Preparaciones", "Materiales de Referencia");
            PreparationsNewButton.Visibility = _preparationsPage.CanCreate ? Visibility.Visible : Visibility.Collapsed; UpdatePreparationAction();
            if (route.Module == "stock")
            {
                ((TextBox)_stockPage.FindName("SearchBox")).Text = route.Search ?? string.Empty;
                await _stockPage.LoadAsync();
                if (route.RecordId is {} id && await _stockPage.ViewModel.OpenDetailAsync(id, CancellationToken.None))
                {
                    var detail = _stockPage.ViewModel.Detail!;
                    var sheet = new StockDetailDialog(detail) { XamlRoot = XamlRoot, RequestedTheme = ActualTheme }; await sheet.ShowAsync();
                }
            }
            else
            {
                ((TextBox)_intermediatePage.FindName("SearchBox")).Text = route.Search ?? string.Empty;
                await _intermediatePage.LoadAsync();
                if (route.RecordId is {} id && await _intermediatePage.ViewModel.OpenDetailAsync(id, CancellationToken.None)) await _intermediatePage.ShowDetailAsync(_intermediatePage.ViewModel.Detail!);
            }
        }
    }

    private async void OnRetryAccessClick(object sender, RoutedEventArgs e) => await RefreshAccessAsync();

    private async void OnNewStandardClick(object sender, RoutedEventArgs e) =>
        await _referenceMaterialsPage.OpenCreateDialogAsync();

    private void UpdatePreparationAction()
    {
        PreparationsNewLabel.Text = _preparationsPage.CreateLabel;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(PreparationsNewButton, _preparationsPage.CreateLabel);
        PreparationsNewButton.IsEnabled = _preparationsPage.CanCreate;
        if (PreparationsHost.Visibility == Visibility.Visible) PreparationsNewButton.Visibility = _preparationsPage.CanCreate ? Visibility.Visible : Visibility.Collapsed;
    }
    private async void OnNewPreparationClick(object sender, RoutedEventArgs e) => await _preparationsPage.OpenCreateAsync();

    private sealed record SystemSearchResult(string Label, string Module, string? Search = null, Guid? Id = null)
    { public override string ToString() => Label; }

    private void OnSystemSearchChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        var options = new List<SystemSearchResult> { new("Inicio", "home") };
        if (ViewModel.CanViewReferenceMaterials) options.Add(new("Estándares", "standards"));
        if (ViewModel.CanViewStock) options.Add(new("Stock", "stock"));
        if (ViewModel.CanViewIntermediate) options.Add(new("Intermedia", "intermediate"));
        if (ViewModel.IsAdministrator) options.Add(new("Gestión de permisos", "permissions"));
        if (_dashboard?.ViewModel.Data is {} data)
            options.AddRange(DashboardAnalytics.PrioritySample(data).Select(m => new SystemSearchResult(m.Name, m.Kind == "Standard" ? "standards" : m.Kind == "Stock" ? "stock" : "intermediate", m.Name, m.Id)));
        sender.ItemsSource = options.Where(o => o.Label.Contains(sender.Text.Trim(), StringComparison.CurrentCultureIgnoreCase)).Take(8).ToArray();
    }
    private async void OnSystemSearchSubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var result = args.ChosenSuggestion as SystemSearchResult;
        result ??= (sender.ItemsSource as IEnumerable<SystemSearchResult>)?.FirstOrDefault(o => string.Equals(o.Label, args.QueryText.Trim(), StringComparison.OrdinalIgnoreCase));
        if (result?.Module == "home") { MainNavigation.SelectedItem = HomeItem; return; }
        if (result is not null) await NavigateDashboardAsync(new(result.Module, Search: result.Search, RecordId: result.Id));
        else if (!string.IsNullOrWhiteSpace(args.QueryText))
        {
            var module = ViewModel.CanViewReferenceMaterials ? "standards" : ViewModel.CanViewStock ? "stock" : ViewModel.CanViewIntermediate ? "intermediate" : null;
            if (module is not null) await NavigateDashboardAsync(new(module, Search: args.QueryText.Trim()));
        }
        sender.Text = string.Empty;
    }

}

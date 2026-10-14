using Lims.Desktop.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Lims.DesignSystem.Presentation;

namespace Lims.Desktop.Views;

public sealed partial class ShellPage : Page
{
    private readonly ReferenceMaterialsPage _referenceMaterialsPage;
    private readonly PreparationsPage _preparationsPage;
    private readonly PermissionsPage? _permissionsPage;
    private readonly DispatcherTimer _accessTimer = new() { Interval = TimeSpan.FromSeconds(15) };
    private Task? _accessRefresh;
    private int _navigationRevision;
    private bool _restoringNavigation;

    public ShellPage(ShellViewModel viewModel, ReferenceMaterialsPage referenceMaterialsPage, StockPage stockPage, IntermediatePage intermediatePage, PermissionsPage? permissionsPage = null)
    {
        ViewModel = viewModel;
        _referenceMaterialsPage = referenceMaterialsPage;
        _permissionsPage = permissionsPage;
        _preparationsPage = new PreparationsPage(stockPage, intermediatePage);
        InitializeComponent();
        _preparationsPage.SelectionChanged += (_, _) => UpdatePreparationAction();
        StandardsHost.Content = referenceMaterialsPage;
        PreparationsHost.Content = _preparationsPage;
        PermissionsHost.Content = permissionsPage;
        MainNavigation.SelectedItem = HomeItem;
        _accessTimer.Tick += async (_, _) => await RefreshAccessAsync();
        Loaded += async (_, _) => { await RefreshAccessAsync(); _accessTimer.Start(); };
        Unloaded += (_, _) => _accessTimer.Stop();
    }

    public ShellViewModel ViewModel { get; }

    private async void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_restoringNavigation) return;
        if (args.SelectedItemContainer?.Tag is not string tag)
        {
            return;
        }
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
                PreparationsNewButton.Visibility = Visibility.Visible;
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

    private Task RefreshAccessAsync() => _accessRefresh is { IsCompleted: false } ? _accessRefresh : _accessRefresh = RefreshAccessCoreAsync();

    private async Task RefreshAccessCoreAsync()
    {
        await ViewModel.RefreshAccessAsync();
        StandardsItem.Visibility = ViewModel.CanViewReferenceMaterials ? Visibility.Visible : Visibility.Collapsed;
        MaterialsGroup.Visibility = MainNavigation.IsPaneOpen && (ViewModel.CanViewReferenceMaterials || ViewModel.CanViewPreparations) ? Visibility.Visible : Visibility.Collapsed;
        PreparationsItem.IsEnabled = ViewModel.CanViewPreparations;
        PreparationsItem.Visibility = ViewModel.CanViewPreparations ? Visibility.Visible : Visibility.Collapsed;
        PermissionsItem.Visibility = ViewModel.IsAdministrator ? Visibility.Visible : Visibility.Collapsed;
        if (!ViewModel.IsAdministrator) _permissionsPage?.ViewModel.RevokeAccess();
        AdministrationGroup.Visibility = ViewModel.IsAdministrator && MainNavigation.IsPaneOpen ? Visibility.Visible : Visibility.Collapsed;
        _preparationsPage.RefreshAccess(ViewModel.CanViewStock, ViewModel.CanViewIntermediate);
        _referenceMaterialsPage.ViewModel.RefreshPermissions();
        StandardsNewButton.IsEnabled = _referenceMaterialsPage.ViewModel.CanCreate;
        var tag = (MainNavigation.SelectedItem as NavigationViewItem)?.Tag as string;
        if (tag == "standards" && !ViewModel.CanViewReferenceMaterials || tag == "preparations" && !ViewModel.CanViewPreparations || tag == "permissions" && !ViewModel.IsAdministrator)
        { HideAllSections(); StandardsNewButton.Visibility = Visibility.Collapsed; PreparationsNewButton.Visibility = Visibility.Collapsed; Show(DeniedPanel, "Acceso denegado", "Sus permisos han cambiado."); }
    }

    private void Show(FrameworkElement element, string title, string subtitle)
    {
        element.Visibility = Visibility.Visible;
        SectionTitle.Text = title;
        SectionSubtitle.Text = subtitle;
        Motion.Enter(element, y: 5, milliseconds: 150);
    }

    private void OnNavigationLoaded(object sender, RoutedEventArgs e)
    {
        Motion.ConfigureVisibility(HomePanel);
        Motion.ConfigureVisibility(StandardsHost);
        UpdatePane(MainNavigation.IsPaneOpen);
    }
    private void OnPaneOpening(NavigationView sender, object args) => UpdatePane(true);
    private void OnPaneClosing(NavigationView sender, NavigationViewPaneClosingEventArgs args) => UpdatePane(false);

    private void UpdatePane(bool expanded)
    {
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

    private async void OnRetryAccessClick(object sender, RoutedEventArgs e) => await RefreshAccessAsync();

    private async void OnNewStandardClick(object sender, RoutedEventArgs e) =>
        await _referenceMaterialsPage.OpenCreateDialogAsync();

    private void UpdatePreparationAction()
    {
        PreparationsNewLabel.Text = _preparationsPage.CreateLabel;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(PreparationsNewButton, _preparationsPage.CreateLabel);
        PreparationsNewButton.IsEnabled = _preparationsPage.CanCreate;
    }
    private async void OnNewPreparationClick(object sender, RoutedEventArgs e) => await _preparationsPage.OpenCreateAsync();

}

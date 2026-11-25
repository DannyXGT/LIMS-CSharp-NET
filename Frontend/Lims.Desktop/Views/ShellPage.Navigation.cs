using Lims.DesignSystem.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Lims.Desktop.Views;

public sealed partial class ShellPage
{
    private bool _navigationReady;
    private bool _planningWorkspaceAllowed;
    private readonly Dictionary<NavigationViewItem, bool> _planningNavigationAccess = [];
    private NavigationViewItem? _workspaceSelection;
    public string SelectedWorkspace { get; private set; } = "laboratory";
    public event Action? WorkspaceChanged;

    public void SelectWorkspace(string workspace)
    {
        if (workspace is not ("laboratory" or "planning" or "administration"))
            throw new ArgumentOutOfRangeException(nameof(workspace));
        ++_navigationRevision;
        _dashboardNavigationTag = null;
        SelectedWorkspace = workspace;
        // Planning tables provide their own insets; avoid a second strip beside the navigation rail.
        Workspace.Padding = new Thickness(workspace == "planning" ? 0 : 24, 8, 24, 24);
        WorkspaceName.Text = workspace switch { "planning" => "Planificación", "administration" => "Administración", _ => "Suministros" };
        _restoringNavigation = true;
        MainNavigation.SelectedItem = null;
        _workspaceSelection = null;
        _restoringNavigation = false;
        HideAllSections();
        MainNavigation.Header = null;
        MainNavigation.AlwaysShowHeader = false;
        StandardsNewButton.Visibility = PreparationsNewButton.Visibility = Visibility.Collapsed;
        UpdateNavigationGroups(MainNavigation.IsPaneOpen);
        if (SelectedWorkspace != "laboratory") _alerts?.Stop();
        else if (IsLoaded && _alerts is not null) _ = _alerts.StartAsync();
        WorkspaceChanged?.Invoke();
    }

    public void OpenLaboratoryWorkspace()
    {
        if (SelectedWorkspace == "laboratory") MainNavigation.SelectedItem = HomeItem;
    }

    internal bool IsWorkspaceRouteAllowed(string tag) => tag switch
    {
        "home" or "standards" or "preparations" or "stock" or "intermediate" or "alerts" => SelectedWorkspace == "laboratory",
        "planning" or "planning-section" => SelectedWorkspace == "planning",
        "permissions" => SelectedWorkspace == "administration",
        _ => false
    };

    private void RestoreWorkspaceSelection()
    {
        _restoringNavigation = true;
        MainNavigation.SelectedItem = _workspaceSelection is { } current && MainNavigation.MenuItems.Contains(current) &&
            current.Tag is string tag && IsWorkspaceRouteAllowed(tag) ? current : null;
        _restoringNavigation = false;
    }

    private void OnNavigationGroupToggled(object sender, RoutedEventArgs args)
    {
        if (_navigationReady) UpdateNavigationGroups(MainNavigation.IsPaneOpen);
    }

    internal void SetPlanningWorkspaceAccess(bool allowed)
    {
        _planningWorkspaceAllowed = allowed;
        UpdateNavigationGroups(MainNavigation.IsPaneOpen);
    }

    internal void SetPlanningNavigationAccess(NavigationViewItem item, bool allowed) => _planningNavigationAccess[item] = allowed;
    internal void RemovePlanningNavigationItem(NavigationViewItem item)
    {
        _planningNavigationAccess.Remove(item);
        MainNavigation.MenuItems.Remove(item);
    }

    private bool CanUsePlanningNavigationItem(NavigationViewItem item) => SelectedWorkspace == "planning" && _planningWorkspaceAllowed &&
        _planningNavigationAccess.GetValueOrDefault(item);

    internal static NavigationViewItem CreateWorkspaceSwitchNavigationItem()
    {
        var item = new NavigationViewItem { Content = "Cambiar espacio" };
        LimsIcon.SetNavigationIcon(item, LimsIconKind.SwitchWorkspace);
        AutomationProperties.SetName(item, "Cambiar espacio");
        ToolTipService.SetToolTip(item, "Elegir espacio de trabajo sin cerrar sesión");
        return item;
    }

    // Shared by the live workspace and the isolated visual harness. Routing remains in Workspaces.
    internal NavigationViewItem AddPlanningNavigationItem(string title, string tag)
    {
        var item = new NavigationViewItem { Content = title, Tag = tag };
        LimsIcon.SetNavigationIcon(item, title switch
        {
            "Consulta de Jobs" => LimsIconKind.Jobs,
            "Report List / Digitación" => LimsIconKind.Digitization,
            "Cierres" => LimsIconKind.Closures,
            "Importaciones" => LimsIconKind.Import,
            "Bandejas de revisión" => LimsIconKind.ReviewQueue,
            "Equivalencias" => LimsIconKind.Equivalences,
            "Accesos" => LimsIconKind.Access,
            _ => LimsIconKind.Documentation
        });
        AutomationProperties.SetName(item, title);
        ToolTipService.SetToolTip(item, title);
        _planningNavigationAccess[item] = true;
        MainNavigation.MenuItems.Insert(MainNavigation.MenuItems.IndexOf(AdministrationGroup), item);
        item.Visibility = Visibility.Collapsed;
        return item;
    }

    internal void UpdateNavigationGroups(bool expanded)
    {
        var planning = MainNavigation.MenuItems.OfType<NavigationViewItem>()
            .Where(item => item.Tag is "planning" or "planning-section").ToArray();
        var supplies = SelectedWorkspace == "laboratory";
        var administration = SelectedWorkspace == "administration" && ViewModel.IsAdministrator;
        var suppliesAvailable = ViewModel.CanViewReferenceMaterials || ViewModel.CanViewPreparations || (ViewModel.CanViewAlerts && _alerts is not null);
        var suppliesOpen = !expanded || SuppliesToggle.IsChecked == true;
        var planningOpen = !expanded || PlanningToggle.IsChecked == true;
        var administrationOpen = !expanded || AdministrationToggle.IsChecked == true;
        HomeItem.Visibility = supplies ? Visibility.Visible : Visibility.Collapsed;
        StandardsItem.Visibility = supplies && suppliesOpen && ViewModel.CanViewReferenceMaterials ? Visibility.Visible : Visibility.Collapsed;
        PreparationsItem.Visibility = supplies && suppliesOpen && ViewModel.CanViewPreparations ? Visibility.Visible : Visibility.Collapsed;
        AlertsItem.Visibility = supplies && suppliesOpen && ViewModel.CanViewAlerts && _alerts is not null ? Visibility.Visible : Visibility.Collapsed;
        PermissionsItem.Visibility = administration && administrationOpen ? Visibility.Visible : Visibility.Collapsed;
        MaterialsGroup.Visibility = expanded && supplies && suppliesAvailable
            ? Visibility.Visible : Visibility.Collapsed;
        PlanningGroup.Visibility = expanded && planning.Any(CanUsePlanningNavigationItem)
            ? Visibility.Visible : Visibility.Collapsed;
        AdministrationGroup.Visibility = expanded && administration
            ? Visibility.Visible : Visibility.Collapsed;
        SuppliesChevron.Icon = SuppliesToggle.IsChecked == true ? LimsIconKind.ChevronDown : LimsIconKind.ChevronRight;
        PlanningChevron.Icon = PlanningToggle.IsChecked == true ? LimsIconKind.ChevronDown : LimsIconKind.ChevronRight;
        AdministrationChevron.Icon = AdministrationToggle.IsChecked == true ? LimsIconKind.ChevronDown : LimsIconKind.ChevronRight;
        foreach (var item in planning)
        {
            item.Visibility = CanUsePlanningNavigationItem(item) && planningOpen ? Visibility.Visible : Visibility.Collapsed;
            ToolTipService.SetToolTip(item, expanded ? null : item.Content);
        }
    }
}

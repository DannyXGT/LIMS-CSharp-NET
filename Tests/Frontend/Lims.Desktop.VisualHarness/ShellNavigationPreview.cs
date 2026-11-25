using System.Text.Json;
using Lims.Contracts.Planning;
using Lims.Desktop.Planning;
using Lims.DesignSystem.Controls;
using Lims.Desktop.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace Lims.Desktop.VisualHarness;

public partial class App
{
    private ContentControl? _shellReportHost;
    private Action? _closeShellReportPreview;

    private async Task PreviewShellNavigationAsync(JsonElement data)
    {
        var shell = _shell!;
        var navigation = (NavigationView)shell.FindName("MainNavigation");
        var reportOnly = data.TryGetProperty("reportOnly", out var reporting) && reporting.GetBoolean();
        if (!reportOnly)
        {
            var restricted = data.TryGetProperty("restricted", out var limited) && limited.GetBoolean();
            _permissionsApi.Current = restricted ? new("Analista", [], true) : new("Administrador", [], true);
            await shell.RefreshAccessAsync();
            if (!navigation.MenuItems.OfType<NavigationViewItem>().Any(item => item.Tag is "planning" or "planning-section"))
                foreach (var title in new[] { "Consulta de Jobs", "Report List / Digitación", "Cierres", "Importaciones", "Bandejas de revisión", "Equivalencias", "Accesos" })
                    shell.AddPlanningNavigationItem(title, title == "Consulta de Jobs" ? "planning" : "planning-section");
            foreach (var item in navigation.MenuItems.OfType<NavigationViewItem>().Where(item => item.Tag is "planning" or "planning-section"))
                shell.SetPlanningNavigationAccess(item, !restricted || item.Content is not ("Importaciones" or "Equivalencias" or "Accesos"));
            shell.SetPlanningWorkspaceAccess(!data.TryGetProperty("planningAccess", out var planningAccess) || planningAccess.GetBoolean());
            if (data.TryGetProperty("resetGroups", out var reset) && reset.GetBoolean())
                foreach (var name in new[] { "SuppliesToggle", "PlanningToggle", "AdministrationToggle" }) ((ToggleButton)shell.FindName(name)).IsChecked = true;
            shell.SelectWorkspace(data.TryGetProperty("workspace", out var workspace) ? workspace.GetString()! : "planning");
            if (navigation.FooterMenuItems.Count == 0) navigation.FooterMenuItems.Add(ShellPage.CreateWorkspaceSwitchNavigationItem());
            navigation.IsPaneOpen = !data.TryGetProperty("expanded", out var expanded) || expanded.GetBoolean();
            var selectedTitle = data.TryGetProperty("selected", out var selected) ? selected.GetString() : null;
            navigation.SelectedItem = navigation.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(item => item.Visibility == Visibility.Visible &&
                (selectedTitle is null || Equals(item.Content, selectedTitle)));
        }
        await Task.Delay(450);

        // A fresh authorization response must not undo a user's collapsed group or selected workspace.
        await shell.RefreshAccessAsync();
        var previous = navigation.SelectedItem;
        var blocked = new List<string>();
        foreach (var item in navigation.MenuItems.OfType<NavigationViewItem>().Where(item => item.Tag is string tag && !shell.IsWorkspaceRouteAllowed(tag)))
        {
            navigation.SelectedItem = item;
            await Task.Delay(30);
            if (!ReferenceEquals(navigation.SelectedItem, previous)) throw new InvalidOperationException($"Cross-workspace route was accepted: {item.Content}");
            blocked.Add(item.Content.ToString()!);
        }
        if (shell.SelectedWorkspace != "laboratory")
        {
            await shell.NavigateAlertsAsync();
            if (!ReferenceEquals(navigation.SelectedItem, previous)) throw new InvalidOperationException("An alert switched workspace.");
        }

        if (data.TryGetProperty("withReportList", out var withReportList) && withReportList.GetBoolean())
        {
            if (_shellReportHost is null)
            {
                var transport = new ReportDirectHttp();
                var view = new PlanningReportListView(new(transport), _ => Task.CompletedTask,
                    () => [PlanningPermissions.View, PlanningPermissions.ReportMark, PlanningPermissions.ReportReview], () => false,
                    scanner: new ReportValidationScanner());
                _closeShellReportPreview = () => { view.Dispose(); transport.Dispose(); };
                _window!.Closed += (_, _) => _closeShellReportPreview?.Invoke();
                _shellReportHost = new ContentControl { Content = view, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
                Grid.SetRow(_shellReportHost, 1);
                ((Grid)shell.FindName("Workspace")).Children.Add(_shellReportHost);
                shell.WorkspaceChanged += () => _shellReportHost.Visibility = Visibility.Collapsed;
            }
            _shellReportHost.Visibility = Visibility.Visible;
            await ((PlanningReportListView)_shellReportHost.Content).Load();
            await Task.Delay(250);
        }

        var items = navigation.MenuItems.OfType<NavigationViewItem>().Where(item => item.Visibility == Visibility.Visible).ToArray();
        var groups = navigation.MenuItems.OfType<NavigationViewItemHeader>().Where(item => item.Visibility == Visibility.Visible).ToArray();
        foreach (var item in items)
        {
            if (item.Icon is null || LimsIcon.GetNavigationIcon(item) == LimsIconKind.None)
                throw new InvalidOperationException($"Missing semantic navigation icon: {item.Content}");
            if (AutomationProperties.GetName(item) != item.Content.ToString())
                throw new InvalidOperationException($"Missing accessible name: {item.Content}");
            if (item.Tag is string tag && !shell.IsWorkspaceRouteAllowed(tag)) throw new InvalidOperationException($"Foreign module is visible: {item.Content}");
        }
        var avatar = (Button)shell.FindName("UserAvatar");
        var footer = (NavigationViewItem)navigation.FooterMenuItems[0];
        double Y(FrameworkElement element) => element.TransformToVisual(shell).TransformPoint(new()).Y;
        var footerFits = Y(avatar) >= 0 && Y(footer) + footer.ActualHeight <= shell.ActualHeight + 1;
        if (!footerFits) throw new InvalidOperationException("User area or workspace switch is outside the viewport.");
        if (!navigation.IsPaneOpen && groups.Length > 0) throw new InvalidOperationException("Group headers remain in the compact pane.");
        var workspaceGrid = (Grid)shell.FindName("Workspace");
        double? reportLeft = _shellReportHost is { Visibility: Visibility.Visible }
            ? _shellReportHost.TransformToVisual(shell).TransformPoint(new()).X : null;
        var workspaceLeft = workspaceGrid.TransformToVisual(shell).TransformPoint(new()).X;
        if (reportLeft is { } left && Math.Abs(left - workspaceLeft) > 1)
            throw new InvalidOperationException("A second left margin remains between the navigation and Report List.");
        File.WriteAllText(Path.Combine(_directory, "shell-navigation.json"), JsonSerializer.Serialize(new
        {
            Passed = true, Boundary = "Real ShellPage and navigation factories; synthetic permissions and menu, no server or database",
            Workspace = shell.SelectedWorkspace, Role = shell.ViewModel.Role, Expanded = navigation.IsPaneOpen, Scale = shell.XamlRoot.RasterizationScale,
            Width = shell.ActualWidth, Height = shell.ActualHeight, FooterFits = footerFits,
            WorkspaceLeft = workspaceLeft, ReportLeft = reportLeft, LeftGap = reportLeft - workspaceLeft,
            DisplayMode = navigation.DisplayMode.ToString(), WorkspacePaddingLeft = workspaceGrid.Padding.Left,
            ReportBoundary = reportLeft is null ? null : "Real Report List in the real shell with synthetic in-memory HTTP; no server or database",
            BlockedRoutes = blocked,
            Groups = groups.Select(item => new { Title = AutomationProperties.GetName((ToggleButton)item.Content), Expanded = ((ToggleButton)item.Content).IsChecked }),
            Items = items.Select(item => new
            {
                Title = item.Content, Icon = LimsIcon.GetNavigationIcon(item).ToString(), item.IsSelected,
                Width = item.Icon.ActualWidth, Height = item.Icon.ActualHeight,
                Y = Y(item), Foreground = (item.Icon.Foreground as SolidColorBrush)?.Color.ToString(System.Globalization.CultureInfo.InvariantCulture)
            })
        }, ReportOptions));
    }
}

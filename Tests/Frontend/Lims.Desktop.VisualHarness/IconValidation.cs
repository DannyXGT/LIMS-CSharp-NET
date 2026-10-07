using Lims.DesignSystem.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Lims.Desktop.VisualHarness;

internal static class IconValidation
{
    private static readonly string[] SidebarItems = ["HomeItem", "StandardsItem"];
    public static FrameworkElement CreateGallery()
    {
        var panel = new StackPanel { Spacing = 16, Padding = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = "Catálogo LIMS · vectores reales WinUI · 16 / 20 / 24 / 28 / 32", FontSize = 20 });
        var states = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 28 };
        foreach (var state in new[] { "Normal", "Hover (puntero)", "Selected", "Disabled", "Danger" })
        {
            var button = new Button { Style = (Style)Application.Current.Resources["LimsSecondaryButtonStyle"] };
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            content.Children.Add(new LimsIcon { Icon = LimsIconKind.NewStandard, Size = Size("Medium"), IsSelected = state == "Selected", IsDanger = state == "Danger" });
            content.Children.Add(new TextBlock { Text = state });
            button.Content = content;
            button.IsEnabled = state != "Disabled";
            AutomationProperties.SetName(button, state);
            states.Children.Add(button);
        }
        panel.Children.Add(states);
        var grid = new Grid { ColumnSpacing = 16, RowSpacing = 14 };
        for (var c = 0; c < 6; c++) grid.ColumnDefinitions.Add(new ColumnDefinition());
        var icons = Enum.GetValues<LimsIconKind>().Where(kind => kind != LimsIconKind.None).ToArray();
        for (var r = 0; r < (icons.Length + 5) / 6; r++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var i = 0; i < icons.Length; i++)
        {
            var cell = new StackPanel { Spacing = 8 };
            cell.Children.Add(new TextBlock { Text = icons[i].ToString(), FontSize = 12 });
            var samples = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            foreach (var key in new[] { "Small", "Medium", "Large", "ExtraLarge", "Hero" })
                samples.Children.Add(new LimsIcon { Icon = icons[i], Size = Size(key) });
            cell.Children.Add(samples);
            Grid.SetColumn(cell, i % 6); Grid.SetRow(cell, i / 6);
            grid.Children.Add(cell);
        }
        panel.Children.Add(grid);
        return new ScrollViewer { Content = panel, Background = (Brush)Application.Current.Resources["LimsBackgroundBrush"] };
    }

    public static async Task<object> CheckAsync(FrameworkElement gallery, NavigationViewItem home, NavigationViewItem standards)
    {
        await Task.Delay(150);
        var icons = Descendants(gallery).OfType<LimsIcon>().ToArray();
        if (icons.Length != (Enum.GetValues<LimsIconKind>().Length - 1) * 5 + 5) throw new InvalidOperationException("Incomplete rendered catalog.");
        foreach (var icon in icons)
        {
            if (Math.Abs(icon.ActualWidth - icon.Size) > .1 || Math.Abs(icon.ActualHeight - icon.Size) > .1)
                throw new InvalidOperationException($"Non-square optical box: {icon.Icon} / {icon.Size}.");
            if (AutomationProperties.GetAccessibilityView(icon) != Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw || icon.IsTabStop || icon.IsHitTestVisible)
                throw new InvalidOperationException($"Decorative icon became interactive: {icon.Icon}.");
            var native = Descendants(icon).OfType<IconElement>().Single();
            if (native is PathIcon { Data: null } || native is FontIcon { Glyph.Length: 0 }) throw new InvalidOperationException($"Missing vector: {icon.Icon}.");
            if (native is PathIcon path && icon.Size != 24d && (path.Data.Transform is not ScaleTransform scale ||
                Math.Abs(scale.ScaleX - icon.Size / 24d) > .001 || Math.Abs(scale.ScaleY - scale.ScaleX) > .001))
                throw new InvalidOperationException($"Path can clip or deform at {icon.Size}: {icon.Icon}.");
        }
        var stateSamples = icons.Take(5).Select(icon => new {
            selected = icon.IsSelected, danger = icon.IsDanger, enabled = icon.IsEnabled,
            color = ((SolidColorBrush)Descendants(icon).OfType<IconElement>().Single().Foreground).Color.ToString(System.Globalization.CultureInfo.InvariantCulture)
        }).ToArray();
        if (stateSamples[0].color == stateSamples[2].color || stateSamples[0].color == stateSamples[3].color || stateSamples[0].color == stateSamples[4].color)
            throw new InvalidOperationException("Selected, disabled or danger did not recolor the same artwork.");
        return new { renderedIcons = icons.Length, squareBoxes = true, decorativeAutomation = true, allVectorsLoaded = true, stateSamples,
            sidebar = new[] { home, standards }.Select(item => new { item = item.Name, type = item.Icon.GetType().Name, width = item.Icon.ActualWidth, height = item.Icon.ActualHeight }) };
    }

    private static double Size(string key) => (double)Application.Current.Resources["IconSize" + key];
    public static object[] SidebarBoxes(FrameworkElement shell) => SidebarItems.Select(name =>
    {
        var item = (NavigationViewItem)shell.FindName(name);
        var center = item.Icon.TransformToVisual(shell).TransformPoint(new Windows.Foundation.Point(item.Icon.ActualWidth / 2, item.Icon.ActualHeight / 2));
        return (object)new { name, width = item.Icon.ActualWidth, height = item.Icon.ActualHeight, centerX = center.X, centerY = center.Y };
    }).ToArray();

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}

using Lims.Contracts.ReferenceMaterials;
using Lims.Desktop.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Lims.Desktop.VisualHarness;

internal static class StandardsAvailabilityChecks
{
    public static ScrollViewer Scroll(ReferenceMaterialsPage page) =>
        Descendants<ScrollViewer>((ListView)page.FindName("MaterialsList")).First();

    public static object Report(ReferenceMaterialsPage page)
    {
        var list = (ListView)page.FindName("MaterialsList");
        var scroll = Scroll(page);
        var rows = list.Items.OfType<ReferenceMaterialSummary>().Select(row =>
        {
            if (list.ContainerFromItem(row) is not ListViewItem { ContentTemplateRoot: Grid grid }) return null;
            var position = grid.TransformToVisual(list).TransformPoint(new Windows.Foundation.Point());
            if (position.Y + grid.ActualHeight <= 0 || position.Y >= list.ActualHeight) return null;
            var bar = (Grid)grid.FindName("RowAvailability");
            var layout = grid.FindName("RowAvailabilityLayout") as Grid;
            var visible = bar.Visibility == Visibility.Visible && (layout is null || layout.Visibility == Visibility.Visible);
            var progress = (ProgressBar)grid.FindName("RowProgress");
            var label = (TextBlock)grid.FindName("RowPercent");
            var expected = row.AvailableQuantity is { } available && row.TotalQuantity > 0 && available >= 0 && available <= row.TotalQuantity
                ? (double?)(available / row.TotalQuantity * 100) : null;
            var expectedVisible = grid.ActualWidth >= 1150 && expected.HasValue;
            return new
            {
                row.Id, row.Name, row.AvailableQuantity, row.TotalQuantity,
                width = grid.ActualWidth, dataContextId = (grid.DataContext as ReferenceMaterialSummary)?.Id,
                expected, expectedVisible, visible,
                value = progress.Value, label = label.Text,
                passed = visible == expectedVisible &&
                    (!expectedVisible || (Math.Abs(progress.Value - expected!.Value) < 0.001 &&
                        label.Text == $"{ReferenceMaterialPresentation.Number(decimal.Round((decimal)expected.Value, 2))} %")),
            };
        }).Where(row => row is not null).ToArray();
        return new { fixtureOnly = true, page.IsLoaded, page.ViewModel.CurrentPage, scroll.VerticalOffset, scroll.ScrollableHeight,
            visibleRows = rows.Length, passed = rows.Length > 0 && rows.All(row => row!.passed), rows };
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}

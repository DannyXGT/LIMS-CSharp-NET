using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Lims.Desktop.Controls;

/// <summary>Equal action columns that wrap inside the available matrix width.</summary>
public sealed class PermissionActionPanel : Panel
{
    private const double Gap = 8;
    private const double MinimumWidth = 128;
    private const double TileHeight = 62;
    private static int Columns(double width) => Math.Max(1, Math.Min(5, (int)((width + Gap) / (MinimumWidth + Gap))));
    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children.Count == 0) return new Size();
        var width = double.IsInfinity(availableSize.Width) ? Children.Count * (MinimumWidth + Gap) - Gap : availableSize.Width;
        var columns = Columns(width);
        var tileWidth = Math.Max(0, (width - Gap * (columns - 1)) / columns);
        foreach (var child in Children) child.Measure(new Size(tileWidth, TileHeight));
        var rows = (Children.Count + columns - 1) / columns;
        return new Size(width, rows * (TileHeight + Gap) - Gap);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        var columns = Columns(finalSize.Width);
        var width = Math.Max(0, (finalSize.Width - Gap * (columns - 1)) / columns);
        for (var i = 0; i < Children.Count; i++) Children[i].Arrange(new Rect(i % columns * (width + Gap), i / columns * (TileHeight + Gap), width, TileHeight));
        return finalSize;
    }
}

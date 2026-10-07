using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using System.Globalization;

namespace Lims.Desktop.VisualHarness;

internal static class StandardsPresentationChecks
{
    private static readonly string[] Fields = ["CasBox", "PresentationBox", "StorageTemperatureBox", "MethodBox", "UnitCombo", "ReceivedDatePicker"];
    public static object Report(ContentDialog editor)
    {
        var root = editor.XamlRoot;
        var surface = Find(editor, "BackgroundElement") as FrameworkElement;
        var bounds = surface?.TransformToVisual(root.Content).TransformBounds(new Rect(0, 0, surface.ActualWidth, surface.ActualHeight));
        return new {
            rootWidth = root.Size.Width, rootHeight = root.Size.Height, surface = bounds,
            centerOffsetX = bounds?.X + bounds?.Width / 2 - root.Size.Width / 2,
            centerOffsetY = bounds?.Y + bounds?.Height / 2 - root.Size.Height / 2,
            fields = Fields
                .Select(name => {
                    var field = editor.FindName(name) as FrameworkElement;
                    var placeholder = field is TextBox ? Find(field, "PlaceholderTextContentPresenter") as TextBlock
                        : (field as ContentControl)?.Content is Grid grid ? grid.Children.OfType<TextBlock>().FirstOrDefault() : null;
                    var brush = placeholder?.Foreground as SolidColorBrush;
                    return new { name, enabled = (field as Control)?.IsEnabled, placeholder = placeholder?.Text,
                        foreground = brush?.Color.ToString(CultureInfo.InvariantCulture), brushOpacity = brush?.Opacity,
                        realForeground = ((field as Control)?.Foreground as SolidColorBrush)?.Color.ToString(CultureInfo.InvariantCulture) };
                })
        };
    }

    private static DependencyObject? Find(DependencyObject parent, string name)
    {
        if (parent is FrameworkElement { Name: var elementName } && elementName == name) return parent;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            if (Find(VisualTreeHelper.GetChild(parent, i), name) is { } found) return found;
        return null;
    }
}

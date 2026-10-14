using Lims.DesignSystem.Controls;
using Lims.DesignSystem.Presentation;
using Lims.Desktop.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
namespace Lims.Desktop.Controls;
internal static class DashboardComponents
{
    public static ElementTheme Theme { get; set; } = ElementTheme.Dark;
    public static Brush BrushResource(string key)
    {
        foreach (var dictionary in Application.Current.Resources.MergedDictionaries)
            if (dictionary.ThemeDictionaries.TryGetValue(Theme.ToString(), out var value) && value is ResourceDictionary theme)
            {
                var colorKey = key.Replace("Brush", "Color", StringComparison.Ordinal);
                if (theme.TryGetValue(colorKey, out var color) && color is Windows.UI.Color actual) return new SolidColorBrush(actual);
                if (theme.TryGetValue(key, out var brush)) return (Brush)brush;
            }
        return (Brush)Application.Current.Resources[key];
    }
    public static TextBlock Text(string text, double size = 13, string brush = "LimsTextPrimaryBrush") => new()
    { Text = text, FontSize = size, Foreground = BrushResource(brush), TextWrapping = TextWrapping.Wrap };
    public static Border Surface(UIElement child, double padding = 18) => new()
    { Child = child, Padding = new Thickness(padding), CornerRadius = new CornerRadius(12), Background = BrushResource("LimsSurfaceBrush"), BorderBrush = BrushResource("LimsBorderBrush"), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Stretch };
    public static string ToneBrush(string tone) => tone switch
    { "Success" => "LimsSuccessBrush", "Danger" => "LimsDangerBrush", "Warning" => "LimsWarningBrush", "Information" => "LimsInformationBrush", _ => "LimsTextSecondaryBrush" };
    public static StackPanel Section(string title, string subtitle)
    {
        var panel = new StackPanel { Spacing = 12 };
        var heading = Text(title, 16); heading.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        panel.Children.Add(heading); panel.Children.Add(Text(subtitle, 12, "LimsTextSecondaryBrush")); return panel;
    }
    public static Button Action(string label, LimsIconKind icon, Action action)
    {
        var content = new Grid { ColumnSpacing = 8 };
        content.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); content.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        content.Children.Add(new LimsIcon { Icon = icon, Size = 16, VerticalAlignment = VerticalAlignment.Center });
        var text = Text(label); Grid.SetColumn(text, 1); content.Children.Add(text);
        var button = new Button { Content = content, Style = (Style)Application.Current.Resources["LimsSecondaryButtonStyle"], HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
        button.Background = BrushResource("LimsSurfaceElevatedBrush"); button.BorderBrush = BrushResource("LimsBorderBrush");
        AutomationProperties.SetName(button, label); button.Click += (_, _) => action();
        Motion.SetIsEnabled(button, true); button.PointerEntered += (_, _) => DashboardMotion.Hover(button, true); button.PointerExited += (_, _) => DashboardMotion.Hover(button, false);
        return button;
    }
    public static FrameworkElement Metric(string title, string value, string caption, LimsIconKind icon, string tone, Action? action = null)
    {
        var body = new StackPanel { Spacing = 8 };
        var header = new Grid { ColumnSpacing = 10 }; header.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        header.Children.Add(Text(title, 13, "LimsTextSecondaryBrush"));
        var symbol = new LimsIcon { Icon = icon, Size = 20, Foreground = BrushResource(ToneBrush(tone)) }; Grid.SetColumn(symbol, 1); header.Children.Add(symbol);
        body.Children.Add(header); var number = Text(value, 28, ToneBrush(tone)); number.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; body.Children.Add(number); body.Children.Add(Text(caption, 12, "LimsTextSecondaryBrush"));
        var card = Surface(body, 14); card.MinHeight = 120;
        AutomationProperties.SetName(card, $"{title}: {value}. {caption}");
        if (action is null) return card;
        var button = new Button { Content = card, Padding = new Thickness(0), BorderThickness = new Thickness(0), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
        AutomationProperties.SetName(button, $"{title}: {value}. Abrir módulo"); button.Click += (_, _) => action(); Motion.SetIsEnabled(button, true);
        button.PointerEntered += (_, _) => DashboardMotion.Hover(button, true); button.PointerExited += (_, _) => DashboardMotion.Hover(button, false);
        return button;
    }
}

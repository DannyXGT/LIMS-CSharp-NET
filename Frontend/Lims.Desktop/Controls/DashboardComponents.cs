using System.Globalization;
using Lims.DesignSystem.Controls;
using Lims.Desktop.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.ViewManagement;
namespace Lims.Desktop.Controls;

internal static class DashboardComponents
{
    public static ElementTheme Theme { get; set; } = ElementTheme.Dark;
    public static Brush BrushResource(string key)
    {
        var contrast = new AccessibilitySettings().HighContrast;
        foreach (var dictionary in Application.Current.Resources.MergedDictionaries)
        {
            if (!dictionary.ThemeDictionaries.TryGetValue(contrast ? "HighContrast" : Theme.ToString(), out var value))
                dictionary.ThemeDictionaries.TryGetValue("Default", out value);
            if (value is not ResourceDictionary theme) continue;
            var colorKey = key.Replace("Brush", "Color", StringComparison.Ordinal);
            if (!contrast && theme.TryGetValue(colorKey, out var color) && color is Color actual) return new SolidColorBrush(actual);
            if (theme.TryGetValue(key, out var brush)) return (Brush)brush;
        }
        return (Brush)Application.Current.Resources[key];
    }
    public static TextBlock Text(string text, double size = 12, string brush = "LimsTextPrimaryBrush") => new()
    { Style = (Style)Application.Current.Resources["LimsBodyStyle"], Text = text, FontSize = size, Foreground = BrushResource(brush), TextWrapping = TextWrapping.Wrap };
    public static Border Surface(UIElement child, double padding = 12) => new()
    {
        Child = child, Style = (Style)Application.Current.Resources["LimsDashboardPanelStyle"], Padding = new Thickness(padding),
        Background = BrushResource("LimsSurfaceBrush"), BorderBrush = BrushResource("LimsBorderBrush")
    };
    public static string ToneBrush(string tone) => tone switch
    { "Success" => "LimsSuccessBrush", "Danger" => "LimsDangerBrush", "Warning" => "LimsWarningBrush", "Information" => "LimsAccentBrush", _ => "LimsTextSecondaryBrush" };
    public static Border IconTile(LimsIconKind icon, string brush, double size = 30) => new()
    {
        Width = size, Height = size, CornerRadius = new(size > 40 ? 8 : 6),
        Background = BrushResource("LimsSurfaceSecondaryBrush"),
        Child = new LimsIcon { Icon = icon, Size = size * .55, Foreground = BrushResource(brush), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
    };
    public static Border AnalyticPanel(string title, string subtitle, UIElement content, Action? detail = null, string actionLabel = "Ver detalle")
    {
        var (icon, tone) = title switch
        {
            "Estado general de estándares" => (LimsIconKind.Standards, "Information"),
            "Vencimientos prioritarios" => (LimsIconKind.Calendar, "Information"),
            "Disponibilidad por método" => (LimsIconKind.Availability, "Success"),
            "Distribución por método" => (LimsIconKind.Results, "Information"),
            "Resumen de disponibilidad" => (LimsIconKind.Inventory, "Information"),
            "Materiales con disponibilidad baja" => (LimsIconKind.Alerts, "Warning"),
            "Alarmas y notificaciones" => (LimsIconKind.Notifications, "Warning"),
            _ => (LimsIconKind.Traceability, "Information")
        };
        var body = new StackPanel { Spacing = 10 };
        var header = new Grid { ColumnSpacing = 8 };
        header.ColumnDefinitions.Add(new() { Width = new(30) });
        header.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        header.Children.Add(IconTile(icon, ToneBrush(tone)));
        var labels = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        var heading = Text(title, 12); heading.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; labels.Children.Add(heading);
        if (subtitle.Length > 0) { var caption = Text(subtitle, 9, "LimsTextSecondaryBrush"); caption.Style = (Style)Application.Current.Resources["LimsCaptionStyle"]; labels.Children.Add(caption); }
        Grid.SetColumn(labels, 1); header.Children.Add(labels);
        if (detail is not null) { var link = Link(actionLabel, detail); Grid.SetColumn(link, 2); header.Children.Add(link); }
        body.Children.Add(header); body.Children.Add(content);
        var card = Surface(body); AutomationProperties.SetName(card, title); return card;
    }
    public static Button Link(string label, Action action, string? automationName = null)
    {
        var button = new Button { Content = label, Style = (Style)Application.Current.Resources["LimsDashboardLinkStyle"] };
        AutomationProperties.SetName(button, automationName ?? label); button.Click += (_, _) => action(); return button;
    }
    public static Button Action(string label, LimsIconKind icon, Action action)
    {
        var content = new Grid { ColumnSpacing = 5 };
        content.ColumnDefinitions.Add(new() { Width = new(14) }); content.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        content.Children.Add(new LimsIcon { Icon = icon, Size = 12, Foreground = BrushResource("LimsAccentBrush"), VerticalAlignment = VerticalAlignment.Center });
        var text = Text(label, 10); text.TextWrapping = TextWrapping.NoWrap; text.TextTrimming = TextTrimming.CharacterEllipsis; Grid.SetColumn(text, 1); content.Children.Add(text);
        var button = Link(label, action); button.Content = content; button.MaxWidth = 400;
        ToolTipService.SetToolTip(button, label); return button;
    }
    public static FrameworkElement Metric(string title, string value, string caption, LimsIconKind icon, string tone,
        Action? action = null, double? percent = null, string? detail = null)
    {
        var brush = ToneBrush(tone);
        var body = new Grid { RowSpacing = 8 };
        body.RowDefinitions.Add(new() { Height = GridLength.Auto }); body.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); body.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var primary = new Grid { ColumnSpacing = 12 };
        primary.ColumnDefinitions.Add(new() { Width = new(44) }); primary.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        primary.Children.Add(IconTile(icon, brush, 44));
        var figures = new StackPanel { Spacing = 2 };
        if (action is null) figures.Children.Add(Text(title, 10));
        else
        {
            var link = Link(title, action, $"{title}: {value}. Abrir módulo"); link.Padding = new(0); link.MinHeight = 16;
            link.Content = Text(title, 10); figures.Children.Add(link);
        }
        var number = Text(value, 28, brush); number.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; number.LineHeight = 32; figures.Children.Add(number);
        Grid.SetColumn(figures, 1); primary.Children.Add(figures); body.Children.Add(primary);
        var captionText = Text(caption, 10, "LimsTextSecondaryBrush"); captionText.MaxLines = 2; Grid.SetRow(captionText, 1); body.Children.Add(captionText);
        var micro = new StackPanel { Spacing = 5 };
        if (percent is {} ratio)
        {
            var bar = Pill(ratio, brush, 7); AutomationProperties.SetName(bar, $"{title}: {ratio:0.#} %"); micro.Children.Add(bar);
        }
        else
        {
            var track = new Border { Height = 7, CornerRadius = new(4), Background = BrushResource("LimsBorderBrush") };
            AutomationProperties.SetName(track, "Proporción no disponible"); micro.Children.Add(track);
        }
        if (detail is not null)
        {
            var line = new Grid(); line.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); line.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var text = Text(detail, 9, "LimsTextSecondaryBrush"); text.MaxLines = 1; text.TextTrimming = TextTrimming.CharacterEllipsis; ToolTipService.SetToolTip(text, detail); line.Children.Add(text);
            if (percent is {} p) { var count = Text($"{p:0.#} %", 10, brush); count.Margin = new(6, 0, 0, 0); Grid.SetColumn(count, 1); line.Children.Add(count); }
            micro.Children.Add(line);
        }
        Grid.SetRow(micro, 2); body.Children.Add(micro);
        var card = Surface(body, 12); card.MinHeight = 128; card.VerticalAlignment = VerticalAlignment.Stretch;
        AutomationProperties.SetName(card, $"{title}: {value}. {caption}"); return card;
    }
    public static Grid Pill(double value, string brush, double height = 8)
    {
        var root = new Grid { Height = height, Background = BrushResource("LimsBorderBrush"), CornerRadius = new(height / 2) };
        var fill = new Border { CornerRadius = new(height / 2), Background = BrushResource(brush), HorizontalAlignment = HorizontalAlignment.Left };
        root.Children.Add(fill);
        root.SizeChanged += (_, _) => fill.Width = root.ActualWidth * Math.Clamp(value, 0, 100) / 100;
        return root;
    }
    public static StatusBadge Badge(string label, string tone) => new()
    { Text = label, Tone = tone, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
    public static StackPanel DataTable(string[] headings, double[] widths, IEnumerable<UIElement[]> rows, Action<int>? open = null)
    {
        var table = new StackPanel();
        Grid Line(bool header)
        {
            var line = new Grid { ColumnSpacing = 8, Padding = new Thickness(5, header ? 5 : 3, 5, header ? 5 : 3),
                MinHeight = 24, Background = header ? BrushResource("LimsSurfaceSecondaryBrush") : null };
            foreach (var width in widths) line.ColumnDefinitions.Add(new() { Width = width < 0 ? new(-width, GridUnitType.Star) : new(width) });
            return line;
        }
        var headingsLine = Line(true);
        for (var i = 0; i < headings.Length; i++) { var label = Text(headings[i], 9, "LimsTextSecondaryBrush"); label.Style = (Style)Application.Current.Resources["LimsTableHeadingStyle"]; Grid.SetColumn(label, i); headingsLine.Children.Add(label); }
        table.Children.Add(headingsLine);
        var list = new ListView { SelectionMode = open is null ? ListViewSelectionMode.None : ListViewSelectionMode.Single, IsItemClickEnabled = open is not null,
            ItemContainerStyle = (Style)Application.Current.Resources["LimsDashboardRowStyle"], HorizontalContentAlignment = HorizontalAlignment.Stretch };
        // Native ListView presenter supplies hover, pressed, selection and keyboard focus.
        list.ContainerContentChanging += (_, args) =>
        {
            if (!args.InRecycleQueue && args.Item is Grid line)
                AutomationProperties.SetName(args.ItemContainer, AutomationProperties.GetName(line));
        };
        ScrollViewer.SetVerticalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollMode(list, ScrollMode.Disabled);
        var index = 0;
        foreach (var cells in rows)
        {
            var line = Line(false); line.Tag = index++;
            for (var i = 0; i < cells.Length; i++) { if (cells[i] is FrameworkElement el) { Grid.SetColumn(el, i); el.VerticalAlignment = VerticalAlignment.Center; } line.Children.Add(cells[i]); }
            var names = cells.Select(cell => cell is TextBlock text ? text.Text : AutomationProperties.GetName(cell)).Where(name => name.Length > 0);
            AutomationProperties.SetName(line, string.Join(" · ", names)); list.Items.Add(line);
        }
        if (open is not null) list.ItemClick += (_, e) => { if (e.ClickedItem is Grid { Tag: int row }) open(row); };
        table.Children.Add(list); return table;
    }
    public static Grid Donut(IReadOnlyList<DashboardChartPoint> points, string label, double size = 154, bool vertical = false)
    {
        var total = points.Sum(p => p.Count);
        var root = new Grid { ColumnSpacing = 14, RowSpacing = 8 };
        if (vertical) { root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new() { Height = GridLength.Auto }); }
        else { root.ColumnDefinitions.Add(new() { Width = new(size) }); root.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); }
        var chart = new Grid { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        var canvas = new Canvas { Width = size, Height = size };
        var inset = 14d; var radius = size / 2 - inset; var thickness = size * .145;
        canvas.Children.Add(new Ellipse { Width = radius * 2, Height = radius * 2, Stroke = BrushResource("LimsBorderBrush"), StrokeThickness = thickness, Margin = new(inset) });
        var angle = -90d;
        foreach (var point in points.Where(p => p.Count > 0))
        {
            var sweep = total > 0 ? 360d * point.Count / total : 0;
            if (sweep >= 359.99) canvas.Children.Add(new Ellipse { Width = radius * 2, Height = radius * 2, Margin = new(inset), Stroke = BrushResource(point.Brush), StrokeThickness = thickness });
            else
            {
                Point At(double degrees) => new(size / 2 + radius * Math.Cos(degrees * Math.PI / 180), size / 2 + radius * Math.Sin(degrees * Math.PI / 180));
                var figure = new PathFigure { StartPoint = At(angle), IsClosed = false };
                figure.Segments.Add(new ArcSegment { Point = At(angle + sweep), Size = new(radius, radius), IsLargeArc = sweep > 180, SweepDirection = SweepDirection.Clockwise });
                var geometry = new PathGeometry(); geometry.Figures.Add(figure);
                canvas.Children.Add(new Microsoft.UI.Xaml.Shapes.Path { Data = geometry, Stroke = BrushResource(point.Brush), StrokeThickness = thickness });
            }
            angle += sweep;
        }
        chart.Children.Add(new Ellipse { Width = size - thickness * 2 - 10, Height = size - thickness * 2 - 10, Fill = BrushResource("LimsSurfaceSecondaryBrush"), Stroke = BrushResource("LimsBorderBrush"), StrokeThickness = 1 });
        chart.Children.Add(canvas);
        var center = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
        var number = Text(total.ToString("N0", CultureInfo.CurrentCulture), size < 130 ? 25 : 30); number.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; number.HorizontalAlignment = HorizontalAlignment.Center;
        center.Children.Add(number); var caption = Text(label, 10, "LimsTextSecondaryBrush"); caption.HorizontalAlignment = HorizontalAlignment.Center; center.Children.Add(caption);
        chart.Children.Add(center); root.Children.Add(chart);
        var legend = new StackPanel { Spacing = 9, VerticalAlignment = VerticalAlignment.Center };
        var percentages = DashboardAnalytics.DisplayPercentages(points);
        for (var i = 0; i < points.Count; i++)
        {
            var point = points[i];
            var line = new Grid { ColumnSpacing = 7 };
            line.ColumnDefinitions.Add(new() { Width = new(10) }); line.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            line.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); line.ColumnDefinitions.Add(new() { Width = new(35) });
            line.Children.Add(new Border { Height = 10, Width = 10, CornerRadius = new(3), Background = BrushResource(point.Brush), VerticalAlignment = VerticalAlignment.Center });
            var text = Text(point.Label, 10, "LimsTextSecondaryBrush"); Grid.SetColumn(text, 1); line.Children.Add(text);
            var count = Text(point.Count.ToString("N0", CultureInfo.CurrentCulture), 10); Grid.SetColumn(count, 2); line.Children.Add(count);
            var percent = Text($"{percentages[i]:0.#} %", 10, "LimsTextSecondaryBrush"); percent.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(percent, 3); line.Children.Add(percent); legend.Children.Add(line);
        }
        if (vertical) Grid.SetRow(legend, 1); else Grid.SetColumn(legend, 1); root.Children.Add(legend);
        AutomationProperties.SetName(root, $"{label}: {total}. {string.Join(". ", points.Select(p => $"{p.Label}: {p.Count}"))}"); return root;
    }
    public static Grid HorizontalBars(IReadOnlyList<DashboardChartPoint> points, string? emptyMessage = null)
    {
        var panel = new StackPanel { Spacing = points.Count <= 3 ? 20 : 7, VerticalAlignment = VerticalAlignment.Center }; var total = points.Sum(p => p.Count);
        var percentages = DashboardAnalytics.DisplayPercentages(points);
        for (var i = 0; i < points.Count; i++)
        {
            var point = points[i];
            var line = new Grid { ColumnSpacing = 8, RowSpacing = 5 };
            line.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); line.ColumnDefinitions.Add(new() { Width = new(1.2, GridUnitType.Star) }); line.ColumnDefinitions.Add(new() { Width = new(49) });
            var label = Text(point.Label, 9, "LimsTextSecondaryBrush"); label.VerticalAlignment = VerticalAlignment.Center; line.Children.Add(label);
            var bar = Pill(total == 0 ? 0 : point.Count * 100d / total, point.Brush); bar.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(bar, 1); line.Children.Add(bar);
            var value = Text($"{point.Count}   {percentages[i]:0.#} %", 9); value.VerticalAlignment = VerticalAlignment.Center; value.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(value, 2); line.Children.Add(value);
            AutomationProperties.SetName(line, $"{point.Label}: {point.Count} de {total}"); panel.Children.Add(line);
        }
        if (points.Count == 0) panel.Children.Add(Text(emptyMessage ?? "Sin materiales en el catálogo autorizado.", 10, "LimsTextSecondaryBrush"));
        var root = new Grid { MinHeight = 130 }; root.Children.Add(panel); return root;
    }
    public static Grid VerticalBars(IReadOnlyList<DashboardAvailabilityPoint> points, string? emptyMessage = null)
    {
        var root = new Grid { ColumnSpacing = 5, RowSpacing = 5 };
        root.ColumnDefinitions.Add(new() { Width = new(28) }); root.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new() { Height = new(128) }); root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var plot = new Grid { Height = 128 };
        foreach (var tick in new[] { 0, 25, 50, 75, 100 })
        {
            var text = Text($"{tick}", 9, "LimsTextSecondaryBrush"); text.VerticalAlignment = VerticalAlignment.Top; text.HorizontalAlignment = HorizontalAlignment.Right; text.Margin = new(0, 18 + (100 - tick) * .96, 0, 0); root.Children.Add(text);
            plot.Children.Add(new Border { Height = 1, Background = BrushResource("LimsBorderBrush"), VerticalAlignment = VerticalAlignment.Top, Margin = new(0, 24 + (100 - tick) * .96, 0, 0) });
        }
        Grid.SetColumn(plot, 1); root.Children.Add(plot);
        var bars = new Grid { ColumnSpacing = 8 }; var labels = new Grid { ColumnSpacing = 8 };
        for (var i = 0; i < points.Count; i++)
        {
            var point = points[i];
            bars.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); labels.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            var column = new Grid();
            var height = point.Percent * .96;
            column.Children.Add(new Border { Height = height, MaxWidth = 40, CornerRadius = new(4, 4, 0, 0), Background = BrushResource(point.Brush), BorderBrush = BrushResource(point.Brush), BorderThickness = new(1), VerticalAlignment = VerticalAlignment.Bottom, Margin = new(0, 0, 0, 8) });
            var value = Text($"{point.Percent:0.#} %", 10); value.HorizontalAlignment = HorizontalAlignment.Center; value.VerticalAlignment = VerticalAlignment.Top;
            value.Margin = new(0, 8 + (100 - point.Percent) * .96, 0, 0); column.Children.Add(value);
            ToolTipService.SetToolTip(column, $"{point.Label}: {point.Percent:0.#} %. Promedio simple de {point.SampleCount} materiales.");
            Grid.SetColumn(column, i); bars.Children.Add(column);
            var label = Text(point.Label, 9, "LimsTextSecondaryBrush"); label.TextAlignment = TextAlignment.Center; Grid.SetColumn(label, i); labels.Children.Add(label);
        }
        Grid.SetColumn(bars, 1); root.Children.Add(bars); Grid.SetRow(labels, 1); Grid.SetColumn(labels, 1); root.Children.Add(labels);
        if (points.Count == 0) { var empty = Text(emptyMessage ?? "Sin materiales utilizables con porcentaje verificable.", 10, "LimsTextSecondaryBrush"); empty.VerticalAlignment = VerticalAlignment.Center; empty.TextAlignment = TextAlignment.Center; Grid.SetColumn(empty, 1); root.Children.Add(empty); }
        AutomationProperties.SetName(root, string.Join(". ", points.Select(p => $"{p.Label}: {p.Percent:0.#} % promedio, {p.SampleCount} materiales"))); return root;
    }
}

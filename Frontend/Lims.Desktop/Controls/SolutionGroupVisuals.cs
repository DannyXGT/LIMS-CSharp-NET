using System.Globalization;
using Lims.Contracts.ReferenceMaterials;
using Lims.Contracts.ReferencePreparations;
using Lims.Desktop.Presentation;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Lims.Desktop.Controls;

internal static class SolutionGroupVisuals
{
    public static Brush Brush(FrameworkElement owner, string key)
    {
        // Resolve through the element's theme; Application.RequestedTheme can differ from the dialog.
        foreach (var dictionary in Application.Current.Resources.MergedDictionaries)
        {
            if (dictionary.ThemeDictionaries.TryGetValue(owner.ActualTheme.ToString(), out var resource) && resource is ResourceDictionary theme)
            {
                if (theme.TryGetValue(key.Replace("Brush", "Color", StringComparison.Ordinal), out var color) && color is Color c) return new SolidColorBrush(c);
                if (theme.TryGetValue(key, out var brush)) return (Brush)brush;
            }
        }
        return (Brush)Application.Current.Resources[key];
    }
    public static TextBlock Text(string text, double size = 12, bool strong = false) => new()
    {
        Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap,
        FontWeight = strong ? FontWeights.SemiBold : FontWeights.Normal,
        IsTextSelectionEnabled = true
    };
    public static Border Card(FrameworkElement owner, UIElement child, double padding = 12) => new()
    {
        Child = child, Padding = new(padding), CornerRadius = new(8), BorderThickness = new(1),
        Background = Brush(owner, "LimsSurfaceSecondaryBrush"), BorderBrush = Brush(owner, "LimsBorderBrush")
    };
    public static Grid Columns(params double[] widths)
    {
        var grid = new Grid { ColumnSpacing = 10, RowSpacing = 6 };
        foreach (var width in widths) grid.ColumnDefinitions.Add(new() { Width = new(width, GridUnitType.Star) });
        return grid;
    }
    public static void Cell(Grid grid, FrameworkElement child, int column, int row = 0)
    {
        while (grid.RowDefinitions.Count <= row) grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        Grid.SetColumn(child, column); Grid.SetRow(child, row); grid.Children.Add(child);
    }
    public static StackPanel Metric(string label, string value)
    {
        var stack = new StackPanel { Spacing = 3 };
        stack.Children.Add(Text(label, 11)); stack.Children.Add(Text(value, 14, true)); return stack;
    }
    public static UIElement Concentrations(SolutionPart part)
    {
        var body = new StackPanel { Spacing = 6 };
        var header = Columns(2.2, 1.1, 1.2, .7);
        var titles = new[] { "Analito", "C. origen", "C. final", "Unidad" };
        for (var i = 0; i < titles.Length; i++) Cell(header, Text(titles[i], 11, true), i);
        body.Children.Add(header);
        var rows = Columns(2.2, 1.1, 1.2, .7);
        var index = 0;
        foreach (var row in SolutionAnalyteRow.From(part))
        {
            Cell(rows, Text(row.Name), 0, index); Cell(rows, Text(row.SourceDisplay), 1, index);
            Cell(rows, Text(row.ResultDisplay, 12, true), 2, index); Cell(rows, Text(row.Unit, 11), 3, index++);
        }
        var scroll = new ScrollViewer { Content = rows, MaxHeight = 190, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new(0, 0, 8, 0) };
        AutomationProperties.SetName(scroll, "Concentraciones por analito de " + part.Label);
        body.Children.Add(scroll); return body;
    }
    public static Expander SourceDetails(IntermediateSource source)
    {
        var body = new StackPanel { Spacing = 6 };
        body.Children.Add(Metric("Intermedia", source.Code)); body.Children.Add(Metric("Nombre", source.Name));
        body.Children.Add(Metric("Método", source.Method)); body.Children.Add(Metric("Identificador del origen", source.Id.ToString()));
        var table = Columns(2, 1, 1);
        Cell(table, Text("Analito", 11, true), 0); Cell(table, Text("CAS", 11, true), 1); Cell(table, Text("Lote", 11, true), 2);
        var i = 1;
        foreach (var a in source.Analytes)
        { Cell(table, Text(a.Name), 0, i); Cell(table, Text(a.CasNumber ?? "—"), 1, i); Cell(table, Text(a.Lot), 2, i++); }
        body.Children.Add(table);
        return new Expander { Header = "Referencias del origen · CAS y lotes", Content = body, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    }
    public static UIElement Debit(FrameworkElement owner, SolutionSourceDebit debit)
    {
        var stack = new StackPanel { Spacing = 7 };
        stack.Children.Add(Text(debit.Source.Code, 12, true));
        var name = Text(debit.Source.Name, 11); name.MaxLines = 1; name.TextTrimming = TextTrimming.CharacterEllipsis;
        ToolTipService.SetToolTip(name, debit.Source.Name); stack.Children.Add(name);
        var metrics = Columns(1, 1, 1);
        Cell(metrics, Metric("Antes", debit.Source.AvailableDisplay), 0);
        Cell(metrics, Metric("Consumo", StockPresentation.Quantity(debit.Quantity, debit.Source.VolumeUnit)), 1);
        Cell(metrics, Metric("Después", StockPresentation.Quantity(debit.RemainingVolume, debit.Source.VolumeUnit)), 2);
        stack.Children.Add(metrics);
        var percent = debit.Source.AvailableVolume <= 0 ? 0 : Math.Clamp(debit.RemainingVolume / debit.Source.AvailableVolume * 100, 0, 100);
        var bar = new ProgressBar { Value = (double)percent, Maximum = 100, Height = 5, Foreground = Brush(owner, debit.RemainingVolume < 0 ? "LimsDangerBrush" : "LimsInformationBrush") };
        AutomationProperties.SetName(bar, "Saldo después respecto al disponible antes: " + percent.ToString("0.##", CultureInfo.CurrentCulture) + " %");
        stack.Children.Add(bar); return Card(owner, stack, 10);
    }
    public static string FinalVolumes(SolutionCalculation calculation) => string.Join(" · ", calculation.Parts
        .GroupBy(p => p.FinalDisplay).Select(g => $"{g.Count()} × {g.Key}"));
    public static UIElement Vessel(FrameworkElement owner, SolutionVolumePreview? preview, SolutionPart? part = null)
    {
        var grid = Columns(.7, 2);
        var graphic = new Grid { Width = 56, Height = 118, VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 4, 0, 4) };
        var color = Brush(owner, preview?.Tone switch { "Danger" => "LimsDangerBrush", "Warning" => "LimsWarningBrush", _ => "LimsInformationBrush" });
        graphic.Children.Add(new Border { BorderThickness = new(2), BorderBrush = Brush(owner, "LimsTextSecondaryBrush"), CornerRadius = new(3, 3, 15, 15) });
        var totalHeight = preview is null ? 0 : (double)Math.Clamp(preview.Percent, 0, 100) * 1.08;
        var standardTaken = part?.InternalStandard is { } s
            ? StockUnits.ConvertVolume(s.VolumeTaken, s.VolumeUnit, part.FinalVolumeUnit) : 0;
        var standardHeight = preview is { Final: > 0 }
            ? totalHeight * (double)(standardTaken / Math.Max(preview.Taken, 0.000001m)) : 0;
        var standardColor = Brush(owner, "LimsSuccessBrush");
        graphic.Children.Add(new Border { Background = part?.IsInternalStandard == true ? standardColor : color, Opacity = .65, Height = totalHeight, Margin = new(5), VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new(0, 0, 10, 10) });
        if (standardHeight > 0)
            graphic.Children.Add(new Border { Background = standardColor, Opacity = .95, Height = standardHeight, Margin = new(5), VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new(0, 0, 10, 10) });
        for (var i = 0; i < 5; i++) graphic.Children.Add(new Border { Height = 1, Width = i % 2 == 0 ? 15 : 9, Background = Brush(owner, "LimsTextSecondaryBrush"), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new(2, 8 + i * 25, 0, 0) });
        Cell(grid, graphic, 0);
        var stats = new StackPanel { Spacing = 7 };
        if (preview is null) stats.Children.Add(Text("Complete los volúmenes para visualizar el aforo."));
        else
        {
            stats.Children.Add(Metric("Incorporado antes del aforo", preview.TakenDisplay));
            if (part?.InternalStandard is { } standard)
            {
                var legend = Text("Estándar interno · " + standard.Source.Code + " · " + StockPresentation.Quantity(standard.VolumeTaken, standard.VolumeUnit), 11, true);
                legend.Foreground = standardColor; stats.Children.Add(legend);
            }
            stats.Children.Add(Metric("Volumen final preparado", preview.FinalDisplay));
            stats.Children.Add(Metric(preview.Exceeds ? "Exceso sobre aforo" : "Pendiente hasta aforo", preview.RemainingDisplay));
            var percent = Text(preview.Percent.ToString("0.##", CultureInfo.CurrentCulture) + " % incorporado", 12, true); percent.Foreground = color; stats.Children.Add(percent);
        }
        Cell(grid, stats, 1);
        AutomationProperties.SetName(grid, preview is null ? "Recipiente sin parámetros" : $"{preview.TakenDisplay} incorporados antes del aforo; volumen final {preview.FinalDisplay}; {preview.Percent:0.##} por ciento incorporado");
        return grid;
    }
}

public sealed class SolutionResultView : UserControl
{
    public void Show(SolutionPart? part, SolutionVolumePreview? preview, string title, string error = "", IntermediateSource? source = null)
    {
        var stack = new StackPanel { Spacing = 10 };
        stack.Children.Add(SolutionGroupVisuals.Text(title, 14, true));
        stack.Children.Add(SolutionGroupVisuals.Vessel(this, preview, part));
        if (error.Length > 0)
        { var text = SolutionGroupVisuals.Text(error); text.Foreground = SolutionGroupVisuals.Brush(this, "LimsDangerBrush"); stack.Children.Add(text); }
        if (part is not null)
        {
            stack.Children.Add(SolutionGroupVisuals.Metric("Solvente de aforo", part.Solvent?.Display ?? "No registrado"));
            stack.Children.Add(SolutionGroupVisuals.Text(part.IsInternalStandard ? "CONCENTRACIONES · ESTÁNDAR INTERNO" : "CONCENTRACIONES FINALES", 11, true));
            stack.Children.Add(SolutionGroupVisuals.Concentrations(part));
            stack.Children.Add(SolutionGroupVisuals.SourceDetails(part.Source));
            if (part.InternalStandard is { } standard)
            {
                stack.Children.Add(SolutionGroupVisuals.Text("ESTÁNDAR INTERNO · " + standard.Source.Code, 11, true));
                foreach (var analyte in standard.Results) stack.Children.Add(SolutionGroupVisuals.Text(analyte.Display));
                stack.Children.Add(SolutionGroupVisuals.SourceDetails(standard.Source));
            }
        }
        else
        {
            stack.Children.Add(SolutionGroupVisuals.Text("Las concentraciones aparecerán al completar una solución válida."));
            if (source is not null) stack.Children.Add(SolutionGroupVisuals.SourceDetails(source));
        }
        Content = stack;
    }
}

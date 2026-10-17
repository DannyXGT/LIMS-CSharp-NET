using Lims.Contracts.ReferencePreparations;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Lims.Desktop.Views;

internal static class StockCompositionVisual
{
    public static void Render(Grid bar, TextBlock legend, IReadOnlyList<StockSolventDetail> rows)
    {
        bar.Children.Clear(); bar.ColumnDefinitions.Clear();
        string[] brushes = ["LimsAccentBrush", "LimsInformationBrush", "LimsSuccessBrush", "LimsWarningBrush", "LimsTextSecondaryBrush"];
        var total = rows.Sum(r => r.Percent);
        var position = 0;
        foreach (var row in rows.Where(r => r.Percent > 0))
        {
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength((double)row.Percent, GridUnitType.Star) });
            var segment = new Border { CornerRadius = new CornerRadius(3), Background = (Brush)Application.Current.Resources[brushes[row.Position % brushes.Length]] };
            var label = $"{row.Abbreviation} {row.PercentDisplay}";
            ToolTipService.SetToolTip(segment, label); AutomationProperties.SetName(segment, label);
            Grid.SetColumn(segment, position++); bar.Children.Add(segment);
        }
        if (total < 100)
        {
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength((double)(100 - total), GridUnitType.Star) });
            var missing = new Border { Background = (Brush)Application.Current.Resources["LimsBorderBrush"], CornerRadius = new CornerRadius(3) };
            AutomationProperties.SetName(missing, $"Sin asignar: {StockSolventComposition.Percent(100 - total)} %");
            Grid.SetColumn(missing, position); bar.Children.Add(missing);
        }
        legend.Text = string.Join("  |  ", rows.Select(r => $"{r.Abbreviation} {r.PercentDisplay}")) +
            (total < 100 ? $"  · Falta {StockSolventComposition.Percent(100 - total)} %" : total > 100 ? "  · Excede el 100 %" : string.Empty);
    }
}

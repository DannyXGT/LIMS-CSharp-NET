using System.Globalization;
using Lims.Contracts.ReferenceMaterials;
using Lims.Contracts.ReferencePreparations;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lims.Desktop.Views;

internal static class CommercialMaterialDetails
{
    public static FrameworkElement Build(ReferenceMaterialDetail material, bool operational = false)
    {
        var panel = new StackPanel { Spacing = 10 };
        if (material.Commercial is not { } certificate) return panel;
        var metadata = new StackPanel { Spacing = 6 };
        metadata.Children.Add(Text(material.PresentationLabel + " · Origen comercial certificado", true));
        metadata.Children.Add(Text($"Certificado: {certificate.Reference}\nMatriz: {certificate.Matrix}\nTrazabilidad: {certificate.Traceability ?? "No documentada"}"));
        metadata.Children.Add(Text($"Nominal: {(certificate.NominalConcentration is { } nominal ? $"{nominal:G29} {certificate.NominalUnit}" : "No documentada")} · Factor k: {certificate.CoverageFactor?.ToString("G29", CultureInfo.CurrentCulture) ?? "No documentado"}"));
        metadata.Children.Add(Text($"Fabricante: {ReferenceMaterialPresentation.Date(material.ExpirationDate)} · Apertura: {(certificate.OpenedDate is { } opened ? ReferenceMaterialPresentation.Date(opened) : "No registrada")}\nUso tras apertura: {certificate.UseAfterOpeningDays?.ToString(CultureInfo.CurrentCulture) ?? certificate.UseAfterOpeningMonths?.ToString(CultureInfo.CurrentCulture) ?? "No establecido"} {(certificate.UseAfterOpeningDays.HasValue ? "días" : "meses")} · Vencimiento efectivo: {ReferenceMaterialPresentation.Date(material.EffectiveExpirationDate ?? material.ExpirationDate)}"));
        if ((certificate.UseAfterOpeningMonths.HasValue || certificate.UseAfterOpeningDays.HasValue) && !certificate.OpenedDate.HasValue)
            metadata.Children.Add(Text("Falta registrar la apertura para habilitar el uso de este frasco."));
        foreach (var e in certificate.OtherExpressions ?? []) metadata.Children.Add(Text($"Otra expresión certificada: {e.Value:G29} ± {e.ExpandedUncertainty?.ToString("G29", CultureInfo.CurrentCulture) ?? "no documentada"} {e.Unit}"));
        if (!operational) panel.Children.Add(new Expander { Header = "Datos del fabricante (opcional)", Content = metadata, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch });
        if (certificate.SolventDetails is { Count: > 0 } solvents)
        {
            panel.Children.Add(Text("SOLVENTE / MATRIZ", true));
            panel.Children.Add(Text(StockSolventComposition.Summary(solvents)));
            var bar = new Grid { Height = 12, ColumnSpacing = 2 }; var legend = Text("");
            StockCompositionVisual.Render(bar, legend, solvents); panel.Children.Add(bar); panel.Children.Add(legend);
        }
        else if (operational && !string.IsNullOrWhiteSpace(certificate.Matrix)) panel.Children.Add(Text("Solvente / matriz: " + certificate.Matrix));
        panel.Children.Add(Text(certificate.Components.Count == 1 ? "COMPUESTO" : $"{certificate.Components.Count} ANALITOS", true));
        var search = new TextBox { PlaceholderText = "Buscar compuesto o CAS", Style = (Style)Application.Current.Resources["LimsCompactTextBoxStyle"] };
        panel.Children.Add(search);
        var rows = new StackPanel { Spacing = 4 };
        var table = new ScrollViewer { Content = rows, MaxHeight = 300, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(table);
        void Render()
        {
            rows.Children.Clear();
            rows.Children.Add(Row(operational ? ["Compuesto", "CAS", "Concentración", "Unidad"] : ["Compuesto", "CAS", "Concentración", "Unidad", "Incertidumbre", "Pureza %"], true));
            foreach (var c in certificate.Components.Where(c => $"{c.Name} {c.CasNumber}".Contains(search.Text.Trim(), StringComparison.OrdinalIgnoreCase)))
                rows.Children.Add(Row(operational ? [c.Name, c.CasNumber ?? "—", c.CertifiedConcentration.ToString("G29", CultureInfo.CurrentCulture), c.ConcentrationUnit] : [c.Name, c.CasNumber ?? "—", c.CertifiedConcentration.ToString("G29", CultureInfo.CurrentCulture), c.ConcentrationUnit,
                    c.ExpandedUncertainty?.ToString("G29", CultureInfo.CurrentCulture) ?? "—", c.PurityPercent?.ToString("G29", CultureInfo.CurrentCulture) ?? "—"], false));
        }
        search.TextChanged += (_, _) => Render(); Render();
        panel.Children.Add(Text("MOVIMIENTOS DEL FRASCO", true));
        foreach (var movement in material.Movements ?? [])
            panel.Children.Add(Text($"{ReferenceMaterialPresentation.LocalTimestamp(movement.OccurredAt)} · {movement.PreparationKind} {movement.PreparationCode}\nTomado {movement.Quantity:G29} {movement.Unit} · saldo {movement.BalanceBefore:G29} → {movement.BalanceAfter:G29} {movement.Unit}"));
        if (material.Movements is not { Count: > 0 }) panel.Children.Add(Text("Sin consumos registrados."));
        if (material.OpeningHistory is { Count: > 0 })
        {
            panel.Children.Add(Text("HISTORIAL DE APERTURA", true));
            foreach (var audit in material.OpeningHistory)
                panel.Children.Add(Text($"{ReferenceMaterialPresentation.LocalTimestamp(audit.ChangedAt)} · Usuario {audit.ActorUserId}: {audit.PreviousDate?.ToString("dd/MM/yyyy", CultureInfo.CurrentCulture) ?? "sin fecha"} → {audit.OpenedDate?.ToString("dd/MM/yyyy", CultureInfo.CurrentCulture) ?? "sin fecha"}"));
        }
        return panel;
    }
    private static TextBlock Text(string value, bool title = false) => new()
    {
        Text = value, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true,
        Style = (Style)Application.Current.Resources[title ? "LimsSectionTitleStyle" : "LimsCaptionStyle"]
    };
    private static Grid Row(string[] values, bool title)
    {
        var grid = new Grid { ColumnSpacing = 10, Padding = new Thickness(0, 5, 0, 5) };
        int[] widths = [210, 100, 115, 85, 110, 80];
        for (int i = 0; i < values.Length; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(widths[i]) });
            var text = Text(values[i], title);
            if (title) { text.FontSize = 12; text.TextWrapping = TextWrapping.NoWrap; }
            Grid.SetColumn(text, i); grid.Children.Add(text);
        }
        return grid;
    }
}

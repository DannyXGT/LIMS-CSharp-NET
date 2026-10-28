using System.Globalization;
using Lims.Contracts.ReferenceMaterials;
using Lims.Contracts.ReferencePreparations;
using Lims.Desktop.Controls;
using Lims.Desktop.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lims.Desktop.Views;

public sealed partial class SolutionGroupDetailDialog : ContentDialog
{
    public SolutionGroupDetailDialog(SolutionGroupDetail detail) { Detail = detail; InitializeComponent(); }
    public SolutionGroupDetail Detail { get; }
    private void OnOpened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        XamlRoot.Changed += OnRootChanged;
        KindAndMethod.Text = $"{Detail.Preparation.Kind} · {Detail.Preparation.Method}";
        TemperatureText.Text = string.IsNullOrWhiteSpace(Detail.StorageTemperature) ? "No registrada" : Detail.StorageTemperature;
        NotesText.Text = string.IsNullOrWhiteSpace(Detail.Notes) ? "Sin observaciones" : Detail.Notes;
        var metrics = new[] {
            ("Soluciones", Detail.Calculation.Parts.Count.ToString(CultureInfo.CurrentCulture)),
            ("Aforos individuales", SolutionGroupVisuals.FinalVolumes(Detail.Calculation)),
            ("Analitos únicos", Detail.Calculation.Parts.SelectMany(p => p.Results.Concat(p.InternalStandard?.Results ?? [])).Select(a => a.MaterialId).Distinct().Count().ToString(CultureInfo.CurrentCulture)),
            ("Orígenes únicos", Detail.Calculation.Debits.Count.ToString(CultureInfo.CurrentCulture)),
            ("Vigencia", Detail.Preparation.StatusLabel)
        };
        foreach (var (label, value) in metrics) SummaryMetrics.Children.Add(SolutionGroupVisuals.Card(this, SolutionGroupVisuals.Metric(label, value), 10));
        foreach (var consumption in Detail.Consumptions)
        {
            var body = new StackPanel { Spacing = 7 };
            body.Children.Add(SolutionGroupVisuals.Text(consumption.SourceCode, 12, true));
            var columns = SolutionGroupVisuals.Columns(1, 1, 1);
            SolutionGroupVisuals.Cell(columns, SolutionGroupVisuals.Metric("Antes", StockPresentation.Quantity(consumption.BalanceBefore, consumption.Unit)), 0);
            SolutionGroupVisuals.Cell(columns, SolutionGroupVisuals.Metric("Consumo", StockPresentation.Quantity(consumption.Quantity, consumption.Unit)), 1);
            SolutionGroupVisuals.Cell(columns, SolutionGroupVisuals.Metric("Después", StockPresentation.Quantity(consumption.BalanceAfter, consumption.Unit)), 2);
            body.Children.Add(columns); ConsumptionRows.Children.Add(SolutionGroupVisuals.Card(this, body, 10));
            var audit = new StackPanel { Spacing = 3 };
            audit.Children.Add(SolutionGroupVisuals.Text(consumption.SourceCode, 12, true));
            audit.Children.Add(SolutionGroupVisuals.Metric("Registrado por", consumption.Actor));
            audit.Children.Add(SolutionGroupVisuals.Metric("Fecha del movimiento", ReferenceMaterialPresentation.LocalTimestamp(consumption.OccurredAt)));
            audit.Children.Add(SolutionGroupVisuals.Metric("Movimiento", consumption.Id.ToString()));
            audit.Children.Add(SolutionGroupVisuals.Metric("Origen", consumption.SourceId.ToString())); AuditRows.Children.Add(audit);
        }
        if (Detail.Consumptions.Count == 0) ConsumptionRows.Children.Add(SolutionGroupVisuals.Text("Sin movimientos registrados en la ficha."));
        AuditRows.Children.Insert(0, SolutionGroupVisuals.Metric("Responsable registrado", Detail.Preparation.PreparedBy));
        AuditRows.Children.Insert(1, SolutionGroupVisuals.Metric("Fecha de creación", Detail.CreatedDisplay));
        SolutionsList.SelectedIndex = Detail.Calculation.Parts.Count > 0 ? 0 : -1;
        Resize();
    }
    private void OnSolutionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (SolutionsList.SelectedItem is SolutionPart part) ResultView.Show(part, SolutionVolumePreview.From(part), part.Label);
    }
    private void OnClosed(ContentDialog sender, ContentDialogClosedEventArgs args) => XamlRoot.Changed -= OnRootChanged;
    private void OnRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => Resize();
    private void Resize()
    {
        var width = Math.Max(280, Math.Min(1220, XamlRoot.Size.Width - 100));
        DetailRoot.Width = width; DetailScroll.MaxHeight = Math.Max(180, XamlRoot.Size.Height - 190);
        var narrow = width < 880;
        PreviewColumn.Width = narrow ? new(0) : new(width < 1100 ? 350 : 390);
        Grid.SetColumn(SelectedResult, narrow ? 0 : 1); Grid.SetRow(SelectedResult, narrow ? 1 : 0);
        SummaryMetrics.ColumnDefinitions.Clear(); SummaryMetrics.RowDefinitions.Clear();
        var columns = width < 620 ? 2 : 5;
        for (var i = 0; i < columns; i++) SummaryMetrics.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        for (var i = 0; i < (SummaryMetrics.Children.Count + columns - 1) / columns; i++) SummaryMetrics.RowDefinitions.Add(new() { Height = GridLength.Auto });
        for (var i = 0; i < SummaryMetrics.Children.Count; i++) { Grid.SetColumn((FrameworkElement)SummaryMetrics.Children[i], i % columns); Grid.SetRow((FrameworkElement)SummaryMetrics.Children[i], i / columns); }
    }
}

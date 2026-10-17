using Lims.Contracts.ReferenceMaterials;
using Lims.Contracts.ReferencePreparations;
using Lims.DesignSystem.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lims.Desktop.Views;

public sealed partial class StockDetailDialog : ContentDialog
{
    public StockDetailDialog(StockDetail detail)
    {
        Detail = detail;
        InitializeComponent();
        CompositionBar.Visibility = Solvents.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (Solvents.Count > 0) StockCompositionVisual.Render(CompositionBar, CompositionLegend, Solvents);

    }
    public StockDetail Detail { get; }
    public IReadOnlyList<StockSolventDetail> Solvents => Detail.Solvents ?? [];
    public string CompoundDisplay => "Compuesto: " + Detail.CompoundDisplay;
    public string BasisDisplay => Solvents.Count == 0 ? string.Empty : StockSolventComposition.BasisLabel;
    public IReadOnlyList<PreparationUsage> UsageHistory => Detail.UsageHistory ?? [];
    public string UsageMessage => Detail.UsageHistory is null ? "Historial de consumos de esta solución no informado por el servidor." : Detail.UsageHistory.Count == 0 ? "Sin consumos de esta solución registrados." : $"{Detail.UsageHistory.Count} movimientos hacia preparaciones derivadas";
    public string ExpirationDisplay => $"Vencimiento {Detail.Preparation.ExpirationDisplay}";
    public string SourceCas => ReferenceMaterialPresentation.Optional(Detail.Source.CasNumber);
    public string SourceCatalog => ReferenceMaterialPresentation.Optional(Detail.Source.CatalogNumber);
    public string ConsumedDisplay => "−" + Detail.Consumption.QuantityDisplay;
    public string StorageDisplay => ReferenceMaterialPresentation.Optional(Detail.StorageTemperature);
    public string PreparedDisplay => $"{Detail.Preparation.PreparedBy} · {Detail.Preparation.DateDisplay}";
    private void OnOpened(ContentDialog sender, ContentDialogOpenedEventArgs args) { Resize(); XamlRoot.Changed += OnRootChanged; Motion.Enter(DetailSurface, y: 5); }
    private void Resize() { DetailSurface.Width = Math.Max(260, Math.Min(800, XamlRoot.Size.Width - 100)); DetailScroll.MaxHeight = Math.Max(220, XamlRoot.Size.Height - 220); }
    private void OnRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => Resize();
    private void OnClosed(ContentDialog sender, ContentDialogClosedEventArgs args) => XamlRoot.Changed -= OnRootChanged;
}

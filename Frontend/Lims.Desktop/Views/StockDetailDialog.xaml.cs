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

    }
    public StockDetail Detail { get; }
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

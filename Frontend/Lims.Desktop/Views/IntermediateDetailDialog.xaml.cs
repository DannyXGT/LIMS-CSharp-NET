using Lims.Contracts.ReferenceMaterials;
using Lims.Contracts.ReferencePreparations;
using Lims.DesignSystem.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lims.Desktop.Views;

public sealed partial class IntermediateDetailDialog : ContentDialog
{
    public IntermediateDetailDialog(IntermediateDetail detail) { Detail = detail; InitializeComponent(); }
    public IntermediateDetail Detail { get; }
    public IReadOnlyList<PreparationUsage> UsageHistory => Detail.UsageHistory ?? [];
    public string UsageMessage => Detail.UsageHistory is null ? "Historial de consumos de esta solución no informado por el servidor." : Detail.UsageHistory.Count == 0 ? "Sin consumos de esta solución registrados." : $"{Detail.UsageHistory.Count} movimientos hacia preparaciones derivadas";
    public IntermediateSource? RequestedSource { get; private set; }
    public string ContextDisplay => $"{Detail.Preparation.Method} · Preparado por {Detail.Preparation.PreparedBy} · {Detail.Preparation.DateDisplay}";
    public string VolumeDisplay => $"{Detail.Calculation.TakenDisplay} de componentes · Aforo {Detail.Calculation.FinalDisplay}";
    public string ExpirationDisplay => $"Expiración {Detail.Preparation.ExpirationDisplay}";
    public string SolventDisplay => "Solvente de aforo: " + (Detail.Solvent?.Display ?? "No registrado");
    public string AuditDisplay => $"Creado por {Detail.Preparation.PreparedBy} · {Detail.CreatedDisplay}\nÚltima modificación por {Detail.UpdatedBy} · {Detail.UpdatedDisplay}";
    private void OnOpened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        Resize(); XamlRoot.Changed += OnRootChanged; Motion.Enter(DetailSurface, y: 5);
        Cylinder.SetVolumes(StockUnits.ConvertVolume(Detail.Calculation.TotalVolumeTaken, Detail.Calculation.VolumeUnit, "mL"),
            StockUnits.ConvertVolume(Detail.Calculation.FinalVolume, Detail.Calculation.VolumeUnit, "mL"), false);
    }
    private void Resize() { DetailSurface.Width = Math.Max(280, Math.Min(860, XamlRoot.Size.Width - 100)); DetailScroll.MaxHeight = Math.Max(240, XamlRoot.Size.Height - 210); }
    private void OnRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => Resize();
    private void OnClosed(ContentDialog sender, ContentDialogClosedEventArgs args) => XamlRoot.Changed -= OnRootChanged;
    private void OnSourceClick(object sender, RoutedEventArgs args) { if (sender is Button { Tag: IntermediateSource source }) { RequestedSource = source; Hide(); } }
}

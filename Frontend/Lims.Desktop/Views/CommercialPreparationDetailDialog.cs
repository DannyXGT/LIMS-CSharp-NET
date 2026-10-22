using Lims.Contracts.ReferenceMaterials;
using Lims.Contracts.ReferencePreparations;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lims.Desktop.Views;

public sealed class CommercialPreparationDetailDialog : ContentDialog
{
    private readonly Grid _surface;
    public CommercialPreparationDetail Detail { get; }
    public CommercialPreparationDetailDialog(CommercialPreparationDetail detail)
    {
        Detail = detail;
        Style = (Style)Application.Current.Resources["LimsEditorDialogStyle"];
        Title = detail.Code + " · " + detail.OriginLabel;
        Resources["ContentDialogMaxWidth"] = 1100d;
        var material = detail.Material;
        var content = new StackPanel { Spacing = 10 };
        void Add(string value, string style = "LimsCaptionStyle") => content.Children.Add(new TextBlock
        { Text = value, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, Style = (Style)Application.Current.Resources[style] });
        Add(material.Name, "LimsSectionTitleStyle");
        var badge = new Border { Padding = new Thickness(8, 3, 8, 3), CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left,
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["LimsSurfaceBrush"], Child = new TextBlock { Text = "Comercial · " + material.StatusLabel, Style = (Style)Application.Current.Resources["LimsCaptionStyle"] } };
        content.Children.Add(badge);
        if (material.Commercial is { } certificate)
        {
            if (detail.Kind == "Stock")
            {
                var used = certificate.ComponentsForUse().Single();
                Add($"{used.CertifiedConcentration:G29} {used.ConcentrationUnit}", "LimsSectionTitleStyle");
                if (certificate.UseNominalConcentration) Add($"Uso nominal según método · Certificada: {certificate.Components[0].CertifiedConcentration:G29} {certificate.Components[0].ConcentrationUnit}");
            }
            else Add($"{certificate.Components.Count} analitos · un único inventario");
        }
        Add(detail.ResponsibleDisplay + " · " + ReferenceMaterialPresentation.LocalTimestamp(detail.RegisteredAt));
        Add($"{material.Brand} · Catálogo {material.CatalogNumber ?? "—"} · Lote {material.Lot}\n{material.Method} · {material.StorageLocation} · {material.StorageTemperature}");
        Add($"Ingreso {ReferenceMaterialPresentation.Date(material.ReceivedDate)} · Vencimiento {ReferenceMaterialPresentation.Date(material.EffectiveExpirationDate ?? material.ExpirationDate)}");
        var balance = detail.Availability;
        Add($"Disponible {balance.AvailableDisplay} de {balance.InitialDisplay} · {balance.PercentDisplay}");
        content.Children.Add(new ProgressBar { Minimum = 0, Maximum = 100, Value = balance.ProgressValue, Height = 6 });
        content.Children.Add(CommercialMaterialDetails.Build(material, operational: true));
        _surface = new Grid { RowSpacing = 12 };
        _surface.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _surface.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _surface.Children.Add(new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        var close = new Button { Content = "Cerrar", Style = (Style)Application.Current.Resources["LimsSecondaryButtonStyle"], HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => Hide(); Grid.SetRow(close, 1); _surface.Children.Add(close);
        Content = _surface;
        Opened += (_, _) => { Resize(); XamlRoot.Changed += OnRootChanged; };
        Closed += (_, _) => XamlRoot.Changed -= OnRootChanged;
    }
    private void OnRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => Resize();
    private void Resize()
    {
        _surface.Width = Math.Max(280, Math.Min(980, XamlRoot.Size.Width - 100));
        _surface.MaxHeight = Math.Max(240, XamlRoot.Size.Height - 150);
    }
}

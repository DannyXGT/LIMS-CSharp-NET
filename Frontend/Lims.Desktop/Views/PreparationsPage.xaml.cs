using Lims.DesignSystem.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lims.Desktop.Views;

public sealed partial class PreparationsPage : Page
{
    private readonly StockPage _stock;
    private readonly IntermediatePage _intermediate;
    private bool _showIntermediate;
    public PreparationsPage(StockPage stock, IntermediatePage intermediate)
    {
        _stock = stock; _intermediate = intermediate; InitializeComponent(); PreparationHost.Content = stock;
    }
    public Task LoadAsync() => _showIntermediate ? _intermediate.LoadAsync() : _stock.LoadAsync();
    public async Task ShowIntermediateAsync()
    {
        _showIntermediate = true; UpdateTabs(); PreparationHost.Content = _intermediate;
        Motion.Enter(_intermediate, y: 4, milliseconds: 150); await _intermediate.LoadAsync();
    }
    private async void OnStock(object sender, RoutedEventArgs args)
    {
        _showIntermediate = false; UpdateTabs(); PreparationHost.Content = _stock;
        Motion.Enter(_stock, y: 4, milliseconds: 150); await _stock.LoadAsync();
    }
    private async void OnIntermediate(object sender, RoutedEventArgs args) => await ShowIntermediateAsync();
    private void UpdateTabs()
    {
        StockTab.Style = (Style)Application.Current.Resources[_showIntermediate ? "LimsSecondaryButtonStyle" : "LimsPrimaryButtonStyle"];
        IntermediateTab.Style = (Style)Application.Current.Resources[_showIntermediate ? "LimsPrimaryButtonStyle" : "LimsSecondaryButtonStyle"];
    }
}

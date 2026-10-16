using Lims.DesignSystem.Presentation;
using Microsoft.UI.Xaml.Controls;

namespace Lims.Desktop.Views;

public sealed partial class PreparationsPage : Page
{
    private readonly StockPage _stock;
    private readonly IntermediatePage _intermediate;
    private bool ShowIntermediate => PreparationTabs.SelectedIndex == 1;
    public string CreateLabel => ShowIntermediate ? "Nueva Intermedia" : "Nuevo Stock";
    public bool CanCreate => ShowIntermediate ? _intermediate.ViewModel.CanCreate : _stock.ViewModel.CanCreate;
    public event EventHandler? SelectionChanged;
    public PreparationsPage(StockPage stock, IntermediatePage intermediate)
    {
        _stock = stock; _intermediate = intermediate; InitializeComponent(); PreparationHost.Content = stock;
    }
    public Task LoadAsync() => ShowIntermediate ? _intermediate.LoadAsync() : _stock.LoadAsync();
    public Task OpenCreateAsync() => ShowIntermediate ? _intermediate.OpenCreateAsync() : _stock.OpenCreateAsync();
    public void RefreshAccess(bool stock, bool intermediate)
    {
        _stock.ViewModel.RefreshPermissions(); _intermediate.ViewModel.RefreshPermissions();
        StockTab.Visibility = stock ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        StockTab.IsEnabled = stock;
        IntermediateTab.Visibility = intermediate ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        IntermediateTab.IsEnabled = intermediate;
        if (ShowIntermediate && !intermediate && stock) PreparationTabs.SelectedIndex = 0;
        if (!ShowIntermediate && !stock && intermediate) PreparationTabs.SelectedIndex = 1;
        if (!stock) { _stock.ViewModel.Items.Clear(); _stock.ViewModel.ClearSelection(); }
        if (!intermediate) { _intermediate.ViewModel.Items.Clear(); _intermediate.ViewModel.ClearSelection(); }
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
    public async Task ShowIntermediateAsync()
    {
        if (ShowIntermediate) await _intermediate.LoadAsync();
        else PreparationTabs.SelectedIndex = 1;
    }
    private async void OnTabChanged(object? sender, EventArgs args)
    {
        PreparationHost.Content = ShowIntermediate ? (Page)_intermediate : _stock;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        Motion.Enter(PreparationHost, x: ShowIntermediate ? 6 : -6, milliseconds: 160);
        await LoadAsync();
    }
}

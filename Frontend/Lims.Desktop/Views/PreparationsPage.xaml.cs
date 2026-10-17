using Lims.DesignSystem.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lims.Desktop.Views;

public sealed partial class PreparationsPage : Page
{
    private readonly StockPage _stock;
    private readonly IntermediatePage _intermediate;
    private readonly SolutionGroupPage? _curve;
    private readonly SolutionGroupPage? _aqs;
    private bool ShowIntermediate => PreparationTabs.SelectedIndex == 1;
    private SolutionGroupPage? Group => PreparationTabs.SelectedIndex switch { 2 => _curve, 3 => _aqs, _ => null };
    public string CreateLabel => PreparationTabs.SelectedIndex switch { 1 => "Nueva Intermedia", 2 => "Nueva Curva", 3 => "Nuevo AQS", _ => "Nuevo Stock" };
    public bool CanCreate => PreparationTabs.SelectedIndex switch { 1 => _intermediate.ViewModel.CanCreate, 2 or 3 => Group?.ViewModel.CanCreate == true, _ => _stock.ViewModel.CanCreate };
    public event EventHandler? SelectionChanged;
    public PreparationsPage(StockPage stock, IntermediatePage intermediate, SolutionGroupPage? curve = null, SolutionGroupPage? aqs = null)
    {
        _stock = stock; _intermediate = intermediate; _curve = curve; _aqs = aqs;
        InitializeComponent(); PreparationHost.Content = stock;
    }
    public Task LoadAsync() => Group is { } group ? group.LoadAsync() : ShowIntermediate ? _intermediate.LoadAsync() : _stock.LoadAsync();
    public Task OpenCreateAsync() => Group is { } group ? group.OpenCreateAsync() : ShowIntermediate ? _intermediate.OpenCreateAsync() : _stock.OpenCreateAsync();
    public void RefreshAccess(bool stock, bool intermediate, bool curve = false, bool aqs = false)
    {
        _stock.ViewModel.RefreshPermissions(); _intermediate.ViewModel.RefreshPermissions();
        _curve?.ViewModel.RefreshPermissions(); _aqs?.ViewModel.RefreshPermissions();
        var access = new[] { stock, intermediate, curve && _curve is not null, aqs && _aqs is not null };
        var tabs = new[] { StockTab, IntermediateTab, CurveTab, AqsTab };
        for (var i = 0; i < tabs.Length; i++) { tabs[i].Visibility = access[i] ? Visibility.Visible : Visibility.Collapsed; tabs[i].IsEnabled = access[i]; }
        if (!access[PreparationTabs.SelectedIndex])
        {
            var first = Array.FindIndex(access, value => value);
            if (first >= 0) PreparationTabs.SelectedIndex = first;
            else PreparationHost.Content = null;
        }
        if (PreparationHost.Content is null && access.Any(value => value))
            PreparationHost.Content = Group is { } group ? group : ShowIntermediate ? (Page)_intermediate : _stock;
        if (!stock) { _stock.ViewModel.Items.Clear(); _stock.ViewModel.ClearSelection(); }
        if (!intermediate) { _intermediate.ViewModel.Items.Clear(); _intermediate.ViewModel.ClearSelection(); }
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
    public async Task ShowIntermediateAsync()
    {
        if (ShowIntermediate) await _intermediate.LoadAsync(); else PreparationTabs.SelectedIndex = 1;
    }
    private async void OnTabChanged(object? sender, EventArgs args)
    {
        PreparationHost.Content = Group is { } group ? group : ShowIntermediate ? (Page)_intermediate : _stock;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        Motion.Enter(PreparationHost, x: 6, milliseconds: 160);
        await LoadAsync();
    }
}

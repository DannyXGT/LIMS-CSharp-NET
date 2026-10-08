using Lims.Contracts.ReferencePreparations;
using Lims.Desktop.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Lims.Desktop.Views;

public sealed partial class IntermediatePage : Page
{
    private bool _dialogOpen;
    private readonly Lims.Desktop.Http.IStockApiClient? _stockApi;
    public IntermediatePage(IntermediateViewModel viewModel, Lims.Desktop.Http.IStockApiClient? stockApi = null)
    {
        ViewModel = viewModel;
        _stockApi = stockApi;
        InitializeComponent();
        // ListView can consume Enter internally; keep the detail action available to keyboard users.
        PreparationsList.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnListKeyDown), true);
    }
    public IntermediateViewModel ViewModel { get; }
    public IntermediateEditorDialog? Editor { get; private set; }
    public Task LoadAsync() => ViewModel.LoadAsync(SearchBox.Text, 1, CancellationToken.None);
    public async Task OpenCreateAsync()
    {
        if (_dialogOpen || !ViewModel.CanCreate) return;
        _dialogOpen = true;
        ViewModel.BeginNew();
        try
        {
            Editor = new IntermediateEditorDialog(ViewModel) { XamlRoot = XamlRoot, RequestedTheme = RequestedTheme };
            await Editor.ShowAsync();
            var viewDetail = Editor.ViewCreatedDetail;
            Editor = null;
            if (viewDetail && ViewModel.Created is { } created)
            {
                await ShowDetailAsync(created);
            }
        }
        finally { Editor = null; _dialogOpen = false; }
    }
    private async void OnNewClick(object sender, RoutedEventArgs e) => await OpenCreateAsync();
    private async void OnSearchClick(object sender, RoutedEventArgs e) => await LoadAsync();
    private async void OnSearchKeyDown(object sender, KeyRoutedEventArgs e) { if (e.Key == VirtualKey.Enter) { e.Handled = true; await LoadAsync(); } }
    private async void OnPreviousPage(object sender, RoutedEventArgs e) { if (ViewModel.Page > 1) await ViewModel.LoadAsync(SearchBox.Text, ViewModel.Page - 1, CancellationToken.None); }
    private async void OnNextPage(object sender, RoutedEventArgs e) { if (ViewModel.Page * 25 < ViewModel.TotalCount) await ViewModel.LoadAsync(SearchBox.Text, ViewModel.Page + 1, CancellationToken.None); }
    private async void OnDetailClick(object sender, RoutedEventArgs e) => await OpenDetailAsync();
    private async void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => await OpenDetailAsync();
    private async void OnListKeyDown(object sender, KeyRoutedEventArgs e) { if (e.Key == VirtualKey.Enter) { e.Handled = true; await OpenDetailAsync(); } }
    public async Task OpenDetailAsync()
    {
        if (_dialogOpen || PreparationsList.SelectedItem is not IntermediateSummary item) return;
        _dialogOpen = true;
        try
        {
            if (await ViewModel.OpenDetailAsync(item.Id, CancellationToken.None))
                await ShowDetailAsync(ViewModel.Detail!);
        }
        finally { _dialogOpen = false; }
    }

    public async Task ShowDetailAsync(IntermediateDetail detail)
    {
        var sheet = new IntermediateDetailDialog(detail) { XamlRoot = XamlRoot, RequestedTheme = RequestedTheme };
        await sheet.ShowAsync();
        if (sheet.RequestedSource is not { } source) return;
        if (source.Kind == "Intermedia" && await ViewModel.OpenDetailAsync(source.Id, CancellationToken.None))
            await ShowDetailAsync(ViewModel.Detail!);
        else if (source.Kind == "Stock" && _stockApi is not null)
        {
            var response = await _stockApi.GetAsync(source.Id, CancellationToken.None);
            if (response.IsSuccess && response.Value is { } stock)
                await new StockDetailDialog(stock) { XamlRoot = XamlRoot, RequestedTheme = RequestedTheme }.ShowAsync();
            else ViewModel.ListMessage = response.Error?.Message ?? "No se pudo abrir el origen.";
        }
    }
}

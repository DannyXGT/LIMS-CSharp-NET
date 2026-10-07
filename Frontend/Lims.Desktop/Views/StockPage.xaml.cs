using Lims.Contracts.ReferencePreparations;
using Lims.Desktop.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Lims.Desktop.Views;

public sealed partial class StockPage : Page
{
    private bool _dialogOpen;
    public StockPage(StockViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        // ListView can consume Enter internally; keep the detail action available to keyboard users.
        PreparationsList.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnListKeyDown), true);
    }
    public StockViewModel ViewModel { get; }
    public StockEditorDialog? Editor { get; private set; }
    public Task LoadAsync() => ViewModel.LoadAsync(SearchBox.Text, 1, CancellationToken.None);
    public async Task OpenCreateAsync()
    {
        if (_dialogOpen || !ViewModel.CanCreate) return;
        _dialogOpen = true;
        ViewModel.BeginNew();
        try
        {
            Editor = new StockEditorDialog(ViewModel) { XamlRoot = XamlRoot, RequestedTheme = RequestedTheme };
            await Editor.ShowAsync();
            var viewDetail = Editor.ViewCreatedDetail;
            Editor = null;
            if (viewDetail && ViewModel.Created is { } created)
            {
                var dialog = new StockDetailDialog(created) { XamlRoot = XamlRoot, RequestedTheme = RequestedTheme };
                await dialog.ShowAsync();
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
        if (_dialogOpen || PreparationsList.SelectedItem is not StockSummary item) return;
        _dialogOpen = true;
        try
        {
            if (await ViewModel.OpenDetailAsync(item.Id, CancellationToken.None))
                await new StockDetailDialog(ViewModel.Detail!) { XamlRoot = XamlRoot, RequestedTheme = RequestedTheme }.ShowAsync();
        }
        finally { _dialogOpen = false; }
    }
}

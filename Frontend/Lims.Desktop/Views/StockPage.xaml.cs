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
    private bool _resetting;
    private readonly DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    public StockPage(StockViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        _searchTimer.Tick += async (_, _) => { _searchTimer.Stop(); await SearchAsync(); };
        Unloaded += (_, _) => _searchTimer.Stop();
        ViewModel.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ViewModel.IsEmpty) && ViewModel.IsEmpty && IsLoaded) Lims.DesignSystem.Presentation.Motion.Enter(EmptyState, y: 4, milliseconds: 160); };
        ViewModel.PreparationOpened += ShowSelectedDetailAsync;
        // ListView can consume Enter internally; keep the detail action available to keyboard users.
        PreparationsList.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnListKeyDown), true);
    }
    public StockViewModel ViewModel { get; }
    public StockEditorDialog? Editor { get; private set; }
    public StockDetailDialog? DetailSheet { get; private set; }
    public Task LoadAsync() => ViewModel.LoadAsync(SearchBox.Text, ViewModel.HasLoadedList ? ViewModel.Page : 1, CancellationToken.None);
    private Task SearchAsync()
    {
        if (_resetting || SearchBox is null || StatusFilter is null || AvailabilityFilter is null || MethodFilter is null) return Task.CompletedTask;
        ViewModel.ListFilter = new((StatusFilter.SelectedItem as ComboBoxItem)?.Tag as string,
            (AvailabilityFilter.SelectedItem as ComboBoxItem)?.Tag as string, MethodFilter.Text);
        return ViewModel.LoadAsync(SearchBox.Text, 1, CancellationToken.None);
    }
    private void OnSearchChanged(object sender, TextChangedEventArgs e) { if (_resetting || !IsLoaded) return; _searchTimer.Stop(); _searchTimer.Start(); }
    private async void OnFilterChanged(object sender, SelectionChangedEventArgs e) { if (IsLoaded && !_resetting) { _searchTimer.Stop(); await SearchAsync(); } }
    private async void OnRetry(object sender, RoutedEventArgs e) => await SearchAsync();
    private void OnTableViewportSizeChanged(object sender, SizeChangedEventArgs e) => TableCanvas.Width = Math.Max(TableCanvas.MinWidth, e.NewSize.Width);
    private void OnPageSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (FilterGrid is null) return;
        var narrow = e.NewSize.Width < 940;
        Grid.SetRow(Filters, narrow ? 1 : 0); Grid.SetColumn(Filters, narrow ? 0 : 1);
        Grid.SetColumnSpan(Filters, narrow ? 2 : 1); Grid.SetColumnSpan(SearchField, narrow ? 2 : 1);
    }
    public async Task OpenCreateAsync()
    {
        if (_dialogOpen || !ViewModel.CanCreate) return;
        _dialogOpen = true;
        ViewModel.BeginNew();
        try
        {
            Editor = new StockEditorDialog(ViewModel) { XamlRoot = XamlRoot, RequestedTheme = ActualTheme };
            await Editor.ShowAsync();
            var viewDetail = Editor.ViewCreatedDetail;
            Editor = null;
            if (viewDetail && ViewModel.Created is { } created)
            {
                var dialog = new StockDetailDialog(created) { XamlRoot = XamlRoot, RequestedTheme = ActualTheme };
                await dialog.ShowAsync();
            }
        }
        finally { Editor = null; _dialogOpen = false; }
    }
    private async void OnNewClick(object sender, RoutedEventArgs e) => await OpenCreateAsync();
    private async void OnClearSearch(object sender, RoutedEventArgs e)
    {
        _searchTimer.Stop(); _resetting = true;
        SearchBox.Text = MethodFilter.Text = string.Empty; StatusFilter.SelectedIndex = AvailabilityFilter.SelectedIndex = 0;
        _resetting = false; await SearchAsync();
    }
    private async void OnSearchKeyDown(object sender, KeyRoutedEventArgs e) { if (e.Key == VirtualKey.Enter) { e.Handled = true; _searchTimer.Stop(); await SearchAsync(); } }
    private async void OnPreviousPage(object sender, RoutedEventArgs e) { if (ViewModel.Page > 1) await ViewModel.LoadAsync(SearchBox.Text, ViewModel.Page - 1, CancellationToken.None); }
    private async void OnNextPage(object sender, RoutedEventArgs e) { if (ViewModel.HasNextPage) await ViewModel.LoadAsync(SearchBox.Text, ViewModel.Page + 1, CancellationToken.None); }
    private async void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (!SelectRow(e.OriginalSource as DependencyObject)) return;
        e.Handled = true;
        await OpenDetailAsync();
    }
    private bool SelectRow(DependencyObject? source)
    {
        while (source is not null && source != PreparationsList && source is not ListViewItem)
            source = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(source);
        if (source is not ListViewItem row) return false;
        PreparationsList.SelectedItem = PreparationsList.ItemFromContainer(row);
        return true;
    }
    private async void OnListKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        SelectRow(e.OriginalSource as DependencyObject);
        e.Handled = true;
        await OpenDetailAsync();
    }
    public Task OpenDetailAsync() => ViewModel.OpenSelectedPreparationCommand.CanExecute(null)
        ? ViewModel.OpenSelectedPreparationCommand.ExecuteAsync(null) : Task.CompletedTask;
    private async Task ShowSelectedDetailAsync(StockDetail detail)
    {
        if (_dialogOpen || !IsLoaded) return;
        _dialogOpen = true;
        try
        {
            DetailSheet = new StockDetailDialog(detail) { XamlRoot = XamlRoot, RequestedTheme = ActualTheme };
            await DetailSheet.ShowAsync();
        }
        finally { DetailSheet = null; _dialogOpen = false; }
    }

}

using Lims.Desktop.Http;
using Lims.Contracts.ReferencePreparations;
using Lims.Desktop.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Lims.Desktop.Views;

public sealed partial class IntermediatePage : Page
{
    private readonly ICommercialPreparationApiClient? _commercialApi;
    public CommercialPreparationDetailDialog? CommercialDetail { get; private set; }
    private bool _dialogOpen;
    private bool _resetting;
    private readonly DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly Lims.Desktop.Http.IStockApiClient? _stockApi;
    public IntermediatePage(IntermediateViewModel viewModel, Lims.Desktop.Http.IStockApiClient? stockApi = null, ICommercialPreparationApiClient? commercialApi = null)
    {
        _commercialApi = commercialApi;
        ViewModel = viewModel;
        _stockApi = stockApi;
        InitializeComponent();
        _searchTimer.Tick += async (_, _) => { _searchTimer.Stop(); await SearchAsync(); };
        Unloaded += (_, _) => _searchTimer.Stop();
        ViewModel.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ViewModel.IsEmpty) && ViewModel.IsEmpty && IsLoaded) Lims.DesignSystem.Presentation.Motion.Enter(EmptyState, y: 4, milliseconds: 160); };
        ViewModel.PreparationOpened += ShowSelectedDetailAsync;
        ViewModel.CommercialOpened += OpenCommercialAsync;
        // ListView can consume Enter internally; keep the detail action available to keyboard users.
        PreparationsList.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnListKeyDown), true);
    }
    public IntermediateViewModel ViewModel { get; }
    public IntermediateEditorDialog? Editor { get; private set; }
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
            Editor = new IntermediateEditorDialog(ViewModel, _commercialApi) { XamlRoot = XamlRoot, RequestedTheme = ActualTheme };
            await Editor.ShowAsync();
            var commercial = Editor.CommercialCreated;
            var viewDetail = Editor.ViewCreatedDetail;
            Editor = null;
            if (commercial is not null) { await LoadAsync(); await ShowCommercialAsync(commercial); }
            if (viewDetail && ViewModel.Created is { } created)
            {
                await ShowDetailAsync(created);
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
    private async Task ShowSelectedDetailAsync(IntermediateDetail detail)
    {
        if (_dialogOpen || !IsLoaded) return;
        _dialogOpen = true;
        try
        {
            await ShowDetailAsync(detail);
        }
        finally { _dialogOpen = false; }
    }

    public async Task ShowDetailAsync(IntermediateDetail detail)
    {
        var sheet = new IntermediateDetailDialog(detail) { XamlRoot = XamlRoot, RequestedTheme = ActualTheme };
        await sheet.ShowAsync();
        if (sheet.RequestedSource is not { } source) return;
        if (Lims.Contracts.ReferenceMaterials.MaterialPresentations.IsCommercial(source.Kind) && _commercialApi is not null)
        {
            var response = await _commercialApi.GetAsync("Intermedia", source.Id, CancellationToken.None);
            if (!response.IsSuccess && response.StatusCode == 404) response = await _commercialApi.GetAsync("Stock", source.Id, CancellationToken.None);
            if (response.IsSuccess && response.Value is { } commercial) await ShowCommercialAsync(commercial);
            else ViewModel.ListMessage = response.Error?.Message ?? "No se pudo abrir el origen comercial.";
        }
        else if (source.Kind == "Intermedia" && await ViewModel.OpenDetailAsync(source.Id, CancellationToken.None))
            await ShowDetailAsync(ViewModel.Detail!);
        else if (source.Kind == "Stock" && _stockApi is not null)
        {
            var response = await _stockApi.GetAsync(source.Id, CancellationToken.None);
            if (response.IsSuccess && response.Value is { } stock)
                await new StockDetailDialog(stock) { XamlRoot = XamlRoot, RequestedTheme = ActualTheme }.ShowAsync();
            else ViewModel.ListMessage = response.Error?.Message ?? "No se pudo abrir el origen.";
        }
    }
    private async Task OpenCommercialAsync(Guid id)
    {
        if (_dialogOpen || !IsLoaded || _commercialApi is null) return;
        _dialogOpen = true;
        try
        {
            var response = await _commercialApi.GetAsync("Intermedia", id, CancellationToken.None);
            if (response.IsSuccess && response.Value is { } detail) await ShowCommercialAsync(detail);
            else ViewModel.ListMessage = response.Error?.Message ?? "No se pudo abrir el ingreso comercial.";
        }
        finally { _dialogOpen = false; }
    }
    private async Task ShowCommercialAsync(CommercialPreparationDetail detail)
    {
        try
        {
            CommercialDetail = new CommercialPreparationDetailDialog(detail) { XamlRoot = XamlRoot, RequestedTheme = ActualTheme };
            await CommercialDetail.ShowAsync();
        }
        finally { CommercialDetail = null; }
    }
}

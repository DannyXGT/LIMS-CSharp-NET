using Lims.Contracts.ReferenceMaterials;
using Lims.Desktop.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Lims.DesignSystem.Presentation;
using Lims.DesignSystem.Controls;
using Lims.Desktop.Presentation;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Automation;
using Windows.System;

namespace Lims.Desktop.Views;

public sealed partial class ReferenceMaterialsPage : Page
{
    private bool _loaded;
    private long _loadingVersion;
    private bool _dialogOpen;
    private bool _openingDetail;
    public ReferenceMaterialDetailDialog? DetailDialog { get; private set; }

    public ReferenceMaterialsPage(ReferenceMaterialsViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        // ListView consumes Enter internally; observe handled key events as well.
        MaterialsList.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnMaterialsKeyDown), true);
        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(ReferenceMaterialsViewModel.IsBusy) or nameof(ReferenceMaterialsViewModel.IsCatalogsLoading)) UpdateLoadingIndicator();
        };
        Unloaded += (_, _) => { _loadingVersion++; LimsPopupField.CloseForRoot(XamlRoot); DetailDialog?.CloseImmediately(); };
    }

    public ReferenceMaterialsViewModel ViewModel { get; }

    public Task LoadAsync() => ViewModel.LoadAsync();

    public async Task OpenCreateDialogAsync()
    {
        if (!ViewModel.CanCreate || !await EnsureCatalogsAsync())
        {
            return;
        }

        var dialog = new ReferenceMaterialEditorDialog(
            ViewModel.Methods,
            ViewModel.Units,
            ViewModel.Locations) { XamlRoot = XamlRoot };
        dialog.SaveAsync = async () =>
        {
            var saved = await ViewModel.CreateAsync(dialog.CreateRequest(), CancellationToken.None);

            return saved ? null : ViewModel.LastSaveFailure;
        };
        await ShowEditorAsync(dialog);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        await Task.WhenAll(ViewModel.LoadAsync(), ViewModel.LoadCatalogsAsync());
    }

    private async void OnSearchSubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args) =>
        await ViewModel.ApplyFiltersAsync(CancellationToken.None);

    private async void OnRefreshClick(object sender, RoutedEventArgs e) =>
        await Task.WhenAll(ViewModel.LoadAsync(), ViewModel.LoadCatalogsAsync());

    private async void OnFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (StatusCombo.SelectedItem is ComboBoxItem item)
        {
            ViewModel.StatusFilter = item.Tag as string;
            if (_loaded)
            {
                await ViewModel.ApplyFiltersAsync(CancellationToken.None);
            }
        }
    }

    private void OnMethodChanged(object? sender, EventArgs e) =>
        ViewModel.MethodFilter = (MethodBox.SelectedItem as ReferenceMethodOption)?.Name;

    private async void OnApplyFiltersClick(object sender, RoutedEventArgs e) =>
        await ViewModel.ApplyFiltersAsync(CancellationToken.None);

    private async void OnClearFiltersClick(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = string.Empty;
        MethodBox.SelectedItem = null;
        StatusCombo.SelectedIndex = 0;
        ViewModel.SearchText = string.Empty;
        ViewModel.MethodFilter = null;
        ViewModel.StatusFilter = null;
        await ViewModel.ApplyFiltersAsync(CancellationToken.None);
    }

    private async void OnPreviousPageClick(object sender, RoutedEventArgs e) =>
        await ViewModel.GoToPreviousPageAsync(CancellationToken.None);

    private async void OnNextPageClick(object sender, RoutedEventArgs e) =>
        await ViewModel.GoToNextPageAsync(CancellationToken.None);

    private void OnMaterialSelected(object sender, SelectionChangedEventArgs e)
    {
        // Selection stays local until the user explicitly opens the technical sheet.
        UpdateSelectedMarkers();
        if (MaterialsList.SelectedItem is not ReferenceMaterialSummary row) return;
        ViewModel.SelectedMaterial = row;
    }

    private async void OnRowDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ReferenceMaterialSummary row) return;
        e.Handled = true;
        MaterialsList.SelectedItem = row;
        await OpenSelectedDetailAsync();
    }

    private async void OnMaterialsKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter || MaterialsList.SelectedItem is null) return;
        e.Handled = true;
        await OpenSelectedDetailAsync();
    }

    public async Task OpenSelectedDetailAsync()
    {
        if (_openingDetail || DetailDialog is not null || _dialogOpen || MaterialsList.SelectedItem is not ReferenceMaterialSummary row) return;
        _openingDetail = true;
        try
        {
            await ViewModel.SelectAsync(row, CancellationToken.None);
            if (ViewModel.SelectedDetail?.Id != row.Id || MaterialsList.SelectedItem is not ReferenceMaterialSummary current || current.Id != row.Id || !IsLoaded) return;
            LimsPopupField.CloseForRoot(XamlRoot);
            var detail = new ReferenceMaterialDetailDialog(ViewModel) { RequestedTheme = ActualTheme };
            DetailDialog = detail;
            detail.EditAsync = EditSelectedAsync;
            detail.ArchiveAsync = ArchiveSelectedAsync;
            detail.ReplaceAsync = ReplaceSelectedAsync;
            await detail.ShowAsync(XamlRoot);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            ViewModel.Message = "No se pudo cargar la ficha. Intente nuevamente.";
        }
        finally
        {
            DetailDialog = null;
            _openingDetail = false;
            if (IsLoaded && MaterialsList.SelectedItem is { } selected && MaterialsList.ContainerFromItem(selected) is ListViewItem item)
                item.Focus(FocusState.Keyboard);
        }
    }

    private void OnRowContainerChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (!args.InRecycleQueue && args.Item is ReferenceMaterialSummary row)
            AutomationProperties.SetName(args.ItemContainer, ReferenceMaterialVisuals.RowName(row));
        UpdateSelectedMarkers();
    }

    private void UpdateSelectedMarkers()
    {
        foreach (var row in ViewModel.Items)
            if (MaterialsList.ContainerFromItem(row) is ListViewItem { ContentTemplateRoot: Grid grid } item && grid.FindName("RowSelectionMarker") is Border marker)
                marker.Opacity = item.IsSelected ? 1 : 0;
    }

    private void OnRowLoaded(object sender, RoutedEventArgs e)
    {
        UpdateRowAvailability((Grid)sender);
        UpdateSelectedMarkers();
    }
    private void OnRowSizeChanged(object sender, SizeChangedEventArgs e) => UpdateRowAvailability((Grid)sender);
    private static void UpdateRowAvailability(Grid grid)
    {
        if (grid.DataContext is not ReferenceMaterialSummary row || grid.FindName("RowAvailability") is not Grid availability) return;
        var percent = ReferenceMaterialVisuals.Availability(row.AvailableQuantity, row.TotalQuantity);
        availability.Visibility = grid.ActualWidth >= 1150 && percent.HasValue ? Visibility.Visible : Visibility.Collapsed;
        ((ProgressBar)grid.FindName("RowProgress")).Value = percent ?? 0;
        ((TextBlock)grid.FindName("RowPercent")).Text = percent.HasValue ? $"{ReferenceMaterialPresentation.Number(decimal.Round((decimal)percent.Value, 2))} %" : string.Empty;
    }

    private async void OnCreateClick(object sender, RoutedEventArgs e) => await OpenCreateDialogAsync();

    private async Task EditSelectedAsync()
    {
        if (!ViewModel.CanEditSelected || ViewModel.SelectedDetail is null)
        {
            return;
        }

        if (!await EnsureCatalogsAsync())
        {
            return;
        }

        var selected = ViewModel.SelectedDetail;
        var dialog = new ReferenceMaterialEditorDialog(
            selected,
            ViewModel.Methods,
            ViewModel.Units,
            ViewModel.Locations) { XamlRoot = XamlRoot };
        dialog.SaveAsync = async () =>
        {
            var saved = await ViewModel.UpdateAsync(dialog.UpdateRequest(selected.Version), CancellationToken.None);

            return saved ? null : ViewModel.LastSaveFailure;
        };
        await ShowEditorAsync(dialog);
    }

    private async Task ArchiveSelectedAsync()
    {
        if (!ViewModel.CanArchiveSelected)
        {
            return;
        }
        await ShowReasonDialogAsync("Archivar estándar", "Archivar",
            "El estándar quedará archivado y conservará su historial.",
            reason => ViewModel.ArchiveAsync(reason, CancellationToken.None));
    }

    private async Task ReplaceSelectedAsync()
    {
        if (!ViewModel.CanReplaceSelected || ViewModel.SelectedDetail is null)
        {
            return;
        }

        if (!await EnsureCatalogsAsync())
        {
            return;
        }

        var source = ViewModel.SelectedDetail;
        var editor = new ReferenceMaterialEditorDialog(
            source,
            isReplacement: true,
            ViewModel.Methods,
            ViewModel.Units,
            ViewModel.Locations) { XamlRoot = XamlRoot };
        await ShowEditorAsync(editor);
        if (!editor.WasAccepted)
        {
            return;
        }

        await ShowReasonDialogAsync("Confirmar reemplazo", "Reemplazar",
            "Se creará el nuevo estándar y el anterior quedará identificado como reemplazado.",
            reason => ViewModel.ReplaceAsync(editor.CreateRequest(), reason, CancellationToken.None));
    }

    private async Task ShowReasonDialogAsync(string title, string action, string description, Func<string, Task<bool>> saveAsync)
    {
        if (_dialogOpen) return;
        var reason = new TextBox
        {
            Header = "Motivo",
            PlaceholderText = "Indique el motivo de esta acción",
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            MaxLength = 500,
            MinHeight = 100,
            Style = (Style)Application.Current.Resources["LimsCompactTextBoxStyle"],
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(reason, "Motivo de la acción");
        var error = new InfoBar { IsOpen = false, IsClosable = false, Severity = InfoBarSeverity.Error };
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap });
        content.Children.Add(reason);
        content.Children.Add(error);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = ActualTheme,
            Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
            CornerRadius = new CornerRadius(12),
            Title = title,
            Content = content,
            PrimaryButtonText = action,
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.None,
            PrimaryButtonStyle = (Style)Application.Current.Resources[
                action == "Archivar" ? "LimsDangerButtonStyle" : "LimsPrimaryButtonStyle"],
            CloseButtonStyle = (Style)Application.Current.Resources["LimsStandardsSecondaryButtonStyle"],
        };
        var saving = false;
        dialog.Closing += (_, args) => args.Cancel = saving;
        dialog.PrimaryButtonClick += async (sender, args) =>
        {
            if (string.IsNullOrWhiteSpace(reason.Text))
            {
                args.Cancel = true;
                error.Message = "Indique el motivo antes de continuar.";
                error.IsOpen = true;
                reason.Focus(FocusState.Programmatic);
                return;
            }
            var deferral = args.GetDeferral();
            saving = true;
            sender.IsPrimaryButtonEnabled = false;
            reason.IsEnabled = false;
            error.IsOpen = false;
            try
            {
                args.Cancel = !await saveAsync(reason.Text.Trim());
                if (args.Cancel)
                {
                    error.Message = ViewModel.Message;
                    error.IsOpen = true;
                }

            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                args.Cancel = true;
                error.Message = "No se pudo conectar con el servicio LIMS. Intente nuevamente.";
                error.IsOpen = true;
            }
            finally
            {
                sender.IsPrimaryButtonEnabled = true;
                reason.IsEnabled = true;
                saving = false;
                deferral.Complete();
            }
        };
        _dialogOpen = true;
        try { LimsPopupField.CloseForRoot(XamlRoot); await dialog.ShowAsync(); }
        finally { _dialogOpen = false; }
    }

    private async Task<bool> EnsureCatalogsAsync()
    {
        if (!ViewModel.CatalogsReady)
        {
            await ViewModel.LoadCatalogsAsync();
        }

        return ViewModel.CatalogsReady;
    }

    private async Task ShowEditorAsync(ReferenceMaterialEditorDialog dialog)
    {
        if (_dialogOpen) return;
        _dialogOpen = true;
        dialog.RequestedTheme = ActualTheme;
        try { LimsPopupField.CloseForRoot(XamlRoot); await dialog.ShowAsync(); }
        finally { _dialogOpen = false; }
    }

    private async void UpdateLoadingIndicator()
    {
        var version = ++_loadingVersion;
        LoadingPlaceholder.Visibility = Visibility.Collapsed;
        DelayedProgress.Visibility = Visibility.Collapsed;
        CatalogLoadingBar.IsOpen = false;
        if (!ViewModel.IsBusy && !ViewModel.IsCatalogsLoading) return;
        await Task.Delay(200);
        if (version != _loadingVersion) return;
        CatalogLoadingBar.IsOpen = ViewModel.IsCatalogsLoading;
        if (ViewModel.IsBusy)
        {
            if (ViewModel.Items.Count == 0)
            {
                LoadingPlaceholder.Visibility = Visibility.Visible;
                Motion.Enter(LoadingPlaceholder, milliseconds: 100);
            }
            else DelayedProgress.Visibility = Visibility.Visible;
        }
    }

    private void OnPageSizeChanged(object sender, SizeChangedEventArgs e) => UpdateFilterLayout(e.NewSize.Width < 800);

    private void OnTableSizeChanged(object sender, SizeChangedEventArgs e) => TableGrid.Width = Math.Max(1080, e.NewSize.Width - 2);

    private void UpdateFilterLayout(bool useCompactLayout)
    {
        if (!useCompactLayout)
        {
            FilterGrid.RowSpacing = 0;
            SearchFilterColumn.Width = new GridLength(2, GridUnitType.Star);
            SearchFilterColumn.MinWidth = 240;
            StatusFilterColumn.Width = new GridLength(150);
            MethodFilterColumn.Width = new GridLength(170);
            ApplyFilterColumn.Width = GridLength.Auto;
            ClearFilterColumn.Width = GridLength.Auto;
            Grid.SetRow(SearchBox, 0);
            Grid.SetColumn(SearchBox, 0);
            Grid.SetColumnSpan(SearchBox, 1);
            Grid.SetRow(StatusCombo, 0);
            Grid.SetColumn(StatusCombo, 1);
            Grid.SetRow(MethodFilterField, 0);
            Grid.SetColumn(MethodFilterField, 2);
            Grid.SetRow(ApplyFiltersButton, 0);
            Grid.SetColumn(ApplyFiltersButton, 3);
            Grid.SetRow(ClearFiltersButton, 0);
            Grid.SetColumn(ClearFiltersButton, 4);
            return;
        }

        FilterGrid.RowSpacing = 10;
        SearchFilterColumn.Width = new GridLength(150);
        SearchFilterColumn.MinWidth = 0;
        StatusFilterColumn.Width = new GridLength(1, GridUnitType.Star);
        MethodFilterColumn.Width = GridLength.Auto;
        ApplyFilterColumn.Width = GridLength.Auto;
        ClearFilterColumn.Width = new GridLength(0);
        Grid.SetRow(SearchBox, 0);
        Grid.SetColumn(SearchBox, 0);
        Grid.SetColumnSpan(SearchBox, 5);
        Grid.SetRow(StatusCombo, 1);
        Grid.SetColumn(StatusCombo, 0);
        Grid.SetRow(MethodFilterField, 1);
        Grid.SetColumn(MethodFilterField, 1);
        Grid.SetRow(ApplyFiltersButton, 1);
        Grid.SetColumn(ApplyFiltersButton, 2);
        Grid.SetRow(ClearFiltersButton, 1);
        Grid.SetColumn(ClearFiltersButton, 3);
    }
}

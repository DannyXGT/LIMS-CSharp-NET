using Lims.Contracts.ReferenceMaterials;
using Lims.Desktop.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lims.Desktop.Views;

public sealed partial class ReferenceMaterialsPage : Page
{
    private bool _loaded;
    private bool _useSideBySideLayout = true;

    public ReferenceMaterialsPage(ReferenceMaterialsViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ReferenceMaterialsViewModel.SelectedDetail))
            {
                UpdateDetailVisibility();
            }
        };
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
            UpdateDetailVisibility();
            return saved ? null : ViewModel.LastSaveFailure;
        };
        await dialog.ShowAsync();
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

    private void OnMethodChanged(object sender, TextChangedEventArgs e) => ViewModel.MethodFilter = MethodBox.Text;

    private async void OnApplyFiltersClick(object sender, RoutedEventArgs e) =>
        await ViewModel.ApplyFiltersAsync(CancellationToken.None);

    private async void OnClearFiltersClick(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = string.Empty;
        MethodBox.Text = string.Empty;
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

    private async void OnMaterialSelected(object sender, SelectionChangedEventArgs e)
    {
        await ViewModel.SelectAsync(MaterialsList.SelectedItem as ReferenceMaterialSummary, CancellationToken.None);
        UpdateDetailVisibility();
    }

    private async void OnCreateClick(object sender, RoutedEventArgs e) => await OpenCreateDialogAsync();

    private async void OnEditClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedDetail is null)
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
            UpdateDetailVisibility();
            return saved ? null : ViewModel.LastSaveFailure;
        };
        await dialog.ShowAsync();
    }

    private async void OnArchiveClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanArchiveSelected)
        {
            return;
        }
        await ShowReasonDialogAsync("Archivar estándar", "Archivar",
            "El estándar quedará archivado y conservará su historial.",
            reason => ViewModel.ArchiveAsync(reason, CancellationToken.None));
    }

    private async void OnReplaceClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedDetail is null)
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
        await editor.ShowAsync();
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
        var reason = new TextBox
        {
            Header = "Motivo",
            PlaceholderText = "Indique el motivo de esta acción",
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            MaxLength = 500,
            MinHeight = 100,
        };
        var error = new InfoBar { IsOpen = false, IsClosable = false, Severity = InfoBarSeverity.Error };
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap });
        content.Children.Add(reason);
        content.Children.Add(error);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = content,
            PrimaryButtonText = action,
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Close,
        };
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
                UpdateDetailVisibility();
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
                deferral.Complete();
            }
        };
        await dialog.ShowAsync();
    }

    private async Task<bool> EnsureCatalogsAsync()
    {
        if (!ViewModel.CatalogsReady)
        {
            await ViewModel.LoadCatalogsAsync();
        }

        return ViewModel.CatalogsReady;
    }

    private void UpdateDetailVisibility()
    {
        var hasDetail = ViewModel.SelectedDetail is not null;
        DetailPanel.Visibility = hasDetail ? Visibility.Visible : Visibility.Collapsed;
        NoSelectionPanel.Visibility = hasDetail ? Visibility.Collapsed : Visibility.Visible;

        if (_useSideBySideLayout)
        {
            DetailBorder.Visibility = Visibility.Visible;
            WorkspaceTableRow.Height = new GridLength(1, GridUnitType.Star);
            WorkspaceDetailRow.Height = new GridLength(0);
            return;
        }

        DetailBorder.Visibility = hasDetail ? Visibility.Visible : Visibility.Collapsed;
        WorkspaceTableRow.Height = hasDetail
            ? new GridLength(3, GridUnitType.Star)
            : new GridLength(1, GridUnitType.Star);
        WorkspaceDetailRow.Height = hasDetail
            ? new GridLength(2, GridUnitType.Star)
            : new GridLength(0);
    }

    private void OnPageSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateFilterLayout(e.NewSize.Width < 800);

        _useSideBySideLayout = e.NewSize.Width >= 960;
        if (_useSideBySideLayout)
        {
            WorkspaceGrid.ColumnSpacing = 14;
            WorkspaceGrid.RowSpacing = 0;
            WorkspaceTableColumn.Width = new GridLength(7, GridUnitType.Star);
            WorkspaceDetailColumn.Width = new GridLength(3, GridUnitType.Star);
            WorkspaceTableRow.Height = new GridLength(1, GridUnitType.Star);
            WorkspaceDetailRow.Height = new GridLength(0);
            Grid.SetRow(TableBorder, 0);
            Grid.SetColumn(TableBorder, 0);
            Grid.SetRowSpan(TableBorder, 1);
            Grid.SetRow(DetailBorder, 0);
            Grid.SetColumn(DetailBorder, 1);
            Grid.SetRowSpan(DetailBorder, 1);
            UpdateDetailVisibility();
            return;
        }

        WorkspaceGrid.ColumnSpacing = 0;
        WorkspaceGrid.RowSpacing = 12;
        WorkspaceTableColumn.Width = new GridLength(1, GridUnitType.Star);
        WorkspaceDetailColumn.Width = new GridLength(0);
        Grid.SetRow(TableBorder, 0);
        Grid.SetColumn(TableBorder, 0);
        Grid.SetRowSpan(TableBorder, 1);
        Grid.SetRow(DetailBorder, 1);
        Grid.SetColumn(DetailBorder, 0);
        Grid.SetRowSpan(DetailBorder, 1);
        UpdateDetailVisibility();
    }

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
            Grid.SetRow(MethodBox, 0);
            Grid.SetColumn(MethodBox, 2);
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
        Grid.SetRow(MethodBox, 1);
        Grid.SetColumn(MethodBox, 1);
        Grid.SetRow(ApplyFiltersButton, 1);
        Grid.SetColumn(ApplyFiltersButton, 2);
        Grid.SetRow(ClearFiltersButton, 1);
        Grid.SetColumn(ClearFiltersButton, 3);
    }
}

using Lims.Contracts.ReferenceMaterials;
using Lims.Desktop.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lims.Desktop.Views;

public sealed partial class ReferenceMaterialsPage : Page
{
    private bool _loaded;

    public ReferenceMaterialsPage(ReferenceMaterialsViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    public ReferenceMaterialsViewModel ViewModel { get; }

    public Task LoadAsync() => ViewModel.LoadAsync();

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        await ViewModel.LoadAsync();
    }

    private async void OnSearchSubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args) =>
        await ViewModel.ApplyFiltersAsync(CancellationToken.None);

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await ViewModel.LoadAsync();

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
        var hasDetail = ViewModel.SelectedDetail is not null;
        DetailPanel.Visibility = hasDetail ? Visibility.Visible : Visibility.Collapsed;
        NoSelectionPanel.Visibility = hasDetail ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void OnCreateClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ReferenceMaterialEditorDialog { XamlRoot = XamlRoot };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.CreateAsync(dialog.CreateRequest(), CancellationToken.None);
        }
    }

    private async void OnEditClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedDetail is null)
        {
            return;
        }

        var dialog = new ReferenceMaterialEditorDialog(ViewModel.SelectedDetail) { XamlRoot = XamlRoot };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.UpdateAsync(dialog.UpdateRequest(ViewModel.SelectedDetail.Version), CancellationToken.None);
        }
    }

    private async void OnArchiveClick(object sender, RoutedEventArgs e)
    {
        var reason = new TextBox { PlaceholderText = "Motivo obligatorio", TextWrapping = TextWrapping.Wrap };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Archivar estándar",
            Content = reason,
            PrimaryButtonText = "Archivar",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.ArchiveAsync(reason.Text, CancellationToken.None);
        }
    }

    private async void OnReplaceClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedDetail is null)
        {
            return;
        }

        var source = ViewModel.SelectedDetail;
        var editor = new ReferenceMaterialEditorDialog(source, isReplacement: true) { XamlRoot = XamlRoot };
        if (await editor.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var reason = new TextBox
        {
            PlaceholderText = "Ej. cambio de lote o certificado",
            TextWrapping = TextWrapping.Wrap,
        };
        var confirmation = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Confirmar reemplazo",
            Content = reason,
            PrimaryButtonText = "Reemplazar",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await confirmation.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.ReplaceAsync(editor.CreateRequest(), reason.Text, CancellationToken.None);
        }
    }
}

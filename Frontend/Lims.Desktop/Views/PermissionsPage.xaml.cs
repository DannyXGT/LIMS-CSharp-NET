using Lims.Contracts.Permissions;
using Lims.Desktop.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lims.Desktop.Views;

public sealed partial class PermissionsPage : Page
{
    private bool _restoring;
    public PermissionsPage(PermissionsViewModel viewModel) { ViewModel = viewModel; InitializeComponent(); }
    public PermissionsViewModel ViewModel { get; }
    public Task LoadAsync() => ViewModel.LoadUsersAsync();
    public async Task<bool> ConfirmDiscardAsync()
    {
        if (ViewModel.IsBusy) { ViewModel.Message = "Espere a que termine la operación en curso."; return false; }
        if (!ViewModel.HasPendingChanges) return true;
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Cambios sin guardar", Content = "Hay cambios pendientes. ¿Desea descartarlos?", PrimaryButtonText = "Descartar cambios", CloseButtonText = "Seguir editando", DefaultButton = ContentDialogButton.Close };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
    private async void OnUserChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_restoring || UsersList.SelectedItem is not PermissionUser user || user.Id == ViewModel.SelectedUser?.Id) return;
        var previous = ViewModel.SelectedUser;
        if (ViewModel.IsBusy || !await ConfirmDiscardAsync())
        { _restoring = true; UsersList.SelectedItem = previous; _restoring = false; return; }
        await ViewModel.SelectAsync(user, true);
    }
    private async void OnSearch(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (ViewModel.IsBusy) return;
        if (!await ConfirmDiscardAsync()) return;
        ViewModel.Clear(); await LoadAsync();
    }
    private async void OnSave(object sender, RoutedEventArgs e) => await ViewModel.SaveAsync();
    private async void OnReload(object sender, RoutedEventArgs e)
    { if (ViewModel.SelectedUser is { } user && await ConfirmDiscardAsync()) await ViewModel.SelectAsync(user, true); }
    private async void OnPreviousHistory(object sender, RoutedEventArgs e)
    { if (ViewModel.HistoryPage <= 1) return; ViewModel.HistoryPage--; await ViewModel.LoadHistoryAsync(); }
    private async void OnNextHistory(object sender, RoutedEventArgs e)
    { if (!ViewModel.HasMoreHistory) return; ViewModel.HistoryPage++; await ViewModel.LoadHistoryAsync(); }
}

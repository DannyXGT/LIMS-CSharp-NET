using Lims.Contracts.Permissions;
using Lims.Desktop.ViewModels;
using Lims.Desktop.Presentation;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lims.Desktop.Views;

public sealed partial class PermissionsPage : Page
{
    private bool _restoring;
    private bool _ready;
    public PermissionsPage(PermissionsViewModel viewModel)
    {
        ViewModel = viewModel; InitializeComponent(); _ready = true;
        foreach (var button in new[] { RefreshUsersButton, ReloadButton, DiscardButton, SaveButton, PreviousHistoryButton, NextHistoryButton }) Lims.DesignSystem.Presentation.Motion.SetPressedScale(button, 0.98f);
        ViewModel.PropertyChanged += OnModelChanged;
        Loaded += (_, _) => RefreshVisualState();
    }
    public PermissionsViewModel ViewModel { get; }
    public Task LoadAsync() => ViewModel.LoadUsersAsync();
    public async Task<bool> ConfirmDiscardAsync()
    {
        if (ViewModel.IsBusy) { ViewModel.Message = "Espere a que termine la operación en curso."; return false; }
        if (!ViewModel.HasPendingChanges) return true;
        var dialog = Dialog("Cambios sin guardar", "Hay cambios pendientes. ¿Desea descartarlos?", "Descartar cambios", "Seguir editando");
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
    private async void OnUserChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_restoring || UsersList.SelectedItem is not PermissionUserRow row || row.User.Id == ViewModel.SelectedUser?.Id) return;
        var user = row.User;
        var previous = ViewModel.SelectedUser;
        if (ViewModel.IsBusy || !await ConfirmDiscardAsync())
        { _restoring = true; UsersList.SelectedItem = ViewModel.UserRows.FirstOrDefault(r => r.User.Id == previous?.Id); _restoring = false; return; }
        PermissionTabs.SelectedIndex = 0;
        await ViewModel.SelectAsync(user, true);
    }
    private async void OnSearch(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (ViewModel.IsBusy) return;
        if (!await ConfirmDiscardAsync()) return;
        ViewModel.Clear(); await LoadAsync();
    }
    private async void OnSave(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanSave) return;
        if (ViewModel.RequiresEmptyConfirmation && !await ConfirmEmptyAsync()) return;
        await ViewModel.SaveAsync();
    }
    private async void OnConfigureEmpty(object sender, RoutedEventArgs e)
    { if (ViewModel.CanConfigureEmpty && await ConfirmEmptyAsync()) await ViewModel.ConfigureEmptyAsync(); }
    private ContentDialog Dialog(string title, string content, string primary, string close) => new()
    {
        XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Style = (Style)Application.Current.Resources["LimsEditorDialogStyle"],
        Title = title, Content = content, PrimaryButtonText = primary, CloseButtonText = close, DefaultButton = ContentDialogButton.Close
    };
    private async Task<bool> ConfirmEmptyAsync() => await Dialog("Guardar sin permisos individuales", $"{ViewModel.SelectedUser?.Name} quedará con una configuración individual vacía y sin acceso individual a Estándares, Stock ni Intermedia. ¿Desea continuar?", "Confirmar sin permisos", "Cancelar").ShowAsync() == ContentDialogResult.Primary;
    private void OnDiscard(object sender, RoutedEventArgs e) => ViewModel.DiscardChanges();
    private async void OnRefreshUsers(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsBusy || !await ConfirmDiscardAsync()) return;
        ViewModel.Clear(); await LoadAsync();
    }
    private async void OnReload(object sender, RoutedEventArgs e)
    { if (ViewModel.SelectedUser is { } user && await ConfirmDiscardAsync()) await ViewModel.SelectAsync(user, true); }
    private async void OnPreviousHistory(object sender, RoutedEventArgs e)
    { if (!ViewModel.HasPreviousHistory) return; ViewModel.HistoryPage--; await ViewModel.LoadHistoryAsync(); }
    private async void OnNextHistory(object sender, RoutedEventArgs e)
    { if (!ViewModel.HasMoreHistory || ViewModel.IsLoadingHistory) return; ViewModel.HistoryPage++; await ViewModel.LoadHistoryAsync(); }
    private async void OnTabChanged(object? sender, EventArgs e)
    {
        if (!_ready) return;
        RefreshVisualState();
        PermissionsMotion.Enter(PermissionTabs.SelectedIndex == 0 ? PermissionScroll : HistoryView);
        if (PermissionTabs.SelectedIndex == 1 && ViewModel.IsLoaded && !ViewModel.IsLoadingHistory && !ViewModel.IsBusy) await ViewModel.LoadHistoryAsync();
    }
    private void OnPageSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_ready) return;
        UsersColumn.Width = new GridLength(e.NewSize.Width < 1050 ? 280 : 300);
        UserWorkspace.Padding = e.NewSize.Width < 900 ? new Thickness(16, 14, 16, 12) : new Thickness(20, 16, 20, 14);
        var compact = e.NewSize.Width < 1000;
        Grid.SetRow(SaveActions, compact ? 1 : 0);
        Grid.SetColumn(SaveActions, compact ? 0 : 1);
        Grid.SetColumnSpan(SaveActions, compact ? 2 : 1);
        Grid.SetColumnSpan(ChangeSummary, compact ? 2 : 1);
    }
    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        RefreshVisualState();
        if (e.PropertyName == nameof(PermissionsViewModel.IsLoaded) && ViewModel.IsLoaded)
        {
            PermissionScroll.ChangeView(null, 0, null, true);
            PermissionsMotion.Enter(UserWorkspace, slide: true);
        }
        if (e.PropertyName == nameof(PermissionsViewModel.Message) && ViewModel.HasMessage) PermissionsMotion.Enter(Feedback);
    }
    private void RefreshVisualState()
    {
        if (!_ready) return;
        static Visibility Show(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
        var history = PermissionTabs.SelectedIndex == 1;
        EmptySelection.Visibility = Show(!ViewModel.HasSelection);
        UserWorkspace.Visibility = Show(ViewModel.HasSelection);
        PermissionScroll.Visibility = Show(!history && ViewModel.IsLoaded);
        HistoryView.Visibility = Show(history && ViewModel.IsLoaded);
        PermissionFooter.Visibility = Show(!history);
        HistoryFooter.Visibility = Show(history);
        UserLoading.Visibility = Show(ViewModel.IsLoadingUser);
        HistoryLoading.Visibility = Show(history && ViewModel.IsLoadingHistory);
        EmptyHistory.Visibility = Show(ViewModel.IsHistoryEmpty);
        LoadFailure.Visibility = Show(ViewModel.HasSelection && !ViewModel.IsLoaded && !ViewModel.IsLoadingUser);
        ConfigureEmptyButton.Visibility = Show(ViewModel.CanConfigureEmpty);
        DiscardButton.IsEnabled = ViewModel.HasPendingChanges && ViewModel.CanEdit;
        ReloadButton.IsEnabled = !ViewModel.IsBusy;
        RefreshUsersButton.IsEnabled = !ViewModel.IsBusy;
        UserSearch.IsEnabled = !ViewModel.IsBusy;
        SaveProgress.Visibility = Show(ViewModel.IsSaving);
        SaveIcon.Visibility = Show(!ViewModel.IsSaving);
        NextHistoryButton.IsEnabled = ViewModel.HasMoreHistory && !ViewModel.IsLoadingHistory;
        IdentityMeta.Text = ViewModel.SelectedUser is { } user ? $"{user.Role} · {user.Department}" : string.Empty;
        Feedback.Severity = ViewModel.MessageIsSuccess ? InfoBarSeverity.Success : InfoBarSeverity.Warning;
    }
}

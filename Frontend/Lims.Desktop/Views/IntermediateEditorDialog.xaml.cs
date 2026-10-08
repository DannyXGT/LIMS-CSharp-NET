using System.ComponentModel;
using Lims.Contracts.ReferencePreparations;
using Lims.Desktop.ViewModels;
using Lims.DesignSystem.Controls;
using Lims.DesignSystem.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lims.Desktop.Views;

public sealed partial class IntermediateEditorDialog : ContentDialog
{
    private bool _ready;
    private bool _syncParent;
    private Func<Task>? _confirmation;
    public IntermediateEditorDialog(IntermediateViewModel viewModel) { ViewModel = viewModel; InitializeComponent(); }
    public IntermediateViewModel ViewModel { get; }
    public bool ViewCreatedDetail { get; private set; }
    private async void OnOpened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        PreparationPicker.Date = ViewModel.PreparationDate; ExpirationPicker.Date = ViewModel.ExpirationDate;
        _ready = true; ViewModel.PropertyChanged += OnModelChanged; XamlRoot.Changed += OnRootChanged;
        Resize(); Render(); Motion.Enter(FormSurface, y: 5, scale: .99f);
        await ViewModel.LoadOptionsAsync(CancellationToken.None); Render(); MethodSelect.Focus(FocusState.Programmatic);
    }
    private void OnModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        Render();
        if (args.PropertyName == nameof(IntermediateViewModel.Calculation) && ViewModel.Calculation is not null)
            Motion.Enter(ResultText, milliseconds: 100);
    }
    private void Render()
    {
        if (!_ready) return;
        var success = ViewModel.Created is not null;
        EditorScroll.Visibility = success ? Visibility.Collapsed : Visibility.Visible;
        SuccessSurface.Visibility = success ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Visibility = ViewModel.Components.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SaveButton.IsEnabled = ViewModel.CanSave; SaveButton.Visibility = success ? Visibility.Collapsed : Visibility.Visible;
        SaveButton.Content = ViewModel.IsBusy ? "Creando…" : "Crear Intermedia";
        CancelButton.Content = success ? "Cerrar" : "Cancelar"; CancelButton.IsEnabled = !ViewModel.IsBusy;
        ConfirmButton.IsEnabled = !ViewModel.IsBusy;
        ViewButton.Visibility = success ? Visibility.Visible : Visibility.Collapsed;
        MessageText.Visibility = ViewModel.Message.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        FormSurface.IsEnabled = ViewModel.InputsEnabled && _confirmation is null;
        Cylinder.SetVolumes(ViewModel.TotalTakenMl, ViewModel.FinalMl);
        if (ViewModel.Created is { } created)
        {
            _confirmation = null; ConfirmSurface.Visibility = Visibility.Collapsed; FooterActions.Visibility = Visibility.Visible;
            SuccessCode.Text = created.Preparation.Code;
            SuccessResult.Text = $"{created.Preparation.ConcentrationDisplay} · {created.Preparation.FinalDisplay}";
            SuccessCount.Text = $"{created.Preparation.ComponentCount} componentes · consumos registrados";
        }
        Resize();
    }
    private void Resize()
    {
        var width = Math.Max(280, Math.Min(1040, XamlRoot.Size.Width - 90));
        EditorRoot.Width = ViewModel.Created is null ? width : Math.Min(480, width);
        EditorRoot.MaxHeight = Math.Max(240, XamlRoot.Size.Height - 140);
        var narrow = width < 1020;
        ResultColumn.Width = narrow ? new GridLength(0) : new GridLength(300);
        Grid.SetColumn(ResultPanel, narrow ? 0 : 1); Grid.SetRow(ResultPanel, narrow ? 1 : 0);
        ResultPanel.HorizontalAlignment = narrow ? HorizontalAlignment.Stretch : HorizontalAlignment.Center;
        CylinderColumn.Width = narrow ? new GridLength(220) : new GridLength(1, GridUnitType.Star);
        NumbersColumn.Width = narrow ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        Grid.SetColumn(ResultNumbers, narrow ? 1 : 0); Grid.SetRow(ResultNumbers, narrow ? 0 : 1);
    }
    private void OnRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => Resize();
    private void OnDateChanged(object? sender, EventArgs args)
    {
        if (!_ready) return; ViewModel.PreparationDate = PreparationPicker.Date; ViewModel.ExpirationDate = ExpirationPicker.Date;
    }
    private void OnAdd(object sender, RoutedEventArgs args) { if (ViewModel.CanAddComponent) ViewModel.AddComponent(); Render(); }
    private void OnComponentLoaded(object sender, RoutedEventArgs args) { if (sender is FrameworkElement row) Motion.Enter(row, y: 4, milliseconds: 170); }
    private async void OnRemove(object sender, RoutedEventArgs args)
    {
        if (!ViewModel.InputsEnabled || sender is not Button { Tag: IntermediateComponentViewModel row } button) return;
        if (button.Parent is Grid { Parent: StackPanel { Parent: Border card } }) await Motion.ExitAsync(card, 90, 1);
        ViewModel.RemoveComponent(row); Render();
    }
    private void OnClear(object sender, RoutedEventArgs args)
    {
        if (ViewModel.Components.Count == 0) return;
        Confirm("¿Limpiar todos los componentes? Los datos generales se conservarán.", () => { ViewModel.SelectDilution(null); SyncParent(); return Task.CompletedTask; });
    }
    private async void OnRefresh(object sender, RoutedEventArgs args) { await ViewModel.LoadOptionsAsync(CancellationToken.None); Render(); }
    private void OnParentChanged(object? sender, EventArgs args)
    {
        if (!_ready || _syncParent || ParentSelect.SelectedItem is not IntermediateSource source || source.Id == ViewModel.DilutedSource?.Id) return;
        SyncParent();
        if (ViewModel.Components.Count > 0)
            Confirm($"Diluir {source.Code} reemplazará los componentes actuales por una alícuota de esa Intermedia.", () => { ViewModel.SelectDilution(source); SyncParent(); return Task.CompletedTask; });
        else { ViewModel.SelectDilution(source); SyncParent(); }
    }
    private void SyncParent() { _syncParent = true; ParentSelect.SelectedItem = ViewModel.DilutedSource; _syncParent = false; }
    private void OnClearParent(object sender, RoutedEventArgs args)
    {
        if (ViewModel.DilutedSource is null) return;
        Confirm("¿Quitar la solución a diluir y su alícuota?", () => { ViewModel.SelectDilution(null); SyncParent(); return Task.CompletedTask; });
    }
    private void OnSave(object sender, RoutedEventArgs args)
    {
        if (!ViewModel.CanSave) return;
        Confirm($"Crear solución intermedia · {ViewModel.Components.Count} componentes · Tomado {ViewModel.TotalDisplay} · Aforo {ViewModel.FinalDisplay}\n{ViewModel.ResultDisplay}", async () => { await ViewModel.SaveAsync(CancellationToken.None); });
    }
    private void Confirm(string text, Func<Task> action)
    {
        _confirmation = action; ConfirmText.Text = text; ConfirmSurface.Visibility = Visibility.Visible;
        FooterActions.Visibility = Visibility.Collapsed; Render(); ConfirmButton.Focus(FocusState.Programmatic);
    }
    private async void OnConfirm(object sender, RoutedEventArgs args)
    {
        var action = _confirmation; if (action is null || ViewModel.IsBusy) return;
        _confirmation = null; ConfirmSurface.Visibility = Visibility.Collapsed; FooterActions.Visibility = Visibility.Visible;
        await action(); Render();
    }
    private void OnDismissConfirmation(object sender, RoutedEventArgs args) { _confirmation = null; ConfirmSurface.Visibility = Visibility.Collapsed; FooterActions.Visibility = Visibility.Visible; Render(); }
    private void OnCancel(object sender, RoutedEventArgs args) { if (!ViewModel.IsBusy) Hide(); }
    private void OnViewCreated(object sender, RoutedEventArgs args) { ViewCreatedDetail = true; Hide(); }
    private void OnClosing(ContentDialog sender, ContentDialogClosingEventArgs args) { if (ViewModel.IsBusy) args.Cancel = true; else LimsPopupField.CloseForRoot(XamlRoot); }
    private void OnClosed(ContentDialog sender, ContentDialogClosedEventArgs args)
    {
        _ready = false; ViewModel.PropertyChanged -= OnModelChanged; XamlRoot.Changed -= OnRootChanged;
    }
}

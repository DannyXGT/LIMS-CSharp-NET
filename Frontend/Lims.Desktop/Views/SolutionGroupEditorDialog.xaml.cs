using System.ComponentModel;
using Lims.Desktop.ViewModels;
using Lims.Desktop.Controls;
using Lims.DesignSystem.Controls;
using Lims.DesignSystem.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Lims.Desktop.Views;

public sealed partial class SolutionGroupEditorDialog : ContentDialog
{
    private bool _ready;
    public SolutionGroupEditorDialog(SolutionGroupViewModel viewModel)
    {
        ViewModel = viewModel; InitializeComponent();
        foreach (var field in new LimsPopupField[] { MethodSelect, PreparationPicker, ExpirationPicker }) field.PopupBoundary = EditorScroll;
        EditorScroll.ViewChanged += (_, _) => { LimsPopupField.CloseForRootAfterLayout(this); ArrangeResults(); };
        EditorScroll.SizeChanged += (_, _) => ArrangeResults();
        ResultViewport.RenderTransform = new TranslateTransform();
    }
    public SolutionGroupViewModel ViewModel { get; }
    public bool ViewCreatedDetail { get; private set; }
    private async void OnOpened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        PreparationPicker.Date = ViewModel.PreparationDate; ExpirationPicker.Date = ViewModel.ExpirationDate;
        _ready = true; XamlRoot.Changed += OnRootChanged; ViewModel.PropertyChanged += OnModelChanged;
        Resize(); Motion.Enter(FormSurface, y: 5, scale: .99f); await ViewModel.LoadOptionsAsync(CancellationToken.None); Render();
    }
    private void OnModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ViewModel.SelectedComponent)) ResultViewport.ChangeView(null, 0, null, true);
        if (args.PropertyName is nameof(ViewModel.CanSave) or nameof(ViewModel.SelectedComponent) or nameof(ViewModel.IsBusy) or nameof(ViewModel.Created)) Render();
    }
    private void Render()
    {
        if (!_ready) return;
        CancelButton.IsEnabled = !ViewModel.IsBusy; CancelButton.Content = ViewModel.Created is null ? "Cancelar" : "Cerrar";
        SaveButton.Visibility = ViewModel.Created is null ? Visibility.Visible : Visibility.Collapsed;
        ViewButton.Visibility = ViewModel.Created is null ? Visibility.Collapsed : Visibility.Visible;
        if (ViewModel.Created is { } created) SuccessText.Text = $"{created.Preparation.Code} · Grupo guardado con {created.Calculation.Parts.Count} soluciones.";
        else SuccessText.Text = ViewModel.IsBusy ? "Guardando el grupo…" : ViewModel.CanSave ? "Listo para guardar" : ViewModel.Validation;
        ToolTipService.SetToolTip(SaveButton, SuccessText.Text);
        var row = ViewModel.SelectedComponent;
        ResultView.Show(row?.Preview, row?.VolumePreview, row?.Title ?? "Seleccione una solución", row?.FieldError ?? string.Empty, row?.Source);
        DebitRows.Children.Clear();
        foreach (var debit in ViewModel.Calculation?.Debits ?? []) DebitRows.Children.Add(SolutionGroupVisuals.Debit(this, debit));
        DebitHint.Text = ViewModel.Calculation is { } calculation
            ? $"{calculation.Parts.Count} soluciones · Consumo total {Lims.Contracts.ReferencePreparations.StockPresentation.Quantity(calculation.Debits.Sum(d => Lims.Contracts.ReferencePreparations.StockUnits.ConvertVolume(d.Quantity, d.Source.VolumeUnit, "mL")), "mL")}"
            : "Complete el grupo para consultar el consumo compartido.";
        Resize();
    }
    private void Resize()
    {
        var width = Math.Max(280, Math.Min(1220, XamlRoot.Size.Width - 100));
        EditorRoot.Width = width; EditorRoot.MaxHeight = Math.Max(200, XamlRoot.Size.Height - 112);
        var narrow = width < 880;
        ResultColumn.Width = narrow ? new GridLength(0) : new GridLength(width < 1100 ? 350 : 390);
        Grid.SetColumn(ResultViewport, narrow ? 0 : 1); Grid.SetRow(ResultViewport, narrow ? 1 : 0);
        var compact = width < 620;
        IdentityGrid.RowSpacing = compact ? 8 : 0; DateGrid.RowSpacing = compact ? 8 : 0;
        var aqs = !ViewModel.IsCurve;
        Grid.SetColumn(MethodField, compact ? 0 : aqs ? 0 : 2); Grid.SetRow(MethodField, compact ? 1 : 0); Grid.SetColumnSpan(MethodField, compact ? 3 : 1);
        Grid.SetColumn(CodeBox, compact ? 0 : aqs ? 1 : 0); Grid.SetColumnSpan(CodeBox, compact ? 3 : 1);
        Grid.SetColumn(NameBox, compact ? 0 : aqs ? 2 : 1); Grid.SetRow(NameBox, compact ? 2 : 0); Grid.SetColumnSpan(NameBox, compact ? 3 : 1);
        MethodLabel.Text = aqs ? "Método · filtrar Intermedias" : "Método de calibración";
        Grid.SetRow(ResponsibleField, compact ? 1 : 0); Grid.SetColumn(ResponsibleField, compact ? 0 : 2); Grid.SetColumnSpan(ResponsibleField, compact ? 3 : 1);
        ArrangeResults();
    }
    private void ArrangeResults()
    {
        if (!_ready || EditorScroll.ViewportHeight <= 0) return;
        var transform = (TranslateTransform)ResultViewport.RenderTransform;
        if (EditorRoot.Width < 880) { transform.Y = 0; ResultViewport.MaxHeight = double.PositiveInfinity; return; }
        var top = Workspace.TransformToVisual(FormSurface).TransformPoint(new Point(0, 0)).Y;
        ResultViewport.MaxHeight = Math.Max(120, EditorScroll.ViewportHeight - Math.Max(0, top - EditorScroll.VerticalOffset));
        transform.Y = Math.Clamp(EditorScroll.VerticalOffset - top, 0, Math.Max(0, Workspace.ActualHeight - ResultViewport.ActualHeight));
    }
    private void OnRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => Resize();
    private void OnPreparationChanged(object? sender, EventArgs args)
    {
        if (!_ready) return; ViewModel.PreparationDate = PreparationPicker.Date; ExpirationPicker.Date = ViewModel.ExpirationDate;
    }
    private void OnExpirationChanged(object? sender, EventArgs args) { if (_ready) ViewModel.ExpirationDate = ExpirationPicker.Date; }
    private void OnPopupLoaded(object sender, RoutedEventArgs args) { if (sender is LimsPopupField field) field.PopupBoundary = field == ResultSelect ? ResultViewport : EditorScroll; }
    private void OnAdd(object sender, RoutedEventArgs args) { if (ViewModel.CanAddComponent) ViewModel.AddComponent(); }
    private void OnClearInternal(object sender, RoutedEventArgs args)
    {
        if (ViewModel.InputsEnabled && sender is Button { Tag: SolutionPartViewModel row }) row.ClearInternalStandard();
    }
    private void OnSelectResult(object sender, RoutedEventArgs args)
    {
        if (sender is Button { Tag: SolutionPartViewModel row }) { ViewModel.SelectedComponent = row; if (EditorRoot.Width < 880) ResultPanel.StartBringIntoView(); }
    }
    private void OnVolumeLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is not TextBox { Tag: SolutionPartViewModel row } box) return;
        void Update() => box.BorderBrush = SolutionGroupVisuals.Brush(this, row.FieldError.Length > 0 ? "LimsDangerBrush" : "LimsInputBorderBrush");
        PropertyChangedEventHandler handler = (_, e) => { if (e.PropertyName == nameof(row.FieldError)) Update(); };
        row.PropertyChanged += handler; Update();
        box.Unloaded += Detach;
        void Detach(object s, RoutedEventArgs e) { row.PropertyChanged -= handler; box.Unloaded -= Detach; }
    }
    private void OnRemove(object sender, RoutedEventArgs args) { if (ViewModel.InputsEnabled && sender is Button { Tag: SolutionPartViewModel row }) ViewModel.RemoveComponent(row); }
    private async void OnRefresh(object sender, RoutedEventArgs args) => await ViewModel.LoadOptionsAsync(CancellationToken.None);
    private async void OnSave(object sender, RoutedEventArgs args) { if (ViewModel.CanSave) await ViewModel.SaveAsync(CancellationToken.None); }
    private void OnCancel(object sender, RoutedEventArgs args) { if (!ViewModel.IsBusy) Hide(); }
    private void OnView(object sender, RoutedEventArgs args) { ViewCreatedDetail = true; Hide(); }
    private void OnClosing(ContentDialog sender, ContentDialogClosingEventArgs args) { if (ViewModel.IsBusy) args.Cancel = true; else LimsPopupField.CloseForRoot(XamlRoot); }
    private void OnClosed(ContentDialog sender, ContentDialogClosedEventArgs args) { _ready = false; XamlRoot.Changed -= OnRootChanged; ViewModel.PropertyChanged -= OnModelChanged; }
}

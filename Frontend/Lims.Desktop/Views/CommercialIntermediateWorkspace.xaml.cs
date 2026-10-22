using System.ComponentModel;
using Lims.Contracts.ReferencePreparations;
using Lims.Desktop.ViewModels;
using Lims.DesignSystem.Controls;
using Lims.DesignSystem.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace Lims.Desktop.Views;

public sealed partial class CommercialIntermediateWorkspace : UserControl
{
    private readonly Dictionary<CommercialComponentRow, Grid> _rowGrids = [];
    private bool _ready;
    private bool _summaryOnly;
    public CommercialIntermediateViewModel ViewModel { get; }
    public event EventHandler? Cancelled;
    public event EventHandler? ExpandRequested;
    public void SetExpanded(bool expanded) => EditorHeading.IsExpanded = expanded;
    public void Release() { ViewModel.PropertyChanged -= OnModelChanged; }

    public event Func<CommercialPreparationDetail, Task>? Saved;
    public CommercialIntermediateWorkspace(CommercialIntermediateViewModel model)
    {
        ViewModel = model; InitializeComponent();
        EditorHeading.ExpandRequested += (_, _) => ExpandRequested?.Invoke(this, EventArgs.Empty);
        CompositionEditor.Initialize(ViewModel.Composition, CaptureScroll);
        CompositionEditor.RetryRequested += async (_, _) => await ViewModel.LoadAsync();
        EntryModeSelect.ItemsSource = new[] { "Nuevo producto", "Ya registrado" }; EntryModeSelect.SelectedIndex = 0;
        foreach (var field in new LimsPopupField[] { EntryModeSelect, ExistingSelect, MethodSelect, VolumeUnitSelect, ReceivedPicker, ExpirationPicker, LocationSelect }) field.PopupBoundary = CaptureScroll;
        var pasteShortcut = new KeyboardAccelerator { Key = VirtualKey.V, Modifiers = VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift };
        pasteShortcut.Invoked += async (_, e) => { e.Handled = true; await PasteClipboardAsync(); };
        PasteButton.KeyboardAccelerators.Add(pasteShortcut);
        ToolTipService.SetToolTip(PasteButton, "Pegar Compuesto, CAS, Concentración y Unidad · Ctrl+Mayús+V");
        ResponsibleText.Text = "Registrado por " + model.RegisteredBy;
        CaptureScroll.SizeChanged += (_, _) => ResizeWorkspace();
        _ready = true;
        ViewModel.PropertyChanged += OnModelChanged;
        Render();
    }
    public Task LoadAsync() => ViewModel.LoadAsync();
    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CommercialIntermediateViewModel.ValidationStatus)) Render();
    }
    private void Render()
    {
        if (!_ready) return;
        FooterStatus.Text = ViewModel.IsReady && !ViewModel.UseExisting && !ViewModel.SupportsSolventComposition
            ? "Integración pendiente: la API no admite guardar la composición de solventes."
            : ViewModel.ValidationStatus;
        ToolTipService.SetToolTip(SaveButton, FooterStatus.Text);
        NewFields.Visibility = ViewModel.UseExisting ? Visibility.Collapsed : Visibility.Visible;
        ExistingPanel.Visibility = ViewModel.UseExisting ? Visibility.Visible : Visibility.Collapsed;
        ProductCaption.Text = ViewModel.IdentificationValid ? ViewModel.ProductDisplay : "Datos del fabricante y del producto";
        SummaryInitial.Text = "Inicial: " + ViewModel.InitialDisplay;
        TableEmptyState.Visibility = ViewModel.Rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        TableHeader.Visibility = ComponentSearchBox.Visibility = ViewModel.Rows.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        RetryButton.Visibility = !ViewModel.IsReady && !ViewModel.IsLoading ? Visibility.Visible : Visibility.Collapsed;
        RetryButton.IsEnabled = !ViewModel.IsLoading && !ViewModel.IsBusy;
        var components = ViewModel.PreviewComponents;
        CompositionEmpty.Visibility = components.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CompositionPreview.Items.Clear();
        foreach (var c in components.Take(3)) CompositionPreview.Items.Add(PreviewRow(c.Name, $"{c.CertifiedConcentration:G29} {c.ConcentrationUnit}"));
        if (components.Count > 3) CompositionPreview.Items.Add(Caption($"+ {components.Count - 3} analitos en la tabla"));
        ExistingDescription.Text = ViewModel.ExistingMaterial is { } m ? $"{m.Brand} · Lote {m.Lot}\n{m.AvailableDisplay} disponibles · {m.StorageLocation} · {m.StorageTemperature}" : "";
        if (ViewModel.UseExisting)
        {
            ExistingComponents.Items.Clear();
            foreach (var c in components) ExistingComponents.Items.Add(PreviewRow(c.Name, $"{c.CertifiedConcentration:G29} {c.ConcentrationUnit}"));
        }
        foreach (var name in new[] { "Name", "Method", "Brand", "Lot", "Catalog", "Volume", "BottleCount", "VolumeUnit", "ReceivedDate", "ExpirationDate", "Location", "Storage" })
        {
            if (FindName(name + "Error") is not TextBlock error) continue;
            error.Text = ViewModel.Error(name); error.Visibility = error.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        }
        RenderFilter(); UpdateBottle(); ResizeWorkspace();
    }
    private static TextBlock Caption(string text) => new() { Text = text, Style = (Style)Application.Current.Resources["LimsCaptionStyle"], TextWrapping = TextWrapping.Wrap };
    private static Grid PreviewRow(string name, string value)
    {
        var grid = new Grid { ColumnSpacing = 8, Padding = new Thickness(0, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = Caption(name); label.TextWrapping = TextWrapping.NoWrap; label.TextTrimming = TextTrimming.CharacterEllipsis;
        ToolTipService.SetToolTip(label, name);
        var concentration = Caption(value); Grid.SetColumn(concentration, 1); grid.Children.Add(label); grid.Children.Add(concentration); return grid;
    }
    private void UpdateBottle()
    {
        var level = ViewModel.InitialVolume > 0 && ViewModel.AvailableVolume.HasValue ? Math.Clamp(ViewModel.AvailabilityPercent / 100d, 0, 1) : 0;
        AutomationProperties.SetName(BottleVisual, $"Frasco comercial · {ViewModel.AvailableDisplay} disponibles · {ViewModel.AvailabilityDisplay}");
        BottleVisual.SetLevel(level);
    }
    private void OnWorkspaceSizeChanged(object sender, SizeChangedEventArgs e) => ResizeWorkspace();
    private void ResizeWorkspace()
    {
        if (!_ready) return;
        var narrow = ActualWidth < 820;
        HeaderActions.Orientation = narrow ? Orientation.Vertical : Orientation.Horizontal;
        SummaryToggle.Visibility = narrow ? Visibility.Visible : Visibility.Collapsed;
        var summary = narrow && _summaryOnly;
        CaptureScroll.Visibility = summary ? Visibility.Collapsed : Visibility.Visible;
        SummaryPanel.Visibility = !narrow || summary ? Visibility.Visible : Visibility.Collapsed;
        CaptureColumn.Width = summary ? new GridLength(0) : new GridLength(narrow ? 1 : 7, GridUnitType.Star);
        SummaryColumn.MinWidth = narrow ? 0 : 270;
        SummaryColumn.Width = narrow && !summary ? new GridLength(0) : new GridLength(narrow ? 1 : 3, GridUnitType.Star);
        ComponentsScroll.Height = ViewModel.Rows.Count == 0 ? 72 : Math.Min(Math.Clamp(CaptureScroll.ActualHeight - 208, 96, 380), Math.Max(100, ViewModel.Rows.Count * 44));
        SummaryToggle.Content = summary ? "Volver a captura" : "Ver resumen";
    }
    private void OnToggleSummary(object sender, RoutedEventArgs e) { _summaryOnly = !_summaryOnly; ResizeWorkspace(); }
    private void OnEntryModeChanged(object? sender, EventArgs e) { if (_ready) ViewModel.UseExisting = EntryModeSelect.SelectedIndex == 1; }
    private void OnFieldLostFocus(object sender, RoutedEventArgs e) { if (_ready && sender is FrameworkElement { Tag: string property }) ViewModel.Touch(property); }
    private void OnTableWidthChanged(object sender, SizeChangedEventArgs e) { if (_ready) ComponentTable.Width = Math.Max(ComponentTable.MinWidth, e.NewSize.Width); }
    private void OnCaptureScrolled(object sender, ScrollViewerViewChangedEventArgs e) => LimsPopupField.CloseForRootAfterLayout(this);
    private void OnTableScrolled(object sender, ScrollViewerViewChangedEventArgs e) => LimsPopupField.CloseForRootAfterLayout(this);
    private void OnComponentSearchChanged(object sender, TextChangedEventArgs e) { if (_ready) RenderFilter(); }
    private void RenderFilter()
    {
        var query = ComponentSearchBox.Text.Trim();
        foreach (var item in _rowGrids.ToArray())
        {
            if (!ViewModel.Rows.Contains(item.Key)) { _rowGrids.Remove(item.Key); continue; }
            foreach (var (name, label) in new[] { ("AnaltyeName", "Nombre"), ("AnalyteCas", "CAS"), ("AnalyteConcentration", "Concentración"), ("AnalyteUnit", "Unidad") })
                if (item.Value.FindName(name) is FrameworkElement field) AutomationProperties.SetName(field, $"Analito {item.Key.Number} · {label}");
            item.Value.Visibility = $"{item.Key.Name} {item.Key.Cas}".Contains(query, StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;
        }
        SearchStatusText.Visibility = query.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        SearchStatusText.Text = query.Length == 0 ? "" : $"{_rowGrids.Count(r => r.Value.Visibility == Visibility.Visible)} filas coinciden · el filtro conserva todos los componentes";
    }
    private void OnRowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Grid { Tag: CommercialComponentRow row } grid) return;
        if (!_rowGrids.ContainsKey(row)) grid.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnRowKeyDown), true);
        _rowGrids[row] = grid;
        foreach (var (name, label) in new[] { ("AnaltyeName", "Nombre"), ("AnalyteCas", "CAS"), ("AnalyteConcentration", "Concentración"), ("AnalyteUnit", "Unidad") })
            if (grid.FindName(name) is FrameworkElement field) AutomationProperties.SetName(field, $"Analito {row.Number} · {label}");
        ((LimsSelect)grid.FindName("AnalyteUnit")).PopupBoundary = ComponentsScroll;
        RenderFilter();
        if (ViewModel.Rows.Count <= 4) Motion.Enter(grid, y: 2, milliseconds: 100);
    }
    private void OnRowLostFocus(object sender, RoutedEventArgs e)
    {
        var grid = RowGrid(sender as DependencyObject);
        if (grid?.Tag is not CommercialComponentRow row) return;
        DispatcherQueue.TryEnqueue(() => { if (RowGrid(FocusManager.GetFocusedElement(XamlRoot) as DependencyObject) != grid) row.Touch(); });
    }
    private static Grid? RowGrid(DependencyObject? element)
    {
        while (element is not null) { if (element is Grid { Tag: CommercialComponentRow } row) return row; element = VisualTreeHelper.GetParent(element); }
        return null;
    }
    private void OnAddRow(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IdentificationValid) ProductExpander.IsExpanded = false;
        ComponentSearchBox.Text = "";
        if (ViewModel.AddRow() is not { } row) return;
        ComponentsCard.StartBringIntoView();
        DispatcherQueue.TryEnqueue(() => { if (_rowGrids.TryGetValue(row, out var grid)) ((Control)grid.FindName("AnaltyeName")).Focus(FocusState.Programmatic); });
    }
    private async void OnRemoveRow(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: CommercialComponentRow row } || !ViewModel.InputsEnabled) return;
        if (_rowGrids.TryGetValue(row, out var grid)) await Motion.ExitAsync(grid, 90, 1);
        ViewModel.RemoveRow(row);
        AddRowButton.Focus(FocusState.Programmatic);
    }
    private void OnRowKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is not (VirtualKey.Enter or VirtualKey.Down or VirtualKey.Up) || sender is not Grid { Tag: CommercialComponentRow row } grid) return;
        DependencyObject? field = e.OriginalSource as DependencyObject;
        while (field is not null && VisualTreeHelper.GetParent(field) != grid) field = VisualTreeHelper.GetParent(field);
        if (field is LimsSelect { IsDropDownOpen: true }) return;
        if (field is not FrameworkElement cell || Grid.GetColumn(cell) is < 1 or > 4) return;
        var index = ViewModel.Rows.IndexOf(row) + (e.Key == VirtualKey.Up ? -1 : 1);
        if (index == ViewModel.Rows.Count && e.Key == VirtualKey.Enter && row.IsValid)
        {
            if (ViewModel.AddRow() is { } added)
            {
                e.Handled = true;
                DispatcherQueue.TryEnqueue(() => FocusRow(added, Grid.GetColumn(cell)));
            }
            return;
        }
        if (index < 0 || index >= ViewModel.Rows.Count || !_rowGrids.TryGetValue(ViewModel.Rows[index], out var target)) return;
        var next = target.Children.OfType<Control>().FirstOrDefault(c => Grid.GetColumn(c) == Grid.GetColumn(cell));
        if (next is null) return;
        e.Handled = true; next.Focus(FocusState.Keyboard); target.StartBringIntoView();
    }
    private void FocusRow(CommercialComponentRow row, int column)
    {
        if (!_rowGrids.TryGetValue(row, out var target)) return;
        target.Children.OfType<Control>().FirstOrDefault(c => Grid.GetColumn(c) == column)?.Focus(FocusState.Keyboard);
        target.StartBringIntoView();
    }
    private async void OnPaste(object sender, RoutedEventArgs e) => await PasteClipboardAsync();
    public async Task<bool> PasteClipboardAsync()
    {
        if (!ViewModel.InputsEnabled) return false;
        try
        {
            var content = Clipboard.GetContent();
            if (!content.Contains(StandardDataFormats.Text)) { ViewModel.ImportMessage = "Copie cuatro columnas desde Excel: Compuesto, CAS, Concentración y Unidad."; return false; }
            var pasted = ViewModel.Paste(await content.GetTextAsync());
            if (pasted) { ProductExpander.IsExpanded = false; ComponentSearchBox.Text = ""; ComponentsCard.StartBringIntoView(); }
            return pasted;
        }
        catch (System.Runtime.InteropServices.COMException) { ViewModel.ImportMessage = "No se pudo leer el portapapeles. Vuelva a copiar las celdas."; return false; }
    }
    private void OnFocusInventory(object sender, RoutedEventArgs e)
    {
        _summaryOnly = false; ResizeWorkspace();
        if (ViewModel.UseExisting) { ExistingSelect.Focus(FocusState.Programmatic); return; }
        ProductExpander.IsExpanded = false; InventoryExpander.IsExpanded = true;
        InventoryExpander.StartBringIntoView(); VolumeBox.Focus(FocusState.Programmatic);
    }
    private void OnFocusTable(object sender, RoutedEventArgs e) { _summaryOnly = false; ResizeWorkspace(); ProductExpander.IsExpanded = false; ComponentsCard.StartBringIntoView(); ComponentSearchBox.Focus(FocusState.Programmatic); }
    private async void OnRetry(object sender, RoutedEventArgs e) => await ViewModel.LoadAsync();
    private async void OnSearchExisting(object sender, RoutedEventArgs e) => await ViewModel.LoadAsync(ExistingSearchBox.Text);
    private void OnCancel(object sender, RoutedEventArgs e) { if (!ViewModel.CanCancel) return; LimsPopupField.CloseForRoot(XamlRoot); Cancelled?.Invoke(this, EventArgs.Empty); }
    private async void OnSave(object sender, RoutedEventArgs e)
    {
        LimsPopupField.CloseForRoot(XamlRoot);
        var saved = await ViewModel.SaveAsync();
        if (saved is not null && Saved is { } handler) await handler(saved);
    }
}

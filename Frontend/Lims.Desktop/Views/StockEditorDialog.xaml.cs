using Lims.Desktop.Http;
using System.ComponentModel;
using Lims.Contracts.ReferencePreparations;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Data;
using Lims.Desktop.ViewModels;
using Lims.DesignSystem.Controls;
using Lims.DesignSystem.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lims.Desktop.Views;

public sealed partial class StockEditorDialog : ContentDialog
{
    private readonly DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private string _search = string.Empty;
    private readonly ICommercialPreparationApiClient? _commercialApi;
    private bool _ready;
    private bool _expanded;
    public StockEditorDialog(StockViewModel viewModel, ICommercialPreparationApiClient? commercialApi = null)
    {
        _commercialApi = commercialApi;
        ViewModel = viewModel;
        InitializeComponent(); InitializeEntry();
        CompositionEditor.Initialize(ViewModel.Composition, StepScroll);
        CompositionEditor.RetryRequested += async (_, _) => await ViewModel.LoadSolventsAsync(CancellationToken.None);
        CommercialEntry.StateChanged += (_, _) => Render();
        EditorHeading.ExpandRequested += (_, _) => { _expanded = !_expanded; EditorHeading.IsExpanded = _expanded; Resize(); };
        foreach (var field in new LimsPopupField[] { SourceList, ConcentrationUnitSelect, VolumeUnitSelect, ActualUnitSelect, PreparationDatePicker, ExpirationDatePicker })
            field.PopupBoundary = StepScroll;
        ActualUnitSelect.PopupBoundary = ResultPanel;
        StepScroll.ViewChanged += (_, _) => LimsPopupField.CloseForRootAfterLayout(this);
        _searchTimer.Tick += async (_, _) => { _searchTimer.Stop(); await ViewModel.SearchSourcesAsync(_search, 1, CancellationToken.None); Render(); };
    }
    public StockViewModel ViewModel { get; }
    public CommercialPreparationDetail? CommercialCreated { get; private set; }
    private bool IsCommercial => EntryTypeSelect.SelectedIndex == 1;
    private void InitializeEntry()
    {
        EntryTypeSelect.ItemsSource = new[] { "Preparado en laboratorio", "Comercial / comprado" };
        EntryTypeSelect.SelectedIndex = 0;
        EntryTypeSelect.PopupBoundary = EditorRoot;
    }
    private async void OnEntryTypeChanged(object? sender, EventArgs args)
    {
        if (!_ready) return;
        LimsPopupField.CloseForRoot(XamlRoot);
        FormSurface.Visibility = IsCommercial ? Visibility.Collapsed : Visibility.Visible;
        CommercialEntry.Visibility = IsCommercial ? Visibility.Visible : Visibility.Collapsed;
        if (IsCommercial) await CommercialEntry.InitializeAsync(_commercialApi, "Stock", ViewModel.PreparedBy, StepScroll);
        Render();
    }
    private void RenderCommercial()
    {
        FooterStatus.Text = CommercialEntry.SaveStatus;
        EditorHeading.Heading = "Nuevo Stock comercial";
        EditorHeading.Description = "Producto adquirido · concentración, frasco e inventario";
        LaboratoryLayout.Visibility = Visibility.Collapsed;
        StepScroll.Visibility = Visibility.Visible; SuccessSurface.Visibility = Visibility.Collapsed;
        FormSurface.Visibility = Visibility.Collapsed; CommercialEntry.Visibility = Visibility.Visible;
        MessageText.Visibility = Visibility.Collapsed; ViewButton.Visibility = Visibility.Collapsed;
        SaveButton.Visibility = Visibility.Visible; SaveButton.Content = CommercialEntry.IsBusy ? "Guardando…" : "Guardar Stock";
        SaveButton.IsEnabled = ViewModel.CanCreate && CommercialEntry.CanSave && !CommercialEntry.IsBusy;
        ToolTipService.SetToolTip(SaveButton, FooterStatus.Text);
        CancelButton.IsEnabled = EntryTypeSelect.IsEnabled = !CommercialEntry.IsBusy;
        CancelButton.Content = "Cancelar";
        Resize();
    }
    public bool ViewCreatedDetail { get; private set; }
    private async void OnOpened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        PreparationDatePicker.Date = ViewModel.PreparationDate;
        ExpirationDatePicker.Date = ViewModel.ExpirationDate;
        _ready = true;
        ViewModel.PropertyChanged += OnModelChanged;
        XamlRoot.Changed += OnRootChanged;
        Resize(); Render(); Motion.Enter(FormSurface, y: 5);
        await ViewModel.LoadOptionsAsync(CancellationToken.None);
        await ViewModel.LoadSolventsAsync(CancellationToken.None);
        await ViewModel.SearchSourcesAsync(null, 1, CancellationToken.None);
        Render(); SourceList.Focus(FocusState.Programmatic);
    }
    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        Render();
    }
    private void Render()
    {
        if (!_ready) return;
        if (IsCommercial) { RenderCommercial(); return; }
        FormSurface.Visibility = Visibility.Visible; CommercialEntry.Visibility = Visibility.Collapsed;
        EntryTypeSelect.IsEnabled = !ViewModel.IsBusy;
        var success = ViewModel.Created is not null;
        StepScroll.Visibility = success ? Visibility.Collapsed : Visibility.Visible;
        SuccessSurface.Visibility = success ? Visibility.Visible : Visibility.Collapsed;
        EditorHeading.Heading = success ? "Stock creado" : "Nuevo Stock preparado";
        EditorHeading.Description = "Preparación en laboratorio · estándar, cantidades y composición";
        LaboratoryLayout.Visibility = success ? Visibility.Collapsed : Visibility.Visible;
        CommercialEntry.Visibility = Visibility.Collapsed;
        MessageText.Visibility = string.IsNullOrWhiteSpace(ViewModel.Message) ? Visibility.Collapsed : Visibility.Visible;
        SaveButton.Visibility = success ? Visibility.Collapsed : Visibility.Visible;
        SaveButton.IsEnabled = ViewModel.CanSave;
        FooterStatus.Text = success ? "Preparación registrada" : ViewModel.CanSave ? "Listo para guardar" : StockRequirement();
        ToolTipService.SetToolTip(SaveButton, FooterStatus.Text);
        SaveButton.Content = ViewModel.IsBusy ? "Creando…" : "Crear Stock";
        CancelButton.Content = success ? "Cerrar" : "Cancelar";
        CancelButton.IsEnabled = !ViewModel.IsBusy;
        ViewButton.Visibility = success ? Visibility.Visible : Visibility.Collapsed;
        FormSurface.IsHitTestVisible = !ViewModel.IsBusy;
        MoreSourcesButton.Visibility = ViewModel.HasMoreSources ? Visibility.Visible : Visibility.Collapsed;
        if (ViewModel.SelectedSource is { } source)
        {
            SourceCard.Visibility = Visibility.Visible;
            SourceIdentity.Text = $"CAS {source.CasNumber ?? "No informado"} · Catálogo {source.CatalogNumber ?? "No informado"} · Lote {source.Lot} · {source.Brand}";
            SourceContext.Text = $"{source.Method} · Pureza {source.PurityDisplay}";
            SourceBalance.Text = $"{source.AvailableDisplay} disponibles · {source.AvailabilityPercentDisplay}";
            SourceValidity.Text = $"Expiración {source.ExpirationDisplay}";
        }
        else SourceCard.Visibility = Visibility.Collapsed;
        var calculation = ViewModel.Calculation;
        CalculatedWeightText.Text = calculation?.RequiredDisplay ?? "—";
        ActualConcentrationText.Text = calculation?.ConcentrationDisplay ?? "—";
        DifferenceText.Text = calculation?.DifferenceDisplay ?? "— %";
        RemainingText.Text = calculation?.RemainingDisplay ?? "—";
        if (ViewModel.Created is { } created)
        {
            SuccessCode.Text = created.Preparation.Code;
            SuccessResult.Text = $"{created.Preparation.ConcentrationDisplay} · {created.Preparation.FinalDisplay}";
            SuccessConsumption.Text = $"{created.Consumption.QuantityDisplay} consumidos";
            EditorRoot.Width = Math.Min(460, XamlRoot.Size.Width - 88);
        }
    }
    private string StockRequirement()
    {
        if (ViewModel.IsBusy) return "Guardando la preparación…";
        if (ViewModel.SelectedSource is null) return "Seleccione el estándar de origen.";
        if (string.IsNullOrWhiteSpace(ViewModel.Compound)) return "Seleccione el analito del estándar.";
        if (string.IsNullOrWhiteSpace(ViewModel.Concentration)) return "Indique la concentración objetivo.";
        if (string.IsNullOrWhiteSpace(ViewModel.FinalVolume)) return "Indique el volumen final.";
        if (string.IsNullOrWhiteSpace(ViewModel.ActualQuantity)) return "Indique el peso tomado realmente.";
        if (ViewModel.IsCalculating) return "Calculando el resultado…";
        if (ViewModel.Calculation is { HasSufficientMaterial: false }) return "El estándar no tiene saldo suficiente para el peso tomado.";
        if (!ViewModel.Composition.IsValid) return ViewModel.Composition.IsReady ?
            ViewModel.SolventRows.Any(r => r.SelectedSolvent is null) ? "Seleccione los solventes de la matriz." : ViewModel.Composition.Error ?? "Complete la composición de solventes." : ViewModel.Composition.CatalogMessage;
        if (ViewModel.ExpirationDate is null) return "Indique la fecha de expiración.";
        return string.IsNullOrWhiteSpace(ViewModel.Message) ? "Revise las cantidades, el nombre y las fechas de la preparación." : ViewModel.Message;
    }
    private void Resize()
    {
        var size = PreparationEditorHeader.SizeFor(XamlRoot, _expanded);
        var width = size.Width;
        EditorRoot.Width = ViewModel.Created is null ? width : Math.Min(460, width);
        EditorRoot.Height = size.Height;
        var narrow = width < 760;
        LaboratoryLayout.ColumnDefinitions[1].Width = narrow ? new GridLength(0) : new GridLength(310);
        Grid.SetColumn(ResultPanel, narrow ? 0 : 1);
        Grid.SetRow(ResultPanel, narrow ? 1 : 0);
        ResultPanel.MaxHeight = narrow ? 220 : double.PositiveInfinity;
    }
    private void OnRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => Resize();
    private void OnSourceSearchChanged(object? sender, string text) { if (!_ready) return; _search = text; _searchTimer.Stop(); _searchTimer.Start(); }
    private async void OnMoreSources(object sender, RoutedEventArgs e) { await ViewModel.SearchSourcesAsync(_search, ViewModel.SourcePage + 1, CancellationToken.None); Render(); }
    private void OnDateChanged(object? sender, EventArgs e)
    {
        if (!_ready) return;
        if (PreparationDatePicker.Date is { } date) ViewModel.PreparationDate = date;
        ViewModel.ExpirationDate = ExpirationDatePicker.Date;
    }
    private async void OnSave(object sender, RoutedEventArgs e)
    {
        if (IsCommercial) { var saving = CommercialEntry.SaveAsync(); Render(); CommercialCreated = await saving; Render(); if (CommercialCreated is not null) Hide(); return; }
        await ViewModel.SaveAsync(CancellationToken.None);
    }
    private void OnCancel(object sender, RoutedEventArgs e) { if (!ViewModel.IsBusy && !CommercialEntry.IsBusy) Hide(); }
    private void OnViewCreated(object sender, RoutedEventArgs e) { ViewCreatedDetail = true; Hide(); }
    private void OnClosing(ContentDialog sender, ContentDialogClosingEventArgs args) { if (ViewModel.IsBusy || CommercialEntry.IsBusy) args.Cancel = true; else LimsPopupField.CloseForRoot(XamlRoot); }
    private void OnClosed(ContentDialog sender, ContentDialogClosedEventArgs args)
    {
        _ready = false; _searchTimer.Stop();
        ViewModel.PropertyChanged -= OnModelChanged;
        XamlRoot.Changed -= OnRootChanged;
    }
}

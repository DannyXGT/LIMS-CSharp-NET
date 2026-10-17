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
    public StockEditorDialog(StockViewModel viewModel, ICommercialPreparationApiClient? commercialApi = null)
    {
        _commercialApi = commercialApi;
        ViewModel = viewModel;
        InitializeComponent(); InitializeEntry();
        foreach (var field in new LimsPopupField[] { SourceList, ConcentrationUnitSelect, VolumeUnitSelect, ActualUnitSelect, PreparationDatePicker, ExpirationDatePicker })
            field.PopupBoundary = StepScroll;
        StepScroll.ViewChanged += (_, _) => LimsPopupField.CloseForRootAfterLayout(this);
        _searchTimer.Tick += async (_, _) => { _searchTimer.Stop(); await ViewModel.SearchSourcesAsync(_search, 1, CancellationToken.None); Render(); };
    }
    public StockViewModel ViewModel { get; }
    public CommercialPreparationDetail? CommercialCreated { get; private set; }
    private bool IsCommercial => EntryTypeSelect.SelectedIndex == 1;
    private void InitializeEntry()
    {
        EntryTypeSelect.ItemsSource = new[] { "Preparada en laboratorio", "Comercial / comprada" };
        EntryTypeSelect.SelectedIndex = 0;
        EntryTypeSelect.PopupBoundary = StepScroll;
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
        Title = "Nuevo Stock comercial";
        StepScroll.Visibility = Visibility.Visible; SuccessSurface.Visibility = Visibility.Collapsed;
        FormSurface.Visibility = Visibility.Collapsed; CommercialEntry.Visibility = Visibility.Visible;
        MessageText.Visibility = Visibility.Collapsed; ViewButton.Visibility = Visibility.Collapsed;
        SaveButton.Visibility = Visibility.Visible; SaveButton.Content = CommercialEntry.IsBusy ? "Guardando…" : "Guardar Stock";
        SaveButton.IsEnabled = ViewModel.CanCreate && CommercialEntry.IsReady && !CommercialEntry.IsBusy;
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
        ViewModel.SolventRows.CollectionChanged += OnSolventRowsChanged;
        BuildSolventRows();
        Resize(); Render(); Motion.Enter(FormSurface, y: 5);
        await ViewModel.LoadOptionsAsync(CancellationToken.None);
        await ViewModel.LoadSolventsAsync(CancellationToken.None);
        await ViewModel.SearchSourcesAsync(null, 1, CancellationToken.None);
        Render(); SourceList.Focus(FocusState.Programmatic);
    }
    private void OnAddSolvent(object sender, RoutedEventArgs args) => ViewModel.AddSolvent();
    private async void OnRetrySolvents(object sender, RoutedEventArgs args) => await ViewModel.LoadSolventsAsync(CancellationToken.None);
    private void OnSolventRowsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs args) => BuildSolventRows();
    private void BuildSolventRows()
    {
        if (_ready) LimsPopupField.CloseForRoot(XamlRoot);
        SolventRowsPanel.Children.Clear();
        foreach (var row in ViewModel.SolventRows)
        {
            var grid = new Grid { ColumnSpacing = 6 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var selector = new LimsSearchSelect { ItemsSource = ViewModel.SolventCatalog.ToArray(), SelectedItem = row.SelectedSolvent, PlaceholderText = "Solvente…", SearchPlaceholder = "Nombre o abreviatura…", PopupBoundary = StepScroll };
            AutomationProperties.SetName(selector, "Solvente " + (ViewModel.SolventRows.IndexOf(row) + 1));
            selector.SetBinding(LimsSelect.SelectedItemProperty, new Binding { Source = row, Path = new PropertyPath(nameof(row.SelectedSolvent)), Mode = BindingMode.TwoWay });
            var percent = new TextBox { Text = row.Proportion, PlaceholderText = "%", Style = (Style)Application.Current.Resources["LimsCompactTextBoxStyle"] };
            AutomationProperties.SetName(percent, "Proporción (%) " + (ViewModel.SolventRows.IndexOf(row) + 1));
            percent.SetBinding(TextBox.TextProperty, new Binding { Source = row, Path = new PropertyPath(nameof(row.Proportion)), Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            var remove = new Button { Content = "×", Style = (Style)Application.Current.Resources["LimsSecondaryButtonStyle"], Padding = new Thickness(10, 4, 10, 4) };
            AutomationProperties.SetName(remove, "Quitar solvente " + (ViewModel.SolventRows.IndexOf(row) + 1));
            remove.Click += (_, _) => { LimsPopupField.CloseForRoot(XamlRoot); ViewModel.RemoveSolvent(row); };
            Grid.SetColumn(percent, 1); Grid.SetColumn(remove, 2);
            grid.Children.Add(selector); grid.Children.Add(percent); grid.Children.Add(remove);
            SolventRowsPanel.Children.Add(grid);
        }
    }
    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModel.IsSolventCatalogReady) && ViewModel.IsSolventCatalogReady) BuildSolventRows();
        Render();
    }
    private void OnSolventScroll(object sender, ScrollViewerViewChangedEventArgs args) => LimsPopupField.CloseForRootAfterLayout(this);
    private void Render()
    {
        if (!_ready) return;
        if (IsCommercial) { RenderCommercial(); return; }
        FormSurface.Visibility = Visibility.Visible; CommercialEntry.Visibility = Visibility.Collapsed;
        EntryTypeSelect.IsEnabled = !ViewModel.IsBusy;
        StockCompositionVisual.Render(CompositionBar, CompositionLegend, ViewModel.CompositionDetails);
        RetrySolventsButton.Visibility = ViewModel.IsSolventCatalogReady ? Visibility.Collapsed : Visibility.Visible;
        var success = ViewModel.Created is not null;
        StepScroll.Visibility = success ? Visibility.Collapsed : Visibility.Visible;
        SuccessSurface.Visibility = success ? Visibility.Visible : Visibility.Collapsed;
        Title = success ? "Stock creado" : "Nueva preparación Stock";
        MessageText.Visibility = string.IsNullOrWhiteSpace(ViewModel.Message) ? Visibility.Collapsed : Visibility.Visible;
        SaveButton.Visibility = success ? Visibility.Collapsed : Visibility.Visible;
        SaveButton.IsEnabled = ViewModel.CanSave;
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
    private void Resize()
    {
        var width = Math.Max(260, Math.Min(920, XamlRoot.Size.Width - 88));
        EditorRoot.Width = ViewModel.Created is null ? width : Math.Min(460, width);
        EditorRoot.MaxHeight = Math.Max(220, XamlRoot.Size.Height - 130);
        var narrow = width < 650;
        ResultColumn.Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetColumn(ResultPanel, narrow ? 0 : 1);
        Grid.SetRow(ResultPanel, narrow ? 1 : 0);
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
        ViewModel.SolventRows.CollectionChanged -= OnSolventRowsChanged;
        XamlRoot.Changed -= OnRootChanged;
    }
}

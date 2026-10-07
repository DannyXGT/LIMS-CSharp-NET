using System.ComponentModel;
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
    private bool _ready;
    public StockEditorDialog(StockViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        _searchTimer.Tick += async (_, _) => { _searchTimer.Stop(); await ViewModel.SearchSourcesAsync(_search, 1, CancellationToken.None); Render(); };
    }
    public StockViewModel ViewModel { get; }
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
        await ViewModel.SearchSourcesAsync(null, 1, CancellationToken.None);
        Render(); SourceList.Focus(FocusState.Programmatic);
    }
    private void OnModelChanged(object? sender, PropertyChangedEventArgs e) => Render();
    private void Render()
    {
        if (!_ready) return;
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
    private async void OnSave(object sender, RoutedEventArgs e) => await ViewModel.SaveAsync(CancellationToken.None);
    private void OnCancel(object sender, RoutedEventArgs e) { if (!ViewModel.IsBusy) Hide(); }
    private void OnViewCreated(object sender, RoutedEventArgs e) { ViewCreatedDetail = true; Hide(); }
    private void OnClosing(ContentDialog sender, ContentDialogClosingEventArgs args) { if (ViewModel.IsBusy) args.Cancel = true; else LimsPopupField.CloseForRoot(XamlRoot); }
    private void OnClosed(ContentDialog sender, ContentDialogClosedEventArgs args)
    {
        _ready = false; _searchTimer.Stop();
        ViewModel.PropertyChanged -= OnModelChanged;
        XamlRoot.Changed -= OnRootChanged;
    }
}

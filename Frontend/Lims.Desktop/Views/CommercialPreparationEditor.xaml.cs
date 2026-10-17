using Lims.Contracts.ReferenceMaterials;
using Lims.Contracts.ReferencePreparations;
using Lims.Desktop.Http;
using Lims.Desktop.Validation;
using Lims.DesignSystem.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lims.Desktop.Views;

public sealed partial class CommercialPreparationEditor : UserControl
{
    private ICommercialPreparationApiClient? _api;
    private string _kind = "Stock";
    private readonly Guid _requestId = Guid.NewGuid();
    private RegisterCommercialPreparationRequest? _pending;
    public bool IsBusy { get; private set; }
    public bool IsReady { get; private set; }
    public CommercialPreparationEditor() { InitializeComponent(); EntrySelect.ItemsSource = new[] { "Registrar un frasco nuevo", "Usar producto ya registrado" }; EntrySelect.SelectedIndex = 0; }
    public async Task InitializeAsync(ICommercialPreparationApiClient? api, string kind, string actor, ScrollViewer boundary)
    {
        if (IsReady) return;
        _api = api; _kind = kind; ResponsibleText.Text = actor;
        ComponentsEditor.SetPresentation(kind == "Intermedia", allowSingle: true);
        ReceivedPicker.Date = ReferenceMaterialPresentation.ApplicationToday().ToDateTime(TimeOnly.MinValue);
        foreach (var field in new LimsPopupField[] { EntrySelect, ExistingSelect, MethodSelect, VolumeUnitSelect, LocationSelect, ReceivedPicker, ExpirationPicker,
            (LimsDatePicker)ComponentsEditor.FindName("OpeningPicker"), (LimsSelect)ComponentsEditor.FindName("PeriodUnitSelect") }) field.PopupBoundary = boundary;
        await LoadOptionsAsync(null);
    }
    private async Task LoadOptionsAsync(string? search)
    {
        if (_api is null) { ErrorText.Text = "El servicio de ingreso comercial no está disponible."; return; }
        try
        {
            var result = await _api.OptionsAsync(_kind, search, CancellationToken.None);
            if (!result.IsSuccess || result.Value is not { } options) { ErrorText.Text = result.Error?.Message ?? "No se pudieron consultar los productos comerciales."; return; }
            MethodSelect.ItemsSource = options.Methods; LocationSelect.ItemsSource = options.Locations;
            VolumeUnitSelect.ItemsSource = options.Units.Where(u => u.Symbol is "mL" or "L").ToArray();
            VolumeUnitSelect.SelectedItem = options.Units.FirstOrDefault(u => u.Symbol == "mL");
            ExistingSelect.ItemsSource = options.Materials;
            IsReady = true; ErrorText.Text = "";
        }
        catch (HttpRequestException) { ErrorText.Text = "No se pudo conectar con el servidor."; }
    }
    public RegisterCommercialPreparationRequest ReadRequest()
    {
        if (EntrySelect.SelectedIndex == 1)
        {
            var existing = ExistingSelect.SelectedItem as ReferenceMaterialDetail ?? throw new ArgumentException("Seleccione el frasco existente.");
            return new(_requestId, existing.Id, existing.Version, null);
        }
        if (string.IsNullOrWhiteSpace(ProductNameBox.Text) || string.IsNullOrWhiteSpace(BrandBox.Text) || string.IsNullOrWhiteSpace(LotBox.Text) || string.IsNullOrWhiteSpace(StorageBox.Text) ||
            MethodSelect.SelectedItem is not ReferenceMethodOption method || LocationSelect.SelectedItem is not ReferenceLocationOption location ||
            VolumeUnitSelect.SelectedItem is not ReferenceUnitOption unit || !ReferenceMaterialInput.TryParseDecimal(VolumeBox.Text, 6, out var volume) || volume <= 0 ||
            ReceivedPicker.Date is not { } received || ExpirationPicker.Date is not { } expires || expires.Date < received.Date)
            throw new ArgumentException("Complete producto, método, fabricante, lote, volumen, fechas y almacenamiento.");
        var certificate = ComponentsEditor.Read();
        foreach (var component in certificate.Components) _ = StockUnits.MilligramsPerLiter(component.ConcentrationUnit);
        var presentation = certificate.Components.Count == 1 ? MaterialPresentations.Solution : MaterialPresentations.Mix;
        return new(_requestId, null, null, new(ProductNameBox.Text.Trim(), null, string.IsNullOrWhiteSpace(CatalogBox.Text) ? null : CatalogBox.Text.Trim(),
            method.Id, null, LotBox.Text.Trim(), BrandBox.Text.Trim(), DateOnly.FromDateTime(received.Date), DateOnly.FromDateTime(expires.Date),
            volume, unit.Id, 1, StorageBox.Text.Trim(), location.Id, presentation, certificate));
    }
    public async Task<CommercialPreparationDetail?> SaveAsync()
    {
        if (IsBusy || !IsReady || _api is null) return null;
        try
        {
            var request = _pending ?? ReadRequest();
            _pending = request; IsBusy = true; IsEnabled = false; ErrorText.Text = "";
            var result = await _api.RegisterAsync(_kind, request, CancellationToken.None);
            if (result.IsSuccess) return result.Value;
            ErrorText.Text = result.Error?.Message ?? "No se pudo guardar el ingreso comercial.";
            if (result.StatusCode is >= 400 and < 500) _pending = null;
        }
        catch (ArgumentException e) { ErrorText.Text = e.Message; }
        catch (HttpRequestException) { ErrorText.Text = "No se pudo confirmar el guardado. Reintente esta misma solicitud."; }
        catch (OperationCanceledException) { ErrorText.Text = "Se agotó el tiempo de espera. Reintente esta misma solicitud."; }
        finally { IsBusy = false; IsEnabled = _pending is null; }
        return null;
    }
    private void OnEntryChanged(object? sender, EventArgs e)
    {
        if (NewSurface is null || ExistingSurface is null) return;
        NewSurface.Visibility = EntrySelect.SelectedIndex == 1 ? Visibility.Collapsed : Visibility.Visible;
        ExistingSurface.Visibility = EntrySelect.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        UpdateBalance();
    }
    private void OnExistingChanged(object? sender, EventArgs e)
    {
        if (ExistingSelect.SelectedItem is ReferenceMaterialDetail m)
            ExistingSummary.Text = $"{m.Name} · Lote {m.Lot} · {m.AvailableDisplay} disponibles de {m.TotalDisplay}\nSe asociará este mismo frasco; su saldo y sus datos certificados se conservan.";
    }
    private async void OnSearchExisting(object sender, RoutedEventArgs e) => await LoadOptionsAsync(RemoteSearch.Text);
    private void OnVolumeChanged(object sender, TextChangedEventArgs e) => UpdateBalance();
    private void UpdateBalance() { if (BalanceText is not null) BalanceText.Text = $"{VolumeBox.Text} {(VolumeUnitSelect.SelectedItem as ReferenceUnitOption)?.Symbol} · Un solo saldo para todos los analitos"; }
    private void OnExpirationChanged(object? sender, EventArgs e) { if (ExpirationPicker.Date is { } d) ComponentsEditor.SetExpiration(DateOnly.FromDateTime(d.Date)); }
}

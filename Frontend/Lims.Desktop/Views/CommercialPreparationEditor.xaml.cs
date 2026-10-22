using Lims.Contracts.ReferenceMaterials;
using Lims.Contracts.ReferencePreparations;
using Lims.Desktop.Http;
using Lims.Desktop.ViewModels;
using Lims.Desktop.Validation;
using Lims.DesignSystem.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lims.Desktop.Views;

public sealed partial class CommercialPreparationEditor : UserControl
{
    private ICommercialPreparationApiClient? _api;
    public SolventCompositionViewModel Composition { get; } = new();
    private bool _supportsComposition;
    public bool CanSave => IsReady && (EntrySelect.SelectedIndex == 1 || (_supportsComposition && Composition.IsValid));
    public string SaveStatus => IsBusy ? "Guardando el producto…" : !IsReady ? "No se cargaron los catálogos. Revise la conexión con la API." :
        EntrySelect.SelectedIndex == 1 ? ExistingSelect.SelectedItem is null ? "Seleccione el frasco comercial existente." : "Listo para asociar el frasco." :
        !_supportsComposition ? "Integración pendiente: la API no admite guardar la composición de solventes." :
        !Composition.IsReady ? Composition.CatalogMessage : !Composition.IsValid ?
        Composition.Rows.Count == 0 || Composition.Rows.Any(r => r.SelectedSolvent is null) ? "Seleccione los solventes de la matriz." :
        Composition.Rows.Any(r => string.IsNullOrWhiteSpace(r.Proportion)) ? "Indique la proporción de cada solvente; el total debe ser 100 %." :
        Composition.Error ?? "Complete la composición de solventes." : "Composición completa · revise los datos del producto antes de guardar.";
    public event EventHandler? StateChanged;
    private string _kind = "Stock";
    private readonly Guid _requestId = Guid.NewGuid();
    private RegisterCommercialPreparationRequest? _pending;
    public bool IsBusy { get; private set; }
    public bool IsReady { get; private set; }
    public CommercialPreparationEditor() { InitializeComponent(); ErrorText.RegisterPropertyChangedCallback(TextBlock.TextProperty, (_, _) => ErrorText.Visibility = string.IsNullOrWhiteSpace(ErrorText.Text) ? Visibility.Collapsed : Visibility.Visible); EntrySelect.ItemsSource = new[] { "Registrar un frasco nuevo", "Usar producto ya registrado" }; EntrySelect.SelectedIndex = 0; }
    public async Task InitializeAsync(ICommercialPreparationApiClient? api, string kind, string actor, ScrollViewer boundary)
    {
        if (IsReady) return;
        _api = api; _kind = kind; ResponsibleText.Text = "Registrado por " + actor;
        CompositionEditor.Initialize(Composition, FieldsScroll);
        CompositionEditor.RetryRequested += async (_, _) => await LoadOptionsAsync(null);
        Composition.PropertyChanged += (_, _) => StateChanged?.Invoke(this, EventArgs.Empty);
        if (Composition.Rows.Count == 0) Composition.Add();
        var mix = kind == "Intermedia";
        CodeLabel.Text = mix ? "Código de Intermedia" : "Código Stock";
        ProductNameBox.Header = mix ? "Nombre del Mix *" : "Nombre del compuesto o solución *";
        ResultTitle.Text = "PRODUCTO COMERCIAL";
        ComponentsEditor.SetOperational(mix);
        ((StackPanel)ComponentsEditor.Content).Children.OfType<Grid>().First().Visibility = Visibility.Collapsed;
        ((FrameworkElement)ComponentsEditor.FindName("CompositionHint")).Visibility = Visibility.Collapsed;
        ((StackPanel)ComponentsEditor.Content).Spacing = 6;
        ComponentsEditor.Changed += (_, _) => { ComponentsEditor.SetSingleName(ProductNameBox.Text); UpdateBalance(); };
        ReceivedPicker.Date = ReferenceMaterialPresentation.ApplicationToday().ToDateTime(TimeOnly.MinValue);
        foreach (var field in new LimsPopupField[] { EntrySelect, ExistingSelect, MethodSelect, VolumeUnitSelect, LocationSelect, ReceivedPicker, ExpirationPicker }) field.PopupBoundary = FieldsScroll;
        EntrySelect.PopupBoundary = Surface; ExistingSelect.PopupBoundary = ExistingSurface;
        FieldsScroll.ViewChanged += (_, _) => LimsPopupField.CloseForRootAfterLayout(this);
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
            Composition.SetCatalog(options.Solvents);
            _supportsComposition = options.SupportsSolventComposition;
            IsReady = true; ErrorText.Text = _supportsComposition ? "" : "Actualice la API para guardar la composición de solventes. El servidor actual no confirma esta capacidad."; UpdateBalance(); StateChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (HttpRequestException) { ErrorText.Text = "No se pudo conectar con el servidor."; }
        catch (OperationCanceledException) { ErrorText.Text = "Se agotó la espera de los catálogos. Vuelva a seleccionar el origen comercial para reintentar."; }
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
        if (!ReferenceMaterialInput.TryParsePackageCount(BottleCountBox.Text, out var bottles)) throw new ArgumentException("Indique un número entero de frascos mayor que cero.");
        ComponentsEditor.SetSingleName(ProductNameBox.Text.Trim());
        var certificate = ComponentsEditor.Read();
        if (!_supportsComposition) throw new ArgumentException("Actualice la API para guardar la composición de solventes.");
        if (!Composition.IsValid) throw new ArgumentException(Composition.Error ?? Composition.CatalogMessage);
        certificate = certificate with { Matrix = Composition.Summary, MatrixSolventId = Composition.Inputs.Count == 1 ? Composition.Inputs[0].SolventId : null,
            Solvents = Composition.Inputs, CompositionBasis = StockSolventComposition.Basis, NominalConcentration = null, NominalUnit = null, UseNominalConcentration = false };
        foreach (var component in certificate.Components) _ = StockUnits.MilligramsPerLiter(component.ConcentrationUnit);
        var presentation = certificate.Components.Count == 1 ? MaterialPresentations.Solution : MaterialPresentations.Mix;
        return new(_requestId, null, null, new(ProductNameBox.Text.Trim(), null, string.IsNullOrWhiteSpace(CatalogBox.Text) ? null : CatalogBox.Text.Trim(),
            method.Id, null, LotBox.Text.Trim(), BrandBox.Text.Trim(), DateOnly.FromDateTime(received.Date), DateOnly.FromDateTime(expires.Date),
            volume, unit.Id, bottles, StorageBox.Text.Trim(), location.Id, presentation, certificate));
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
        UpdateEntryLayout(); UpdateBalance(); StateChanged?.Invoke(this, EventArgs.Empty);
    }
    private void OnExistingChanged(object? sender, EventArgs e)
    {
        if (ExistingSelect.SelectedItem is ReferenceMaterialDetail m)
            ExistingSummary.Text = $"{m.Name} · Lote {m.Lot} · {m.AvailableDisplay} disponibles de {m.TotalDisplay}\nSe asociará este mismo frasco; su saldo y sus datos certificados se conservan.";
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
    private async void OnSearchExisting(object sender, RoutedEventArgs e) => await LoadOptionsAsync(RemoteSearch.Text);
    private void OnVolumeChanged(object sender, TextChangedEventArgs e) => UpdateBalance();
    private void OnIdentityChanged(object sender, TextChangedEventArgs e) => UpdateBalance();
    private void OnProductChanged(object sender, TextChangedEventArgs e)
    {
        ComponentsEditor?.SetSingleName(ProductNameBox.Text); UpdateBalance();
    }
    private void OnSurfaceSizeChanged(object sender, SizeChangedEventArgs e) => UpdateEntryLayout();
    private void UpdateEntryLayout()
    {
        if (ResultSurface is null) return;
        EntryLayout.Visibility = EntrySelect.SelectedIndex == 1 ? Visibility.Collapsed : Visibility.Visible;
        var wide = Surface.ActualWidth >= 760;
        Grid.SetColumn(ResultSurface, wide ? 1 : 0); Grid.SetRow(ResultSurface, wide ? 0 : 1);
        ResultColumn.Width = wide ? new GridLength(260) : new GridLength(0);
    }
    private void UpdateBalance()
    {
        if (BalanceText is null || ComponentsEditor is null || ResultName is null) return;
        var rows = ComponentsEditor.PreviewComponents;
        ResultName.Text = string.IsNullOrWhiteSpace(ProductNameBox.Text) ? "Identifique el producto" : ProductNameBox.Text;
        ResultComponents.Text = _kind == "Intermedia" ? $"{rows.Count} componentes · un único inventario" : "CONCENTRACIÓN CERTIFICADA";
        ResultIdentity.Text = string.Join(" · ", new[] { BrandBox.Text, string.IsNullOrWhiteSpace(LotBox.Text) ? "" : "Lote " + LotBox.Text }.Where(s => !string.IsNullOrWhiteSpace(s)));
        ResultIdentity.Visibility = string.IsNullOrWhiteSpace(ResultIdentity.Text) ? Visibility.Collapsed : Visibility.Visible;
        if (_pending is null && !IsBusy && _supportsComposition) ErrorText.Text = "";
        var valid = ReferenceMaterialInput.TryParseDecimal(VolumeBox.Text, 6, out var volume) && volume > 0 &&
            ReferenceMaterialInput.TryParsePackageCount(BottleCountBox.Text, out _);
        var bottles = ReferenceMaterialInput.TryParsePackageCount(BottleCountBox.Text, out var n) ? n : 1;
        var unit = (VolumeUnitSelect.SelectedItem as ReferenceUnitOption)?.Symbol ?? "mL";
        try
        {
            BalanceText.Text = valid ? $"{volume * bottles:G29} {unit}" : "—";
            InitialVolumeText.Text = valid ? $"Inicial: {volume * bottles:G29} {unit} · 100 %" : "Volumen pendiente";
        }
        catch (OverflowException) { BalanceText.Text = "Revise el volumen y la cantidad de frascos."; valid = false; }
        BalanceBar.Value = valid ? 100 : 0;
        BottleVisual.SetLevel(valid ? 1 : 0);
        ResultValidity.Text = ExpirationPicker.Date is { } date
            ? $"Expira {date:dd/MM/yyyy} · {(DateOnly.FromDateTime(date.Date) < ReferenceMaterialPresentation.ApplicationToday() ? "Vencido" : "Vigente")}" : "Expiración pendiente";
        ResultConcentrations.Text = rows.Any(c => c.CertifiedConcentration > 0)
            ? string.Join("\n", rows.Where(c => c.CertifiedConcentration > 0).Select(c => $"{(_kind == "Stock" ? "" : c.Name + " · ")}{c.CertifiedConcentration.ToString("G29", System.Globalization.CultureInfo.CurrentCulture)} {c.ConcentrationUnit}")) : "—";
    }
    private void OnExpirationChanged(object? sender, EventArgs e) => UpdateBalance();
}

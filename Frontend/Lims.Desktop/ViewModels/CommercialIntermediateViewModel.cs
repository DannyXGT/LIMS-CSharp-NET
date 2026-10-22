using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Lims.Contracts.ReferenceMaterials;
using Lims.Contracts.ReferencePreparations;
using Lims.Desktop.Http;
using Lims.Desktop.Validation;

namespace Lims.Desktop.ViewModels;

public sealed partial class CommercialComponentRow : ObservableObject
{
    public IReadOnlyList<string> Units { get; } = CommercialIntermediateViewModel.ConcentrationUnits;
    public Guid Id { get; } = Guid.NewGuid();
    [ObservableProperty] public partial int Number { get; set; }
    [ObservableProperty] public partial string Name { get; set; } = "";
    [ObservableProperty] public partial string Cas { get; set; } = "";
    [ObservableProperty] public partial string Concentration { get; set; } = "";
    [ObservableProperty] public partial string Unit { get; set; } = "µg/mL";
    private bool _touched;
    public bool IsBlank => string.IsNullOrWhiteSpace(Name) && string.IsNullOrWhiteSpace(Cas) && string.IsNullOrWhiteSpace(Concentration);
    public bool IsValid => TryRead(out _, out _);
    public string VisibleError => _touched && !IsBlank && !TryRead(out _, out var error) ? error : "";
    public bool HasVisibleError => VisibleError.Length > 0;
    public void Touch() { _touched = true; OnPropertyChanged(nameof(VisibleError)); OnPropertyChanged(nameof(HasVisibleError)); }
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(Name) or nameof(Cas) or nameof(Concentration) or nameof(Unit))
        {
            OnPropertyChanged(nameof(IsBlank)); OnPropertyChanged(nameof(IsValid)); OnPropertyChanged(nameof(VisibleError)); OnPropertyChanged(nameof(HasVisibleError));
        }
    }
    public bool TryRead(out CertifiedComponent? component, out string error)
    {
        component = null; error = "";
        if (string.IsNullOrWhiteSpace(Name)) { error = "Indique el nombre del analito."; return false; }
        if (Name.Trim().Length > 200 || Cas.Trim().Length > 80 || Name.Contains('\t') || Name.Contains('\n') || Cas.Contains('\t') || Cas.Contains('\n'))
        { error = "Revise el nombre y el CAS."; return false; }
        if (!ReferenceMaterialInput.TryParseDecimal(Concentration, 28, out var value) || value <= 0)
        { error = "La concentración debe ser un decimal positivo."; return false; }
        try
        {
            component = CommercialComponentPaste.Parse($"{Name.Trim()}\t{Cas.Trim()}\t{Concentration.Trim()}\t{Unit}").Single() with { Id = Id };
            return true;
        }
        catch (ArgumentException e) { error = e.Message; return false; }
    }
    public static CommercialComponentRow From(CertifiedComponent value) => new()
    { Name = value.Name, Cas = value.CasNumber ?? "", Concentration = value.CertifiedConcentration.ToString("G29", CultureInfo.InvariantCulture), Unit = value.ConcentrationUnit };
}

// Presentation state only. Registration, identities, balance and ledger stay in the existing commercial service.
public sealed partial class CommercialIntermediateViewModel : ObservableObject
{
    private readonly ICommercialPreparationApiClient? _api;
    private readonly Func<bool> _canRegister;
    private readonly Guid _requestId = Guid.NewGuid();
    private readonly HashSet<string> _touched = [];
    private RegisterCommercialPreparationRequest? _pending;
    private bool _refreshing;
    private bool _batching;
    public CommercialIntermediateViewModel(ICommercialPreparationApiClient? api, string actor, Func<bool> canRegister)
    {
        Composition.PropertyChanged += (_, _) => Refresh();
        _api = api; RegisteredBy = actor; _canRegister = canRegister;
        ReceivedDate = ReferenceMaterialPresentation.ApplicationToday().ToDateTime(TimeOnly.MinValue);
        Rows.CollectionChanged += (_, e) =>
        {
            if (e.OldItems is not null) foreach (CommercialComponentRow row in e.OldItems) row.PropertyChanged -= OnRowChanged;
            if (e.NewItems is not null) foreach (CommercialComponentRow row in e.NewItems) row.PropertyChanged += OnRowChanged;
            for (var i = 0; i < Rows.Count; i++) Rows[i].Number = i + 1;
            Refresh();
        };
    }
    public static IReadOnlyList<string> ConcentrationUnits { get; } = ["µg/mL", "mg/L", "µg/L", "g/L"];
    public string RegisteredBy { get; }
    public ObservableCollection<CommercialComponentRow> Rows { get; } = [];
    public ObservableCollection<ReferenceMethodOption> Methods { get; } = [];
    public ObservableCollection<ReferenceLocationOption> Locations { get; } = [];
    public ObservableCollection<ReferenceUnitOption> VolumeUnits { get; } = [];
    public SolventCompositionViewModel Composition { get; } = new();
    public ObservableCollection<SolventOption> Solvents => Composition.Catalog;
    public bool SupportsSolventComposition { get; private set; }
    public ObservableCollection<ReferenceMaterialDetail> ExistingMaterials { get; } = [];
    [ObservableProperty] public partial string Name { get; set; } = "";
    [ObservableProperty] public partial string Brand { get; set; } = "";
    [ObservableProperty] public partial string Catalog { get; set; } = "";
    [ObservableProperty] public partial string Lot { get; set; } = "";
    [ObservableProperty] public partial ReferenceMethodOption? Method { get; set; }
    [ObservableProperty] public partial string Volume { get; set; } = "";
    [ObservableProperty] public partial string BottleCount { get; set; } = "1";
    [ObservableProperty] public partial ReferenceUnitOption? VolumeUnit { get; set; }
    [ObservableProperty] public partial DateTimeOffset? ReceivedDate { get; set; }
    [ObservableProperty] public partial DateTimeOffset? ExpirationDate { get; set; }
    [ObservableProperty] public partial ReferenceLocationOption? Location { get; set; }
    [ObservableProperty] public partial string Storage { get; set; } = "";
    [ObservableProperty] public partial bool UseExisting { get; set; }
    [ObservableProperty] public partial ReferenceMaterialDetail? ExistingMaterial { get; set; }
    [ObservableProperty] public partial bool IsReady { get; set; }
    [ObservableProperty] public partial bool IsLoading { get; set; }
    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial string Message { get; set; } = "";
    [ObservableProperty] public partial string ImportMessage { get; set; } = "";
    [ObservableProperty] public partial CommercialPreparationDetail? Created { get; set; }
    public bool InputsEnabled => _canRegister() && !IsBusy && !IsLoading && _pending is null && Created is null;
    public bool CanCancel => !IsBusy;
    public bool IdentificationValid => new[] { nameof(Name), nameof(Method), nameof(Brand), nameof(Lot), nameof(Catalog) }.All(p => RawError(p).Length == 0);
    public bool InventoryValid => new[] { nameof(Volume), nameof(BottleCount), nameof(VolumeUnit), nameof(ReceivedDate), nameof(ExpirationDate), nameof(Location), nameof(Storage) }.All(p => RawError(p).Length == 0);
    public int ValidCount => UseExisting ? ExistingMaterial?.Commercial?.Components.Count ?? 0 : Rows.Count(r => r.IsValid);
    public int IncompleteCount => Rows.Count(r => !r.IsBlank && !r.IsValid);
    public string CountDisplay => $"{ValidCount} analitos válidos";
    public string ComponentStatus => UseExisting ? (ExistingMaterial is null ? "Seleccione un producto registrado" : "Composición conservada") :
        IncompleteCount > 0 ? $"{IncompleteCount} filas por completar" : ValidCount == 0 ? "Sin analitos registrados" : "Componentes completos";
    public string ProductDisplay => UseExisting ? ExistingMaterial?.Name ?? "Seleccione el producto" : string.IsNullOrWhiteSpace(Name) ? "Nombre del Mix pendiente" : Name.Trim();
    public string MethodDisplay => UseExisting ? ExistingMaterial?.Method ?? "Método pendiente" : Method?.Name ?? "Método pendiente";
    public string CodeDisplay => Created?.Code ?? "Automático al guardar";
    public decimal? InitialVolume
    {
        get
        {
            if (UseExisting) return ExistingMaterial?.TotalQuantity;
            if (!ReferenceMaterialInput.TryParseDecimal(Volume, 6, out var value) || value <= 0 || !ReferenceMaterialInput.TryParsePackageCount(BottleCount, out var count)) return null;
            try { var total = checked(value * count); return total < 1000000000000m ? total : null; } catch (OverflowException) { return null; }
        }
    }
    public decimal? AvailableVolume => UseExisting ? ExistingMaterial?.AvailableQuantity : InitialVolume;
    public string VolumeSymbol => UseExisting ? ExistingMaterial?.Unit ?? "mL" : VolumeUnit?.Symbol ?? "mL";
    public double AvailabilityPercent => new StockAvailability(InitialVolume ?? 0, VolumeSymbol, AvailableVolume).ProgressValue;
    public string InitialDisplay => InitialVolume is { } n && (UseExisting || VolumeUnit is not null) ? StockPresentation.Quantity(n, VolumeSymbol) : "—";
    public string AvailableDisplay => AvailableVolume is { } n && (UseExisting || VolumeUnit is not null) ? StockPresentation.Quantity(n, VolumeSymbol) : "—";
    public string AvailabilityDisplay => InitialVolume > 0 && AvailableVolume.HasValue ? $"{AvailabilityPercent:0.#} % disponible" : "Volumen pendiente";
    public string BottlesDisplay => UseExisting ? $"{ExistingMaterial?.PackageCount ?? 0} frascos" : ReferenceMaterialInput.TryParsePackageCount(BottleCount, out var n) ? $"{n} frascos" : "Cantidad pendiente";
    public DateOnly? EffectiveExpiration => UseExisting ? ExistingMaterial?.EffectiveExpirationDate ?? ExistingMaterial?.ExpirationDate : ExpirationDate is { } d ? DateOnly.FromDateTime(d.Date) : null;
    public string ExpirationDisplay => EffectiveExpiration is { } d ? ReferenceMaterialPresentation.Date(d) : "Fecha pendiente";
    public string ExpirationStatus => EffectiveExpiration is not { } d ? "Sin fecha de expiración" : d < ReferenceMaterialPresentation.ApplicationToday() ? "Vencido" : "Vigente";
    public string MaterialStatus => UseExisting ? ExistingMaterial?.StatusLabel ?? "Sin producto" : InitialVolume is null ? "Inventario pendiente" : "Nuevo ingreso · sin consumos";
    public IReadOnlyList<CertifiedComponent> PreviewComponents => UseExisting ? ExistingMaterial?.Commercial?.Components ?? [] :
        Rows.Where(r => r.IsValid).Select(r => { r.TryRead(out var c, out _); return c!; }).ToArray();
    public string ValidationStatus => !IsReady ? "Cargando catálogos" : _pending is not null ? "Guardado por confirmar · reintente la misma solicitud" :
        UseExisting ? ExistingMaterial is null ? "Seleccione el producto comercial" : "Listo para asociar el mismo inventario" :
        !IdentificationValid ? "Complete la identificación del producto" : ValidCount == 0 ? "Agregue al menos un analito válido" : IncompleteCount > 0 ? "Revise las filas incompletas" :
        !InventoryValid ? "Complete el inventario y la vigencia" : !SupportsSolventComposition ? "Actualice la API para guardar la composición de solventes" : !Composition.IsValid ? Composition.Error ?? Composition.CatalogMessage : "Listo para registrar la Intermedia";
    public bool CanSave => _canRegister() && IsReady && !IsBusy && !IsLoading && Created is null && (_pending is not null ||
        (UseExisting ? ExistingMaterial?.Commercial is { Components.Count: > 0 } : IdentificationValid && InventoryValid && ValidCount > 0 && IncompleteCount == 0 && SupportsSolventComposition && Composition.IsValid));
    public string SaveLabel => IsBusy ? "Guardando…" : _pending is null ? "Guardar Intermedia" : "Reintentar guardado";
    public string Error(string property) => _touched.Contains(property) ? RawError(property) : "";
    public void Touch(string property) { _touched.Add(property); Refresh(); }
    private string RawError(string property) => property switch
    {
        nameof(Name) => string.IsNullOrWhiteSpace(Name) ? "Indique el nombre del Mix." : Name.Trim().Length > 200 ? "Máximo 200 caracteres." : "",
        nameof(Method) => Method is null ? "Seleccione el método." : "",
        nameof(Brand) => string.IsNullOrWhiteSpace(Brand) ? "Indique el fabricante." : Brand.Trim().Length > 120 ? "Máximo 120 caracteres." : "",
        nameof(Lot) => string.IsNullOrWhiteSpace(Lot) ? "Indique el lote." : Lot.Trim().Length > 120 ? "Máximo 120 caracteres." : "",
        nameof(Catalog) => Catalog.Trim().Length > 80 ? "Máximo 80 caracteres." : "",
        nameof(Volume) => InitialVolume is null ? "Indique un volumen positivo válido (hasta 6 decimales)." : "",
        nameof(BottleCount) => ReferenceMaterialInput.TryParsePackageCount(BottleCount, out _) ? "" : "Indique una cantidad entera positiva.",
        nameof(VolumeUnit) => VolumeUnit is null ? "Seleccione la unidad." : "",
        nameof(ReceivedDate) => ReceivedDate is null ? "Indique la fecha de ingreso." : "",
        nameof(ExpirationDate) => ExpirationDate is null ? "Indique la expiración." : ReceivedDate is { } d && ExpirationDate.Value.Date < d.Date ? "La expiración debe ser igual o posterior al ingreso." : "",
        nameof(Location) => Location is null ? "Seleccione la ubicación." : "",
        nameof(Storage) => string.IsNullOrWhiteSpace(Storage) ? "Indique las condiciones." : Storage.Trim().Length > 160 ? "Máximo 160 caracteres." : "",
        _ => ""
    };
    protected override void OnPropertyChanged(PropertyChangedEventArgs e) { base.OnPropertyChanged(e); if (!_refreshing && !_batching) Refresh(); }
    private void OnRowChanged(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName is nameof(CommercialComponentRow.Name) or nameof(CommercialComponentRow.Cas) or nameof(CommercialComponentRow.Concentration) or nameof(CommercialComponentRow.Unit)) Refresh(); }
    public void Refresh()
    {
        if (_refreshing || _batching) return;
        _refreshing = true;
        try
        {
            foreach (var property in new[] { nameof(InputsEnabled), nameof(CanCancel), nameof(CanSave), nameof(CountDisplay), nameof(ValidCount), nameof(ComponentStatus), nameof(ProductDisplay), nameof(MethodDisplay), nameof(CodeDisplay), nameof(InitialDisplay), nameof(AvailableDisplay), nameof(AvailabilityDisplay), nameof(AvailabilityPercent), nameof(BottlesDisplay), nameof(ExpirationDisplay), nameof(ExpirationStatus), nameof(MaterialStatus), nameof(ValidationStatus), nameof(PreviewComponents), nameof(SaveLabel), nameof(IdentificationValid), nameof(InventoryValid) }) OnPropertyChanged(property);
        }
        finally { _refreshing = false; }
    }
    public CommercialComponentRow? AddRow() { if (!InputsEnabled) return null; var row = new CommercialComponentRow(); Rows.Add(row); return row; }
    public void RemoveRow(CommercialComponentRow row) { if (InputsEnabled) Rows.Remove(row); }
    public bool Paste(string text)
    {
        if (!InputsEnabled) return false;
        try
        {
            var components = CommercialComponentPaste.Parse(text); // Parse the entire paste before changing any row.
            _batching = true;
            foreach (var row in Rows.Where(r => r.IsBlank).ToArray()) Rows.Remove(row);
            foreach (var c in components) Rows.Add(CommercialComponentRow.From(c));
            ImportMessage = $"{components.Count} analitos importados. Revise las concentraciones antes de guardar.";
            return true;
        }
        catch (ArgumentException e) { ImportMessage = e.Message; return false; }
        finally { _batching = false; Refresh(); }
    }
    public async Task LoadAsync(string? search = null, CancellationToken ct = default)
    {
        if (IsLoading || IsBusy || _pending is not null) return;
        IsLoading = true; Message = "";
        try
        {
            if (_api is null) { Message = "El servicio comercial no está disponible."; return; }
            var response = await _api.OptionsAsync("Intermedia", search, ct);
            if (!response.IsSuccess || response.Value is not { } options) { Message = response.Error?.Message ?? "No se pudieron consultar los catálogos comerciales."; return; }
            Replace(Methods, options.Methods); Replace(Locations, options.Locations); Replace(VolumeUnits, options.Units.Where(u => u.Symbol is "mL" or "L"));
            Composition.SetCatalog(options.Solvents); SupportsSolventComposition = options.SupportsSolventComposition;
            if (Composition.Rows.Count == 0) Composition.Add();
            Replace(ExistingMaterials, options.Materials);
            VolumeUnit ??= VolumeUnits.FirstOrDefault(u => u.Symbol == "mL");
            IsReady = true;
        }
        catch (HttpRequestException) { Message = "No se pudo conectar con el servidor. Reintente cargar los catálogos."; }
        catch (OperationCanceledException) { Message = "Se agotó la espera del servidor. Reintente cargar los catálogos."; }
        finally { IsLoading = false; }
    }
    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values) { target.Clear(); foreach (var value in values) target.Add(value); }
    public RegisterCommercialPreparationRequest ReadRequest()
    {
        if (UseExisting)
        {
            if (ExistingMaterial is not { Commercial.Components.Count: > 0 } existing) throw new ArgumentException("Seleccione un producto comercial registrado.");
            return new(_requestId, existing.Id, existing.Version, null);
        }
        if (!IdentificationValid || !InventoryValid || ValidCount == 0 || IncompleteCount > 0 || !SupportsSolventComposition || !Composition.IsValid) throw new ArgumentException(ValidationStatus);
        var components = PreviewComponents;
        var certificate = new CommercialCertificate("", Composition.Summary, null, null, null, null, null, null, components,
            MatrixSolventId: Composition.Inputs.Count == 1 ? Composition.Inputs[0].SolventId : null, Solvents: Composition.Inputs, CompositionBasis: StockSolventComposition.Basis);
        return new(_requestId, null, null, new(Name.Trim(), null, string.IsNullOrWhiteSpace(Catalog) ? null : Catalog.Trim(), Method!.Id, null, Lot.Trim(), Brand.Trim(),
            DateOnly.FromDateTime(ReceivedDate!.Value.Date), DateOnly.FromDateTime(ExpirationDate!.Value.Date), ReferenceMaterialInput.ParseDecimal(Volume), VolumeUnit!.Id,
            ReferenceMaterialInput.ParsePackageCount(BottleCount), Storage.Trim(), Location!.Id, components.Count == 1 ? MaterialPresentations.Solution : MaterialPresentations.Mix, certificate));
    }
    public async Task<CommercialPreparationDetail?> SaveAsync(CancellationToken ct = default)
    {
        if (!CanSave || _api is null) return null;
        IsBusy = true;
        try
        {
            _pending ??= ReadRequest(); Message = "";
            var response = await _api.RegisterAsync("Intermedia", _pending, ct);
            if (response.IsSuccess && response.Value is { } saved) { Created = saved; return saved; }
            Message = response.Error?.Message ?? "No se confirmó el guardado. Reintente esta misma solicitud.";
            if (response.StatusCode is >= 400 and < 500) _pending = null;
        }
        catch (HttpRequestException) { Message = "No se confirmó el guardado. Reintente esta misma solicitud."; }
        catch (OperationCanceledException) { Message = "Se agotó la espera del guardado. Reintente esta misma solicitud."; }
        finally { IsBusy = false; }
        return null;
    }
}

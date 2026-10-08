using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Lims.Contracts.ReferenceMaterials;
using Lims.Contracts.ReferencePreparations;
using Lims.Desktop.Http;
using Lims.Desktop.Services;
using Lims.Desktop.Validation;

namespace Lims.Desktop.ViewModels;

public sealed partial class IntermediateComponentViewModel : ObservableObject
{
    public ObservableCollection<IntermediateSource> Sources { get; } = [];
    [ObservableProperty] public partial IntermediateSource? Source { get; set; }
    [ObservableProperty] public partial string Volume { get; set; } = string.Empty;
    [ObservableProperty] public partial string Unit { get; set; } = "mL";
    [ObservableProperty] public partial string Error { get; set; } = string.Empty;
    [ObservableProperty] public partial string BalanceDisplay { get; set; } = "Seleccione un origen disponible.";
    public string IdentityDisplay => Source is null ? string.Empty : $"{Source.KindLabel} · {Source.ConcentrationDisplay}";
    public string AnalytesDisplay => Source?.ConcentrationDisplay ?? string.Empty;
    public IReadOnlyList<string> Units { get; } = ["mL", "L"];
    public bool HasError => Error.Length > 0;
    partial void OnErrorChanged(string value) => OnPropertyChanged(nameof(HasError));
    partial void OnSourceChanged(IntermediateSource? value) { OnPropertyChanged(nameof(IdentityDisplay)); OnPropertyChanged(nameof(AnalytesDisplay)); }
}

public sealed partial class IntermediateViewModel(IIntermediateApiClient api, ISessionService session) : ObservableObject
{
    private readonly List<IntermediateSource> _sources = [];
    private int _listRevision;
    private CreateIntermediateRequest? _pending;
    private Guid _requestId = Guid.NewGuid();
    private bool _updating;
    public ObservableCollection<IntermediateComponentViewModel> Components { get; } = [];
    public ObservableCollection<IntermediateSummary> Items { get; } = [];
    public ObservableCollection<IntermediateMethod> Methods { get; } = [];
    public ObservableCollection<IntermediateSource> DilutionSources { get; } = [];
    public IReadOnlyList<string> VolumeUnits { get; } = ["mL", "L"];
    [ObservableProperty] public partial IntermediateMethod? Method { get; set; }
    [ObservableProperty] public partial IntermediateSource? DilutedSource { get; set; }
    [ObservableProperty] public partial string Name { get; set; } = string.Empty;
    [ObservableProperty] public partial string FinalVolume { get; set; } = string.Empty;
    [ObservableProperty] public partial string FinalUnit { get; set; } = "mL";
    [ObservableProperty] public partial DateTimeOffset? PreparationDate { get; set; }
    [ObservableProperty] public partial DateTimeOffset? ExpirationDate { get; set; }
    [ObservableProperty] public partial string Notes { get; set; } = string.Empty;
    [ObservableProperty] public partial string Message { get; set; } = string.Empty;
    [ObservableProperty] public partial string ListMessage { get; set; } = string.Empty;
    [ObservableProperty] public partial string NameError { get; set; } = string.Empty;
    [ObservableProperty] public partial string DateError { get; set; } = string.Empty;
    [ObservableProperty] public partial string MethodError { get; set; } = string.Empty;
    [ObservableProperty] public partial string VolumeError { get; set; } = string.Empty;
    [ObservableProperty] public partial IntermediateCalculation? Calculation { get; set; }
    [ObservableProperty] public partial IntermediateDetail? Created { get; set; }
    [ObservableProperty] public partial IntermediateDetail? Detail { get; set; }
    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial bool IsLoadingOptions { get; set; }
    [ObservableProperty] public partial int Page { get; set; } = 1;
    [ObservableProperty] public partial int TotalCount { get; set; }
    public string PreparedBy => session.Profile?.Name ?? "Usuario no informado";
    public bool CanView => HasPermission(ReferenceMaterialPermissions.View);
    public bool CanCreate => CanView && HasPermission(ReferenceMaterialPermissions.Create);
    public bool CanSave => CanCreate && !IsBusy && !IsLoadingOptions && Created is null && (_pending is not null ||
        (NameError.Length == 0 && MethodError.Length == 0 && DateError.Length == 0 && VolumeError.Length == 0 && Notes.Trim().Length <= 2000 &&
        Calculation is { ExceedsFinalVolume: false, HasSufficientVolume: true } && Components.All(c => c.Error.Length == 0)));
    public bool InputsEnabled => !IsBusy && !IsLoadingOptions && _pending is null;
    public bool HasNameError => NameError.Length > 0 && Name.Length > 0;
    public bool HasDateError => DateError.Length > 0 && ExpirationDate is not null;
    public bool HasVolumeError => VolumeError.Length > 0 && FinalVolume.Length > 0;
    public bool CanAddComponent => InputsEnabled && DilutedSource is null && Components.Count < 100;
    public bool HasMultiplePages => TotalCount > 25;
    public bool HasPreviousPage => Page > 1;
    public bool HasNextPage => Page * 25 < TotalCount;
    public string PageDisplay => $"Página {Page} · {TotalCount} intermedias";
    public decimal TotalTakenMl => Components.Where(c => c.Source is not null).Sum(c => Parse(c.Volume, out var v) ? SafeMilliliters(v, c.Unit) : 0);
    public decimal FinalMl => Parse(FinalVolume, out var v) ? SafeMilliliters(v, FinalUnit) : 0;
    public decimal OccupiedPercent => IntermediateCalculator.OccupiedPercent(TotalTakenMl, FinalMl);
    public string TotalDisplay => StockPresentation.Quantity(TotalTakenMl, "mL");
    public string FinalDisplay => StockPresentation.Quantity(FinalMl, "mL");
    public string RemainingDisplay => StockPresentation.Quantity(FinalMl - TotalTakenMl, "mL");
    public string ResultDisplay => Calculation?.ConcentrationDisplay ?? "Agregue componentes para comenzar.";

    public void BeginNew()
    {
        _updating = true;
        ClearComponents(); _pending = null; _requestId = Guid.NewGuid(); Created = null;
        Name = FinalVolume = Notes = Message = string.Empty; FinalUnit = "mL"; Method = null; DilutedSource = null;
        PreparationDate = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-6)); ExpirationDate = null;
        _updating = false; Recalculate();
    }
    public async Task LoadOptionsAsync(CancellationToken cancellationToken)
    {
        if (!CanCreate || IsBusy || _pending is not null || IsLoadingOptions) return;
        IsLoadingOptions = true;
        try
        {
            var response = await api.OptionsAsync(cancellationToken).ConfigureAwait(true);
            if (!response.IsSuccess || response.Value is null) { Message = Error(response.Error?.Message); return; }
            var previousMethod = Method;
            _updating = true;
            _sources.Clear(); _sources.AddRange(response.Value.Sources);
            Replace(Methods, response.Value.Methods);
            Method = Methods.FirstOrDefault(m => m.Id == previousMethod?.Id);
            foreach (var row in Components)
                if (row.Source is { } source && _sources.Find(s => s.Id == source.Id) is { } fresh) row.Source = fresh;
            if (DilutedSource is { } parent) DilutedSource = _sources.Find(s => s.Id == parent.Id) ?? parent;
            _updating = false;
            UpdateSources(); Recalculate();
        }
        catch (HttpRequestException) { Message = "No se pudo conectar con el servidor."; }
        catch (OperationCanceledException) { Message = "La consulta se canceló."; }
        finally { _updating = false; IsLoadingOptions = false; }
    }
    public IntermediateComponentViewModel AddComponent()
    {
        var row = new IntermediateComponentViewModel(); row.PropertyChanged += OnComponentChanged;
        Components.Add(row); UpdateSources(); Recalculate(); return row;
    }
    public void RemoveComponent(IntermediateComponentViewModel row)
    {
        row.PropertyChanged -= OnComponentChanged; Components.Remove(row); UpdateSources(); Recalculate();
    }
    public void ClearComponents()
    {
        foreach (var row in Components) row.PropertyChanged -= OnComponentChanged;
        Components.Clear(); Recalculate();
    }
    public void SelectDilution(IntermediateSource? source)
    {
        _updating = true; ClearComponents(); DilutedSource = source;
        if (source is not null) { var row = AddComponent(); row.Source = source; }
        _updating = false; UpdateSources(); Recalculate();
    }
    private void UpdateSources()
    {
        SyncSources(DilutionSources, _sources.Where(s => s.Kind == "Intermedia" && (Method is null || s.MethodId == Method.Id)), DilutedSource);
        foreach (var row in Components)
            SyncSources(row.Sources, DilutedSource is { } parent ? [parent] : _sources.Where(s => s.Kind == "Stock" && (Method is null || s.MethodId == Method.Id)), row.Source);
    }
    private void OnComponentChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IntermediateComponentViewModel.Source) or nameof(IntermediateComponentViewModel.Volume) or nameof(IntermediateComponentViewModel.Unit)) Recalculate();
    }
    public void Recalculate()
    {
        if (_updating) return;
        _pending = null; _requestId = Guid.NewGuid(); Calculation = null; Message = string.Empty;
        NameError = Name.Trim().Length is > 0 and <= 200 ? string.Empty : "Ingrese el nombre de la solución.";
        MethodError = Method is null ? "Seleccione un método." : string.Empty;
        var today = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-6)).Date;
        DateError = PreparationDate is null || ExpirationDate is null || PreparationDate.Value.Date > today ||
            ExpirationDate.Value.Date < PreparationDate.Value.Date || ExpirationDate.Value.Date < today
            ? "Seleccione preparación y expiración válidas; la preparación no puede ser futura." : string.Empty;
        VolumeError = Parse(FinalVolume, out var final) && final > 0 ? string.Empty : "Ingrese un volumen de aforo positivo.";
        foreach (var row in Components)
        {
            row.Error = row.Source is null ? "Seleccione un origen." : row.Source.MethodId != Method?.Id ? "Origen incompatible con el método; revise su selección." : string.Empty;
            if (!Parse(row.Volume, out var volume) || volume <= 0) row.Error = "Ingrese el volumen tomado positivo, con hasta seis decimales.";
            if (row.Source is { } source)
            {
                if (!_sources.Any(s => s.Id == source.Id && s.Version == source.Version)) row.Error = "El origen ya no está disponible. Seleccione otro origen.";
                var debit = SafeVolume(volume, row.Unit, source.VolumeUnit);
                row.BalanceDisplay = $"Disponible {source.AvailableDisplay} · Después {StockPresentation.Quantity(source.AvailableVolume - debit, source.VolumeUnit)}";
                if (debit > source.AvailableVolume) row.Error = "No hay volumen disponible suficiente en este origen.";
                if (PreparationDate is { } date && (DateOnly.FromDateTime(date.Date) < source.PreparationDate || DateOnly.FromDateTime(date.Date) > source.ExpirationDate))
                    row.Error = "La fecha de preparación queda fuera de la vigencia del origen.";
            }
        }
        if (TryCalculationRequest(out var request))
        {
            try
            {
                Calculation = IntermediateCalculator.Calculate(request!, Components.Select(c => c.Source!).ToArray());
                if (Calculation.ExceedsFinalVolume) VolumeError = "El volumen de los componentes supera el volumen de aforo.";
            }
            catch (ArgumentException e) { Message = e.Message; }
            catch (OverflowException) { Message = "Las cantidades exceden el rango admitido."; }
        }
        Notify();
    }
    private bool TryCalculationRequest(out IntermediateCalculationRequest? request)
    {
        request = null;
        if (Method is null || !Parse(FinalVolume, out var volume) || volume <= 0 || Components.Count == 0) return false;
        var components = new List<IntermediateComponentRequest>();
        foreach (var row in Components)
        {
            if (row.Source is null || !Parse(row.Volume, out var taken) || taken <= 0) return false;
            components.Add(new(row.Source.Id, row.Source.Version, taken, row.Unit));
        }
        request = new(Method.Id, volume, FinalUnit, components, DilutedSource?.Id); return true;
    }
    public async Task<bool> SaveAsync(CancellationToken cancellationToken)
    {
        if (!CanSave) return false;
        if (_pending is null)
        {
            if (!TryCalculationRequest(out var calculation)) return false;
            _pending = new(_requestId, Name.Trim(), calculation!, DateOnly.FromDateTime(PreparationDate!.Value.Date), DateOnly.FromDateTime(ExpirationDate!.Value.Date), Notes);
        }
        IsBusy = true; Message = string.Empty;
        try
        {
            var response = await api.CreateAsync(_pending, cancellationToken).ConfigureAwait(true);
            if (!response.IsSuccess || response.Value is null)
            {
                if (response.StatusCode is >= 400 and < 500) _pending = null;
                Message = Error(response.Error?.Message); return false;
            }
            Created = response.Value; _pending = null;
            await LoadAsync(null, 1, cancellationToken).ConfigureAwait(true); return true;
        }
        catch (HttpRequestException) { Message = "No se pudo confirmar la respuesta. Reintente la misma solicitud; no se duplicará el consumo."; return false; }
        catch (OperationCanceledException) { Message = "No se pudo confirmar la respuesta. Reintente la misma solicitud."; return false; }
        finally { IsBusy = false; Notify(); }
    }
    public async Task LoadAsync(string? search, int page, CancellationToken cancellationToken)
    {
        if (!CanView) { ListMessage = "No tiene permiso para consultar preparaciones."; return; }
        var revision = ++_listRevision;
        try
        {
            var response = await api.ListAsync(search, page, cancellationToken).ConfigureAwait(true);
            if (revision != _listRevision) return;
            if (!response.IsSuccess || response.Value is null) { ListMessage = Error(response.Error?.Message); return; }
            Replace(Items, response.Value.Items); Page = response.Value.Page; TotalCount = response.Value.TotalCount;
            ListMessage = Items.Count == 0 ? "No hay soluciones intermedias para esta búsqueda." : string.Empty;
            Notify();
        }
        catch (HttpRequestException) { if (revision == _listRevision) ListMessage = "No se pudo conectar con el servidor."; }
        catch (OperationCanceledException) { }
    }
    public async Task<bool> OpenDetailAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!CanView) return false;
        try
        {
            var response = await api.GetAsync(id, cancellationToken).ConfigureAwait(true);
            if (!response.IsSuccess || response.Value is null) { ListMessage = Error(response.Error?.Message); return false; }
            Detail = response.Value; return true;
        }
        catch (HttpRequestException) { ListMessage = "No se pudo conectar con el servidor."; return false; }
        catch (OperationCanceledException) { return false; }
    }
    private bool HasPermission(string permission) => string.Equals(session.Profile?.Role, "Administrador", StringComparison.OrdinalIgnoreCase) || session.Profile?.Permissions.Contains(permission, StringComparer.Ordinal) == true;
    private static bool Parse(string text, out decimal value) => ReferenceMaterialInput.TryParseDecimal(text, 6, out value) && value is >= 0 and < 1000000000000m;
    private static decimal SafeMilliliters(decimal volume, string unit) => SafeVolume(volume, unit, "mL");
    private static decimal SafeVolume(decimal volume, string from, string to) { try { return StockUnits.ConvertVolume(volume, from, to); } catch (ArgumentException) { return 0; } }
    private static string Error(string? message) => string.IsNullOrWhiteSpace(message) ? "No se pudo completar la operación. Intente nuevamente." : message;
    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values) { target.Clear(); foreach (var value in values) target.Add(value); }
    private static void SyncSources(ObservableCollection<IntermediateSource> target, IEnumerable<IntermediateSource> values, IntermediateSource? selected)
    {
        var desired = values.ToList();
        if (selected is not null && !desired.Contains(selected)) desired.Add(selected);
        foreach (var item in desired) if (!target.Contains(item)) target.Add(item);
        foreach (var item in target.ToArray()) if (!desired.Contains(item)) target.Remove(item);
    }
    private void Notify()
    {
        foreach (var property in new[] { nameof(CanSave), nameof(InputsEnabled), nameof(CanAddComponent), nameof(TotalTakenMl), nameof(FinalMl), nameof(OccupiedPercent), nameof(TotalDisplay), nameof(FinalDisplay), nameof(RemainingDisplay), nameof(ResultDisplay), nameof(PageDisplay), nameof(HasMultiplePages), nameof(HasPreviousPage), nameof(HasNextPage), nameof(HasNameError), nameof(HasDateError), nameof(HasVolumeError) }) OnPropertyChanged(property);
    }
    partial void OnMethodChanged(IntermediateMethod? value) { if (!_updating) UpdateSources(); Recalculate(); }
    partial void OnNameChanged(string value) => Recalculate();
    partial void OnFinalVolumeChanged(string value) => Recalculate();
    partial void OnFinalUnitChanged(string value) => Recalculate();
    partial void OnPreparationDateChanged(DateTimeOffset? value) => Recalculate();
    partial void OnExpirationDateChanged(DateTimeOffset? value) => Recalculate();
    partial void OnNotesChanged(string value) => Recalculate();
    partial void OnIsBusyChanged(bool value) => Notify();
    partial void OnIsLoadingOptionsChanged(bool value) => Notify();
}

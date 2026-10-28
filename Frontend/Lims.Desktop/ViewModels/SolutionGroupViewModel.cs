using System.Collections.ObjectModel;
using Lims.Contracts.ReferenceMaterials;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lims.Contracts.Permissions;
using Lims.Contracts.ReferencePreparations;
using Lims.Desktop.Http;
using Lims.Desktop.Presentation;
using Lims.Desktop.Services;
using Lims.Desktop.Validation;

namespace Lims.Desktop.ViewModels;

public sealed partial class SolutionPartViewModel : ObservableObject
{
    public ObservableCollection<IntermediateSource> Sources { get; } = [];
    public ObservableCollection<SolutionSourceOption> SourceOptions { get; } = [];
    public ObservableCollection<SolventOption> Solvents { get; } = [];
    public ObservableCollection<IntermediateSource> InternalStandardSources { get; } = [];
    [ObservableProperty] public partial SolventOption? Solvent { get; set; }
    [ObservableProperty] public partial IntermediateSource? InternalStandardSource { get; set; }
    [ObservableProperty] public partial string InternalStandardVolume { get; set; } = string.Empty;
    [ObservableProperty] public partial string InternalStandardUnit { get; set; } = "mL";
    public void ClearInternalStandard() { InternalStandardSource = null; InternalStandardVolume = string.Empty; }
    public IntermediateComponentRequest? StandardRequest => InternalStandardSource is { } s &&
        ReferenceMaterialInput.TryParseDecimal(InternalStandardVolume, 6, out var volume) && volume > 0
        ? new(s.Id, s.Version, volume, InternalStandardUnit) : null;
    public IReadOnlyList<string> Units { get; } = ["mL", "L"];
    public IReadOnlyList<string> Roles { get; } = ["Solución principal", "Estándar interno"];
    public string Role { get => Roles[IsInternalStandard ? 1 : 0]; set => IsInternalStandard = value == Roles[1]; }
    public SolutionSourceOption? SourceOption { get => SourceOptions.FirstOrDefault(s => s.Source.Id == Source?.Id); set => Source = value?.Source; }
    [ObservableProperty] public partial IntermediateSource? Source { get; set; }
    [ObservableProperty] public partial string Volume { get; set; } = string.Empty;
    [ObservableProperty] public partial string Unit { get; set; } = "mL";
    [ObservableProperty] public partial string FinalVolume { get; set; } = string.Empty;
    [ObservableProperty] public partial string FinalUnit { get; set; } = "mL";
    [ObservableProperty] public partial bool IsInternalStandard { get; set; }
    [ObservableProperty] public partial string ResultDisplay { get; set; } = "Complete origen y volúmenes.";
    [ObservableProperty] public partial string BalanceDisplay { get; set; } = string.Empty;
    [ObservableProperty] public partial int Position { get; set; }
    [ObservableProperty] public partial SolutionPart? Preview { get; set; }
    [ObservableProperty] public partial SolutionVolumePreview? VolumePreview { get; set; }
    [ObservableProperty] public partial string FieldError { get; set; } = string.Empty;
    public bool HasFieldError => FieldError.Length > 0;
    [ObservableProperty] public partial string SourceBalance { get; set; } = string.Empty;
    public string Title => $"Solución {Position + 1:00} · {(IsInternalStandard ? "Estándar interno" : MaterialPresentations.IsCommercial(Source?.Kind ?? "") ? Source!.KindLabel : "Intermedia")}";
    public string RoleExplanation => IsInternalStandard
        ? $"Estándar interno: {Source?.Code ?? "seleccione su origen"}. Esta solución conserva su propia alícuota y aforo."
        : "Dilución del origen seleccionado; una alícuota para todos sus analitos.";
    public string CompactResult => Preview is { } p ? $"{p.Results.Count} analitos · Aforo {p.FinalDisplay}" : "Complete origen y volúmenes";
    public string CardSummary => (Preview is { } p ? $"{p.Results.Count} analitos · " : string.Empty) + SourceBalance;
    public string IdentityDisplay => Source?.ConcentrationDisplay ?? string.Empty;
    public void OnOptionsChanged() => OnPropertyChanged(nameof(SourceOption));
    partial void OnSourceChanged(IntermediateSource? value) { OnPropertyChanged(nameof(IdentityDisplay)); OnPropertyChanged(nameof(SourceOption)); OnPropertyChanged(nameof(RoleExplanation)); OnPropertyChanged(nameof(Title)); }
    partial void OnIsInternalStandardChanged(bool value) { OnPropertyChanged(nameof(Title)); OnPropertyChanged(nameof(Role)); OnPropertyChanged(nameof(RoleExplanation)); }
    partial void OnPositionChanged(int value) => OnPropertyChanged(nameof(Title));
    partial void OnFieldErrorChanged(string value) => OnPropertyChanged(nameof(HasFieldError));
    partial void OnPreviewChanged(SolutionPart? value) { OnPropertyChanged(nameof(CompactResult)); OnPropertyChanged(nameof(CardSummary)); }
    partial void OnSourceBalanceChanged(string value) => OnPropertyChanged(nameof(CardSummary));
}

public sealed partial class SolutionGroupViewModel(string kind, ISolutionGroupApiClient api, ISessionService session) : ObservableObject
{
    private readonly List<IntermediateSource> _sources = [];
    private readonly List<SolventOption> _solvents = [];
    [ObservableProperty] public partial bool SupportsDilutionAdditives { get; set; }
    public string AdditivesMessage => SupportsDilutionAdditives ? string.Empty : "Actualice la API del servidor para seleccionar solvente y agregar una Intermedia como estándar interno.";
    partial void OnSupportsDilutionAdditivesChanged(bool value) => OnPropertyChanged(nameof(AdditivesMessage));
    private CreateSolutionGroupRequest? _pending;
    private Guid _requestId = Guid.NewGuid();
    private int _revision;
    private bool _updating;
    private string? _search;
    private int _pageSize = 25;
    public string Kind { get; } = SolutionKinds.IsGroup(kind) ? kind : throw new ArgumentException("Tipo inválido.", nameof(kind));
    public string CreateLabel => Kind == "Curva" ? "Nueva Curva" : "Nuevo AQS";
    public string SaveLabel => "Guardar " + Kind;
    public bool IsCurve => Kind == "Curva";
    public string WorkflowDescription => IsCurve ? "Preparación de soluciones de calibración · aforo independiente por solución" : "Control de calidad AQS · origen principal y estándar interno de otro grupo cuando aplique";
    public string SolutionsLabel => IsCurve ? "SOLUCIONES DE CALIBRACIÓN" : "CONTROLES DEL GRUPO AQS";
    public string CodeLabel => IsCurve ? "Código de la Curva" : "Código AQS";
    public string AddLabel => IsCurve ? "Agregar solución" : "Agregar control AQS";
    public string PreparedBy => session.Profile?.Name ?? "Usuario no informado";
    public ObservableCollection<SolutionPartViewModel> Components { get; } = [];
    public ObservableCollection<SolutionGroupSummary> Items { get; } = [];
    public ObservableCollection<IntermediateMethod> Methods { get; } = [];
    [ObservableProperty] public partial IntermediateMethod? Method { get; set; }
    [ObservableProperty] public partial string Code { get; set; } = string.Empty;
    [ObservableProperty] public partial string Name { get; set; } = string.Empty;
    [ObservableProperty] public partial string Notes { get; set; } = string.Empty;
    [ObservableProperty] public partial string StorageTemperature { get; set; } = string.Empty;
    [ObservableProperty] public partial DateTimeOffset? PreparationDate { get; set; }
    [ObservableProperty] public partial DateTimeOffset? ExpirationDate { get; set; }
    [ObservableProperty] public partial string Message { get; set; } = string.Empty;
    [ObservableProperty] public partial string Validation { get; set; } = string.Empty;
    [ObservableProperty] public partial string ListMessage { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial bool IsLoadingOptions { get; set; }
    [ObservableProperty] public partial bool IsLoadingList { get; set; }
    [ObservableProperty] public partial bool HasLoadedList { get; set; }
    [ObservableProperty] public partial bool HasSearchCriteria { get; set; }
    [ObservableProperty] public partial int Page { get; set; } = 1;
    [ObservableProperty] public partial int TotalCount { get; set; }
    [ObservableProperty] public partial SolutionCalculation? Calculation { get; set; }
    [ObservableProperty] public partial SolutionGroupDetail? Created { get; set; }
    [ObservableProperty] public partial SolutionGroupDetail? Detail { get; set; }
    [ObservableProperty] public partial SolutionPartViewModel? SelectedComponent { get; set; }
    // Missing untouched header fields are guidance, not operation errors on opening.
    public string VisibleValidation => Components.Any(r => r.FieldError.Length > 0) ? "Corrija los volúmenes indicados en las soluciones." :
        Code.Length == 0 || Method is null || Components.Count == 0 ? string.Empty :
        Validation.StartsWith("Agregue soluciones", StringComparison.Ordinal) ? string.Empty : Validation;
    public bool HasVisibleValidation => VisibleValidation.Length > 0;
    public bool HasMessage => Message.Length > 0;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenSelectedPreparationCommand))]
    public partial SolutionGroupSummary? SelectedPreparation { get; set; }
    public PreparationListFilter ListFilter { get; set; } = new();
    public event Func<SolutionGroupDetail, Task>? PreparationOpened;
    private bool Has(string permission) => session.Profile?.Role == "Administrador" || session.Profile?.Permissions.Contains(permission, StringComparer.Ordinal) == true;
    public bool CanView => Has(Kind == "Curva" ? PermissionCatalog.CurveView : PermissionCatalog.AqsView);
    public bool CanCreate => CanView && Has(Kind == "Curva" ? PermissionCatalog.CurveCreate : PermissionCatalog.AqsCreate);
    public bool CanSave => CanCreate && !IsBusy && !IsLoadingOptions && Created is null && (_pending is not null || (Validation.Length == 0 && Calculation is { HasSufficientVolume: true }));
    public bool InputsEnabled => !IsBusy && !IsLoadingOptions && _pending is null && Created is null;
    public bool CanAddComponent => InputsEnabled && Components.Count < 100;
    public string ResultDisplay => Calculation?.ResultDisplay ?? "Complete las soluciones para revisar sus concentraciones.";
    public string BalanceDisplay => Calculation is null ? string.Empty : string.Join("\n", Calculation.Debits.Select(d => d.Display));
    public bool HasResults => !IsLoadingList && ListMessage.Length == 0 && Items.Count > 0;
    public bool HasListMessage => !IsLoadingList && ListMessage.Length > 0;
    public bool IsEmpty => HasLoadedList && !IsLoadingList && ListMessage.Length == 0 && Items.Count == 0;
    public string EmptyTitle => HasSearchCriteria ? "No encontramos preparaciones con estos criterios." : "No hay preparaciones " + Kind;
    public string EmptyDescription => HasSearchCriteria ? "Cambie los filtros o limpie la búsqueda." : "Registre un grupo a partir de orígenes disponibles.";
    public bool HasMultiplePages => TotalCount > _pageSize;
    public bool HasPreviousPage => Page > 1;
    public bool HasNextPage => Page * _pageSize < TotalCount;
    public bool ShowFooter => HasResults;
    public bool ShowSelectionAction => CanOpenSelectedPreparation;
    public bool CanOpenSelectedPreparation => CanView && HasResults && SelectedPreparation is { } p && Items.Contains(p);
    public string PageDisplay => TotalCount == 0 ? "0 preparaciones" : $"{(Page - 1) * _pageSize + 1}–{Math.Min(Page * _pageSize, TotalCount)} de {TotalCount} · Página {Page}";
    public void RefreshPermissions()
    {
        if (!CanView) { Items.Clear(); ClearSelection(); }
        OnPropertyChanged(nameof(CanView)); OnPropertyChanged(nameof(CanCreate)); Notify();
    }
    public void ClearSelection() { ++_revision; SelectedPreparation = null; Detail = null; IsLoadingList = false; Notify(); }
    public void BeginNew()
    {
        _updating = true;
        foreach (var row in Components) row.PropertyChanged -= OnPartChanged;
        Components.Clear(); SelectedComponent = null; _pending = null; _requestId = Guid.NewGuid(); Created = null; Calculation = null;
        Code = Name = Notes = StorageTemperature = Message = string.Empty; Method = null;
        PreparationDate = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-6));
        ExpirationDate = PreparationDate.Value.AddDays(Kind == "Curva" ? 188 : 2);
        _updating = false; Recalculate();
    }
    public SolutionPartViewModel AddComponent()
    {
        var row = new SolutionPartViewModel(); row.PropertyChanged += OnPartChanged; Components.Add(row); SelectedComponent = row; UpdateSources(); Recalculate(); return row;
    }
    public void RemoveComponent(SolutionPartViewModel row) { row.PropertyChanged -= OnPartChanged; Components.Remove(row); if (SelectedComponent == row) SelectedComponent = Components.FirstOrDefault(); Recalculate(); }
    public async Task LoadOptionsAsync(CancellationToken ct)
    {
        if (!CanCreate || IsBusy || IsLoadingOptions || _pending is not null) return;
        IsLoadingOptions = true;
        try
        {
            var result = await api.OptionsAsync(Kind, ct);
            if (!result.IsSuccess || result.Value is null) { Message = ReferenceMaterialErrors.PreparationMessage(result.Error); return; }
            _updating = true; var methodId = Method?.Id;
            _sources.Clear(); _sources.AddRange(result.Value.Sources);
            SupportsDilutionAdditives = result.Value.SupportsDilutionAdditives;
            _solvents.Clear(); _solvents.AddRange(SupportsDilutionAdditives ? result.Value.Solvents ?? [] : []);
            Replace(Methods, result.Value.Methods); Method = Methods.FirstOrDefault(m => m.Id == methodId);
            foreach (var row in Components)
            {
                if (row.Source is { } previous) row.Source = _sources.Find(s => s.Id == previous.Id);
                if (row.InternalStandardSource is { } standard) row.InternalStandardSource = _sources.Find(s => s.Id == standard.Id);
                if (row.Solvent is { } solvent) row.Solvent = _solvents.Find(s => s.Id == solvent.Id);
            }
            _updating = false; UpdateSources(); Recalculate(); Message = string.Empty;
        }
        catch (HttpRequestException) { Message = "No se pudo conectar con el servidor."; }
        catch (OperationCanceledException) { Message = "La consulta se canceló."; }
        finally { _updating = false; IsLoadingOptions = false; Notify(); }
    }
    private void UpdateSources()
    {
        foreach (var row in Components)
        {
            Replace(row.Sources, _sources.Where(s => (s.Kind == "Intermedia" || MaterialPresentations.IsCommercial(s.Kind)) && (Method is null || s.MethodId == Method.Id)));
            Replace(row.SourceOptions, row.Sources.Select(s => new SolutionSourceOption(s)));
            Replace(row.Solvents, _solvents);
            Replace(row.InternalStandardSources, SupportsDilutionAdditives
                ? _sources.Where(s => s.CanBeInternalStandard && (Method is null || s.MethodId == Method.Id)) : []);
            // Refresh the selected wrapper after replacing options without discarding the draft source.
            row.OnOptionsChanged();
        }
    }
    private void OnPartChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SolutionPartViewModel.Source) or nameof(SolutionPartViewModel.Volume) or nameof(SolutionPartViewModel.Unit) or nameof(SolutionPartViewModel.FinalVolume) or nameof(SolutionPartViewModel.FinalUnit) or nameof(SolutionPartViewModel.IsInternalStandard)
            or nameof(SolutionPartViewModel.Solvent) or nameof(SolutionPartViewModel.InternalStandardSource) or nameof(SolutionPartViewModel.InternalStandardVolume) or nameof(SolutionPartViewModel.InternalStandardUnit))
        { SelectedComponent = sender as SolutionPartViewModel; Recalculate(); }
    }
    public void Recalculate()
    {
        if (_updating) return;
        _pending = null; _requestId = Guid.NewGuid(); Calculation = null;
        Validation = string.Empty;
        foreach (var row in Components)
        {
            row.Position = Components.IndexOf(row); row.Preview = null; row.VolumePreview = null; row.FieldError = string.Empty;
            row.ResultDisplay = "Complete origen y volúmenes."; row.BalanceDisplay = row.Source is null ? string.Empty : "Disponible " + row.Source.AvailableDisplay;
            row.SourceBalance = row.Source is null ? string.Empty : "Origen disponible: " + row.Source.AvailableDisplay;
            if (Parse(row.Volume, out var taken) && Parse(row.FinalVolume, out var final))
            {
                try
                {
                    var standardVolume = row.StandardRequest is { } standard
                        ? StockUnits.ConvertVolume(standard.VolumeTaken, standard.VolumeUnit, row.FinalUnit) : 0;
                    row.VolumePreview = new(StockUnits.ConvertVolume(taken, row.Unit, row.FinalUnit) + standardVolume, final, row.FinalUnit);
                    row.FieldError = row.VolumePreview.Error;
                    if (row.Source is not null && Method is not null && !row.VolumePreview.Exceeds)
                        row.Preview = SolutionGroupCalculator.Calculate(new(Method.Id, [new(row.Source.Id, row.Source.Version, taken, row.Unit, final, row.FinalUnit, row.IsInternalStandard, row.Solvent?.Id, row.StandardRequest)]), _sources, _solvents).Parts[0] with { Position = row.Position };
                }
                catch (ArgumentException e) { row.FieldError = e.Message; }
                catch (OverflowException) { row.FieldError = "Las cantidades exceden el rango admitido."; }
            }
            else if ((row.Volume.Length > 0 && !Parse(row.Volume, out _)) || (row.FinalVolume.Length > 0 && !Parse(row.FinalVolume, out _)))
                row.FieldError = "Ingrese volúmenes positivos, con hasta 6 decimales.";
            if ((row.InternalStandardSource is not null || row.InternalStandardVolume.Length > 0) && row.StandardRequest is null)
                row.FieldError = "Seleccione la Intermedia del estándar interno e ingrese su volumen positivo.";
        }
        var today = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-6)).Date;
        if (Code.Trim().Length is not (> 0 and <= 40)) Validation = "Ingrese un código de hasta 40 caracteres.";
        else if (Name.Trim().Length > 200 || Notes.Trim().Length > 2000 || StorageTemperature.Trim().Length > 200) Validation = "Revise la longitud de los datos generales.";
        else if (Method is null) Validation = "Seleccione un método.";
        else if (PreparationDate is null || ExpirationDate is null || (HistoricalEntryPolicy.EnforcePreparationDates &&
            (PreparationDate.Value.Date > today || ExpirationDate.Value.Date < PreparationDate.Value.Date || ExpirationDate.Value.Date < today))) Validation = "Revise las fechas de preparación y expiración.";
        if (TryRequest(out var request))
        {
            try
            {
                Calculation = SolutionGroupCalculator.Calculate(request!, _sources, _solvents);
                foreach (var part in Calculation.Parts)
                {
                    var row = Components[part.Position]; row.Preview = part; row.ResultDisplay = part.ResultDisplay;
                    row.BalanceDisplay = Calculation.Debits.Single(d => d.Source.Id == part.Source.Id).Display;
                    var debit = Calculation.Debits.Single(d => d.Source.Id == part.Source.Id);
                    row.SourceBalance = $"Origen: {part.Source.AvailableDisplay} · Después del grupo: {StockPresentation.Quantity(debit.RemainingVolume, part.Source.VolumeUnit)}";
                    if (HistoricalEntryPolicy.EnforcePreparationDates && PreparationDate is { } date && (DateOnly.FromDateTime(date.Date) < part.Source.PreparationDate || DateOnly.FromDateTime(date.Date) > part.Source.ExpirationDate)) Validation = "La preparación queda fuera de la vigencia de un origen.";
                }
                if (!Calculation.HasSufficientVolume) Validation = "El consumo acumulado supera el saldo de un origen.";
                if (HistoricalEntryPolicy.EnforcePreparationDates && PreparationDate is { } prepared && Calculation.Debits.Any(d => DateOnly.FromDateTime(prepared.Date) < d.Source.PreparationDate || DateOnly.FromDateTime(prepared.Date) > d.Source.ExpirationDate))
                    Validation = "La preparación queda fuera de la vigencia de un origen o estándar interno.";
            }
            catch (ArgumentException e) { Validation = e.Message; }
            catch (OverflowException) { Validation = "Las cantidades exceden el rango admitido."; }
        }
        else if (Validation.Length == 0) Validation = "Agregue soluciones con origen, volumen tomado y aforo positivos.";
        Notify();
    }
    private bool TryRequest(out SolutionCalculationRequest? request)
    {
        request = null; if (Method is null || Components.Count == 0) return false;
        var parts = new List<SolutionPartRequest>();
        foreach (var row in Components)
        {
            if (row.Source is null || !Parse(row.Volume, out var taken) || !Parse(row.FinalVolume, out var final)) return false;
            if ((row.InternalStandardSource is not null || row.InternalStandardVolume.Length > 0) && row.StandardRequest is null) return false;
            if (!SupportsDilutionAdditives && (row.Solvent is not null || row.StandardRequest is not null)) return false;
            parts.Add(new(row.Source.Id, row.Source.Version, taken, row.Unit, final, row.FinalUnit, row.IsInternalStandard, row.Solvent?.Id, row.StandardRequest));
        }
        request = new(Method.Id, parts); return true;
    }
    public async Task<bool> SaveAsync(CancellationToken ct)
    {
        if (!CanSave) return false;
        if (_pending is null)
        {
            if (!TryRequest(out var calculation)) return false;
            _pending = new(_requestId, Code.Trim(), Name.Trim(), calculation!, DateOnly.FromDateTime(PreparationDate!.Value.Date), DateOnly.FromDateTime(ExpirationDate!.Value.Date), Notes, StorageTemperature);
        }
        IsBusy = true; Message = string.Empty;
        try
        {
            var response = await api.CreateAsync(Kind, _pending, ct);
            if (!response.IsSuccess || response.Value is null)
            {
                if (response.StatusCode is >= 400 and < 500) _pending = null;
                Message = ReferenceMaterialErrors.PreparationMessage(response.Error); return false;
            }
            Created = response.Value; _pending = null;
            await LoadAsync(_search, Page, ct); return true;
        }
        catch (HttpRequestException) { Message = "No se pudo confirmar la respuesta. Reintente la misma solicitud; el consumo no se duplicará."; return false; }
        catch (OperationCanceledException) { Message = "No se pudo confirmar la respuesta. Reintente la misma solicitud."; return false; }
        finally { IsBusy = false; Notify(); }
    }
    public async Task LoadAsync(string? search, int page, CancellationToken ct)
    {
        if (!CanView) { Items.Clear(); ClearSelection(); ListMessage = "No tiene permiso para consultar esta preparación."; return; }
        var revision = ++_revision; _search = search; IsLoadingList = true; ListMessage = string.Empty;
        HasSearchCriteria = !string.IsNullOrWhiteSpace(search) || ListFilter.HasCriteria;
        try
        {
            var result = await api.ListAsync(Kind, search, page, ct, ListFilter);
            if (revision != _revision) return;
            if (!result.IsSuccess || result.Value is null) { Items.Clear(); SelectedPreparation = null; ListMessage = ReferenceMaterialErrors.PreparationMessage(result.Error); return; }
            var selected = SelectedPreparation?.Id; SelectedPreparation = null; Replace(Items, result.Value.Items);
            SelectedPreparation = Items.FirstOrDefault(p => p.Id == selected); Page = result.Value.Page; _pageSize = Math.Max(1, result.Value.PageSize); TotalCount = result.Value.TotalCount; HasLoadedList = true;
        }
        catch (HttpRequestException) { if (revision == _revision) ListMessage = "No se pudo conectar con el servidor."; }
        catch (OperationCanceledException) { if (revision == _revision) ListMessage = "La consulta se canceló."; }
        finally { if (revision == _revision) { IsLoadingList = false; Notify(); } }
    }
    [RelayCommand(CanExecute = nameof(CanOpenSelectedPreparation))]
    private async Task OpenSelectedPreparationAsync(CancellationToken ct)
    {
        if (!CanOpenSelectedPreparation || SelectedPreparation is not { } selected) return;
        var revision = _revision;
        try
        {
            var response = await api.GetAsync(Kind, selected.Id, ct);
            if (revision != _revision || SelectedPreparation?.Id != selected.Id || !CanView) return;
            if (!response.IsSuccess || response.Value is null) { ListMessage = ReferenceMaterialErrors.PreparationMessage(response.Error); return; }
            Detail = response.Value;
            if (PreparationOpened is { } show) await show(Detail);
        }
        catch (HttpRequestException) { ListMessage = "No se pudo conectar con el servidor."; }
        catch (OperationCanceledException) { }
    }
    private static bool Parse(string text, out decimal value) => ReferenceMaterialInput.TryParseDecimal(text, 6, out value) && value is > 0 and < 1000000000000m;
    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values) { target.Clear(); foreach (var item in values) target.Add(item); }
    private void Notify()
    {
        foreach (var property in new[] { nameof(CanSave), nameof(VisibleValidation), nameof(HasVisibleValidation), nameof(InputsEnabled), nameof(CanAddComponent), nameof(ResultDisplay), nameof(BalanceDisplay), nameof(HasResults), nameof(HasListMessage), nameof(IsEmpty), nameof(EmptyTitle), nameof(EmptyDescription), nameof(HasMultiplePages), nameof(HasPreviousPage), nameof(HasNextPage), nameof(ShowFooter), nameof(ShowSelectionAction), nameof(PageDisplay) }) OnPropertyChanged(property);
        OpenSelectedPreparationCommand.NotifyCanExecuteChanged();
    }
    partial void OnMethodChanged(IntermediateMethod? value) { if (!_updating) UpdateSources(); Recalculate(); }
    partial void OnCodeChanged(string value) => Recalculate();
    partial void OnNameChanged(string value) => Recalculate();
    partial void OnNotesChanged(string value) => Recalculate();
    partial void OnStorageTemperatureChanged(string value) => Recalculate();
    partial void OnPreparationDateChanged(DateTimeOffset? value)
    {
        if (!_updating && value is not null) ExpirationDate = value.Value.AddDays(Kind == "Curva" ? 188 : 2);
        Recalculate();
    }
    partial void OnExpirationDateChanged(DateTimeOffset? value) => Recalculate();
    partial void OnSelectedPreparationChanged(SolutionGroupSummary? value) => Notify();
    partial void OnIsBusyChanged(bool value) => Notify();
    partial void OnIsLoadingListChanged(bool value) => Notify();
    partial void OnListMessageChanged(string value) => Notify();
    partial void OnMessageChanged(string value) => OnPropertyChanged(nameof(HasMessage));
}

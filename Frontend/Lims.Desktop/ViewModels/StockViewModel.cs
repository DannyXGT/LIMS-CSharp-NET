using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Lims.Contracts.ReferenceMaterials;
using Lims.Contracts.ReferencePreparations;
using Lims.Desktop.Http;
using Lims.Desktop.Services;
using Lims.Desktop.Validation;

namespace Lims.Desktop.ViewModels;

public sealed partial class StockViewModel(IStockApiClient api, ISessionService session) : ObservableObject
{
    private int _inputRevision;
    private int _sourceRevision;
    private int _listRevision;
    private Guid _requestId = Guid.NewGuid();
    private CreateStockRequest? _pendingRequest;
    public ObservableCollection<StockSource> Sources { get; } = [];
    public ObservableCollection<StockSummary> Items { get; } = [];
    public ObservableCollection<string> ConcentrationUnits { get; } = [];
    public ObservableCollection<string> FinalVolumeUnits { get; } = [];
    [ObservableProperty] public partial StockSource? SelectedSource { get; set; }
    [ObservableProperty] public partial StockCalculation? Calculation { get; set; }
    [ObservableProperty] public partial StockDetail? Created { get; set; }
    [ObservableProperty] public partial StockDetail? Detail { get; set; }
    [ObservableProperty] public partial string Name { get; set; } = string.Empty;
    [ObservableProperty] public partial string Concentration { get; set; } = string.Empty;
    [ObservableProperty] public partial string ConcentrationUnit { get; set; } = "mg/L";
    [ObservableProperty] public partial string FinalVolume { get; set; } = string.Empty;
    [ObservableProperty] public partial string FinalVolumeUnit { get; set; } = "mL";
    [ObservableProperty] public partial string ActualQuantity { get; set; } = string.Empty;
    [ObservableProperty] public partial string ActualWeightUnit { get; set; } = "mg";
    public IReadOnlyList<string> WeightUnits { get; } = ["g", "mg", "µg"];
    [ObservableProperty] public partial DateTimeOffset? ExpirationDate { get; set; }
    [ObservableProperty] public partial string StorageTemperature { get; set; } = string.Empty;
    [ObservableProperty] public partial string Notes { get; set; } = string.Empty;
    [ObservableProperty] public partial DateTimeOffset PreparationDate { get; set; } = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-6));
    [ObservableProperty] public partial string Message { get; set; } = string.Empty;
    [ObservableProperty] public partial string ListMessage { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial bool IsCalculating { get; set; }
    [ObservableProperty] public partial int Page { get; set; } = 1;
    [ObservableProperty] public partial int TotalCount { get; set; }
    [ObservableProperty] public partial int SourcePage { get; set; } = 1;
    [ObservableProperty] public partial int SourceCount { get; set; }
    public string PreparedBy => session.Profile?.Name ?? "Usuario no informado";
    public bool CanView => HasPermission(ReferenceMaterialPermissions.View);
    public bool CanCreate => CanView && HasPermission(ReferenceMaterialPermissions.Create);
    public bool CanSave => CanCreate && Created is null && !IsBusy && !IsCalculating &&
        Calculation is { HasSufficientMaterial: true, ActualWeight: > 0, ActualConcentration: > 0 } &&
        Name.Trim().Length is > 0 and <= 200 && Notes.Trim().Length <= 2000 && StorageTemperature.Trim().Length <= 200 &&
        SelectedSource is { } source && source.ExpirationDate >= DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-6)).DateTime) &&
        PreparationDate.Date <= DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-6)).Date &&
        (source.ReceivedDate is null || DateOnly.FromDateTime(PreparationDate.Date) >= source.ReceivedDate) &&
        DateOnly.FromDateTime(PreparationDate.Date) <= source.ExpirationDate && ExpirationDate?.Date >= PreparationDate.Date;
    public bool HasMultiplePages => TotalCount > 25;
    public bool HasPreviousPage => Page > 1;
    public bool HasNextPage => Page * 25 < TotalCount;
    public bool HasMoreSources => Sources.Count < SourceCount;
    public string PageDisplay => $"Página {Page} · {TotalCount} preparaciones";
    public string SourcePageDisplay => $"Página {SourcePage} · {SourceCount} estándares";

    public void BeginNew()
    {
        SelectedSource = null;
        Name = Concentration = FinalVolume = ActualQuantity = Notes = Message = string.Empty;
        PreparationDate = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-6));
        ExpirationDate = null;
        StorageTemperature = string.Empty;
        Created = null;
        _requestId = Guid.NewGuid();
        _pendingRequest = null;
    }

    public async Task LoadOptionsAsync(CancellationToken cancellationToken)
    {
        if (!CanCreate) return;
        try
        {
            var response = await api.OptionsAsync(cancellationToken).ConfigureAwait(true);
            if (!response.IsSuccess || response.Value is null) { Message = Error(response.Error?.Message); return; }
            Replace(ConcentrationUnits, response.Value.ConcentrationUnits);
            Replace(FinalVolumeUnits, response.Value.FinalVolumeUnits);
        }
        catch (HttpRequestException) { Message = "No se pudo conectar con el servidor."; }
        catch (OperationCanceledException) { Message = "La consulta se canceló. Intente nuevamente."; }
    }

    public async Task SearchSourcesAsync(string? search, int page, CancellationToken cancellationToken)
    {
        if (!CanCreate) return;
        var revision = ++_sourceRevision;
        try
        {
            var response = await api.SourcesAsync(search, page, cancellationToken).ConfigureAwait(true);
            if (revision != _sourceRevision) return;
            if (!response.IsSuccess || response.Value is null) { Message = Error(response.Error?.Message); return; }
            if (page == 1) Replace(Sources, response.Value.Items);
            else foreach (var item in response.Value.Items) if (!Sources.Any(existing => existing.Id == item.Id)) Sources.Add(item);
            SourcePage = response.Value.Page;
            SourceCount = response.Value.TotalCount;
            Message = Sources.Count == 0 ? "No se encontraron estándares disponibles para Stock por pesada." : string.Empty;
        }
        catch (HttpRequestException) { if (revision == _sourceRevision) Message = "No se pudo conectar con el servidor."; }
        catch (OperationCanceledException) { }
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
            Replace(Items, response.Value.Items);
            Page = response.Value.Page;
            TotalCount = response.Value.TotalCount;
            ListMessage = Items.Count == 0 ? "No hay preparaciones Stock para esta búsqueda." : string.Empty;
        }
        catch (HttpRequestException) { if (revision == _listRevision) ListMessage = "No se pudo conectar con el servidor."; }
        catch (OperationCanceledException) { }
    }

    public async Task PreviewAsync(CancellationToken cancellationToken)
    {
        if (!CanCreate || IsBusy) return;
        var revision = ++_inputRevision;
        Calculation = null;
        if (!TryRequest(out var request)) { Message = "Ingrese concentración y volumen positivos con hasta seis decimales."; return; }
        IsCalculating = true;
        try
        {
            var response = await api.PreviewAsync(request!, cancellationToken).ConfigureAwait(true);
            if (revision != _inputRevision) return;
            Calculation = response.IsSuccess ? response.Value : null;
            Message = !response.IsSuccess ? Error(response.Error?.Message) : Calculation?.HasSufficientMaterial == false
                ? "No hay suficiente material disponible." : string.Empty;
        }
        catch (HttpRequestException) { if (revision == _inputRevision) Message = "No se pudo conectar con el servidor."; }
        catch (OperationCanceledException) { }
        finally { if (revision == _inputRevision) IsCalculating = false; }
    }

    public async Task<bool> SaveAsync(CancellationToken cancellationToken)
    {
        if (!CanSave || !TryRequest(out var calculation)) return false;
        IsBusy = true;
        Message = string.Empty;
        _pendingRequest ??= new CreateStockRequest(_requestId, Name.Trim(), calculation!, DateOnly.FromDateTime(PreparationDate.DateTime), Notes,
            DateOnly.FromDateTime(ExpirationDate!.Value.DateTime), StorageTemperature);
        try
        {
            var response = await api.CreateAsync(_pendingRequest, cancellationToken).ConfigureAwait(true);
            if (!response.IsSuccess || response.Value is null)
            {
                var message = Error(response.Error?.Message);
                if (response.Error?.Code is Lims.Contracts.Errors.ErrorCodes.Conflict or Lims.Contracts.Errors.ErrorCodes.InvalidState)
                {
                    SelectedSource = null;
                    await SearchSourcesAsync(null, 1, cancellationToken).ConfigureAwait(true);
                }
                Message = message;
                return false;
            }
            Created = response.Value;
            // Reflect the committed source balance immediately; a fresh selector query follows reopening.
            if (SelectedSource is { } source)
            {
                var updated = source with { AvailableQuantity = response.Value.Consumption.BalanceAfter };
                var index = Sources.IndexOf(source);
                if (index >= 0) Sources[index] = updated;
            }
            await LoadAsync(null, 1, cancellationToken).ConfigureAwait(true);
            return true;
        }
        catch (HttpRequestException) { Message = "No se pudo confirmar la respuesta. Reintente para consultar esta misma solicitud sin duplicar el consumo."; return false; }
        catch (OperationCanceledException) { Message = "No se pudo confirmar la respuesta. Reintente esta misma solicitud."; return false; }
        finally { IsBusy = false; }
    }

    public async Task<bool> OpenDetailAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!CanView) return false;
        try
        {
            var response = await api.GetAsync(id, cancellationToken).ConfigureAwait(true);
            if (!response.IsSuccess || response.Value is null) { ListMessage = Error(response.Error?.Message); return false; }
            Detail = response.Value;
            return true;
        }
        catch (HttpRequestException) { ListMessage = "No se pudo conectar con el servidor."; return false; }
        catch (OperationCanceledException) { return false; }
    }

    private bool TryRequest(out StockCalculationRequest? request)
    {
        request = null;
        if (SelectedSource is null || !ReferenceMaterialInput.TryParseDecimal(Concentration, 6, out var concentration) || concentration <= 0 ||
            !ReferenceMaterialInput.TryParseDecimal(FinalVolume, 6, out var volume) || volume <= 0) return false;
        decimal? actual = null;
        if (!string.IsNullOrWhiteSpace(ActualQuantity))
        {
            if (!ReferenceMaterialInput.TryParseDecimal(ActualQuantity, 6, out var parsed) || parsed <= 0) return false;
            actual = parsed;
        }
        request = new(SelectedSource.Id, SelectedSource.Version, concentration, ConcentrationUnit, volume, FinalVolumeUnit, actual, ActualWeightUnit);
        return true;
    }
    private bool HasPermission(string permission) => string.Equals(session.Profile?.Role, "Administrador", StringComparison.OrdinalIgnoreCase) || session.Profile?.Permissions.Contains(permission, StringComparer.Ordinal) == true;
    private static string Error(string? message) => string.IsNullOrWhiteSpace(message) ? "No se pudo completar la operación. Intente nuevamente." : message;
    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values) { target.Clear(); foreach (var value in values) target.Add(value); }
    private void Invalidate()
    {
        _inputRevision++;
        Calculation = null;
        IsCalculating = false;
        _pendingRequest = null;
        _requestId = Guid.NewGuid();
        if (TryRequest(out var request))
        {
            try
            {
                Calculation = StockUnits.Calculate(SelectedSource!.PurityPercent, SelectedSource.AvailableQuantity, SelectedSource.Unit, request!);
                Message = Calculation.HasSufficientMaterial ? string.Empty : "No hay suficiente material disponible.";
            }
            catch (ArgumentException exception) { Message = exception.Message; }
            catch (OverflowException) { Message = "Las cantidades exceden el rango admitido."; }
        }
        else Message = string.Empty;
        NotifyActions();
    }
    private void NotifyActions() => OnPropertyChanged(nameof(CanSave));
    partial void OnSelectedSourceChanged(StockSource? oldValue, StockSource? newValue)
    {
        ActualQuantity = string.Empty;
        if (newValue is not null) ActualWeightUnit = newValue.Unit;
        Invalidate();
        var previousName = oldValue is null ? string.Empty : $"Stock de {oldValue.Name}";
        if (newValue is not null && (string.IsNullOrWhiteSpace(Name) || Name == previousName[..Math.Min(200, previousName.Length)]))
        {
            var name = $"Stock de {newValue.Name}";
            Name = name[..Math.Min(200, name.Length)];
        }
    }
    partial void OnCreatedChanged(StockDetail? value) => NotifyActions();
    partial void OnConcentrationChanged(string value) => Invalidate();
    partial void OnConcentrationUnitChanged(string value) => Invalidate();
    partial void OnFinalVolumeChanged(string value) => Invalidate();
    partial void OnFinalVolumeUnitChanged(string value) => Invalidate();
    partial void OnActualQuantityChanged(string value) => Invalidate();
    partial void OnActualWeightUnitChanged(string value) => Invalidate();
    partial void OnExpirationDateChanged(DateTimeOffset? value) { _pendingRequest = null; _requestId = Guid.NewGuid(); NotifyActions(); }
    partial void OnStorageTemperatureChanged(string value) { _pendingRequest = null; _requestId = Guid.NewGuid(); NotifyActions(); }
    partial void OnNameChanged(string value) { _pendingRequest = null; _requestId = Guid.NewGuid(); NotifyActions(); }
    partial void OnNotesChanged(string value) { _pendingRequest = null; _requestId = Guid.NewGuid(); NotifyActions(); }
    partial void OnPreparationDateChanged(DateTimeOffset value) { _pendingRequest = null; _requestId = Guid.NewGuid(); NotifyActions(); }
    partial void OnCalculationChanged(StockCalculation? value) => NotifyActions();
    partial void OnIsBusyChanged(bool value) => NotifyActions();
    partial void OnIsCalculatingChanged(bool value) => NotifyActions();
    partial void OnPageChanged(int value) { OnPropertyChanged(nameof(PageDisplay)); OnPropertyChanged(nameof(HasPreviousPage)); OnPropertyChanged(nameof(HasNextPage)); }
    partial void OnTotalCountChanged(int value) { OnPropertyChanged(nameof(PageDisplay)); OnPropertyChanged(nameof(HasMultiplePages)); OnPropertyChanged(nameof(HasNextPage)); }
    partial void OnSourcePageChanged(int value) => OnPropertyChanged(nameof(SourcePageDisplay));
    partial void OnSourceCountChanged(int value) { OnPropertyChanged(nameof(SourcePageDisplay)); OnPropertyChanged(nameof(HasMoreSources)); }
}

using Lims.Contracts.Authentication;
using Lims.Contracts.Errors;
using Lims.Contracts.ReferencePreparations;
using Lims.Desktop.Http;
using Lims.Desktop.Services;
using Lims.Desktop.ViewModels;

namespace Lims.Desktop.Tests;

public sealed class StockViewModelTests
{
    [Fact]
    public async Task CommercialSelectionOpensPhysicalMaterialWithoutRequestingInternalPreparation()
    {
        var api = new FakeStockApi(); var vm = Model(api);
        var commercial = api.Created.Preparation with { IsCommercial = true, TargetConcentration = null, ActualWeight = null };
        vm.Items.Add(commercial); vm.SelectedPreparation = commercial;
        Guid? opened = null; vm.CommercialOpened += id => { opened = id; return Task.CompletedTask; };
        await vm.OpenSelectedPreparationCommand.ExecuteAsync(null);
        Assert.Equal(commercial.Id, opened); Assert.Empty(api.DetailIds); Assert.Null(vm.Detail);
    }
    [Fact]
    public async Task LoadingErrorsAndOlderResponsesCannotMasqueradeAsAnEmptyList()
    {
        var api = new FakeStockApi();
        var vm = Model(api);
        var delayed = new TaskCompletionSource<ApiCallResult<StockPage>>();
        api.ListHandler = _ => delayed.Task;
        var first = vm.LoadAsync("anterior", 1, CancellationToken.None);
        Assert.True(vm.IsLoadingList);
        Assert.False(vm.IsEmpty);
        Assert.False(vm.ShowFooter);
        api.ListHandler = _ => throw new HttpRequestException();
        await vm.LoadAsync("actual", 1, CancellationToken.None);
        Assert.False(vm.IsLoadingList);
        Assert.True(vm.HasListMessage);
        Assert.False(vm.IsEmpty);
        delayed.SetResult(Ok(new StockPage([api.Created.Preparation], 1, 25, 1)));
        await first;
        Assert.Empty(vm.Items);
        Assert.True(vm.HasListMessage);
    }

    [Fact]
    public async Task ServerFiltersAreForwardedAndRequireExplicitSupportBeforeShowingRows()
    {
        var api = new FakeStockApi();
        var vm = Model(api);
        vm.ListFilter = new("Expired", "Low", "Método real");
        api.ListHandler = filter =>
        {
            Assert.Same(vm.ListFilter, filter);
            return Task.FromResult(Ok(new StockPage([api.Created.Preparation], 1, 25, 1)));
        };
        await vm.LoadAsync(null, 1, CancellationToken.None);
        Assert.True(vm.HasListMessage);
        Assert.False(vm.HasResults);
        Assert.Contains("actualización", vm.ListMessage, StringComparison.Ordinal);
        api.ListHandler = _ => Task.FromResult(Ok(new StockPage([], 1, 25, 0, true)));
        await vm.LoadAsync(null, 1, CancellationToken.None);
        Assert.True(vm.IsEmpty);
        Assert.True(vm.HasSearchCriteria);
        Assert.Contains("criterios", vm.EmptyTitle, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PaginationUsesTheServerPageSizeAndSelectionActionIsContextual()
    {
        var api = new FakeStockApi();
        api.ListHandler = _ => Task.FromResult(Ok(new StockPage([api.Created.Preparation], 2, 10, 21)));
        var vm = Model(api);
        await vm.LoadAsync(null, 2, CancellationToken.None);
        Assert.True(vm.HasNextPage);
        Assert.True(vm.HasPreviousPage);
        Assert.Equal("11–20 de 21 · Página 2 de 3", vm.PageDisplay);
        Assert.False(vm.ShowSelectionAction);
        vm.SelectedPreparation = vm.Items[0];
        Assert.True(vm.ShowSelectionAction);
    }

    [Fact]
    public async Task EmptyListHasNoSelectionAndCannotOpenDetail()
    {
        var api = new FakeStockApi { ListItems = [] };
        var vm = Model(api);
        await vm.LoadAsync(null, 1, CancellationToken.None);
        Assert.Null(vm.SelectedPreparation);
        Assert.False(vm.OpenSelectedPreparationCommand.CanExecute(null));
        await vm.OpenSelectedPreparationCommand.ExecuteAsync(null);
        Assert.Empty(api.DetailIds);
        Assert.True(vm.IsEmpty);
        Assert.False(vm.HasSearchCriteria);
        Assert.Equal("0 preparaciones", vm.PageDisplay);
    }

    [Fact]
    public async Task LoadedRowDoesNotEnableDetailUntilActuallySelected()
    {
        var api = new FakeStockApi();
        var vm = Model(api);
        await vm.LoadAsync(null, 1, CancellationToken.None);
        Assert.Single(vm.Items);
        Assert.Null(vm.SelectedPreparation);
        Assert.False(vm.OpenSelectedPreparationCommand.CanExecute(null));
        var notifications = 0;
        vm.OpenSelectedPreparationCommand.CanExecuteChanged += (_, _) => notifications++;
        vm.SelectedPreparation = vm.Items[0];
        Assert.True(vm.OpenSelectedPreparationCommand.CanExecute(null));
        Assert.True(notifications > 0);
        vm.SelectedPreparation = api.Created.Preparation with { Id = Guid.NewGuid() };
        Assert.False(vm.OpenSelectedPreparationCommand.CanExecute(null));
    }

    [Fact]
    public async Task CommandOpensExactlySelectedPreparationAndStaysDisabledWhileSheetIsOpen()
    {
        var api = new FakeStockApi();
        var vm = Model(api);
        await vm.LoadAsync(null, 1, CancellationToken.None);
        vm.SelectedPreparation = vm.Items[0];
        StockDetail? opened = null;
        var close = new TaskCompletionSource();
        vm.PreparationOpened += detail => { opened = detail; return close.Task; };
        var execution = vm.OpenSelectedPreparationCommand.ExecuteAsync(null);
        Assert.Same(api.Created, opened);
        Assert.Equal(vm.SelectedPreparation.Id, Assert.Single(api.DetailIds));
        Assert.False(vm.OpenSelectedPreparationCommand.CanExecute(null));
        close.SetResult();
        await execution;
        Assert.True(vm.OpenSelectedPreparationCommand.CanExecute(null));
    }

    [Fact]
    public async Task RefreshPreservesSelectionByIdentityAndClearsItWhenMissing()
    {
        var api = new FakeStockApi();
        var vm = Model(api);
        await vm.LoadAsync(null, 1, CancellationToken.None);
        vm.SelectedPreparation = vm.Items[0];
        var fresh = api.Created.Preparation with { Name = "Actualizado" };
        api.ListItems = [fresh];
        await vm.LoadAsync(null, 1, CancellationToken.None);
        Assert.Same(fresh, vm.SelectedPreparation);
        api.ListItems = [];
        await vm.LoadAsync(null, 1, CancellationToken.None);
        Assert.Null(vm.SelectedPreparation);
        Assert.False(vm.OpenSelectedPreparationCommand.CanExecute(null));
    }

    [Fact]
    public async Task TabSelectionResetDisablesDetailAndRejectsPendingDetail()
    {
        var api = new FakeStockApi();
        var vm = Model(api);
        await vm.LoadAsync(null, 1, CancellationToken.None);
        vm.SelectedPreparation = vm.Items[0];
        var response = new TaskCompletionSource<ApiCallResult<StockDetail>>();
        api.DetailHandler = _ => response.Task;
        var opens = 0;
        vm.PreparationOpened += _ => { opens++; return Task.CompletedTask; };
        var execution = vm.OpenSelectedPreparationCommand.ExecuteAsync(null);
        vm.ClearSelection();
        response.SetResult(Ok(api.Created));
        await execution;
        Assert.Null(vm.SelectedPreparation);
        Assert.False(vm.OpenSelectedPreparationCommand.CanExecute(null));
        Assert.Equal(0, opens);
    }

    [Fact]
    public async Task EmptySearchUsesCriteriaMessageAndClearingRestoresCreateState()
    {
        var vm = Model(new FakeStockApi { ListItems = [] });
        await vm.LoadAsync("inexistente", 1, CancellationToken.None);
        Assert.True(vm.HasSearchCriteria);
        Assert.Equal("No encontramos preparaciones con estos criterios.", vm.EmptyTitle);
        await vm.LoadAsync(null, 1, CancellationToken.None);
        Assert.False(vm.HasSearchCriteria);
        Assert.Equal("No hay preparaciones Stock", vm.EmptyTitle);
    }

    [Fact]
    public async Task ViewerCanListButCannotCreateOrQuerySources()
    {
        var api = new FakeStockApi();
        var vm = new StockViewModel(api, new Session("Usuario", ["stock.view"]));
        await vm.LoadAsync(null, 1, CancellationToken.None);
        await vm.SearchSourcesAsync(null, 1, CancellationToken.None);
        Assert.True(vm.CanView);
        Assert.False(vm.CanCreate);
        Assert.Equal(1, api.ListCalls);
        Assert.Equal(0, api.SourceCalls);
    }

    [Fact]
    public async Task CompleteSingleFormFlowUsesBackendCalculationAndCurrentSessionName()
    {
        var api = new FakeStockApi();
        var vm = Model(api);
        Assert.True(vm.CanCreate);
        await ReadyAsync(vm);
        Assert.Equal(api.Result, vm.Calculation);
        Assert.True(await vm.SaveAsync(CancellationToken.None));
        Assert.Equal("Ana López", vm.PreparedBy);
        Assert.Equal(vm.Created?.Preparation.Code, api.Created.Preparation.Code);
        Assert.Equal(90, vm.Sources[0].AvailableQuantity);
        Assert.Equal(Source.Id, api.Requests[0].Calculation.SourceMaterialId);
        Assert.Equal(Source.Version, api.Requests[0].Calculation.SourceVersion);
        Assert.Equal(100, api.Requests[0].Calculation.TargetConcentration);
        Assert.Single(vm.Items);
    }

    [Fact]
    public async Task InsufficientMaterialCannotContinueOrSave()
    {
        var api = new FakeStockApi { Result = Calculation with { HasSufficientMaterial = false, ShortfallQuantity = 20 } };
        var vm = Model(api);
        await ReadyAsync(vm);
        Assert.False(vm.CanSave);
        Assert.False(await vm.SaveAsync(CancellationToken.None));
        Assert.Empty(api.Requests);
        Assert.Equal("No hay suficiente material disponible.", vm.Message);
    }

    [Fact]
    public async Task ChangingInputInvalidatesPreviousCalculationAndRejectsDelayedResponse()
    {
        var api = new FakeStockApi();
        var vm = Model(api);
        await ReadyAsync(vm);
        var delayed = new TaskCompletionSource<ApiCallResult<StockCalculation>>(TaskCreationOptions.RunContinuationsAsynchronously);
        api.PreviewHandler = _ => delayed.Task;
        var task = vm.PreviewAsync(CancellationToken.None);
        vm.FinalVolume = "200";
        delayed.SetResult(Ok(Calculation));
        await task;
        Assert.Equal(20, vm.Calculation?.CalculatedWeight);
        Assert.Equal(50, vm.Calculation?.ActualConcentration);
        Assert.True(vm.CanSave);
        Assert.False(vm.IsCalculating);
    }

    [Fact]
    public async Task ConcurrentConsumptionConflictReloadsSourcesAndRequiresFreshSelection()
    {
        var api = new FakeStockApi { CreateErrorCode = ErrorCodes.Conflict };
        var vm = Model(api);
        await ReadyAsync(vm);
        Assert.False(await vm.SaveAsync(CancellationToken.None));
        Assert.Null(vm.SelectedSource);
        Assert.Null(vm.Calculation);
        Assert.Equal(2, api.SourceCalls);
        Assert.False(vm.CanSave);
    }

    [Fact]
    public async Task NetworkRetryReusesIdenticalRequestToAvoidDuplicateConsumption()
    {
        var api = new FakeStockApi { FailFirstCreate = true };
        var vm = Model(api);
        await ReadyAsync(vm);
        Assert.False(await vm.SaveAsync(CancellationToken.None));
        Assert.True(await vm.SaveAsync(CancellationToken.None));
        Assert.Equal(api.Requests[0], api.Requests[1]);
    }

    [Fact]
    public async Task ServerFailureShowsSupportReferenceAndRetryPreservesTheRequest()
    {
        var api = new FakeStockApi { CreateError = new(ErrorCodes.UnexpectedError,
            "Ocurrió un error inesperado. Use el identificador de soporte para solicitar ayuda.", "stock-save-123") };
        var vm = Model(api);
        await ReadyAsync(vm);
        Assert.False(await vm.SaveAsync(CancellationToken.None));
        Assert.Contains("Referencia de soporte: stock-save-123", vm.Message, StringComparison.Ordinal);
        Assert.Null(vm.Created);
        Assert.Equal(100, vm.SelectedSource!.AvailableQuantity);
        api.CreateError = null;
        Assert.True(await vm.SaveAsync(CancellationToken.None));
        Assert.Equal(api.Requests[0], api.Requests[1]);
    }

    [Fact]
    public async Task NewDraftResetsIdentityAndValidationAcceptsLocalizedDecimals()
    {
        var api = new FakeStockApi();
        var vm = Model(api);
        await ReadyAsync(vm);
        vm.Concentration = "100,5";
        await vm.PreviewAsync(CancellationToken.None);
        Assert.Equal(100.5m, api.LastPreview?.TargetConcentration);
        vm.Concentration = "1,000.2";
        await vm.PreviewAsync(CancellationToken.None);
        Assert.Null(vm.Calculation);
        vm.BeginNew();
        Assert.Null(vm.SelectedSource);
        Assert.Null(vm.Created);
    }

    [Fact]
    public async Task LiveCalculationKeepsTheoreticalWeightWhileRequiringMeasuredWeightAndDates()
    {
        var api = new FakeStockApi();
        var vm = Model(api);
        await ReadyAsync(vm);
        vm.ActualQuantity = string.Empty;
        Assert.Equal(10, vm.Calculation?.CalculatedWeight);
        Assert.Null(vm.Calculation?.ActualWeight);
        Assert.Null(vm.Calculation?.ActualConcentration);
        Assert.False(vm.CanSave);
        Assert.False(await vm.SaveAsync(CancellationToken.None));
        Assert.Empty(api.Requests);
        vm.ActualQuantity = "10.088";
        Assert.Equal(10, vm.Calculation?.CalculatedWeight);
        Assert.Equal(100.88m, vm.Calculation?.ActualConcentration);
        Assert.True(vm.CanSave);
        vm.ExpirationDate = null;
        Assert.False(vm.CanSave);
        vm.ExpirationDate = vm.PreparationDate.AddDays(-1);
        Assert.False(vm.CanSave);
        vm.ExpirationDate = vm.PreparationDate.AddDays(30);
        vm.ActualQuantity = "101";
        Assert.False(vm.CanSave);
    }
    [Fact]
    public async Task HistoricalExpiredSourceAndStockCanBeSavedWithOriginalDates()
    {
        var api = new FakeStockApi();
        var vm = Model(api);
        await ReadyAsync(vm);
        var today = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-6));
        vm.SelectedSource = Source with
        {
            ReceivedDate = DateOnly.FromDateTime(today.AddDays(-90).Date),
            ExpirationDate = DateOnly.FromDateTime(today.AddDays(-30).Date),
        };
        vm.PreparationDate = today.AddDays(-60);
        vm.ExpirationDate = today.AddDays(-40);
        await vm.PreviewAsync(CancellationToken.None);
        Assert.True(vm.CanSave);
        Assert.True(await vm.SaveAsync(CancellationToken.None));
        var request = Assert.Single(api.Requests);
        Assert.Equal(DateOnly.FromDateTime(today.AddDays(-60).Date), request.PreparationDate);
        Assert.Equal(DateOnly.FromDateTime(today.AddDays(-40).Date), request.ExpirationDate);
    }

    [Fact]
    public async Task HistoricalStockBeforeSourceReceiptCanBeSavedWithoutChangingRecordedDates()
    {
        var api = new FakeStockApi();
        var vm = Model(api);
        await ReadyAsync(vm);
        var today = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-6));
        vm.SelectedSource = Source with { ReceivedDate = DateOnly.FromDateTime(today.AddDays(-10).Date) };
        vm.PreparationDate = today.AddYears(-1);
        vm.ExpirationDate = today.AddDays(-30);
        vm.ActualQuantity = "10";
        Assert.True(vm.CanSave);
        Assert.True(await vm.SaveAsync(CancellationToken.None));
        var request = Assert.Single(api.Requests);
        Assert.Equal(DateOnly.FromDateTime(today.AddYears(-1).Date), request.PreparationDate);
        Assert.Equal(DateOnly.FromDateTime(today.AddDays(-30).Date), request.ExpirationDate);
        Assert.Equal(DateOnly.FromDateTime(today.AddDays(-10).Date), vm.SelectedSource!.ReceivedDate);

        vm.Created = null;
        vm.PreparationDate = today.AddDays(1);
        Assert.False(vm.CanSave);
        vm.PreparationDate = today.AddYears(-1);
        vm.ExpirationDate = vm.PreparationDate.AddDays(-1);
        Assert.False(vm.CanSave);
        vm.ExpirationDate = today.AddYears(3);
        vm.SelectedSource = Source with { ExpirationDate = DateOnly.FromDateTime(today.AddDays(-60).Date) };
        vm.PreparationDate = today.AddDays(-30);
        vm.ActualQuantity = "10";
        Assert.False(vm.CanSave);
    }

    [Fact]
    public async Task CompositionEditorPreservesPercentagesAndBlocksIncompleteDuplicateAndRemovedRows()
    {
        var api = new FakeStockApi(); var vm = Model(api); await ReadyAsync(vm);
        var original = vm.Calculation;
        vm.SolventRows[0].Proportion = "50";
        var water = vm.AddSolvent(); water.SelectedSolvent = vm.SolventCatalog[1]; water.Proportion = "50";
        Assert.True(vm.CanSave);
        Assert.Equal("50", vm.SolventRows[0].Proportion);
        var acn = vm.AddSolvent();
        Assert.False(vm.CanSave); Assert.Equal("50", water.Proportion);
        acn.SelectedSolvent = vm.SolventCatalog[2]; acn.Proportion = "20";
        vm.SolventRows[0].Proportion = "30";
        Assert.True(vm.CanSave);
        Assert.Equal("MeOH / H2O / ACN \u2014 30:50:20", vm.CompositionSummary);
        acn.SelectedSolvent = water.SelectedSolvent; Assert.False(vm.CanSave);
        acn.SelectedSolvent = vm.SolventCatalog[2]; acn.Proportion = "20.00001"; Assert.False(vm.CanSave);
        acn.Proportion = "20"; vm.RemoveSolvent(acn); Assert.False(vm.CanSave);
        vm.SolventRows[0].Proportion = "50"; Assert.True(vm.CanSave);
        vm.SolventRows[0].Proportion = "50.125"; water.Proportion = "49.875"; Assert.True(vm.CanSave);
        Assert.Same(original, vm.Calculation);
        Assert.True(await vm.SaveAsync(default));
        var saved = Assert.Single(api.Requests);
        Assert.Equal(vm.Compound, saved.Compound);
        Assert.Equal<decimal>([50.125m, 49.875m], saved.Solvents!.Select(r => r.Percent));
    }

    [Fact]
    public async Task CompoundSuggestionIsEditableAndBeginNewClearsComposition()
    {
        var vm = Model(new FakeStockApi()); await ReadyAsync(vm);
        Assert.Equal(Source.Name, vm.Compound);
        vm.Compound = "Analito corregido";
        vm.SelectedSource = Source with { Name = "Otro origen" };
        Assert.Equal("Analito corregido", vm.Compound);
        vm.BeginNew(); Assert.Empty(vm.SolventRows); Assert.Equal(string.Empty, vm.Compound); Assert.False(vm.CanSave);
    }

    private static StockViewModel Model(FakeStockApi api) => new(api, new Session("Administrador", []));
    private static async Task ReadyAsync(StockViewModel vm)
    {
        vm.BeginNew();
        await vm.LoadSolventsAsync(CancellationToken.None);
        var solvent = vm.AddSolvent(); solvent.SelectedSolvent = vm.SolventCatalog[0]; solvent.Proportion = "100";
        await vm.SearchSourcesAsync(null, 1, CancellationToken.None);
        vm.SelectedSource = vm.Sources[0];
        vm.Concentration = "100";
        vm.FinalVolume = "100";
        vm.ActualQuantity = "10";
        vm.ExpirationDate = new DateTimeOffset(2027, 12, 31, 0, 0, 0, TimeSpan.FromHours(-6));
        await vm.PreviewAsync(CancellationToken.None);
    }
    private static readonly StockSource Source = new(Guid.NewGuid(), "Estándar", "64-17-5", "CAT", "Lote", "Marca", "Ensayo", 100, 100, 100, "mg", new DateOnly(2027, 12, 31), "Laboratorio", Guid.NewGuid());
    private static readonly StockCalculation Calculation = new(10, 10, "mg", 100, 90, 0, true, 100, 100, 100, "mg/L", 100, "mL", "m = C × V / pureza");
    private static ApiCallResult<T> Ok<T>(T value) => new(true, value, null, 200);
    private sealed class FakeStockApi : IStockApiClient
    {
        public Func<PreparationListFilter?, Task<ApiCallResult<StockPage>>>? ListHandler { get; set; }
        public IReadOnlyList<StockSummary>? ListItems { get; set; }
        public List<Guid> DetailIds { get; } = [];
        public Func<Guid, Task<ApiCallResult<StockDetail>>>? DetailHandler { get; set; }
        public ApiError? CreateError { get; set; }
        public StockCalculation Result { get; set; } = Calculation;
        public int ListCalls { get; private set; }
        public int SourceCalls { get; private set; }
        public List<CreateStockRequest> Requests { get; } = [];
        public StockCalculationRequest? LastPreview { get; private set; }
        public Func<StockCalculationRequest, Task<ApiCallResult<StockCalculation>>>? PreviewHandler { get; set; }
        public bool FailFirstCreate { get; set; }
        public string? CreateErrorCode { get; set; }
        public StockDetail Created { get; } = new(new StockSummary(Guid.NewGuid(), "STK-20261006-001", "Stock", Source.Name, Source.Lot,
            100, 100, "mg/L", 100, "mL", new DateOnly(2026, 10, 6), "Ana López", "Active"), Source, Calculation,
            new(Guid.NewGuid(), 10, "mg", 100, 90, "Ana López", DateTimeOffset.UtcNow), null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, Guid.NewGuid());
        public Task<ApiCallResult<IReadOnlyList<SolventOption>>> SolventsAsync(string? search, CancellationToken cancellationToken) => Task.FromResult(Ok<IReadOnlyList<SolventOption>>([new(4, "Metanol", "MeOH", true, 3), new(8, "Agua", "H2O", true, 7), new(3, "Acetonitrilo", "ACN", true, 2)]));
        public Task<ApiCallResult<StockOptions>> OptionsAsync(CancellationToken cancellationToken) => Task.FromResult(Ok(new StockOptions(["mg/L"], ["mL"])));
        public Task<ApiCallResult<StockSourcePage>> SourcesAsync(string? search, int page, CancellationToken cancellationToken) { SourceCalls++; return Task.FromResult(Ok(new StockSourcePage([Source], page, 25, 1))); }
        public Task<ApiCallResult<StockCalculation>> PreviewAsync(StockCalculationRequest request, CancellationToken cancellationToken) { LastPreview = request; return PreviewHandler?.Invoke(request) ?? Task.FromResult(Ok(Result)); }
        public Task<ApiCallResult<StockDetail>> CreateAsync(CreateStockRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (CreateError is { } error) return Task.FromResult(new ApiCallResult<StockDetail>(false, null, error, 500));
            if (CreateErrorCode is { } code) return Task.FromResult(new ApiCallResult<StockDetail>(false, null, new ApiError(code, "El estándar cambió.", string.Empty), 409));
            if (FailFirstCreate && Requests.Count == 1) throw new HttpRequestException("Simulated lost response");
            return Task.FromResult(Ok(Created));
        }
        public Task<ApiCallResult<StockPage>> ListAsync(string? search, int page, CancellationToken cancellationToken, PreparationListFilter? filter = null) { ListCalls++; var items = ListItems ?? [Created.Preparation]; return ListHandler?.Invoke(filter) ?? Task.FromResult(Ok(new StockPage(items, page, 25, items.Count))); }
        public Task<ApiCallResult<StockDetail>> GetAsync(Guid id, CancellationToken cancellationToken) { DetailIds.Add(id); return DetailHandler?.Invoke(id) ?? Task.FromResult(Ok(Created)); }
    }
    private sealed class Session(string role, IReadOnlyList<string> permissions) : ISessionService
    {
        public string? AccessToken => null;
        public UserProfile? Profile { get; } = new(42, "Ana López", "ana@intertek.com", role, "Laboratorio", permissions);
        public Task SetAuthenticatedAsync(AuthenticationResponse response, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<string?> ReadRefreshTokenAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);
        public Task ClearAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

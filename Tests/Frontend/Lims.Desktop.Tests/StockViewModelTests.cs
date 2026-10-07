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
    public async Task ViewerCanListButCannotCreateOrQuerySources()
    {
        var api = new FakeStockApi();
        var vm = new StockViewModel(api, new Session("Usuario", ["reference_materials.view"]));
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
    private static StockViewModel Model(FakeStockApi api) => new(api, new Session("Administrador", []));
    private static async Task ReadyAsync(StockViewModel vm)
    {
        vm.BeginNew();
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
        public Task<ApiCallResult<StockOptions>> OptionsAsync(CancellationToken cancellationToken) => Task.FromResult(Ok(new StockOptions(["mg/L"], ["mL"])));
        public Task<ApiCallResult<StockSourcePage>> SourcesAsync(string? search, int page, CancellationToken cancellationToken) { SourceCalls++; return Task.FromResult(Ok(new StockSourcePage([Source], page, 25, 1))); }
        public Task<ApiCallResult<StockCalculation>> PreviewAsync(StockCalculationRequest request, CancellationToken cancellationToken) { LastPreview = request; return PreviewHandler?.Invoke(request) ?? Task.FromResult(Ok(Result)); }
        public Task<ApiCallResult<StockDetail>> CreateAsync(CreateStockRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (CreateErrorCode is { } code) return Task.FromResult(new ApiCallResult<StockDetail>(false, null, new ApiError(code, "El estándar cambió.", string.Empty), 409));
            if (FailFirstCreate && Requests.Count == 1) throw new HttpRequestException("Simulated lost response");
            return Task.FromResult(Ok(Created));
        }
        public Task<ApiCallResult<StockPage>> ListAsync(string? search, int page, CancellationToken cancellationToken) { ListCalls++; return Task.FromResult(Ok(new StockPage([Created.Preparation], page, 25, 1))); }
        public Task<ApiCallResult<StockDetail>> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Ok(Created));
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

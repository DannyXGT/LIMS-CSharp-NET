using Lims.Contracts.Authentication;
using Lims.Contracts.Errors;
using Lims.Contracts.ReferencePreparations;
using Lims.Desktop.Http;
using Lims.Desktop.Services;
using Lims.Desktop.ViewModels;

namespace Lims.Desktop.Tests;

public sealed class IntermediateViewModelTests
{
    [Fact]
    public async Task LiveCompositionUpdatesCylinderResultsAndDoesNotMakeHttpRequestsPerKeystroke()
    {
        var api = new FakeApi(); var vm = await Ready(api);
        var row = vm.AddComponent(); row.Source = api.Source; row.Volume = "25";
        Assert.Equal(25, vm.OccupiedPercent); Assert.Equal(25, vm.Calculation!.Results[0].Concentration);
        Assert.True(vm.CanSave); Assert.Equal(1, api.OptionCalls); Assert.Equal(0, api.PreviewCalls);
        row.Volume = "75"; Assert.Equal(75, vm.OccupiedPercent);
        row.Unit = "L"; Assert.False(vm.CanSave); Assert.True(vm.Calculation!.ExceedsFinalVolume);
        row.Unit = "mL"; row.Volume = "100"; Assert.Equal(100, vm.OccupiedPercent); Assert.True(vm.CanSave);
        row.Volume = "110"; Assert.False(vm.CanSave); Assert.Contains("supera", vm.VolumeError, StringComparison.Ordinal);
        vm.RemoveComponent(row); Assert.Equal(0, vm.OccupiedPercent); Assert.False(vm.CanSave);
    }
    [Fact]
    public async Task MethodChangePreservesSelectedComponentsAndRequiresCompatibilityCorrection()
    {
        var api = new FakeApi(); var vm = await Ready(api); var row = vm.AddComponent(); row.Source = api.Source; row.Volume = "25";
        vm.Method = vm.Methods[1]; Assert.Same(api.Source, row.Source); Assert.Equal("25", row.Volume);
        Assert.Contains("incompatible", row.Error, StringComparison.Ordinal); Assert.False(vm.CanSave);
        vm.Method = vm.Methods[0]; Assert.True(vm.CanSave);
    }
    [Fact]
    public async Task RetryAfterLostResponseReusesExactRequestAndFreezesInputsUntilResolved()
    {
        var api = new FakeApi { LoseFirstResponse = true }; var vm = await Ready(api); var row = vm.AddComponent(); row.Source = api.Source; row.Volume = "25";
        Assert.False(await vm.SaveAsync(CancellationToken.None)); Assert.False(vm.InputsEnabled); Assert.True(vm.CanSave);
        Assert.True(await vm.SaveAsync(CancellationToken.None)); Assert.Equal(api.Requests[0], api.Requests[1]);
        Assert.Equal("Ana López", vm.PreparedBy); Assert.Single(vm.Items); Assert.NotNull(vm.Created); Assert.False(vm.CanSave);
    }
    [Fact]
    public async Task DilutionUsesOneComponentAndInsufficientVolumeOrMissingDatesBlockCreate()
    {
        var api = new FakeApi(); var vm = await Ready(api);
        var parent = api.Source with { Kind = "Intermedia", Id = Guid.NewGuid(), AvailableVolume = 20 };
        vm.SelectDilution(parent); Assert.Single(vm.Components); Assert.False(vm.CanAddComponent);
        vm.Components[0].Volume = "25"; Assert.False(vm.CanSave);
        Assert.Contains("suficiente", vm.Components[0].Error, StringComparison.Ordinal);
        vm.ExpirationDate = null; Assert.False(vm.CanSave); Assert.NotEmpty(vm.DateError);
    }
    [Fact]
    public async Task ViewerCannotQueryOriginsOrCreateAndCanList()
    {
        var api = new FakeApi(); var vm = new IntermediateViewModel(api, new Session("Usuario"));
        vm.BeginNew(); await vm.LoadOptionsAsync(CancellationToken.None); await vm.LoadAsync(null, 1, CancellationToken.None);
        Assert.True(vm.CanView); Assert.False(vm.CanCreate); Assert.Equal(0, api.OptionCalls);
    }
    private static async Task<IntermediateViewModel> Ready(FakeApi api)
    {
        var vm = new IntermediateViewModel(api, new Session("Administrador")); vm.BeginNew(); await vm.LoadOptionsAsync(CancellationToken.None);
        vm.Method = vm.Methods[0]; vm.Name = "Intermedia de prueba"; vm.FinalVolume = "100";
        vm.ExpirationDate = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-6)).AddDays(30); return vm;
    }
    private sealed class Session(string role) : ISessionService
    {
        public string? AccessToken => null;
        public UserProfile? Profile { get; } = new(42, "Ana López", "ana@intertek.com", role, "Laboratorio", ["reference_materials.view"]);
        public Task SetAuthenticatedAsync(AuthenticationResponse response, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<string?> ReadRefreshTokenAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);
        public Task ClearAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
    private sealed class FakeApi : IIntermediateApiClient
    {
        public IntermediateSource Source { get; } = new(Guid.NewGuid(), "Stock", "STK-20261007-001", "Analito", 1, "Ensayo", 200, 200, "mL", new(2026, 1, 1), new(2099, 1, 1), Guid.NewGuid(), [new(Guid.NewGuid(), "Analito", null, "LOTE", 100, "mg/L")]);
        public int OptionCalls { get; private set; } public int PreviewCalls { get; private set; }
        public bool LoseFirstResponse { get; init; }
        public List<CreateIntermediateRequest> Requests { get; } = [];
        private IntermediateDetail? _created;
        public Task<ApiCallResult<IntermediateOptions>> OptionsAsync(CancellationToken cancellationToken) { OptionCalls++; return Task.FromResult(Ok(new IntermediateOptions([new(1, "Ensayo"), new(2, "Otro método")], [Source]))); }
        public Task<ApiCallResult<IntermediateCalculation>> PreviewAsync(IntermediateCalculationRequest request, CancellationToken cancellationToken) { PreviewCalls++; return Task.FromResult(Ok(IntermediateCalculator.Calculate(request, [Source]))); }
        public Task<ApiCallResult<IntermediateDetail>> CreateAsync(CreateIntermediateRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request); if (LoseFirstResponse && Requests.Count == 1) throw new HttpRequestException("Lost response");
            var calculation = IntermediateCalculator.Calculate(request.Calculation, [Source]);
            _created = new(new(request.RequestId, "INT-20261007-001", request.Name, "Ensayo", 1, 100, "mL", 100, calculation.Results,
                request.PreparationDate, request.ExpirationDate, "Ana López", "Active"), calculation, [], null, request.Notes,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "Ana López", Guid.NewGuid()); return Task.FromResult(Ok(_created));
        }
        public Task<ApiCallResult<IntermediatePage>> ListAsync(string? search, int page, CancellationToken cancellationToken) => Task.FromResult(Ok(new IntermediatePage(_created is null ? [] : [_created.Preparation], page, 25, _created is null ? 0 : 1)));
        public Task<ApiCallResult<IntermediateDetail>> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(_created is null ? new(false, null, new ApiError(ErrorCodes.NotFound, "No existe.", ""), 404) : Ok(_created));
        private static ApiCallResult<T> Ok<T>(T value) => new(true, value, null, 200);
    }
}

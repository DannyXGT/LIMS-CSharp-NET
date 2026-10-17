using Lims.Contracts.Authentication;
using Lims.Contracts.Errors;
using Lims.Contracts.ReferencePreparations;
using Lims.Desktop.Http;
using Lims.Desktop.Services;
using Lims.Desktop.ViewModels;

namespace Lims.Desktop.Tests;

public sealed class SolutionGroupViewModelTests
{
    [Theory]
    [InlineData("Curva")]
    [InlineData("AQS")]
    public async Task ThreeMultianalyteSolutionsShareOneDebitAndKeepExactDisplayedResults(string kind)
    {
        var api = new FakeApi(); var vm = await Ready(kind, api);
        var a = Add(vm, api.Source, "2", "100"); a.IsInternalStandard = true;
        Add(vm, api.Source, "3", "100"); var c = Add(vm, api.Source, "5", "100");
        var debit = Assert.Single(vm.Calculation!.Debits);
        Assert.Equal(10, debit.Quantity); Assert.Equal(90, debit.RemainingVolume);
        Assert.Equal("Solución 01 · Estándar interno", a.Title);
        Assert.Equal(api.Source.Id, a.Preview!.Source.Id);
        Assert.All(vm.Components, row => Assert.Contains("90 mL", row.SourceBalance));
        foreach (var part in vm.Calculation.Parts)
        {
            var rows = Lims.Desktop.Presentation.SolutionAnalyteRow.From(part);
            Assert.Equal(part.Results.Count, rows.Count);
            for (var i = 0; i < rows.Count; i++)
            {
                Assert.Equal(part.Source.Analytes[i].Concentration, rows[i].SourceConcentration);
                Assert.Equal(part.Results[i].Concentration, rows[i].ResultConcentration);
            }
        }
        c.Volume = "25";
        Assert.Equal(25, c.VolumePreview!.Percent); Assert.Equal(75, c.VolumePreview.Remaining);
        Assert.Equal(70, Assert.Single(vm.Calculation.Debits).RemainingVolume);
        vm.RemoveComponent(a); Assert.Equal("Solución 01 · Intermedia", vm.Components[0].Title);
    }
    [Fact]
    public async Task ExceededAforoKeepsVesselAndFieldErrorAndBlocksSaveAfterUnitConversion()
    {
        var api = new FakeApi(); var vm = await Ready("AQS", api); var row = Add(vm, api.Source, "0.110", "100");
        row.Unit = "L";
        Assert.False(vm.CanSave); Assert.Null(vm.Calculation); Assert.Null(row.Preview);
        Assert.True(row.VolumePreview!.Exceeds); Assert.Equal(110, row.VolumePreview.Percent);
        Assert.Equal("Danger", row.VolumePreview.Tone); Assert.Contains("10 mL", row.FieldError);
        row.Volume = "0.025";
        Assert.True(vm.CanSave); Assert.Empty(row.FieldError); Assert.Equal(25, row.VolumePreview.Percent);
        Assert.Equal("Information", row.VolumePreview.Tone);
        row.Volume = "0.095"; Assert.Equal("Warning", row.VolumePreview.Tone);
    }
    [Fact]
    public async Task NewDraftHasNoOldErrorsOrInventedResultsAndIncompleteRowDoesNotHideOtherPreview()
    {
        var api = new FakeApi(); var vm = await Ready("Curva", api);
        var complete = Add(vm, api.Source, "2", "100"); var incomplete = vm.AddComponent();
        Assert.NotNull(complete.Preview); Assert.Null(incomplete.VolumePreview); Assert.Null(incomplete.Preview);
        Assert.False(vm.CanSave);
        vm.Message = "Error anterior"; vm.BeginNew();
        Assert.Empty(vm.Message); Assert.Empty(vm.VisibleValidation); Assert.Null(vm.SelectedComponent);
        Assert.Null(vm.Calculation); Assert.Empty(vm.Components);
    }
    [Fact]
    public async Task RejectedSaveRetainsDraftAndPreventsSimultaneousSubmission()
    {
        var gate = new TaskCompletionSource<bool>();
        var api = new FakeApi { SaveGate = gate, RejectCreate = true }; var vm = await Ready("AQS", api);
        var row = Add(vm, api.Source, "2", "100"); var code = vm.Code;
        var first = vm.SaveAsync(default);
        Assert.True(vm.IsBusy); Assert.False(vm.InputsEnabled); Assert.False(await vm.SaveAsync(default));
        gate.SetResult(true); Assert.False(await first);
        Assert.Single(api.Requests); Assert.Equal(code, vm.Code); Assert.Equal("2", row.Volume);
        Assert.NotEmpty(vm.Message); Assert.True(vm.InputsEnabled); Assert.Null(vm.Created);
        vm.BeginNew(); Assert.Empty(vm.Message); Assert.Empty(vm.VisibleValidation);
    }
    [Theory]
    [InlineData("Curva", 188)]
    [InlineData("AQS", 2)]
    public async Task LiveCalculationUsesPerSolutionAforoAndAggregateSourceBalance(string kind, int lifetime)
    {
        var api = new FakeApi(); var vm = await Ready(kind, api);
        Assert.Equal(vm.PreparationDate!.Value.Date.AddDays(lifetime), vm.ExpirationDate!.Value.Date);
        var a = Add(vm, api.Source, "10", "100"); var b = Add(vm, api.Source, "15", "50"); a.IsInternalStandard = true;
        Assert.True(vm.CanSave); Assert.Equal(10, vm.Calculation!.Parts[0].Results[0].Concentration);
        Assert.Equal(30, vm.Calculation.Parts[1].Results[0].Concentration); Assert.Equal(75, Assert.Single(vm.Calculation.Debits).RemainingVolume);
        Assert.True(vm.Calculation.Parts[0].IsInternalStandard);
        b.Volume = "95"; b.FinalVolume = "100"; Assert.False(vm.CanSave); Assert.Contains("saldo", vm.Validation, StringComparison.Ordinal);
        vm.RemoveComponent(b); Assert.True(vm.CanSave);
        a.Unit = "g"; Assert.False(vm.CanSave); a.Unit = "mL"; Assert.True(vm.CanSave);
        vm.ExpirationDate = null; Assert.False(vm.CanSave); Assert.Equal(1, api.OptionCalls);
    }
    [Fact]
    public async Task LostResponseFreezesInputsAndRetriesExactRequestWithoutSecondConsumption()
    {
        var api = new FakeApi { LoseResponse = true }; var vm = await Ready("Curva", api); Add(vm, api.Source, "10", "100");
        Assert.False(await vm.SaveAsync(default)); Assert.False(vm.InputsEnabled); Assert.True(vm.CanSave);
        Assert.True(await vm.SaveAsync(default)); Assert.Equal(api.Requests[0], api.Requests[1]);
        Assert.Single(vm.Items); Assert.NotNull(vm.Created); Assert.False(vm.CanSave);
        Assert.Equal(1, api.Writes);
    }
    [Fact]
    public async Task OrdinaryUsersNeedExplicitNewPermissionsAndCurveOnlyUserCanReachPreparations()
    {
        var api = new FakeApi(); var session = new Session("Analista", ["reference_materials.view", "reference_materials.create"]);
        var vm = new SolutionGroupViewModel("AQS", api, session); vm.BeginNew(); await vm.LoadOptionsAsync(default);
        Assert.False(vm.CanView); Assert.False(vm.CanCreate); Assert.Equal(0, api.OptionCalls);
        var curveSession = new Session("Analista", ["curva.view"]);
        var shell = new ShellViewModel(curveSession, null!, null!); shell.RefreshProfile();
        Assert.True(shell.CanViewPreparations); Assert.True(shell.CanViewCurve); Assert.False(shell.CanViewAqs);
        vm = new("Curva", api, curveSession); Assert.True(vm.CanView); Assert.False(vm.CanCreate);
    }
    [Fact]
    public async Task TabsHaveIndependentFiltersSelectionsAndDraftsAndConnectionErrorsAreNotEmptyLists()
    {
        var api = new FakeApi(); var curve = await Ready("Curva", api); var aqs = await Ready("AQS", api);
        Add(curve, api.Source, "10", "100"); Assert.True(await curve.SaveAsync(default));
        curve.SelectedPreparation = curve.Items[0]; aqs.ListFilter = new(Method: "Otro método");
        Assert.Null(aqs.SelectedPreparation); Assert.Empty(aqs.Components); Assert.False(curve.ListFilter.HasCriteria);
        Assert.True(curve.OpenSelectedPreparationCommand.CanExecute(null));
        api.FailList = true; await aqs.LoadAsync(null, 1, default);
        Assert.True(aqs.HasListMessage); Assert.False(aqs.IsEmpty); Assert.False(aqs.ShowFooter);
        Assert.NotNull(curve.SelectedPreparation);
    }
    [Fact]
    public async Task RemovedOriginCannotSaveUntilExplicitlySelectedAgain()
    {
        var api = new FakeApi(); var vm = await Ready("AQS", api); var row = Add(vm, api.Source, "10", "100");
        Assert.True(vm.CanSave); api.HideSource = true; await vm.LoadOptionsAsync(default);
        Assert.Null(row.Source); Assert.False(vm.CanSave);
    }
    private static async Task<SolutionGroupViewModel> Ready(string kind, FakeApi api)
    {
        var vm = new SolutionGroupViewModel(kind, api, new Session("Administrador", [])); vm.BeginNew(); await vm.LoadOptionsAsync(default);
        vm.Method = vm.Methods[0]; vm.Code = kind + "-TEST"; return vm;
    }
    private static SolutionPartViewModel Add(SolutionGroupViewModel vm, IntermediateSource source, string volume, string final)
    {
        var row = vm.AddComponent(); row.Source = source; row.Volume = volume; row.FinalVolume = final; return row;
    }
    private sealed class Session(string role, IReadOnlyList<string> permissions) : ISessionService
    {
        public string? AccessToken => null;
        public UserProfile? Profile { get; } = new(42, "Ana López", "ana@intertek.com", role, "Laboratorio Analítico", permissions);
        public Task SetAuthenticatedAsync(AuthenticationResponse response, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<string?> ReadRefreshTokenAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);
        public Task ClearAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
    private sealed class FakeApi : ISolutionGroupApiClient
    {
        public IntermediateSource Source { get; } = new(Guid.NewGuid(), "Intermedia", "INT-TEST", "Analito", 1, "Ensayo", 100, 100, "mL", new(2026, 1, 1), new(2099, 1, 1), Guid.NewGuid(), [new(Guid.NewGuid(), "Analito", null, "LOTE", 100, "mg/L"), new(Guid.NewGuid(), "Segundo analito", "50-00-0", "LOTE-2", 0.123456789123456789m, "mg/L")]);
        public TaskCompletionSource<bool>? SaveGate { get; init; }
        public bool RejectCreate { get; init; }
        public bool LoseResponse { get; init; }
        public bool FailList { get; set; }
        public bool HideSource { get; set; }
        public int OptionCalls { get; private set; }
        public int Writes { get; private set; }
        public List<CreateSolutionGroupRequest> Requests { get; } = [];
        private SolutionGroupDetail? _created;
        public Task<ApiCallResult<IntermediateOptions>> OptionsAsync(string kind, CancellationToken cancellationToken)
        { OptionCalls++; return Task.FromResult(Ok(new IntermediateOptions([new(1, "Ensayo")], HideSource ? [] : [Source]))); }
        public Task<ApiCallResult<SolutionCalculation>> PreviewAsync(string kind, SolutionCalculationRequest request, CancellationToken cancellationToken) => Task.FromResult(Ok(SolutionGroupCalculator.Calculate(request, [Source])));
        public async Task<ApiCallResult<SolutionGroupDetail>> CreateAsync(string kind, CreateSolutionGroupRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (SaveGate is not null) await SaveGate.Task;
            if (RejectCreate) return new(false, null, new ApiError(ErrorCodes.InvalidState, "Saldo insuficiente.", "REGRESSION-409"), 409);
            if (_created is null)
            {
                Writes++;
                _created = new(new(request.RequestId, kind, request.Code, request.Name ?? request.Code, "Ensayo", request.Calculation.Parts.Count, Source.Code, request.PreparationDate, request.ExpirationDate, "Ana López", "Active"),
                    SolutionGroupCalculator.Calculate(request.Calculation, [Source]), [], request.Notes, request.StorageTemperature, DateTimeOffset.UtcNow, Guid.NewGuid());
            }
            if (LoseResponse && Requests.Count == 1) throw new HttpRequestException("Lost response after commit");
            return Ok(_created);
        }
        public Task<ApiCallResult<SolutionGroupPage>> ListAsync(string kind, string? search, int page, CancellationToken cancellationToken, PreparationListFilter? filter = null)
        {
            if (FailList) throw new HttpRequestException("Connection failed");
            return Task.FromResult(Ok(new SolutionGroupPage(_created?.Preparation.Kind == kind ? [_created.Preparation] : [], page, 25, _created?.Preparation.Kind == kind ? 1 : 0)));
        }
        public Task<ApiCallResult<SolutionGroupDetail>> GetAsync(string kind, Guid id, CancellationToken cancellationToken) => Task.FromResult(_created is null ? new(false, null, new ApiError(ErrorCodes.NotFound, "No existe.", ""), 404) : Ok(_created));
        private static ApiCallResult<T> Ok<T>(T value) => new(true, value, null, 200);
    }
}

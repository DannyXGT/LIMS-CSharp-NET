using Lims.Contracts.ReferenceMaterials;
using Lims.Contracts.ReferencePreparations;
using Lims.Desktop.Http;
using Lims.Desktop.ViewModels;

namespace Lims.Desktop.Tests;

public sealed class CommercialIntermediateViewModelTests
{
    [Fact]
    public async Task EmptyDraftDoesNotInventComponentsConcentrationsOrInventory()
    {
        var vm = await Ready(new Api());
        Assert.Empty(vm.Rows); Assert.Equal(0, vm.ValidCount); Assert.Empty(vm.PreviewComponents);
        Assert.Null(vm.InitialVolume); Assert.Null(vm.AvailableVolume); Assert.Equal(0, vm.AvailabilityPercent);
        Assert.Equal("—", vm.InitialDisplay); Assert.False(vm.CanSave); Assert.Empty(vm.Error(nameof(vm.Name)));
        vm.AddRow(); Assert.Equal(0, vm.ValidCount); Assert.Equal(0, vm.IncompleteCount);
    }
    [Fact]
    public async Task PartialRowsBlockSaveAndErrorsWaitUntilRowIsTouched()
    {
        var vm = await Ready(new Api(), true); var row = vm.AddRow()!;
        row.Name = "Analito"; Assert.Empty(row.VisibleError); Assert.False(vm.CanSave);
        row.Touch(); Assert.NotEmpty(row.VisibleError);
        row.Concentration = "50"; Assert.True(row.IsValid); Assert.True(vm.CanSave); Assert.Empty(row.VisibleError);
        var id = row.Id; row.Cas = "95-53-5"; Assert.False(row.IsValid); Assert.False(vm.CanSave);
        row.Cas = "95-53-4"; Assert.True(row.IsValid); Assert.Equal(id, vm.PreviewComponents[0].Id);
        vm.AddRow(); Assert.Equal(1, vm.ValidCount); Assert.True(vm.CanSave);
        vm.Rows[1].Name = "Incompleto"; Assert.False(vm.CanSave);
        vm.RemoveRow(vm.Rows[1]); Assert.True(vm.CanSave);
    }
    [Theory]
    [InlineData(3)] [InlineData(20)]
    public async Task ExcelPasteIsAtomicAndPreservesEachConcentration(int count)
    {
        var vm = await Ready(new Api(), true); vm.AddRow();
        Assert.True(vm.Paste(string.Join("\n", Enumerable.Range(1, count).Select(i => $"Analito {i}\t\t{(i == 2 ? "49.9" : "50")}\tµg/mL"))));
        Assert.Equal(count, vm.Rows.Count); Assert.Equal(count, vm.ValidCount); Assert.True(vm.CanSave);
        Assert.Equal(49.9m, vm.PreviewComponents[1].CertifiedConcentration);
        var ids = vm.Rows.Select(r => r.Id).ToArray();
        Assert.False(vm.Paste("Correcto\t\t50\tµg/mL\nInválido\t95-53-5\t50\tµg/mL"));
        Assert.Equal(ids, vm.Rows.Select(r => r.Id)); Assert.Equal(count, vm.ValidCount);
    }
    [Fact]
    public async Task SaveUsesExistingCommercialContractOneInventoryAndSolventCatalogueId()
    {
        var api = new Api(); var vm = await Ready(api, true); vm.Paste("A\t\t50\tµg/mL\nB\t\t49.9\tµg/mL");
        vm.Composition.Rows[0].SelectedSolvent = vm.Solvents.Single(); vm.BottleCount = "2";
        Assert.Equal(20, vm.InitialVolume); Assert.Equal(100, vm.AvailabilityPercent);
        Assert.NotNull(await vm.SaveAsync()); Assert.Null(await vm.SaveAsync());
        var request = Assert.Single(api.Requests); var material = request.NewMaterial!;
        Assert.Equal("Intermedia", api.Kind); Assert.Null(request.ExistingMaterialId); Assert.Null(material.PurityPercent);
        Assert.Null(material.CasNumber); Assert.Equal(MaterialPresentations.Mix, material.Presentation);
        Assert.Equal(2, material.PackageCount); Assert.Equal(10, material.PresentationQuantity);
        Assert.Equal(4, material.Commercial!.MatrixSolventId); Assert.Equal(2, material.Commercial.Components.Count);
        Assert.False(vm.CanSave);
    }
    [Fact]
    public async Task LostResponseRetriesExactRequestAndBlocksEditsAndConcurrentSaves()
    {
        var api = new Api { LoseFirst = true }; var vm = await Ready(api, true); vm.Paste("A\t\t50\tµg/mL");
        Assert.Null(await vm.SaveAsync()); Assert.False(vm.InputsEnabled); Assert.Null(vm.AddRow()); Assert.True(vm.CanSave);
        Assert.NotNull(await vm.SaveAsync()); Assert.Same(api.Requests[0], api.Requests[1]);
        api = new Api { Gate = new(TaskCreationOptions.RunContinuationsAsynchronously) }; vm = await Ready(api, true); vm.Paste("A\t\t50\tµg/mL");
        var first = vm.SaveAsync(); Assert.True(vm.IsBusy); Assert.Null(await vm.SaveAsync()); Assert.Single(api.Requests);
        api.Gate.SetResult(); Assert.NotNull(await first);
    }
    [Fact]
    public async Task ExistingAssociationUsesSameIdVersionAndActualBalanceIncludingUnknown()
    {
        var api = new Api(); var vm = await Ready(api, true); vm.UseExisting = true;
        Assert.False(vm.CanSave); Assert.Throws<ArgumentException>(() => vm.ReadRequest());
        vm.ExistingMaterial = api.Material; Assert.Equal(8, vm.AvailableVolume); Assert.Equal(80, vm.AvailabilityPercent);
        var request = vm.ReadRequest(); Assert.Equal(api.Material.Id, request.ExistingMaterialId);
        Assert.Equal(api.Material.Version, request.MaterialVersion); Assert.Null(request.NewMaterial);
        vm.ExistingMaterial = api.Material with { AvailableQuantity = null };
        Assert.Null(vm.AvailableVolume); Assert.Equal(0, vm.AvailabilityPercent); Assert.Equal("—", vm.AvailableDisplay);
    }
    [Fact]
    public async Task PermissionRevocationAndInvalidDatesBlockSaveWithoutLosingDraft()
    {
        var allowed = true; var api = new Api(); var vm = new CommercialIntermediateViewModel(api, "Ana", () => allowed);
        await vm.LoadAsync(); Fill(vm); vm.Paste("A\t\t50\tµg/mL"); Assert.True(vm.CanSave);
        allowed = false; Assert.False(vm.CanSave); Assert.Null(await vm.SaveAsync()); Assert.Empty(api.Requests);
        allowed = true; vm.ExpirationDate = vm.ReceivedDate!.Value.AddDays(-1); Assert.False(vm.CanSave);
        vm.ExpirationDate = vm.ReceivedDate; Assert.True(vm.CanSave); Assert.Single(vm.Rows);
    }
    private static async Task<CommercialIntermediateViewModel> Ready(Api api, bool filled = false)
    {
        var vm = new CommercialIntermediateViewModel(api, "Ana", () => true); await vm.LoadAsync(); if (filled) Fill(vm); return vm;
    }
    private static void Fill(CommercialIntermediateViewModel vm)
    {
        vm.Name = "Mix"; vm.Brand = "Fabricante"; vm.Lot = "L-01"; vm.Method = vm.Methods[0];
        vm.Composition.Rows[0].SelectedSolvent = vm.Solvents[0]; vm.Composition.Rows[0].Proportion = "100";
        vm.Volume = "10"; vm.Location = vm.Locations[0]; vm.Storage = "Ambiente"; vm.ExpirationDate = vm.ReceivedDate!.Value.AddYears(1);
    }
    private sealed class Api : ICommercialPreparationApiClient
    {
        public ReferenceMaterialDetail Material { get; } = new(Guid.NewGuid(), "Mix", null, null, 1, "Ensayo", null, "L-01", "Fabricante",
            new(2026, 1, 1), new(2099, 1, 1), 10, 1, "mL", 1, 10, 8, "Ambiente", 1, "Lab", "Active", 1, DateTimeOffset.UtcNow,
            1, DateTimeOffset.UtcNow, null, null, null, null, Guid.NewGuid(), Presentation: MaterialPresentations.Mix,
            Commercial: new("", "", null, null, null, null, null, null, [new(Guid.NewGuid(), "A", null, 50, "µg/mL")]));
        public List<RegisterCommercialPreparationRequest> Requests { get; } = [];
        public bool LoseFirst { get; init; }
        public TaskCompletionSource? Gate { get; init; }
        public string? Kind { get; private set; }
        public Task<ApiCallResult<CommercialPreparationOptions>> OptionsAsync(string kind, string? search, CancellationToken ct) => Task.FromResult(Ok(new CommercialPreparationOptions([new(1, "Ensayo")], [new(1, "Mililitros", "mL")], [new(1, "Lab")], [Material], [new(4, "Metanol", "MeOH", true, 1)], true)));
        public async Task<ApiCallResult<CommercialPreparationDetail>> RegisterAsync(string kind, RegisterCommercialPreparationRequest request, CancellationToken ct)
        {
            Kind = kind; Requests.Add(request);
            if (Gate is not null) await Gate.Task;
            if (LoseFirst && Requests.Count == 1) throw new HttpRequestException();
            return Ok(new CommercialPreparationDetail(kind, "INT-TEST", Material, "Ana", DateTimeOffset.UtcNow));
        }
        public Task<ApiCallResult<CommercialPreparationDetail>> GetAsync(string kind, Guid id, CancellationToken ct) => throw new NotSupportedException();
        private static ApiCallResult<T> Ok<T>(T value) => new(true, value, null, 200);
    }
}

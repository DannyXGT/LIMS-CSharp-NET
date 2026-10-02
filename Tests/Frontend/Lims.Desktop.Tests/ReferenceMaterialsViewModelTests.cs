using Lims.Contracts.Authentication;
using Lims.Contracts.Errors;
using Lims.Contracts.ReferenceMaterials;
using Lims.Desktop.Http;
using Lims.Desktop.Services;
using Lims.Desktop.ViewModels;

namespace Lims.Desktop.Tests;

public sealed class ReferenceMaterialsViewModelTests
{
    [Fact]
    public async Task LoadWithoutViewPermissionDoesNotCallApi()
    {
        var api = new FakeApi();
        var viewModel = new ReferenceMaterialsViewModel(api, new FakeSession([]));

        await viewModel.LoadAsync();

        Assert.Equal(0, api.ListCalls);
        Assert.Contains("permiso", viewModel.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AdministratorCanUseReferenceMaterialsWithoutDuplicatedPermissionRows()
    {
        var api = new FakeApi();
        var viewModel = new ReferenceMaterialsViewModel(
            api,
            new FakeSession([], "Administrador"));

        await viewModel.LoadAsync();

        Assert.Equal(1, api.ListCalls);
        Assert.True(viewModel.CanView);
        Assert.True(viewModel.CanCreate);
        Assert.True(viewModel.CanEdit);
        Assert.True(viewModel.CanArchive);
    }

    [Fact]
    public async Task LoadWithPermissionReturnsRealApiRowsAndKeepsActionsPermissionGated()
    {
        var api = new FakeApi
        {
            Page = new ReferenceMaterialPage(
                [new ReferenceMaterialSummary(
                    Guid.NewGuid(),
                    "Etanol CRM",
                    "64-17-5",
                    "CAT-001",
                    "GC",
                    "LOT-1",
                    "Proveedor",
                    99.5m,
                    new DateOnly(2027, 9, 30),
                    "Active",
                    25m,
                    "g",
                    null,
                    Guid.NewGuid())],
                1,
                25,
                1),
        };
        var viewModel = new ReferenceMaterialsViewModel(
            api,
            new FakeSession([ReferenceMaterialPermissions.View]));

        await viewModel.LoadAsync();

        Assert.Single(viewModel.Items);
        Assert.True(viewModel.CanView);
        Assert.False(viewModel.CanCreate);
        Assert.False(viewModel.CanEdit);
        Assert.False(viewModel.CanArchive);
    }

    [Fact]
    public async Task NextPageRequestsTheFollowingApiPage()
    {
        var api = new FakeApi { Page = new ReferenceMaterialPage([], 1, 25, 26) };
        var viewModel = new ReferenceMaterialsViewModel(
            api,
            new FakeSession([ReferenceMaterialPermissions.View]));

        await viewModel.LoadAsync();
        await viewModel.GoToNextPageAsync(CancellationToken.None);

        Assert.Equal(2, api.LastRequestedPage);
    }

    [Fact]
    public async Task LoadCatalogsUsesApiOptionsAndReportsReady()
    {
        var viewModel = new ReferenceMaterialsViewModel(
            new FakeApi(),
            new FakeSession([ReferenceMaterialPermissions.View]));

        await viewModel.LoadCatalogsAsync();

        Assert.True(viewModel.CatalogsReady);
        Assert.Single(viewModel.Methods);
        Assert.Single(viewModel.Units);
        Assert.Single(viewModel.Locations);
        Assert.Equal("g", viewModel.Units[0].Symbol);
    }

    [Fact]
    public async Task RapidSelectionDoesNotLetSlowerPreviousRequestOverwriteDetail()
    {
        var first = Summary("Primero");
        var second = Summary("Segundo");
        var firstCompletion = new TaskCompletionSource<ApiCallResult<ReferenceMaterialDetail>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var api = new FakeApi
        {
            GetHandler = id => id == first.Id
                ? firstCompletion.Task
                : Task.FromResult(new ApiCallResult<ReferenceMaterialDetail>(true, Detail(second), null, 200)),
        };
        var viewModel = new ReferenceMaterialsViewModel(
            api,
            new FakeSession([ReferenceMaterialPermissions.View]));

        var firstLoad = viewModel.SelectAsync(first, CancellationToken.None);
        await viewModel.SelectAsync(second, CancellationToken.None);
        firstCompletion.SetResult(new ApiCallResult<ReferenceMaterialDetail>(true, Detail(first), null, 200));
        await firstLoad;

        Assert.Equal(second.Id, viewModel.SelectedDetail?.Id);
    }

    [Fact]
    public async Task CreatePreservesSupportIdAndExplainsOutdatedDatabaseSchema()
    {
        var api = new FakeApi
        {
            CreateResult = new ApiCallResult<ReferenceMaterialDetail>(
                false,
                null,
                new ApiError(
                    ErrorCodes.DatabaseSchemaOutOfDate,
                    "El esquema de base de datos requiere actualización.",
                    "support-schema-123"),
                503),
        };
        var viewModel = new ReferenceMaterialsViewModel(
            api,
            new FakeSession([ReferenceMaterialPermissions.Create]));

        var saved = await viewModel.CreateAsync(CreateRequest(), CancellationToken.None);

        Assert.False(saved);
        Assert.Equal("El esquema de base de datos requiere actualización.", viewModel.LastSaveFailure?.Message);
        Assert.Equal("support-schema-123", viewModel.LastSaveFailure?.SupportId);
    }

    [Fact]
    public async Task CreateExplainsStaleMethodWithoutTreatingItAsInternalFailure()
    {
        var api = new FakeApi
        {
            CreateResult = new ApiCallResult<ReferenceMaterialDetail>(
                false,
                null,
                new ApiError(
                    ErrorCodes.ValidationError,
                    "Los datos enviados no son válidos.",
                    "validation-123",
                    new Dictionary<string, string[]> { ["methodId"] = ["inactive"] }),
                400),
        };
        var viewModel = new ReferenceMaterialsViewModel(
            api,
            new FakeSession([ReferenceMaterialPermissions.Create]));

        var saved = await viewModel.CreateAsync(CreateRequest(), CancellationToken.None);

        Assert.False(saved);
        Assert.Contains("método seleccionado", viewModel.LastSaveFailure?.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(viewModel.LastSaveFailure?.SupportId);
    }

    private static CreateReferenceMaterialRequest CreateRequest() => new(
        "Naphthol AS", "92-77-3", "DRE-C15431000", 13, 98.09m, "H1622997", "Dr. Ehrenstorfer",
        new DateOnly(2026, 10, 1), new DateOnly(2029, 5, 6), 100m, 3, 1, "T ambiente", 1);

    private static ReferenceMaterialSummary Summary(string name) => new(
        Guid.NewGuid(), name, "64-17-5", "CAT-001", "GC", "LOT-1", "Proveedor", 99.5m,
        new DateOnly(2027, 9, 30), "Active", 25m, "g", 25m, Guid.NewGuid());

    private static ReferenceMaterialDetail Detail(ReferenceMaterialSummary summary) => new(
        summary.Id, summary.Name, summary.CasNumber, summary.CatalogNumber, 1, summary.Method,
        summary.PurityPercent, summary.Lot, summary.Brand, new DateOnly(2026, 9, 30),
        summary.ExpirationDate, 5m, 2, summary.Unit, 5, summary.TotalQuantity, summary.AvailableQuantity,
        "2–8 °C", 1, "Laboratorio", summary.Status, 42, DateTimeOffset.UtcNow, 42,
        DateTimeOffset.UtcNow, null, null, null, null, summary.Version);

    private sealed class FakeApi : IReferenceMaterialsApiClient
    {
        public int ListCalls { get; private set; }
        public int LastRequestedPage { get; private set; }
        public ReferenceMaterialPage Page { get; set; } = new([], 1, 25, 0);
        public Func<Guid, Task<ApiCallResult<ReferenceMaterialDetail>>>? GetHandler { get; init; }
        public ApiCallResult<ReferenceMaterialDetail>? CreateResult { get; init; }

        public Task<ApiCallResult<IReadOnlyList<ReferenceMethodOption>>> GetMethodsAsync(
            CancellationToken cancellationToken) => Task.FromResult(
            new ApiCallResult<IReadOnlyList<ReferenceMethodOption>>(true, [new(1, "APEOs")], null, 200));

        public Task<ApiCallResult<IReadOnlyList<ReferenceUnitOption>>> GetUnitsAsync(
            CancellationToken cancellationToken) => Task.FromResult(
            new ApiCallResult<IReadOnlyList<ReferenceUnitOption>>(true, [new(2, "Gramo", "g")], null, 200));

        public Task<ApiCallResult<IReadOnlyList<ReferenceLocationOption>>> GetLocationsAsync(
            CancellationToken cancellationToken) => Task.FromResult(
            new ApiCallResult<IReadOnlyList<ReferenceLocationOption>>(true, [new(1, "Laboratorio")], null, 200));

        public Task<ApiCallResult<ReferenceMaterialPage>> ListAsync(
            string? search,
            string? status,
            string? method,
            int page,
            int pageSize,
            CancellationToken cancellationToken)
        {
            ListCalls++;
            LastRequestedPage = page;
            var requestedPage = Page with { Page = page };
            return Task.FromResult(new ApiCallResult<ReferenceMaterialPage>(true, requestedPage, null, 200));
        }

        public Task<ApiCallResult<ReferenceMaterialDetail>> GetAsync(Guid id, CancellationToken cancellationToken) =>
            GetHandler?.Invoke(id) ?? throw new NotSupportedException();

        public Task<ApiCallResult<ReferenceMaterialDetail>> CreateAsync(CreateReferenceMaterialRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(CreateResult ?? throw new NotSupportedException());

        public Task<ApiCallResult<ReferenceMaterialDetail>> UpdateAsync(Guid id, UpdateReferenceMaterialRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ApiCallResult<ReferenceMaterialDetail>> ArchiveAsync(Guid id, ArchiveReferenceMaterialRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ApiCallResult<ReferenceMaterialDetail>> ReplaceAsync(Guid id, ReplaceReferenceMaterialRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeSession(
        IReadOnlyList<string> permissions,
        string role = "Usuario") : ISessionService
    {
        public string? AccessToken => null;
        public UserProfile? Profile { get; } = new(
            42,
            "Test User",
            "test.user@intertek.com",
            role,
            "Laboratorio Químico",
            permissions);

        public Task SetAuthenticatedAsync(AuthenticationResponse response, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<string?> ReadRefreshTokenAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);
        public Task ClearAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

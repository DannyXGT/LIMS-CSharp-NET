using Lims.Contracts.Authentication;
using Lims.Contracts.ReferenceMaterials;
using Lims.Desktop.Http;
using Lims.Desktop.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lims.Desktop.VisualHarness;

// Deliberately isolated from production DI, PasswordVault, HTTP, and databases.
internal sealed class FixtureSession : ISessionService
{
    public string? AccessToken => null;
    public UserProfile? Profile { get; } = new(1, "Usuario de prueba", "fixture@example.invalid", "Administrador", "Validación visual", []);
    public Task SetAuthenticatedAsync(AuthenticationResponse response, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task<string?> ReadRefreshTokenAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);
    public Task ClearAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class FixtureAuthentication : IAuthenticationGateway
{
    public Task<AuthenticationOutcome> SignInAsync(string identifier, string password, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<LogoutOutcome> LogoutAsync(CancellationToken cancellationToken) => Task.FromResult(new LogoutOutcome(true));
}

internal sealed class FixtureNavigation : INavigationService
{
    public void Initialize(ContentControl host, FrameworkElement loginView, FrameworkElement shellView) { }
    public void ShowLogin() { }
    public void ShowAuthenticatedShell() { }
}

internal sealed class FixtureApi : IReferenceMaterialsApiClient
{
    private readonly List<ReferenceMaterialDetail> _details = [];
    public int DelayMilliseconds { get; set; } = 40;
    public int SaveCalls { get; private set; }
    public decimal? NextAvailableQuantity { get; set; }
    internal ReferenceMaterialDetail SetAvailability(Guid id, decimal? quantity, string status)
    {
        var index = _details.FindIndex(detail => detail.Id == id);
        var updated = _details[index] with { AvailableQuantity = quantity, Status = status };
        _details[index] = updated;
        return updated;
    }
    public FixtureApi()
    {
        for (var index = 0; index < 30; index++)
        {
            _details.Add(new ReferenceMaterialDetail(Guid.NewGuid(),
                index == 0 ? "Naphthol AS" : index == 1 ? "4-Aminobiphenyl" : $"Estándar de validación {index + 1:00}",
                index == 1 ? "92-67-1" : "92-77-3", index == 1 ? "CAT-VALIDACIÓN-NOMBRE-LARGO-PARA-TRUNCAMIENTO" : $"DEMO-{index + 1:000}",
                1, "Método de prueba 01", 98.09m, $"DEMO-{index + 1:000}", "Proveedor ficticio de validación",
                new DateOnly(2026, 10, 3), new DateOnly(2029, 5, 6), 100m, 1, "mg", 1, 100m, 100m,
                "20 °C ± 4 °C", 1, "Laboratorio de prueba", index == 2 ? "Archived" : "Active", 1,
                new DateTimeOffset(2026, 10, 3, 13, 51, 0, TimeSpan.Zero), 1,
                new DateTimeOffset(2026, 10, 3, 13, 51, 0, TimeSpan.Zero), index == 2 ? 1 : null,
                index == 2 ? new DateTimeOffset(2026, 10, 3, 14, 0, 0, TimeSpan.Zero) : null,
                index == 2 ? "Fixture archivado para validar la presentación" : null, null, Guid.NewGuid(),
                "Usuario de prueba", "Usuario de prueba", index == 2 ? "Usuario de prueba" : null));
        }
    }

    private async Task<ApiCallResult<T>> ResultAsync<T>(T value, CancellationToken token)
    {
        await Task.Delay(DelayMilliseconds, token);
        return new(true, value, null, 200);
    }
    public Task<ApiCallResult<IReadOnlyList<ReferenceMethodOption>>> GetMethodsAsync(CancellationToken token) =>
        ResultAsync<IReadOnlyList<ReferenceMethodOption>>(Enumerable.Range(1, 23).Select(i => new ReferenceMethodOption(i, $"Método de prueba {i:00}")).ToArray(), token);
    public Task<ApiCallResult<IReadOnlyList<ReferenceUnitOption>>> GetUnitsAsync(CancellationToken token) =>
        ResultAsync<IReadOnlyList<ReferenceUnitOption>>([new(1, "Miligramo", "mg"), new(2, "Gramo", "g"), new(3, "Mililitro", "mL")], token);
    public Task<ApiCallResult<IReadOnlyList<ReferenceLocationOption>>> GetLocationsAsync(CancellationToken token) =>
        ResultAsync<IReadOnlyList<ReferenceLocationOption>>([new(1, "Laboratorio de prueba"), new(2, "Bodega de prueba")], token);
    public Task<ApiCallResult<ReferenceMaterialPage>> ListAsync(string? search, string? status, string? method, int page, int pageSize, CancellationToken token)
    {
        var filtered = _details.Where(d => (string.IsNullOrWhiteSpace(search) || d.Name.Contains(search, StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrWhiteSpace(status) || d.Status == status) && (string.IsNullOrWhiteSpace(method) || d.Method == method)).ToArray();
        return ResultAsync(new ReferenceMaterialPage(filtered.Skip((page - 1) * pageSize).Take(pageSize).Select(Summary).ToArray(), page, pageSize, filtered.Length), token);
    }
    internal static ReferenceMaterialSummary Summary(ReferenceMaterialDetail d) => new(d.Id, d.Name, d.CasNumber, d.CatalogNumber, d.Method, d.Lot, d.Brand, d.PurityPercent, d.ExpirationDate, d.Status, d.TotalQuantity, d.Unit, d.AvailableQuantity, d.Version);
    public Task<ApiCallResult<ReferenceMaterialDetail>> GetAsync(Guid id, CancellationToken token) => ResultAsync(_details.Single(d => d.Id == id), token);
    public async Task<ApiCallResult<ReferenceMaterialDetail>> CreateAsync(CreateReferenceMaterialRequest request, CancellationToken token)
    {
        SaveCalls++;
        var detail = _details[0] with { Id = Guid.NewGuid(), Name = request.Name, Lot = request.Lot, Status = "Active" };
        _details.Add(detail);
        await Task.Delay(700, token);
        return new(true, detail, null, 201);
    }
    public Task<ApiCallResult<ReferenceMaterialDetail>> UpdateAsync(Guid id, UpdateReferenceMaterialRequest request, CancellationToken token)
    {
        SaveCalls++;
        var index = _details.FindIndex(d => d.Id == id);
        var updated = _details[index] with { Name = request.Name, Lot = request.Lot, Brand = request.Brand,
            Version = Guid.NewGuid(), UpdatedAt = new DateTimeOffset(2026, 10, 6, 21, 53, 0, TimeSpan.Zero),
            AvailableQuantity = NextAvailableQuantity ?? _details[index].AvailableQuantity };
        NextAvailableQuantity = null;
        _details[index] = updated;
        return ResultAsync(updated, token);
    }
    public Task<ApiCallResult<ReferenceMaterialDetail>> ArchiveAsync(Guid id, ArchiveReferenceMaterialRequest request, CancellationToken token)
    {
        SaveCalls++;
        var index = _details.FindIndex(d => d.Id == id);
        var archived = _details[index] with { Status = "Archived", ArchiveReason = request.Reason, ArchivedByUserId = 1,
            ArchivedByName = "Usuario de prueba", ArchivedAt = new DateTimeOffset(2026, 10, 6, 22, 0, 0, TimeSpan.Zero), Version = Guid.NewGuid() };
        _details[index] = archived;
        return ResultAsync(archived, token);
    }
    public Task<ApiCallResult<ReferenceMaterialDetail>> ReplaceAsync(Guid id, ReplaceReferenceMaterialRequest request, CancellationToken token)
    {
        SaveCalls++;
        var index = _details.FindIndex(d => d.Id == id);
        var replacement = _details[index] with { Id = Guid.NewGuid(), Name = request.Replacement.Name,
            Lot = request.Replacement.Lot, Status = "Active", Version = Guid.NewGuid() };
        _details[index] = _details[index] with { Status = "Replaced", ReplacedByMaterialId = replacement.Id,
            ReplacedByMaterialName = replacement.Name, ArchiveReason = request.Reason, Version = Guid.NewGuid() };
        _details.Add(replacement);
        return ResultAsync(replacement, token);
    }
}

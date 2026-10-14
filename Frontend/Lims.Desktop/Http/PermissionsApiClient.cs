using System.Globalization;
using System.Net;
using Lims.Contracts.Authentication;
using Lims.Contracts.ReferenceMaterials;
using System.Net.Http.Json;
using System.Text.Json;
using Lims.Contracts.Errors;
using Lims.Contracts.Permissions;

namespace Lims.Desktop.Http;

internal sealed class PermissionsApiClient(IHttpClientFactory clients) : IPermissionsApiClient
{
    private const string Path = "api/administration/permissions";
    private static string UserPath(int user) => Path + "/users/" + user.ToString(CultureInfo.InvariantCulture);
    public Task<ApiCallResult<PermissionUsers>> UsersAsync(string? search, CancellationToken ct) => Send<PermissionUsers>(HttpMethod.Get, Path + "/users?search=" + Uri.EscapeDataString(search ?? string.Empty), null, ct);
    public Task<ApiCallResult<IReadOnlyList<PermissionDefinition>>> CatalogAsync(CancellationToken ct) => Send<IReadOnlyList<PermissionDefinition>>(HttpMethod.Get, Path + "/catalog", null, ct);
    public Task<ApiCallResult<UserPermissionState>> GetAsync(int user, CancellationToken ct) => Send<UserPermissionState>(HttpMethod.Get, UserPath(user), null, ct);
    public Task<ApiCallResult<UserPermissionState>> SaveAsync(int user, SavePermissionsRequest request, CancellationToken ct) => Send<UserPermissionState>(HttpMethod.Put, UserPath(user), JsonContent.Create(request), ct);
    public Task<ApiCallResult<PermissionHistoryPage>> HistoryAsync(int user, int page, CancellationToken ct) => Send<PermissionHistoryPage>(HttpMethod.Get, UserPath(user) + "/history?page=" + page.ToString(CultureInfo.InvariantCulture), null, ct);
    public async Task<ApiCallResult<EffectivePermissions>> EffectiveAsync(CancellationToken ct)
    {
        var current = await Send<EffectivePermissions>(HttpMethod.Get, "api/auth/permissions", null, ct).ConfigureAwait(false);
        if (current.StatusCode != (int)HttpStatusCode.NotFound) return current;

        // Older APIs expose the current server-side profile, but not individual permissions.
        var profile = await Send<UserProfile>(HttpMethod.Get, "api/auth/me", null, ct).ConfigureAwait(false);
        if (!profile.IsSuccess || profile.Value is null)
            return new(false, null, profile.Error, profile.StatusCode);
        var codes = profile.Value.Permissions.ToHashSet(StringComparer.Ordinal);
        if (codes.Contains(ReferenceMaterialPermissions.View)) { codes.Add(PermissionCatalog.StockView); codes.Add(PermissionCatalog.IntermediateView); }
        if (codes.Contains(ReferenceMaterialPermissions.Create)) { codes.Add(PermissionCatalog.StockCreate); codes.Add(PermissionCatalog.IntermediateCreate); }
        if (codes.Contains(ReferenceMaterialPermissions.Create) && codes.Contains(ReferenceMaterialPermissions.Archive)) codes.Add(PermissionCatalog.Replace);
        return new(true, new(profile.Value.Role, codes.ToArray(), false), null, profile.StatusCode);
    }
    private async Task<ApiCallResult<T>> Send<T>(HttpMethod method, string path, HttpContent? body, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(method, path) { Content = body };
            using var response = await clients.CreateClient(ApiClientNames.Authorized).SendAsync(request, ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode) return new(true, await response.Content.ReadFromJsonAsync<T>(ct).ConfigureAwait(false), null, (int)response.StatusCode);
            ApiError? error = null;
            try { error = await response.Content.ReadFromJsonAsync<ApiError>(ct).ConfigureAwait(false); }
            catch (JsonException) { /* Preserve HTTP status for empty or HTML error responses. */ }
            if (response.StatusCode == HttpStatusCode.NotFound)
                error = new(ErrorCodes.NotFound, "El servidor todavía no tiene habilitada la gestión de permisos. Es necesario actualizar el servicio antes de consultar o guardar asignaciones.", error?.CorrelationId ?? string.Empty);
            return new(false, default, error, (int)response.StatusCode);
        }
        catch (HttpRequestException) { return Failure<T>(ErrorCodes.NetworkUnavailable, "No se pudo conectar con el servidor. Sus cambios siguen pendientes."); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return Failure<T>(ErrorCodes.Timeout, "No se pudo confirmar el guardado. Recargue el usuario para comprobar su estado."); }
        catch (JsonException) { return Failure<T>(ErrorCodes.UnexpectedError, "No se pudo interpretar la respuesta del servidor."); }
    }
    private static ApiCallResult<T> Failure<T>(string code, string message) => new(false, default, new(code, message, string.Empty), 0);
}

using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Lims.Contracts.Alerts;
using Lims.Contracts.Errors;

namespace Lims.Desktop.Http;

internal sealed class AlertsApiClient(IHttpClientFactory clients) : IAlertsApiClient
{
    public Task<ApiCallResult<AlertJobResult>> EvaluateAsync(CancellationToken ct) => Send<AlertJobResult>(HttpMethod.Post, "/evaluate", null, ct);
    public Task<ApiCallResult<IReadOnlyList<AlertEvaluationItem>>> EvaluationsAsync(CancellationToken ct) => Send<IReadOnlyList<AlertEvaluationItem>>(HttpMethod.Get, "/evaluations", null, ct);
    public Task<ApiCallResult<AlertPage>> ListAsync(AlertFilter f, CancellationToken ct)
    {
        var args = new Dictionary<string, string?> { ["Search"] = f.Search, ["Category"] = f.Category, ["Priority"] = f.Priority?.ToString(), ["Module"] = f.Module?.ToString(), ["State"] = f.State, ["From"] = f.From?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ["Until"] = f.Until?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ["Sort"] = f.Sort, ["Page"] = f.Page.ToString(CultureInfo.InvariantCulture), ["PageSize"] = f.PageSize.ToString(CultureInfo.InvariantCulture) };
        return Send<AlertPage>(HttpMethod.Get, "?" + string.Join('&', args.Where(p => !string.IsNullOrEmpty(p.Value)).Select(p => p.Key + "=" + Uri.EscapeDataString(p.Value!))), null, ct);
    }
    public Task<ApiCallResult<AlertDetail>> DetailAsync(Guid id, CancellationToken ct) => Send<AlertDetail>(HttpMethod.Get, $"/{id:D}", null, ct);
    public Task<ApiCallResult<AlertDetail>> AcknowledgeAsync(Guid id, CancellationToken ct) => Send<AlertDetail>(HttpMethod.Post, $"/{id:D}/acknowledge", new AlertAction(Guid.Empty, null), ct);
    public Task<ApiCallResult<NotificationPage>> NotificationsAsync(CancellationToken ct) => Send<NotificationPage>(HttpMethod.Get, "/notifications", null, ct);
    public Task<ApiCallResult<bool>> ReadAsync(Guid? id, CancellationToken ct) => Send<bool>(HttpMethod.Post, id is { } i ? $"/notifications/{i:D}/read" : "/notifications/read-all", null, ct);
    public Task<ApiCallResult<bool>> ClaimWindowsAsync(Guid id, CancellationToken ct) => Send<bool>(HttpMethod.Post, $"/notifications/{id:D}/claim-windows", null, ct);
    public Task<ApiCallResult<NotificationPreferences>> PreferencesAsync(NotificationPreferences p, CancellationToken ct) => Send<NotificationPreferences>(HttpMethod.Put, "/preferences", p, ct);
    public Task<ApiCallResult<AlertAdministration>> AdministrationAsync(CancellationToken ct) => Send<AlertAdministration>(HttpMethod.Get, "/administration", null, ct);
    public Task<ApiCallResult<AlertAdministration>> ConfigureAsync(AlertSettings s, CancellationToken ct) => Send<AlertAdministration>(HttpMethod.Put, "/administration", s, ct);
    public Task<ApiCallResult<DeliveryPage>> DeliveriesAsync(int page, CancellationToken ct) => Send<DeliveryPage>(HttpMethod.Get, $"/deliveries?page={page}", null, ct);
    public Task<ApiCallResult<DeliveryItem>> TestEmailAsync(Guid id, CancellationToken ct) => Send<DeliveryItem>(HttpMethod.Post, "/test-email", new MailTestRequest(id), ct);
    private async Task<ApiCallResult<T>> Send<T>(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(method, "api/alerts" + path);
            if (body is not null) request.Content = JsonContent.Create(body, body.GetType());
            using var response = await clients.CreateClient(ApiClientNames.Authorized).SendAsync(request, ct).ConfigureAwait(false);
            var status = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode)
            {
                ApiError? error = null;
                try { error = await response.Content.ReadFromJsonAsync<ApiError>(ct).ConfigureAwait(false); }
                catch (JsonException) { /* IIS may return an empty/HTML error. Preserve the real HTTP status. */ }
                return new(false, default, string.IsNullOrWhiteSpace(error?.Message) ? AlertApiFailures.ForStatus(status) : error, status);
            }
            try
            {
                var value = await response.Content.ReadFromJsonAsync<T>(ct).ConfigureAwait(false);
                return value is null || !ValidPayload(value) ? new(false, default, AlertApiFailures.InvalidContract, status) : new(true, value, null, status);
            }
            catch (JsonException) { return new(false, default, AlertApiFailures.InvalidContract, status); }
        }
        catch (HttpRequestException) { return new(false, default, new("connection_error", "No se pudo conectar con el servidor. Reintente.", ""), 0); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return new(false, default, new("timeout", "El servidor tardó demasiado en responder. Reintente.", ""), 0); }
    }
    private static bool ValidPayload<T>(T value) => value switch
    {
        AlertPage p => p.Items is not null && p.Summary is not null && p.Summary.ServerTime != default && p.Page > 0 && p.PageSize > 0 && p.Total >= 0,
        AlertDetail d => d.Alert is not null && d.History is not null,
        NotificationPage n => n.Items is not null && n.Preferences is not null && n.ServerTime != default && n.Unread >= 0,
        AlertAdministration a => a.Settings is not null && a.Departments is not null && a.Users is not null && a.Mail is not null,
        DeliveryPage d => d.Items is not null && d.Page > 0 && d.Total >= 0,
        _ => true
    };
}

public static class AlertApiFailures
{
    public static ApiError InvalidContract => new("alerts.invalid_contract", "La API respondió, pero el contenido no coincide con el contrato de Alertas. Revise que cliente y backend correspondan a la misma versión.", "");
    public static ApiError ForStatus(int status) => status switch
    {
        401 => new("alerts.unauthorized", "La sesión no es válida o expiró. Inicie sesión nuevamente para consultar las alertas.", ""),
        403 => new("alerts.forbidden", "El servidor rechazó la consulta: su cuenta no tiene permiso para acceder a Alertas.", ""),
        404 => new("alerts.not_deployed", "La ruta de Alertas no está disponible en el servidor configurado (HTTP 404). Compruebe la URL y publique la versión del backend que incluye este módulo.", ""),
        >= 500 => new("alerts.server_error", $"El servidor de Alertas devolvió HTTP {status}. Reintente; si continúa, el administrador debe revisar el servicio y sus dependencias.", ""),
        _ => new("alerts.http_error", $"No se pudo completar la consulta de Alertas (HTTP {status}).", "")
    };
}

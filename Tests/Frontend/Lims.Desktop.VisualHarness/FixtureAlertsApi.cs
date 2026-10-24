using Lims.Contracts.Alerts;
using Lims.Contracts.Errors;
using Lims.Contracts.ReferenceMaterials;
using Lims.Desktop.Http;

namespace Lims.Desktop.VisualHarness;

// Visual fixtures only. No production API, SMTP transport or inventory writes.
internal sealed class FixtureAlertsApi : IAlertsApiClient
{
        public Task<ApiCallResult<AlertJobResult>> EvaluateAsync(CancellationToken ct) => Task.FromResult(new ApiCallResult<AlertJobResult>(true, new(true, 12, 0, 0), null, 200));
        public Task<ApiCallResult<IReadOnlyList<AlertEvaluationItem>>> EvaluationsAsync(CancellationToken ct) => Task.FromResult(new ApiCallResult<IReadOnlyList<AlertEvaluationItem>>(true, [], null, 200));
    private readonly List<AlertItem> _items = [];
    private readonly HashSet<Guid> _read = [];
    private readonly Dictionary<Guid, Guid> _notifications = [];
    private NotificationPreferences _preferences = new();
    private AlertSettings _settings = AlertSettings.Default with { Version = Guid.NewGuid(), Scopes = [new(AlertModule.Standards, 7)] };
    public bool Fail { get; set; }
    public bool Empty { get; set; }
    public bool EnginePending { get; set; }
    public int FailureStatus { get; set; }
    public int DelayMs { get; set; }
    public int ListCalls { get; private set; }
    public int AcknowledgeCalls { get; private set; }
    public int ReadCalls { get; private set; }
    public FixtureAlertsApi(IReadOnlyList<ReferenceMaterialDetail> materials)
    {
        var now = DateTimeOffset.UtcNow; var today = DateOnly.FromDateTime(now.ToOffset(TimeSpan.FromHours(-6)).DateTime);
        for (var i = 0; i < 12; i++)
        {
            var m = materials[i % materials.Count]; var category = i % 2 == 0 ? "Availability" : "Expiration";
            var condition = category == "Availability" ? i % 4 == 0 ? "Critical" : "Low" : i % 3 == 0 ? "Expired" : "Due7";
            var title = condition switch { "Critical" => "Disponibilidad crítica", "Low" => "Disponibilidad baja", "Expired" => "Material vencido", _ => "Próximo vencimiento" };
            var priority = condition == "Critical" ? AlertPriority.Critical : condition == "Expired" ? AlertPriority.High : AlertPriority.Warning;
            var item = new AlertItem(Guid.NewGuid(), m.Id, "ReferenceMaterial", AlertModule.Standards, 7, m.Name, m.CatalogNumber ?? "TEST", m.Lot,
                "Estándar sólido", category, condition, priority, title, category == "Availability" ? $"Lote {m.Lot}: queda {(condition == "Critical" ? 8 : 20)} % del material." : $"Lote {m.Lot}: revisar vencimiento documentado.",
                "Revisar la condición y gestionar la reposición según el procedimiento autorizado.", condition == "Critical" ? 8 : 20, condition == "Critical" ? 8 : 20, "mg", today.AddDays(condition == "Expired" ? -1 : 7), now.AddMinutes(-i * 12), now, null, null, 1);
            _items.Add(item); _notifications.Add(Guid.NewGuid(), item.Id);
        }
    }
    private Task<ApiCallResult<T>> Result<T>(T value) => Task.FromResult(Fail || FailureStatus > 0 ? new ApiCallResult<T>(false, default, new(ErrorCodes.NetworkUnavailable, FailureStatus == 404 ? "La ruta de Alertas no está disponible en el servidor configurado (HTTP 404). Compruebe la URL y publique la versión del backend que incluye este módulo." : "No se pudo sincronizar con el servidor de alertas. Reintente.", "visual-fixture"), FailureStatus > 0 ? FailureStatus : 503) : new(true, value, null, 200));
    private AlertItem Personal(AlertItem a) { var id = _notifications.Single(n => n.Value == a.Id).Key; return a with { NotificationId = id, ReadAt = _read.Contains(id) ? DateTimeOffset.UtcNow : null }; }
    public async Task<ApiCallResult<AlertPage>> ListAsync(AlertFilter f, CancellationToken ct)
    {
        ListCalls++; if (DelayMs > 0) await Task.Delay(DelayMs, ct);
        var active = Empty || EnginePending ? [] : _items.ToArray(); IEnumerable<AlertItem> q = active;
        if (f.Search is { Length: > 0 } term) q = q.Where(a => (a.Material + a.Code + a.Lot).Contains(term, StringComparison.OrdinalIgnoreCase));
        if (f.Priority is { } p) q = q.Where(a => a.Priority == p);
        if (f.Module is { } m) q = q.Where(a => a.Modules.HasFlag(m));
        if (f.Category is { Length: > 0 }) q = q.Where(a => f.Category == "Expired" ? a.Condition == "Expired" : f.Category == "Upcoming" ? a.Category == "Expiration" && a.Condition != "Expired" : a.Category == f.Category);
        if (f.State == "Resolved") q = q.Where(a => a.ResolvedAt != null);
        if (f.State == "Pending") q = q.Where(a => a.AcknowledgedAt == null);
        if (f.From is { } from) q = q.Where(a => DateOnly.FromDateTime(a.DetectedAt.DateTime) >= from);
        if (f.Until is { } until) q = q.Where(a => DateOnly.FromDateTime(a.DetectedAt.DateTime) <= until);
        q = q.OrderByDescending(a => a.Priority).ThenBy(a => a.Material);
        if (f.Sort == "Newest") q = q.OrderByDescending(a => a.DetectedAt); else if (f.Sort == "Expiration") q = q.OrderBy(a => a.Expiration);
        var nearest = active.Where(a => a.Category == "Expiration" && a.Condition != "Expired").OrderBy(a => a.Expiration).FirstOrDefault();
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-6)).DateTime);
        var metrics = new AlertMetrics(active.Count(a => a.Condition == "Depleted"), active.Count(a => a.Condition == "Critical"), active.Count(a => a.Condition == "Expired" && a.AcknowledgedAt == null), nearest?.Material, nearest?.Id, nearest is null ? null : nearest.Expiration.DayNumber - today.DayNumber);
        var summary = new AlertSummary(active.Length, active.Count(a => a.Priority == AlertPriority.Critical), active.Count(a => a.Category == "Expiration" && a.Condition != "Expired"), nearest?.Expiration, active.Count(a => a.Condition == "Expired"), active.Count(a => a.AcknowledgedAt == null), Empty || EnginePending ? 0 : _notifications.Count - _read.Count, EnginePending ? null : DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, metrics);
        return await Result(new AlertPage(q.Skip((f.Page - 1) * f.PageSize).Take(f.PageSize).Select(Personal).ToArray(), f.Page, f.PageSize, q.Count(), summary));
    }
    public Task<ApiCallResult<AlertDetail>> DetailAsync(Guid id, CancellationToken ct)
    {
        var a = _items.Single(a => a.Id == id);
        return Result(new AlertDetail(Personal(a), [new(Guid.NewGuid(), "Detectada", a.Condition, a.DetectedAt, null, "Datos ficticios para validación visual aislada.")]));
    }
    public Task<ApiCallResult<AlertDetail>> AcknowledgeAsync(Guid id, CancellationToken ct) { AcknowledgeCalls++; var i = _items.FindIndex(a => a.Id == id); _items[i] = _items[i] with { AcknowledgedAt = DateTimeOffset.UtcNow }; return DetailAsync(id, ct); }
    public Task<ApiCallResult<NotificationPage>> NotificationsAsync(CancellationToken ct) => Result(new NotificationPage(Empty ? [] : _notifications.Select(n => { var a = _items.Single(a => a.Id == n.Value); return new NotificationItem(n.Key, a.Id, a.Title + " — " + a.Material, a.Description, a.Priority, a.DetectedAt, _read.Contains(n.Key) ? DateTimeOffset.UtcNow : null, false); }).ToArray(), Empty ? 0 : _notifications.Count - _read.Count, _preferences, true, DateTimeOffset.UtcNow));
    public Task<ApiCallResult<bool>> ReadAsync(Guid? id, CancellationToken ct) { ReadCalls++; foreach (var n in _notifications.Keys.Where(n => id is null || n == id)) _read.Add(n); return Result(true); }
    public Task<ApiCallResult<bool>> ClaimWindowsAsync(Guid id, CancellationToken ct) => Result(false);
    public Task<ApiCallResult<NotificationPreferences>> PreferencesAsync(NotificationPreferences p, CancellationToken ct) { _preferences = p; return Result(p); }
    public Task<ApiCallResult<AlertAdministration>> AdministrationAsync(CancellationToken ct) => Result(new AlertAdministration(_settings, [new(7, "Laboratorio Analítico · PRUEBA")], [new(1, "Usuario de prueba", "fixture@example.invalid", 7, true)], new(false, false, "", 587, "", "Password", "StartTls", "SMTP pendiente de configuración · no se enviaron correos")));
    public async Task<ApiCallResult<AlertAdministration>> ConfigureAsync(AlertSettings s, CancellationToken ct) { _settings = s with { Version = Guid.NewGuid() }; return await AdministrationAsync(ct); }
    public Task<ApiCallResult<DeliveryPage>> DeliveriesAsync(int page, CancellationToken ct) => Result(new DeliveryPage([], page, 0, "NotConfigured"));
    public Task<ApiCallResult<DeliveryItem>> TestEmailAsync(Guid id, CancellationToken ct) => throw new InvalidOperationException("No hay SMTP real en la validación visual.");
}

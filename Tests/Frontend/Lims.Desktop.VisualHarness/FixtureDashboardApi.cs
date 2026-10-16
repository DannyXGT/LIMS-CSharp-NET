using Lims.Contracts.Dashboard;
using Lims.Contracts.Errors;
using Lims.Contracts.Permissions;
using Lims.Desktop.Http;
namespace Lims.Desktop.VisualHarness;
internal sealed class FixtureDashboardApi : IDashboardApiClient
{
    public string Scenario { get; set; } = "Administrator";
    public IReadOnlyList<Lims.Contracts.ReferenceMaterials.ReferenceMaterialDetail>? ReferenceRows { get; set; }
    public async Task<ApiCallResult<DashboardResponse>> GetAsync(CancellationToken ct)
    {
        await Task.Delay(240, ct);
        if (Scenario == "Error") return new(false, null, new ApiError("test", "Error de API simulado en validación aislada.", "fixture"), 503);
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-6)).DateTime);
        var empty = Scenario == "Empty"; var restricted = Scenario == "Restricted"; var purchasing = Scenario == "Purchasing"; var limited = Scenario == "Limited";
        var standard = empty || restricted || purchasing || limited ? Array.Empty<DashboardMaterial>() : Enumerable.Range(0,6).Select(i => new DashboardMaterial(Guid.Parse($"00000000-0000-0000-0000-{i+1:000000000000}"), "Standard", $"Estándar visual aislado {i+1}", $"LOTE-PRUEBA-{i+1}", "Método de prueba", today.AddDays(i*7), i*7, new decimal[] { 100, 20, 60, 0, 75, 50 }[i])).ToArray();
        if (!empty && !restricted && !purchasing && !limited && ReferenceRows is {} references)
            standard = references.Take(6).Select(r => new DashboardMaterial(r.Id, "Standard", r.Name, r.Lot, r.Method, r.ExpirationDate, r.ExpirationDate.DayNumber-today.DayNumber, r.TotalQuantity > 0 && r.AvailableQuantity.HasValue ? r.AvailableQuantity/r.TotalQuantity*100 : null)).ToArray();
        var stock = Array.Empty<DashboardMaterial>();
        var showStandards = !limited && !restricted && !purchasing;
        var counts = empty ? 0 : ReferenceRows?.Count ?? 18;
        var view = Scenario is "Empty" or "Error" or "Healthy" ? "Administrator" : limited ? "Laboratory" : Scenario;
        var permissions = limited ? new[] { PermissionCatalog.StockView } : restricted || purchasing ? Array.Empty<string>() : PermissionCatalog.All.Select(p => p.Code).ToArray();
        var standards = showStandards ? new DashboardStandards(counts, empty ? 0 : 9, empty ? 0 : 2, empty ? 0 : 4, null, empty ? 0 : 3, empty ? [] : [new("Active",9),new("Expired",3),new("Depleted",2),new("Archived",3),new("Replaced",1)]) : null;
        if (showStandards && !empty && ReferenceRows is {} all)
        {
            var distribution = all.GroupBy(r=>r.Status).Select(g=>new DashboardState(g.Key,g.Count())).ToArray();
            var active=all.Where(r=>r.Status=="Active" && r.AvailableQuantity>0 && r.ExpirationDate>=today).ToArray();
            standards=new(counts, active.Length, active.Count(r=>r.AvailableQuantity/r.TotalQuantity<.25m), active.Count(r=>r.AvailableQuantity/r.TotalQuantity<=.60m), null, all.Count(r=>r.Status=="Expired"), distribution);
        }
        if (Scenario == "Healthy")
        {
            standard = standard.Take(3).Select((r, i) => r with { AvailabilityPercent = 100, ExpirationDate = today.AddDays(252+i*200), DaysRemaining = 252+i*200 }).ToArray();
            standards = new(3, 3, 0, 0, null, 0, [new("Active", 3)]);
        }
        var prep = restricted || purchasing ? null : new DashboardPreparations(0,0,0,null,0);
        DashboardMethodStatistics? methodStatistics = null;
        if (showStandards)
        {
            var catalogue = empty ? [] : ReferenceRows ?? [];
            var methods = Scenario == "Healthy"
                ? standard.GroupBy(r => r.Method).Select(g => new DashboardMethodSummary(g.Key, g.Count(), g.Count(), 100)).ToArray()
                : catalogue.GroupBy(r => string.IsNullOrWhiteSpace(r.Method) ? "Sin método" : r.Method).Select(g =>
                {
                    var valid = g.Where(r => r.Status == "Active" && r.ExpirationDate >= today && r.TotalQuantity > 0 &&
                        r.AvailableQuantity > 0 && r.AvailableQuantity <= r.TotalQuantity).ToArray();
                    return new DashboardMethodSummary(g.Key, g.Count(), valid.Length,
                        valid.Length == 0 ? null : valid.Average(r => r.AvailableQuantity!.Value / r.TotalQuantity * 100));
                }).ToArray();
            methodStatistics = new("Standards", standards!.Registered, methods.Sum(m => m.AvailabilityCount), methods);
        }
        else if (limited) methodStatistics = new("Stock", 0, 0, []);
        var rows = standard.Concat(stock).Where(r => r.DaysRemaining >= 0).ToArray();
        return new(true, new(view, "Usuario de prueba visual", view == "Administrator" ? "Administrador" : view == "Purchasing" ? "Comprador" : "Analista", view == "Purchasing" ? "Compras (fixture)" : "Laboratorio Analítico (fixture)", DateTimeOffset.UtcNow, null, permissions, standards, prep, limited ? null : prep, rows, rows.Where(r => r.AvailabilityPercent is > 0 and <= 60).ToArray(), rows.Take(4).Select(r => new DashboardActivity("Registro de prueba visual", r.Name, "Analista ficticio", DateTimeOffset.UtcNow.AddHours(-2), r.Kind, r.Id)).ToArray(), methodStatistics), null, 200);
    }
}

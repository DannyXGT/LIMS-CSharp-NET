using Lims.Contracts.Dashboard;
namespace Lims.Application.Dashboard;
public sealed class DashboardOptions
{
    // Null explicitly means the business window has not been configured.
    public int? ExpirationWindowDays { get; set; }
    // Exact names must match the existing identity catalog; no role or department is created.
    public string? PurchasingDepartmentName { get; set; }
    public string? PurchasingRoleName { get; set; }
}
public sealed record DashboardAccess(string View, bool Standards, bool Stock, bool Intermediate);
public static class DashboardSelection
{
    public static DashboardAccess Resolve(string role, string? department, IReadOnlyList<string> permissions, DashboardOptions options)
    {
        var admin = role == "Administrador";
        bool Has(string p) => admin || permissions.Contains(p, StringComparer.Ordinal);
        var lab = department is "Laboratorio Analítico" or "Laboratorio Analitico";
        var purchasing = (!string.IsNullOrWhiteSpace(options.PurchasingDepartmentName) && department == options.PurchasingDepartmentName) ||
            (!string.IsNullOrWhiteSpace(options.PurchasingRoleName) && role == options.PurchasingRoleName);
        // The current model has ONE role and ONE department. Explicit priority: admin, lab, purchasing, authorized operations, restricted.
        var operational = Has("reference_materials.view") || Has("stock.view") || Has("intermedia.view");
        var view = admin ? "Administrator" : lab ? "Laboratory" : purchasing ? "Purchasing" : operational ? "Laboratory" : "Restricted";
        return new(view, view != "Purchasing" && Has("reference_materials.view"), view != "Purchasing" && Has("stock.view"), view != "Purchasing" && Has("intermedia.view"));
    }
}
public interface IDashboardRepository
{
    Task<DashboardResponse?> GetAsync(int userId, CancellationToken ct);
}

namespace Lims.Contracts.Permissions;

public sealed record PermissionDefinition(string Code, string Module, string Name);
public static class PermissionCatalog
{
    public const string DepartmentName = "Laboratorio Analítico";
    public const string Replace = "reference_materials.replace";
    public const string StockView = "stock.view";
    public const string StockCreate = "stock.create";
    public const string IntermediateView = "intermedia.view";
    public const string IntermediateCreate = "intermedia.create";
    public static IReadOnlyList<PermissionDefinition> All { get; } = Array.AsReadOnly<PermissionDefinition>([
        new("reference_materials.view", "Estándares", "Consultar estándares"),
        new("reference_materials.create", "Estándares", "Registrar estándares"),
        new("reference_materials.edit", "Estándares", "Editar estándares"),
        new("reference_materials.archive", "Estándares", "Archivar estándares"),
        new(Replace, "Estándares", "Reemplazar estándares"),
        new(StockView, "Stock", "Consultar Stock"), new(StockCreate, "Stock", "Registrar Stock"),
        new(IntermediateView, "Intermedia", "Consultar Intermedia"), new(IntermediateCreate, "Intermedia", "Registrar Intermedia")]);
}
public sealed record PermissionUser(int Id, string Name, string Email, string Role, string Department)
{
    // WinUI's default item automation peer uses ToString for the accessible row name.
    public override string ToString() => Name;
}
public sealed record PermissionUsers(IReadOnlyList<PermissionUser> Items, bool DepartmentExists);
public sealed record UserPermissionState(PermissionUser User, IReadOnlyList<string> Permissions, Guid Version, bool IsConfigured);
public sealed record SavePermissionsRequest(IReadOnlyList<string> Permissions, Guid Version);
public sealed record PermissionHistory(string Permission, string User, string Actor, string Action, bool Before, bool After, DateTimeOffset At);
public sealed record PermissionHistoryPage(IReadOnlyList<PermissionHistory> Items, int Page, int Total);
public sealed record EffectivePermissions(string Role, IReadOnlyList<string> Permissions, bool IndividualEnforcement);

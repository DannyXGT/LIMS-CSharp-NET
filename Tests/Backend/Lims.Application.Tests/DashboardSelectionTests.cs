using Lims.Application.Dashboard;
using Lims.Contracts.Permissions;
namespace Lims.Application.Tests;
public sealed class DashboardSelectionTests
{
    [Theory]
    [InlineData("Administrador", "Compras", "Administrator")]
    [InlineData("Analista", "Laboratorio Analítico", "Laboratory")]
    [InlineData("Comprador", "Compras", "Purchasing")]
    [InlineData("Compras", "Laboratorio Analitico", "Laboratory")]
    [InlineData("Usuario", null, "Restricted")]
    public void PriorityUsesExistingSingleRoleAndDepartment(string role, string? department, string expected)
    {
        var result = DashboardSelection.Resolve(role, department, [], new() { PurchasingDepartmentName = "Compras", PurchasingRoleName = "Compras" });
        Assert.Equal(expected, result.View);
        if (expected is "Restricted" or "Purchasing") Assert.False(result.Standards || result.Stock || result.Intermediate);
    }
    [Fact] public void LimitedPermissionsDoNotUnlockRelatedModules()
    {
        var result = DashboardSelection.Resolve("Analista", "Laboratorio Analítico", [PermissionCatalog.StockView], new());
        Assert.True(result.Stock); Assert.False(result.Standards); Assert.False(result.Intermediate);
    }
    [Fact] public void UnknownPurchasingNamesAreNotAssumed()
    {
        Assert.Equal("Restricted", DashboardSelection.Resolve("Comprador", "Compras", [], new()).View);
    }
}

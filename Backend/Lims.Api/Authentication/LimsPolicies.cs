namespace Lims.Api.Authentication;

public static class LimsPolicies
{
    public const string RequireAdministrator = "RequireAdministrator";
    public const string UsersManage = "Permission:users.manage";
    public const string ChemicalDepartment = "Department:Laboratorio Químico";
}

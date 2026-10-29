using System.Data;
using Dapper;

namespace AidlyPms.Api.Common.Security;

public static class RoleAssignmentPolicy
{
    public static async Task<bool> CanAssignAsync(IDbConnection connection, IDbTransaction? transaction,
        RequestAccess? caller, long roleNo, long pharmacyNo, long branchNo)
    {
        if (caller == null) return false;
        var target = await connection.QuerySingleOrDefaultAsync<TargetRole>(@"
            SELECT r.pharmacy_no, r.branch_no, r.status, c.access_scope FROM sys_roles r
            LEFT JOIN sys_role_capabilities c ON c.role_no = r.role_no WHERE r.role_no = @RoleNo;",
            new { RoleNo = roleNo }, transaction);
        if (target == null || !target.Status) return false;
        if (target.AccessScope == "PLATFORM_OWNER") return caller.IsPlatformOwner;
        if (target.PharmacyNo != pharmacyNo || (target.BranchNo.HasValue && target.BranchNo != branchNo)) return false;
        if (caller.IsFullAccess) return true;
        if (target.AccessScope != null) return false;
        var permissions = await connection.QueryAsync<string>(@"
            SELECT p.code FROM sys_role_permissions rp JOIN sys_permissions p ON p.permission_no = rp.permission_no
            WHERE rp.pharmacy_no = @PharmacyNo AND rp.role_no = @RoleNo;", new { PharmacyNo = pharmacyNo, RoleNo = roleNo }, transaction);
        if (permissions.Any(p => !caller.Permissions.Contains(p))) return false;
        var grants = await connection.QueryAsync<MenuGrant>(@"
            SELECT m.menu_code, rmp.can_view, rmp.can_create, rmp.can_edit, rmp.can_delete
            FROM sys_role_menu_permissions rmp JOIN sys_menus m ON m.menu_no = rmp.menu_no
            WHERE rmp.pharmacy_no = @PharmacyNo AND rmp.role_no = @RoleNo;", new { PharmacyNo = pharmacyNo, RoleNo = roleNo }, transaction);
        return grants.All(g => (!g.CanView || caller.HasMenu([g.MenuCode], MenuOperation.View)) &&
            (!g.CanCreate || caller.HasMenu([g.MenuCode], MenuOperation.Create)) &&
            (!g.CanEdit || caller.HasMenu([g.MenuCode], MenuOperation.Edit)) &&
            (!g.CanDelete || caller.HasMenu([g.MenuCode], MenuOperation.Delete)));
    }
    private sealed class TargetRole
    {
        public long PharmacyNo { get; set; }
        public long? BranchNo { get; set; }
        public bool Status { get; set; }
        public string? AccessScope { get; set; }
    }
}

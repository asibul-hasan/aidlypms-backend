using AidlyPms.Api.Common.Database;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AidlyPms.Api.Common;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public class RequirePermissionAttribute : Attribute, IAsyncActionFilter
{
    private readonly string _permissionCode;

    public RequirePermissionAttribute(string permissionCode)
    {
        _permissionCode = permissionCode;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var tenant = context.HttpContext.RequestServices.GetRequiredService<ITenantContext>();
        var db = context.HttpContext.RequestServices.GetRequiredService<IDbConnectionFactory>();

        var pharmacyNo = tenant.PharmacyNo;
        var branchNo = tenant.BranchNo;
        var roleNo = tenant.RoleNo;
        var userNo = tenant.UserNo;

        using var conn = db.CreateConnection();

        if (roleNo <= 0 && userNo > 0)
        {
            roleNo = await conn.ExecuteScalarAsync<long>(@"
                SELECT role_no FROM sys_users
                WHERE user_no = @UserNo AND pharmacy_no = @PharmacyNo AND branch_no = @BranchNo;",
                new { UserNo = userNo, PharmacyNo = pharmacyNo, BranchNo = branchNo });
        }

        // Fast-path: System Manager (Role 9 or alias 'PMS Manager') has all permissions
        if (roleNo == 9)
        {
            await next();
            return;
        }

        if (roleNo > 0)
        {
            var isManager = await conn.ExecuteScalarAsync<bool>(@"
                SELECT EXISTS(
                    SELECT 1 FROM sys_roles
                    WHERE role_no = @RoleNo AND alias = 'PMS Manager'
                );",
                new { RoleNo = roleNo });

            if (isManager)
            {
                await next();
                return;
            }
        }

        // Check if role has the specific permission in sys_role_permissions
        var hasPermission = await conn.ExecuteScalarAsync<bool>(@"
            SELECT EXISTS(
                SELECT 1
                FROM sys_role_permissions rp
                JOIN sys_permissions p ON rp.permission_no = p.permission_no
                WHERE rp.role_no = @RoleNo
                  AND rp.pharmacy_no = @PharmacyNo
                  AND p.code = @PermissionCode
            );",
            new { RoleNo = roleNo, PharmacyNo = pharmacyNo, PermissionCode = _permissionCode });

        if (!hasPermission)
        {
            context.Result = new ObjectResult(ApiResponse<object>.Fail(
                "Forbidden",
                $"Access denied: User does not have required permission '{_permissionCode}'."))
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
            return;
        }

        await next();
    }
}

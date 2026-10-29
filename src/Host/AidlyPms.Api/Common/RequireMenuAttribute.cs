using AidlyPms.Api.Common.Database;
using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AidlyPms.Api.Common;

public enum MenuOperation
{
    Auto = 0,
    View = 1,
    Create = 2,
    Edit = 3,
    Delete = 4
}

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public class RequireMenuAttribute : Attribute, IAsyncActionFilter
{
    private readonly string[] _menuCodes;
    public IReadOnlyList<string> MenuCodes => _menuCodes;
    public MenuOperation Operation { get; set; } = MenuOperation.Auto;

    public RequireMenuAttribute(params string[] menuCodes)
    {
        _menuCodes = menuCodes ?? Array.Empty<string>();
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        // Allow anonymous or authenticated-only endpoints to bypass menu checks
        if (context.ActionDescriptor.EndpointMetadata.Any(em => em is Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute || em is AuthenticatedOnlyAttribute))
        {
            await next();
            return;
        }

        var isPlatformOwner = string.Equals(context.HttpContext.User?.FindFirst("platform_owner")?.Value, "true", StringComparison.OrdinalIgnoreCase);
        var isAdminClaim = string.Equals(context.HttpContext.User?.FindFirst("is_admin")?.Value, "true", StringComparison.OrdinalIgnoreCase);
        var roleClaim = context.HttpContext.User?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        var isAdmin = isPlatformOwner || isAdminClaim || string.Equals(roleClaim, "Admin", StringComparison.OrdinalIgnoreCase);

        if (isAdmin)
        {
            await next();
            return;
        }

        // Developer/Platform-only menus are strictly reserved for Admin (MENU_1904, MENU_1905)
        var isDeveloperMenu = _menuCodes.Any(m => m is "MENU_1904" or "MENU_1905");
        if (isDeveloperMenu)
        {
            context.Result = new ObjectResult(ApiResponse<object>.Fail(
                "Forbidden",
                $"Access denied: Menu [{string.Join(", ", _menuCodes)}] is reserved for System Admin."))
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
            return;
        }

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

        if (roleNo == 1)
        {
            await next();
            return;
        }

        // Fast-path: Store Manager (alias 'PMS Manager') has store-level access (excluding developer menus)
        if (roleNo > 0)
        {
            var isManager = await conn.ExecuteScalarAsync<bool>(@"
                SELECT EXISTS(
                    SELECT 1 FROM sys_roles
                    WHERE role_no = @RoleNo AND pharmacy_no = @PharmacyNo AND (alias = 'PMS Manager' OR role_name = 'PMS Manager')
                );",
                new { RoleNo = roleNo, PharmacyNo = pharmacyNo });

            if (isManager)
            {
                await next();
                return;
            }
        }

        // Determine operation
        var op = Operation;
        if (op == MenuOperation.Auto)
        {
            var method = context.HttpContext.Request.Method.ToUpperInvariant();
            op = method switch
            {
                "GET" or "HEAD" => MenuOperation.View,
                "POST" => MenuOperation.Create,
                "PUT" or "PATCH" => MenuOperation.Edit,
                "DELETE" => MenuOperation.Delete,
                _ => MenuOperation.View
            };
        }

        // Check sys_role_menu_permissions joined with sys_menus
        // Any-of list allowed for shared read-only lookups
        var hasAccess = await conn.ExecuteScalarAsync<bool>(@"
            SELECT EXISTS(
                SELECT 1
                FROM sys_role_menu_permissions rmp
                JOIN sys_menus m ON rmp.menu_no = m.menu_no
                WHERE rmp.role_no = @RoleNo
                  AND rmp.pharmacy_no = @PharmacyNo
                  AND m.menu_code = ANY(@MenuCodes)
                  AND (
                      (@Op = 1 AND rmp.can_view = TRUE) OR
                      (@Op = 2 AND rmp.can_create = TRUE) OR
                      (@Op = 3 AND rmp.can_edit = TRUE) OR
                      (@Op = 4 AND rmp.can_delete = TRUE)
                  )
            );",
            new { RoleNo = roleNo, PharmacyNo = pharmacyNo, MenuCodes = _menuCodes, Op = (int)op });

        if (!hasAccess)
        {
            context.Result = new ObjectResult(ApiResponse<object>.Fail(
                "Forbidden",
                $"Access denied: Role does not have required {op} permission for menu(s) [{string.Join(", ", _menuCodes)}]."))
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
            return;
        }

        await next();
    }
}

using AidlyPms.Api.Common;
using AidlyPms.Api.Common.Database;
using AidlyPms.Api.Modules.Roles.Models;
using Dapper;
using Microsoft.AspNetCore.Mvc;

namespace AidlyPms.Api.Modules.Roles.Controllers;

[Route("api/v1/role")]
[RequireMenu("ROLE_1901", "ROLE_1903")]
public class RoleController : BaseController
{
    private readonly IDbConnectionFactory _db;

    public RoleController(IDbConnectionFactory db)
    {
        _db = db;
    }

    [HttpGet("permissions")]
    public async Task<ActionResult<ApiResponse<List<SysPermission>>>> GetPermissions()
    {
        using var conn = _db.CreateConnection();
        var permissions = (await conn.QueryAsync<SysPermission>(@"
            SELECT * FROM sys_permissions
            ORDER BY module_group ASC, name ASC;")).ToList();

        return OkResponse(permissions);
    }

    [HttpGet("roles")]
    public async Task<ActionResult<ApiResponse<List<SysRole>>>> GetRoles()
    {
        using var conn = _db.CreateConnection();
        var roles = (await conn.QueryAsync<SysRole>(@"
            SELECT * FROM sys_roles
            WHERE pharmacy_no = @CurrentPharmacyNo
            ORDER BY role_no ASC;",
            new { CurrentPharmacyNo })).ToList();

        foreach (var role in roles)
        {
            var perms = (await conn.QueryAsync<string>(@"
                SELECT p.code
                FROM sys_role_permissions rp
                JOIN sys_permissions p ON rp.permission_no = p.permission_no
                WHERE rp.role_no = @RoleNo AND rp.pharmacy_no = @CurrentPharmacyNo;",
                new { role.RoleNo, CurrentPharmacyNo })).ToList();

            role.Permissions = perms;
        }

        return OkResponse(roles);
    }

    [HttpGet("roles/{id}")]
    public async Task<ActionResult<ApiResponse<SysRole>>> GetRoleById(long id)
    {
        using var conn = _db.CreateConnection();
        var role = await conn.QuerySingleOrDefaultAsync<SysRole>(@"
            SELECT * FROM sys_roles
            WHERE role_no = @Id AND pharmacy_no = @CurrentPharmacyNo;",
            new { Id = id, CurrentPharmacyNo });

        if (role == null)
            return FailResponse<SysRole>("Role not found.", statusCode: 404);

        var perms = (await conn.QueryAsync<string>(@"
            SELECT p.code
            FROM sys_role_permissions rp
            JOIN sys_permissions p ON rp.permission_no = p.permission_no
            WHERE rp.role_no = @Id AND rp.pharmacy_no = @CurrentPharmacyNo;",
            new { Id = id, CurrentPharmacyNo })).ToList();

        role.Permissions = perms;
        return OkResponse(role);
    }

    [HttpPost("roles")]
    [RequirePermission("role.manage")]
    public async Task<ActionResult<ApiResponse<SysRole>>> CreateRole([FromBody] SysRole model)
    {
        if (string.IsNullOrWhiteSpace(model.Alias))
            return FailResponse<SysRole>("Role name/alias is required.");

        using var conn = _db.CreateConnection();
        conn.Open();
        using var tran = conn.BeginTransaction();

        try
        {
            var isManager = await IsCallerManagerAsync(conn, tran);

            // Validate permissions
            if (model.Permissions != null && model.Permissions.Count > 0)
            {
                var validCodes = (await conn.QueryAsync<string>(
                    "SELECT code FROM sys_permissions WHERE code = ANY(@Codes);",
                    new { Codes = model.Permissions.ToArray() }, transaction: tran))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var code in model.Permissions)
                {
                    if (!validCodes.Contains(code))
                    {
                        return FailResponse<SysRole>($"Unknown permission code '{code}'.", statusCode: 400);
                    }
                }

                if (!isManager)
                {
                    var callerPerms = (await conn.QueryAsync<string>(@"
                        SELECT p.code
                        FROM sys_role_permissions rp
                        JOIN sys_permissions p ON rp.permission_no = p.permission_no
                        WHERE rp.role_no = @RoleNo AND rp.pharmacy_no = @PharmacyNo;",
                        new { RoleNo = CurrentRoleNo, PharmacyNo = CurrentPharmacyNo }, transaction: tran))
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    foreach (var code in model.Permissions)
                    {
                        if (!callerPerms.Contains(code))
                        {
                            return FailResponse<SysRole>($"Privilege rule: Cannot grant permission '{code}' not held by caller.", statusCode: 403);
                        }
                    }
                }
            }

            var exists = await conn.ExecuteScalarAsync<bool>(@"
                SELECT EXISTS(
                    SELECT 1 FROM sys_roles
                    WHERE alias = @Alias AND pharmacy_no = @CurrentPharmacyNo
                );",
                new { model.Alias, CurrentPharmacyNo },
                transaction: tran);

            if (exists)
                return FailResponse<SysRole>($"Role '{model.Alias}' already exists.");

            var nextNo = await conn.ExecuteScalarAsync<long>(
                "SELECT COALESCE(MAX(role_no), 0) + 1 FROM sys_roles;", transaction: tran);

            model.RoleNo = nextNo;
            model.PharmacyNo = CurrentPharmacyNo;
            model.BranchNo = CurrentBranchNo;
            model.RoleName = !string.IsNullOrWhiteSpace(model.RoleName) ? model.RoleName : model.Alias;
            model.Alias = !string.IsNullOrWhiteSpace(model.Alias) ? model.Alias : model.RoleName;
            model.IsSystem = false;
            model.CreatedAt = DateTime.UtcNow;
            model.UpdatedAt = DateTime.UtcNow;

            await conn.ExecuteAsync(@"
                INSERT INTO sys_roles (
                    role_no, pharmacy_no, branch_no, role_name, alias, description, is_system, status, created_at, updated_at
                ) VALUES (
                    @RoleNo, @PharmacyNo, @BranchNo, @RoleName, @Alias, @Description, @IsSystem, @Status, @CreatedAt, @UpdatedAt
                );", model, transaction: tran);

            if (model.Permissions != null && model.Permissions.Count > 0)
            {
                foreach (var code in model.Permissions)
                {
                    var pNo = await conn.QuerySingleOrDefaultAsync<long?>(
                        "SELECT permission_no FROM sys_permissions WHERE code = @Code;",
                        new { Code = code }, transaction: tran);

                    if (pNo.HasValue)
                    {
                        await conn.ExecuteAsync(@"
                            INSERT INTO sys_role_permissions (role_no, permission_no, pharmacy_no, branch_no)
                            VALUES (@RoleNo, @PermissionNo, @PharmacyNo, @BranchNo)
                            ON CONFLICT DO NOTHING;",
                            new { RoleNo = nextNo, PermissionNo = pNo.Value, PharmacyNo = CurrentPharmacyNo, BranchNo = CurrentBranchNo },
                            transaction: tran);
                    }
                }
            }

            tran.Commit();
            return OkResponse(model, $"Role '{model.Alias}' created successfully.");
        }
        catch
        {
            tran.Rollback();
            throw;
        }
    }

    [HttpPut("roles/{id}")]
    [RequirePermission("role.manage")]
    public async Task<ActionResult<ApiResponse<SysRole>>> UpdateRole(long id, [FromBody] SysRole model)
    {
        using var conn = _db.CreateConnection();
        conn.Open();
        using var tran = conn.BeginTransaction();

        try
        {
            var isManager = await IsCallerManagerAsync(conn, tran);

            var existing = await conn.QuerySingleOrDefaultAsync<SysRole>(@"
                SELECT * FROM sys_roles
                WHERE role_no = @Id AND pharmacy_no = @CurrentPharmacyNo;",
                new { Id = id, CurrentPharmacyNo },
                transaction: tran);

            if (existing == null)
                return FailResponse<SysRole>("Role not found.", statusCode: 404);

            if (model.Permissions != null && model.Permissions.Count > 0)
            {
                var validCodes = (await conn.QueryAsync<string>(
                    "SELECT code FROM sys_permissions WHERE code = ANY(@Codes);",
                    new { Codes = model.Permissions.ToArray() }, transaction: tran))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var code in model.Permissions)
                {
                    if (!validCodes.Contains(code))
                    {
                        return FailResponse<SysRole>($"Unknown permission code '{code}'.", statusCode: 400);
                    }
                }

                if (!isManager)
                {
                    var callerPerms = (await conn.QueryAsync<string>(@"
                        SELECT p.code
                        FROM sys_role_permissions rp
                        JOIN sys_permissions p ON rp.permission_no = p.permission_no
                        WHERE rp.role_no = @RoleNo AND rp.pharmacy_no = @PharmacyNo;",
                        new { RoleNo = CurrentRoleNo, PharmacyNo = CurrentPharmacyNo }, transaction: tran))
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    foreach (var code in model.Permissions)
                    {
                        if (!callerPerms.Contains(code))
                        {
                            return FailResponse<SysRole>($"Privilege rule: Cannot grant permission '{code}' not held by caller.", statusCode: 403);
                        }
                    }
                }
            }

            existing.RoleName = !string.IsNullOrWhiteSpace(model.RoleName) ? model.RoleName : (!string.IsNullOrWhiteSpace(model.Alias) ? model.Alias : existing.RoleName);
            existing.Alias = !string.IsNullOrWhiteSpace(model.Alias) ? model.Alias : (!string.IsNullOrWhiteSpace(model.RoleName) ? model.RoleName : existing.Alias);
            existing.Description = model.Description ?? existing.Description;
            existing.Status = model.Status;
            existing.UpdatedAt = DateTime.UtcNow;

            await conn.ExecuteAsync(@"
                UPDATE sys_roles
                SET role_name = @RoleName, alias = @Alias, description = @Description, status = @Status, updated_at = @UpdatedAt
                WHERE role_no = @RoleNo AND pharmacy_no = @PharmacyNo;",
                existing, transaction: tran);

            if (model.Permissions != null)
            {
                await conn.ExecuteAsync(
                    "DELETE FROM sys_role_permissions WHERE role_no = @Id AND pharmacy_no = @CurrentPharmacyNo;",
                    new { Id = id, CurrentPharmacyNo }, transaction: tran);

                foreach (var code in model.Permissions)
                {
                    var pNo = await conn.QuerySingleOrDefaultAsync<long?>(
                        "SELECT permission_no FROM sys_permissions WHERE code = @Code;",
                        new { Code = code }, transaction: tran);

                    if (pNo.HasValue)
                    {
                        await conn.ExecuteAsync(@"
                            INSERT INTO sys_role_permissions (role_no, permission_no, pharmacy_no, branch_no)
                            VALUES (@RoleNo, @PermissionNo, @PharmacyNo, @BranchNo)
                            ON CONFLICT DO NOTHING;",
                            new { RoleNo = id, PermissionNo = pNo.Value, PharmacyNo = CurrentPharmacyNo, BranchNo = CurrentBranchNo },
                            transaction: tran);
                    }
                }
            }

            tran.Commit();
            existing.Permissions = model.Permissions;
            return OkResponse(existing, $"Role '{existing.Alias}' updated successfully.");
        }
        catch
        {
            tran.Rollback();
            throw;
        }
    }

    [HttpDelete("roles/{id}")]
    [RequirePermission("role.manage")]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteRole(long id)
    {
        using var conn = _db.CreateConnection();
        var role = await conn.QuerySingleOrDefaultAsync<SysRole>(@"
            SELECT * FROM sys_roles WHERE role_no = @Id AND pharmacy_no = @CurrentPharmacyNo;",
            new { Id = id, CurrentPharmacyNo });

        if (role == null)
            return NotFoundResponse<bool>("Role not found.");

        if (role.IsSystem)
            return FailResponse<bool>("System roles cannot be deleted.");

        await conn.ExecuteAsync("DELETE FROM sys_role_permissions WHERE role_no = @Id AND pharmacy_no = @CurrentPharmacyNo;", new { Id = id, CurrentPharmacyNo });
        await conn.ExecuteAsync("DELETE FROM sys_role_menu_permissions WHERE role_no = @Id AND pharmacy_no = @CurrentPharmacyNo;", new { Id = id, CurrentPharmacyNo });
        var rows = await conn.ExecuteAsync(@"
            DELETE FROM sys_roles
            WHERE role_no = @Id AND pharmacy_no = @CurrentPharmacyNo;",
            new { Id = id, CurrentPharmacyNo });

        if (rows == 0) return NotFoundResponse<bool>("Role not found.");
        return OkResponse(true, "Role deleted successfully.");
    }

    [HttpGet("{roleNo}/menu-permissions")]
    public async Task<ActionResult<ApiResponse<List<RoleMenuPermissionItemDto>>>> GetRoleMenuPermissions(long roleNo)
    {
        using var conn = _db.CreateConnection();

        // Verify role belongs to current pharmacy
        var roleExists = await conn.ExecuteScalarAsync<bool>(@"
            SELECT EXISTS(SELECT 1 FROM sys_roles WHERE role_no = @roleNo AND pharmacy_no = @CurrentPharmacyNo);",
            new { roleNo, CurrentPharmacyNo });

        if (!roleExists)
            return FailResponse<List<RoleMenuPermissionItemDto>>("Role not found in the current pharmacy.", statusCode: 404);

        var sql = @"
            SELECT 
                m.menu_no,
                m.menu_code,
                m.title,
                m.title_bn,
                m.path,
                m.route,
                m.icon,
                m.parent_menu_no,
                p.title AS parent_title,
                m.order_no,
                m.menu_type,
                COALESCE(rmp.can_view, FALSE) AS can_view,
                COALESCE(rmp.can_create, FALSE) AS can_create,
                COALESCE(rmp.can_edit, FALSE) AS can_edit,
                COALESCE(rmp.can_delete, FALSE) AS can_delete
            FROM sys_menus m
            LEFT JOIN sys_menus p ON m.parent_menu_no = p.menu_no
            LEFT JOIN sys_role_menu_permissions rmp 
                ON m.menu_no = rmp.menu_no 
                AND rmp.role_no = @roleNo 
                AND rmp.pharmacy_no = @CurrentPharmacyNo
            WHERE m.status = TRUE
            ORDER BY COALESCE(m.parent_menu_no, m.menu_no), m.order_no ASC;";

        var perms = (await conn.QueryAsync<RoleMenuPermissionItemDto>(sql, new { roleNo, CurrentPharmacyNo })).ToList();
        return OkResponse(perms);
    }

    [HttpPost("{roleNo}/menu-permissions")]
    [RequirePermission("role.manage")]
    public async Task<ActionResult<ApiResponse<bool>>> SaveRoleMenuPermissions(long roleNo, [FromBody] List<RoleMenuPermissionItemDto> items)
    {
        using var conn = _db.CreateConnection();
        conn.Open();
        using var tran = conn.BeginTransaction();

        try
        {
            var role = await conn.QuerySingleOrDefaultAsync<SysRole>(@"
                SELECT * FROM sys_roles WHERE role_no = @roleNo AND pharmacy_no = @CurrentPharmacyNo;",
                new { roleNo, CurrentPharmacyNo }, transaction: tran);

            if (role == null)
                return FailResponse<bool>("Role not found in current pharmacy.", statusCode: 404);

            var isManager = await IsCallerManagerAsync(conn, tran);

            if (items != null && items.Count > 0)
            {
                // Validate all menu_no exist in sys_menus
                var menuNos = items.Select(x => x.MenuNo).Distinct().ToArray();
                var validMenuNos = (await conn.QueryAsync<long>(
                    "SELECT menu_no FROM sys_menus WHERE menu_no = ANY(@MenuNos);",
                    new { MenuNos = menuNos }, transaction: tran)).ToHashSet();

                foreach (var item in items)
                {
                    if (!validMenuNos.Contains(item.MenuNo))
                    {
                        return FailResponse<bool>($"Unknown menu_no {item.MenuNo}.", statusCode: 400);
                    }

                    // Normalize: create, edit, delete imply view
                    if (item.CanCreate || item.CanEdit || item.CanDelete)
                    {
                        item.CanView = true;
                    }
                }

                // Privilege rule: non-manager cannot grant grants they do not hold
                if (!isManager)
                {
                    var callerGrants = (await conn.QueryAsync<(long MenuNo, bool CanView, bool CanCreate, bool CanEdit, bool CanDelete)>(@"
                        SELECT menu_no, can_view, can_create, can_edit, can_delete
                        FROM sys_role_menu_permissions
                        WHERE role_no = @RoleNo AND pharmacy_no = @PharmacyNo AND menu_no = ANY(@MenuNos);",
                        new { RoleNo = CurrentRoleNo, PharmacyNo = CurrentPharmacyNo, MenuNos = menuNos },
                        transaction: tran))
                        .ToDictionary(g => g.MenuNo);

                    foreach (var item in items)
                    {
                        callerGrants.TryGetValue(item.MenuNo, out var cg);
                        if (item.CanView && !cg.CanView)
                            return FailResponse<bool>($"Privilege rule: Cannot grant View on menu {item.MenuNo} not held by caller.", statusCode: 403);
                        if (item.CanCreate && !cg.CanCreate)
                            return FailResponse<bool>($"Privilege rule: Cannot grant Create on menu {item.MenuNo} not held by caller.", statusCode: 403);
                        if (item.CanEdit && !cg.CanEdit)
                            return FailResponse<bool>($"Privilege rule: Cannot grant Edit on menu {item.MenuNo} not held by caller.", statusCode: 403);
                        if (item.CanDelete && !cg.CanDelete)
                            return FailResponse<bool>($"Privilege rule: Cannot grant Delete on menu {item.MenuNo} not held by caller.", statusCode: 403);
                    }
                }
            }

            var now = DateTime.UtcNow;

            if (items != null)
            {
                foreach (var item in items)
                {
                    var upsertSql = @"
                        INSERT INTO sys_role_menu_permissions (
                            role_menu_perm_no, pharmacy_no, role_no, menu_no,
                            can_view, can_create, can_edit, can_delete,
                            created_at, updated_at
                        ) VALUES (
                            nextval('seq_role_menu_perm_no'), @CurrentPharmacyNo, @roleNo, @MenuNo,
                            @CanView, @CanCreate, @CanEdit, @CanDelete,
                            @Now, @Now
                        )
                        ON CONFLICT (pharmacy_no, role_no, menu_no) DO UPDATE SET
                            can_view = EXCLUDED.can_view,
                            can_create = EXCLUDED.can_create,
                            can_edit = EXCLUDED.can_edit,
                            can_delete = EXCLUDED.can_delete,
                            updated_at = EXCLUDED.updated_at;";

                    await conn.ExecuteAsync(upsertSql, new
                    {
                        CurrentPharmacyNo,
                        roleNo,
                        item.MenuNo,
                        item.CanView,
                        item.CanCreate,
                        item.CanEdit,
                        item.CanDelete,
                        Now = now
                    }, transaction: tran);
                }
            }

            tran.Commit();
            return OkResponse(true, "Menu permissions updated successfully.");
        }
        catch
        {
            tran.Rollback();
            throw;
        }
    }

    [HttpGet("my-menu-permissions")]
    [AuthenticatedOnly]
    public async Task<ActionResult<ApiResponse<List<RoleMenuPermissionItemDto>>>> GetMyMenuPermissions()
    {
        using var conn = _db.CreateConnection();
        var sql = @"
            SELECT 
                m.menu_no,
                m.menu_code,
                m.title,
                m.title_bn,
                m.path,
                m.route,
                m.icon,
                m.parent_menu_no,
                p.title AS parent_title,
                m.order_no,
                m.menu_type,
                COALESCE(rmp.can_view, FALSE) AS can_view,
                COALESCE(rmp.can_create, FALSE) AS can_create,
                COALESCE(rmp.can_edit, FALSE) AS can_edit,
                COALESCE(rmp.can_delete, FALSE) AS can_delete
            FROM sys_menus m
            LEFT JOIN sys_menus p ON m.parent_menu_no = p.menu_no
            LEFT JOIN sys_role_menu_permissions rmp 
                ON m.menu_no = rmp.menu_no 
                AND rmp.role_no = @CurrentRoleNo 
                AND rmp.pharmacy_no = @CurrentPharmacyNo
            WHERE m.status = TRUE
            ORDER BY COALESCE(m.parent_menu_no, m.menu_no), m.order_no ASC;";

        var perms = (await conn.QueryAsync<RoleMenuPermissionItemDto>(sql, new { CurrentRoleNo, CurrentPharmacyNo })).ToList();
        return OkResponse(perms);
    }

    private async Task<bool> IsCallerManagerAsync(System.Data.IDbConnection conn, System.Data.IDbTransaction? tran = null)
    {
        if (CurrentRoleNo > 0)
        {
            return await conn.ExecuteScalarAsync<bool>(@"
                SELECT EXISTS(
                    SELECT 1 FROM sys_roles
                    WHERE role_no = @RoleNo AND pharmacy_no = @PharmacyNo AND (alias = 'PMS Manager' OR role_name = 'PMS Manager')
                );", new { RoleNo = CurrentRoleNo, PharmacyNo = CurrentPharmacyNo }, transaction: tran);
        }
        return false;
    }
}

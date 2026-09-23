using AidlyPms.Api.Common;
using AidlyPms.Api.Common.Database;
using AidlyPms.Api.Modules.Roles.Models;
using Dapper;
using Microsoft.AspNetCore.Mvc;

namespace AidlyPms.Api.Modules.Roles.Controllers;

[Route("api/v1/role")]
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
                WHERE rp.role_no = @RoleNo;",
                new { role.RoleNo })).ToList();

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
            WHERE rp.role_no = @Id;",
            new { Id = id })).ToList();

        role.Permissions = perms;
        return OkResponse(role);
    }

    [HttpPost("roles")]
    public async Task<ActionResult<ApiResponse<SysRole>>> CreateRole([FromBody] SysRole model)
    {
        if (string.IsNullOrWhiteSpace(model.Alias))
            return FailResponse<SysRole>("Role name/alias is required.");

        using var conn = _db.CreateConnection();
        conn.Open();
        using var tran = conn.BeginTransaction();

        try
        {
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
            model.IsSystem = false;
            model.CreatedAt = DateTime.UtcNow;
            model.UpdatedAt = DateTime.UtcNow;

            await conn.ExecuteAsync(@"
                INSERT INTO sys_roles (
                    role_no, pharmacy_no, branch_no, alias, description, is_system, created_at, updated_at
                ) VALUES (
                    @RoleNo, @PharmacyNo, @BranchNo, @Alias, @Description, @IsSystem, @CreatedAt, @UpdatedAt
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
        catch (Exception ex)
        {
            tran.Rollback();
            return FailResponse<SysRole>($"Role creation failed: {ex.Message}");
        }
    }

    [HttpPut("roles/{id}")]
    public async Task<ActionResult<ApiResponse<SysRole>>> UpdateRole(long id, [FromBody] SysRole model)
    {
        using var conn = _db.CreateConnection();
        conn.Open();
        using var tran = conn.BeginTransaction();

        try
        {
            var existing = await conn.QuerySingleOrDefaultAsync<SysRole>(@"
                SELECT * FROM sys_roles
                WHERE role_no = @Id AND pharmacy_no = @CurrentPharmacyNo;",
                new { Id = id, CurrentPharmacyNo },
                transaction: tran);

            if (existing == null)
                return FailResponse<SysRole>("Role not found.", statusCode: 404);

            existing.Alias = !string.IsNullOrWhiteSpace(model.Alias) ? model.Alias : existing.Alias;
            existing.Description = model.Description ?? existing.Description;
            existing.UpdatedAt = DateTime.UtcNow;

            await conn.ExecuteAsync(@"
                UPDATE sys_roles
                SET alias = @Alias, description = @Description, updated_at = @UpdatedAt
                WHERE role_no = @RoleNo AND pharmacy_no = @PharmacyNo;",
                existing, transaction: tran);

            if (model.Permissions != null)
            {
                await conn.ExecuteAsync(
                    "DELETE FROM sys_role_permissions WHERE role_no = @Id;",
                    new { Id = id }, transaction: tran);

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
        catch (Exception ex)
        {
            tran.Rollback();
            return FailResponse<SysRole>($"Role update failed: {ex.Message}");
        }
    }

    [HttpDelete("roles/{id}")]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteRole(long id)
    {
        using var conn = _db.CreateConnection();
        var isSystem = await conn.ExecuteScalarAsync<bool>(@"
            SELECT is_system FROM sys_roles WHERE role_no = @Id AND pharmacy_no = @CurrentPharmacyNo;",
            new { Id = id, CurrentPharmacyNo });

        if (isSystem)
            return FailResponse<bool>("System roles cannot be deleted.");

        await conn.ExecuteAsync("DELETE FROM sys_role_permissions WHERE role_no = @Id;", new { Id = id });
        var rows = await conn.ExecuteAsync(@"
            DELETE FROM sys_roles
            WHERE role_no = @Id AND pharmacy_no = @CurrentPharmacyNo;",
            new { Id = id, CurrentPharmacyNo });

        if (rows == 0) return NotFoundResponse<bool>("Role not found.");
        return OkResponse(true, "Role deleted successfully.");
    }
}

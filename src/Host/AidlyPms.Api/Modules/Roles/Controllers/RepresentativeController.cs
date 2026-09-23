using AidlyPms.Api.Common;
using AidlyPms.Api.Common.Database;
using AidlyPms.Api.Modules.Roles.Models;
using Dapper;
using Microsoft.AspNetCore.Mvc;

namespace AidlyPms.Api.Modules.Roles.Controllers;

[Route("api/v1")]
public class RepresentativeController : BaseController
{
    private readonly IDbConnectionFactory _db;

    public RepresentativeController(IDbConnectionFactory db)
    {
        _db = db;
    }

    [HttpGet("sys/users")]
    [HttpGet("representative")]
    public async Task<ActionResult<ApiResponse<PagedResult<SysUser>>>> GetRepresentatives(
        [FromQuery] QueryFilter filter,
        [FromQuery] long? roleNo,
        [FromQuery] string? search)
    {
        using var conn = _db.CreateConnection();
        var sql = @"
            SELECT u.*, r.alias AS role_name
            FROM sys_users u
            LEFT JOIN sys_roles r ON u.role_no = r.role_no
            WHERE u.pharmacy_no = @CurrentPharmacyNo AND u.branch_no = @CurrentBranchNo";

        var countSql = @"
            SELECT COUNT(1)
            FROM sys_users u
            WHERE u.pharmacy_no = @CurrentPharmacyNo AND u.branch_no = @CurrentBranchNo";

        var pms = new DynamicParameters();
        pms.Add("CurrentPharmacyNo", CurrentPharmacyNo);
        pms.Add("CurrentBranchNo", CurrentBranchNo);

        if (roleNo.HasValue)
        {
            sql += " AND u.role_no = @RoleNo";
            countSql += " AND u.role_no = @RoleNo";
            pms.Add("RoleNo", roleNo.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            sql += " AND (u.first_name ILIKE @Search OR u.last_name ILIKE @Search OR u.phone ILIKE @Search OR u.email ILIKE @Search)";
            countSql += " AND (u.first_name ILIKE @Search OR u.last_name ILIKE @Search OR u.phone ILIKE @Search OR u.email ILIKE @Search)";
            pms.Add("Search", $"%{search.Trim()}%");
        }

        sql += " ORDER BY u.user_no DESC LIMIT @PageSize OFFSET @Offset;";
        pms.Add("PageSize", filter.PageSize);
        pms.Add("Offset", (filter.PageIndex - 1) * filter.PageSize);

        var total = await conn.ExecuteScalarAsync<int>(countSql, pms);
        var items = (await conn.QueryAsync<SysUser>(sql, pms)).ToList();

        var result = new PagedResult<SysUser>(items, total, filter.PageIndex, filter.PageSize);
        return PagedResponse(result);
    }

    [HttpGet("sys/users/{id}")]
    [HttpGet("representative/{id}")]
    public async Task<ActionResult<ApiResponse<SysUser>>> GetRepresentativeById(long id)
    {
        using var conn = _db.CreateConnection();
        var user = await conn.QuerySingleOrDefaultAsync<SysUser>(@"
            SELECT u.*, r.alias AS role_name
            FROM sys_users u
            LEFT JOIN sys_roles r ON u.role_no = r.role_no
            WHERE u.user_no = @Id AND u.pharmacy_no = @CurrentPharmacyNo;",
            new { Id = id, CurrentPharmacyNo });

        if (user == null)
            return FailResponse<SysUser>("Staff user not found.", statusCode: 404);

        return OkResponse(user);
    }

    [HttpPost("sys/users")]
    [HttpPost("representative")]
    public async Task<ActionResult<ApiResponse<SysUser>>> CreateRepresentative([FromBody] SysUser model)
    {
        if (string.IsNullOrWhiteSpace(model.FirstName))
            return FailResponse<SysUser>("First name is required.");

        if (string.IsNullOrWhiteSpace(model.Phone))
            return FailResponse<SysUser>("Phone number is required.");

        if (model.RoleNo <= 0)
            return FailResponse<SysUser>("A valid role must be assigned.");

        using var conn = _db.CreateConnection();
        conn.Open();
        using var tran = conn.BeginTransaction();

        try
        {
            var phoneExists = await conn.ExecuteScalarAsync<bool>(@"
                SELECT EXISTS(SELECT 1 FROM sys_users WHERE phone = @Phone);",
                new { model.Phone }, transaction: tran);

            if (phoneExists)
                return FailResponse<SysUser>($"User with phone '{model.Phone}' already exists.");

            var nextNo = await conn.ExecuteScalarAsync<long>(
                "SELECT COALESCE(MAX(user_no), 0) + 1 FROM sys_users;", transaction: tran);

            model.UserNo = nextNo;
            model.Uuid = Guid.NewGuid();
            model.PharmacyNo = CurrentPharmacyNo;
            model.BranchNo = CurrentBranchNo;
            model.IsActive = true;
            model.CreatedAt = DateTime.UtcNow;
            model.UpdatedAt = DateTime.UtcNow;

            await conn.ExecuteAsync(@"
                INSERT INTO sys_users (
                    user_no, uuid, pharmacy_no, branch_no, role_no,
                    first_name, last_name, phone, email, password_hash,
                    gender, is_active, created_at, updated_at
                ) VALUES (
                    @UserNo, @Uuid, @PharmacyNo, @BranchNo, @RoleNo,
                    @FirstName, @LastName, @Phone, @Email, '',
                    @Gender, @IsActive, @CreatedAt, @UpdatedAt
                );", model, transaction: tran);

            // Grant branch access
            var nextAccessNo = await conn.ExecuteScalarAsync<long>(
                "SELECT COALESCE(MAX(user_pharmacy_access_no), 0) + 1 FROM sys_user_pharmacy_access;", transaction: tran);

            await conn.ExecuteAsync(@"
                INSERT INTO sys_user_pharmacy_access (
                    user_pharmacy_access_no, user_no, pharmacy_no, branch_no, is_default, created_at
                ) VALUES (
                    @NextAccessNo, @UserNo, @PharmacyNo, @BranchNo, TRUE, NOW()
                );",
                new { NextAccessNo = nextAccessNo, UserNo = nextNo, PharmacyNo = CurrentPharmacyNo, BranchNo = CurrentBranchNo },
                transaction: tran);

            tran.Commit();
            return OkResponse(model, $"Representative '{model.FirstName} {model.LastName}' created successfully.");
        }
        catch (Exception ex)
        {
            tran.Rollback();
            return FailResponse<SysUser>($"Registration failed: {ex.Message}");
        }
    }

    [HttpPut("sys/users/{id}")]
    [HttpPut("representative/{id}")]
    public async Task<ActionResult<ApiResponse<SysUser>>> UpdateRepresentative(long id, [FromBody] SysUser model)
    {
        using var conn = _db.CreateConnection();
        var existing = await conn.QuerySingleOrDefaultAsync<SysUser>(@"
            SELECT * FROM sys_users
            WHERE user_no = @Id AND pharmacy_no = @CurrentPharmacyNo;",
            new { Id = id, CurrentPharmacyNo });

        if (existing == null)
            return FailResponse<SysUser>("Staff user not found.", statusCode: 404);

        existing.FirstName = !string.IsNullOrWhiteSpace(model.FirstName) ? model.FirstName : existing.FirstName;
        existing.LastName = !string.IsNullOrWhiteSpace(model.LastName) ? model.LastName : existing.LastName;
        existing.Email = model.Email ?? existing.Email;
        existing.RoleNo = model.RoleNo > 0 ? model.RoleNo : existing.RoleNo;
        existing.Gender = model.Gender ?? existing.Gender;
        existing.IsActive = model.IsActive;
        existing.UpdatedAt = DateTime.UtcNow;

        await conn.ExecuteAsync(@"
            UPDATE sys_users
            SET first_name = @FirstName, last_name = @LastName,
                email = @Email, role_no = @RoleNo, gender = @Gender,
                is_active = @IsActive, updated_at = @UpdatedAt
            WHERE user_no = @UserNo AND pharmacy_no = @PharmacyNo;",
            existing);

        return OkResponse(existing, $"Representative '{existing.FirstName}' updated successfully.");
    }

    [HttpDelete("sys/users/{id}")]
    [HttpDelete("representative/{id}")]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteRepresentative(long id)
    {
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync("DELETE FROM sys_user_pharmacy_access WHERE user_no = @Id;", new { Id = id });
        var rows = await conn.ExecuteAsync(@"
            DELETE FROM sys_users
            WHERE user_no = @Id AND pharmacy_no = @CurrentPharmacyNo;",
            new { Id = id, CurrentPharmacyNo });

        if (rows == 0) return NotFoundResponse<bool>("Staff user not found.");
        return OkResponse(true, "Staff user deleted successfully.");
    }
}

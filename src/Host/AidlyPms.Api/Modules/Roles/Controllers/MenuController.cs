using System.Security.Claims;
using AidlyPms.Api.Common;
using AidlyPms.Api.Common.Database;
using AidlyPms.Api.Modules.Roles.Models;
using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace AidlyPms.Api.Modules.Roles.Controllers;

[Route("api/v1/menus")]
[RequireMenu("MENU_1904", "ROLE_1903")]
public class MenuController : BaseController
{
    private readonly IDbConnectionFactory _db;
    private readonly IConfiguration _config;

    public MenuController(IDbConnectionFactory db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    private bool IsPlatformOwner()
    {
        var isPlatformOwnerClaim = string.Equals(User.FindFirst("platform_owner")?.Value, "true", StringComparison.OrdinalIgnoreCase);
        if (isPlatformOwnerClaim) return true;

        var ownerConfigUserNos = _config["PlatformOwnerUserNos"] ?? _config["PlatformOwner:UserId"] ?? "1";
        var allowedUserNos = ownerConfigUserNos.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var currentUserId = CurrentUserNo.ToString();
        if (allowedUserNos.Contains(currentUserId)) return true;

        var ownerEmailConfig = _config["PlatformOwner:Email"];
        var userEmail = User.FindFirst(ClaimTypes.Email)?.Value ?? User.FindFirst("email")?.Value;
        if (!string.IsNullOrWhiteSpace(ownerEmailConfig) && string.Equals(userEmail, ownerEmailConfig, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<SysMenu>>>> GetMenus([FromQuery] bool? activeOnly)
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
                m.status,
                m.created_at,
                m.updated_at
            FROM sys_menus m
            LEFT JOIN sys_menus p ON m.parent_menu_no = p.menu_no
            WHERE (@ActiveOnly IS NULL OR @ActiveOnly = FALSE OR m.status = TRUE)
            ORDER BY COALESCE(m.parent_menu_no, m.menu_no), m.order_no ASC;";

        var menus = (await conn.QueryAsync<SysMenu>(sql, new { ActiveOnly = activeOnly })).ToList();
        return OkResponse(menus);
    }

    [HttpGet("tree")]
    public async Task<ActionResult<ApiResponse<List<SysMenu>>>> GetMenuTree()
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
                m.status,
                m.created_at,
                m.updated_at
            FROM sys_menus m
            LEFT JOIN sys_menus p ON m.parent_menu_no = p.menu_no
            ORDER BY m.order_no ASC;";

        var allMenus = (await conn.QueryAsync<SysMenu>(sql)).ToList();

        var roots = allMenus.Where(m => m.ParentMenuNo == null).OrderBy(m => m.OrderNo).ToList();
        foreach (var root in roots)
        {
            root.Children = allMenus.Where(m => m.ParentMenuNo == root.MenuNo).OrderBy(m => m.OrderNo).ToList();
            if (root.Children != null)
            {
                foreach (var child in root.Children)
                {
                    child.Children = allMenus.Where(m => m.ParentMenuNo == child.MenuNo).OrderBy(m => m.OrderNo).ToList();
                }
            }
        }

        return OkResponse(roots);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<SysMenu>>> GetMenuById(long id)
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
                m.status,
                m.created_at,
                m.updated_at
            FROM sys_menus m
            LEFT JOIN sys_menus p ON m.parent_menu_no = p.menu_no
            WHERE m.menu_no = @Id;";

        var menu = await conn.QuerySingleOrDefaultAsync<SysMenu>(sql, new { Id = id });
        if (menu == null)
            return FailResponse<SysMenu>("Menu not found.", statusCode: 404);

        return OkResponse(menu);
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<SysMenu>>> CreateMenu([FromBody] SysMenu model)
    {
        if (!IsPlatformOwner())
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<SysMenu>.Fail("Forbidden", "Access denied: Platform owner authorization required to create menus."));
        }

        if (string.IsNullOrWhiteSpace(model.Title))
            return FailResponse<SysMenu>("Menu title is required.");

        if (string.IsNullOrWhiteSpace(model.MenuCode))
            return FailResponse<SysMenu>("Menu code is required.");

        using var conn = _db.CreateConnection();

        var codeExists = await conn.ExecuteScalarAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM sys_menus WHERE menu_code = @MenuCode);", new { model.MenuCode });
        if (codeExists)
            return FailResponse<SysMenu>($"Menu with code '{model.MenuCode}' already exists.");

        if (model.ParentMenuNo.HasValue)
        {
            var parentExists = await conn.ExecuteScalarAsync<bool>(
                "SELECT EXISTS(SELECT 1 FROM sys_menus WHERE menu_no = @ParentMenuNo);",
                new { model.ParentMenuNo });

            if (!parentExists)
                return FailResponse<SysMenu>("Selected parent menu does not exist.");
        }

        var nextNo = await conn.ExecuteScalarAsync<long>("SELECT nextval('seq_sys_menu_no');");
        model.MenuNo = nextNo;
        model.Route = model.Route ?? model.Path;
        model.Path = model.Path ?? model.Route;
        model.TitleBn = !string.IsNullOrWhiteSpace(model.TitleBn) ? model.TitleBn : model.Title;
        model.MenuType = !string.IsNullOrWhiteSpace(model.MenuType) ? model.MenuType : "SCREEN";
        model.CreatedAt = DateTime.UtcNow;
        model.UpdatedAt = DateTime.UtcNow;

        var insertSql = @"
            INSERT INTO sys_menus (
                menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at
            ) VALUES (
                @MenuNo, @MenuCode, @Title, @TitleBn, @Path, @Route, @Icon, @ParentMenuNo, @OrderNo, @MenuType, @Status, @CreatedAt, @UpdatedAt
            );";

        await conn.ExecuteAsync(insertSql, model);

        return await GetMenuById(model.MenuNo);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<ApiResponse<SysMenu>>> UpdateMenu(long id, [FromBody] SysMenu model)
    {
        if (!IsPlatformOwner())
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<SysMenu>.Fail("Forbidden", "Access denied: Platform owner authorization required to edit menus."));
        }

        if (string.IsNullOrWhiteSpace(model.Title))
            return FailResponse<SysMenu>("Menu title is required.");

        if (model.ParentMenuNo.HasValue && model.ParentMenuNo.Value == id)
            return FailResponse<SysMenu>("A menu cannot be its own parent.");

        using var conn = _db.CreateConnection();

        var existing = await conn.QuerySingleOrDefaultAsync<SysMenu>(
            "SELECT * FROM sys_menus WHERE menu_no = @Id;", new { Id = id });

        if (existing == null)
            return FailResponse<SysMenu>("Menu not found.", statusCode: 404);

        if (!string.IsNullOrWhiteSpace(model.MenuCode) && !string.Equals(model.MenuCode, existing.MenuCode, StringComparison.OrdinalIgnoreCase))
        {
            var codeExists = await conn.ExecuteScalarAsync<bool>(
                "SELECT EXISTS(SELECT 1 FROM sys_menus WHERE menu_code = @MenuCode AND menu_no <> @Id);",
                new { model.MenuCode, Id = id });
            if (codeExists)
                return FailResponse<SysMenu>($"Menu code '{model.MenuCode}' is already taken by another menu.");
        }

        if (model.ParentMenuNo.HasValue)
        {
            var parentExists = await conn.ExecuteScalarAsync<bool>(
                "SELECT EXISTS(SELECT 1 FROM sys_menus WHERE menu_no = @ParentMenuNo);",
                new { model.ParentMenuNo });

            if (!parentExists)
                return FailResponse<SysMenu>("Selected parent menu does not exist.");
        }

        var updateSql = @"
            UPDATE sys_menus SET
                menu_code = COALESCE(@MenuCode, menu_code),
                title = @Title,
                title_bn = COALESCE(@TitleBn, title_bn),
                path = @Path,
                route = @Route,
                icon = @Icon,
                parent_menu_no = @ParentMenuNo,
                order_no = @OrderNo,
                menu_type = COALESCE(@MenuType, menu_type),
                status = @Status,
                updated_at = @UpdatedAt
            WHERE menu_no = @MenuNo;";

        await conn.ExecuteAsync(updateSql, new
        {
            MenuNo = id,
            MenuCode = !string.IsNullOrWhiteSpace(model.MenuCode) ? model.MenuCode : existing.MenuCode,
            model.Title,
            TitleBn = !string.IsNullOrWhiteSpace(model.TitleBn) ? model.TitleBn : existing.TitleBn,
            Path = model.Path ?? model.Route ?? existing.Path,
            Route = model.Route ?? model.Path ?? existing.Route,
            model.Icon,
            model.ParentMenuNo,
            model.OrderNo,
            MenuType = !string.IsNullOrWhiteSpace(model.MenuType) ? model.MenuType : existing.MenuType,
            model.Status,
            UpdatedAt = DateTime.UtcNow
        });

        return await GetMenuById(id);
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteMenu(long id)
    {
        if (!IsPlatformOwner())
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<bool>.Fail("Forbidden", "Access denied: Platform owner authorization required to delete menus."));
        }

        using var conn = _db.CreateConnection();

        var childCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM sys_menus WHERE parent_menu_no = @Id;", new { Id = id });

        if (childCount > 0)
            return FailResponse<bool>($"Cannot delete menu because it has {childCount} child sub-menus. Please delete or reassign child sub-menus first.");

        await conn.ExecuteAsync("DELETE FROM sys_role_menu_permissions WHERE menu_no = @Id;", new { Id = id });

        var rows = await conn.ExecuteAsync("DELETE FROM sys_menus WHERE menu_no = @Id;", new { Id = id });
        if (rows == 0) return NotFoundResponse<bool>("Menu not found.");

        return OkResponse(true, "Menu deleted successfully.");
    }
}

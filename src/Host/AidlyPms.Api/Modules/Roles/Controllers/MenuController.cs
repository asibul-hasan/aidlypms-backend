using AidlyPms.Api.Common;
using AidlyPms.Api.Common.Database;
using AidlyPms.Api.Modules.Roles.Models;
using Dapper;
using Microsoft.AspNetCore.Mvc;

namespace AidlyPms.Api.Modules.Roles.Controllers;

[Route("api/v1/menus")]
[RequirePermission("Role-Module")]
public class MenuController : BaseController
{
    private readonly IDbConnectionFactory _db;

    public MenuController(IDbConnectionFactory db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<SysMenu>>>> GetMenus([FromQuery] bool? activeOnly)
    {
        using var conn = _db.CreateConnection();
        var sql = @"
            SELECT 
                m.menu_no,
                m.title,
                m.path,
                m.icon,
                m.parent_menu_no,
                p.title AS parent_title,
                m.order_no,
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
                m.title,
                m.path,
                m.icon,
                m.parent_menu_no,
                p.title AS parent_title,
                m.order_no,
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
                m.title,
                m.path,
                m.icon,
                m.parent_menu_no,
                p.title AS parent_title,
                m.order_no,
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
    [RequirePermission("role.manage")]
    public async Task<ActionResult<ApiResponse<SysMenu>>> CreateMenu([FromBody] SysMenu model)
    {
        if (string.IsNullOrWhiteSpace(model.Title))
            return FailResponse<SysMenu>("Menu title is required.");

        using var conn = _db.CreateConnection();

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
        model.CreatedAt = DateTime.UtcNow;
        model.UpdatedAt = DateTime.UtcNow;

        var insertSql = @"
            INSERT INTO sys_menus (
                menu_no, title, path, icon, parent_menu_no, order_no, status, created_at, updated_at
            ) VALUES (
                @MenuNo, @Title, @Path, @Icon, @ParentMenuNo, @OrderNo, @Status, @CreatedAt, @UpdatedAt
            );";

        await conn.ExecuteAsync(insertSql, model);

        // Fetch back with parent title if any
        return await GetMenuById(model.MenuNo);
    }

    [HttpPut("{id}")]
    [RequirePermission("role.manage")]
    public async Task<ActionResult<ApiResponse<SysMenu>>> UpdateMenu(long id, [FromBody] SysMenu model)
    {
        if (string.IsNullOrWhiteSpace(model.Title))
            return FailResponse<SysMenu>("Menu title is required.");

        if (model.ParentMenuNo.HasValue && model.ParentMenuNo.Value == id)
            return FailResponse<SysMenu>("A menu cannot be its own parent.");

        using var conn = _db.CreateConnection();

        var existing = await conn.QuerySingleOrDefaultAsync<SysMenu>(
            "SELECT * FROM sys_menus WHERE menu_no = @Id;", new { Id = id });

        if (existing == null)
            return FailResponse<SysMenu>("Menu not found.", statusCode: 404);

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
                title = @Title,
                path = @Path,
                icon = @Icon,
                parent_menu_no = @ParentMenuNo,
                order_no = @OrderNo,
                status = @Status,
                updated_at = @UpdatedAt
            WHERE menu_no = @MenuNo;";

        await conn.ExecuteAsync(updateSql, new
        {
            MenuNo = id,
            model.Title,
            model.Path,
            model.Icon,
            model.ParentMenuNo,
            model.OrderNo,
            model.Status,
            UpdatedAt = DateTime.UtcNow
        });

        return await GetMenuById(id);
    }

    [HttpDelete("{id}")]
    [RequirePermission("role.manage")]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteMenu(long id)
    {
        using var conn = _db.CreateConnection();

        // Check if there are child menus
        var childCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM sys_menus WHERE parent_menu_no = @Id;", new { Id = id });

        if (childCount > 0)
            return FailResponse<bool>($"Cannot delete menu because it has {childCount} child sub-menus. Please delete or reassign child sub-menus first.");

        // Delete any role permissions associated with this menu
        await conn.ExecuteAsync("DELETE FROM sys_role_menu_permissions WHERE menu_no = @Id;", new { Id = id });

        var rows = await conn.ExecuteAsync("DELETE FROM sys_menus WHERE menu_no = @Id;", new { Id = id });
        if (rows == 0) return NotFoundResponse<bool>("Menu not found.");

        return OkResponse(true, "Menu deleted successfully.");
    }
}

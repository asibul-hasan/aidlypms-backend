using AidlyPms.Api.Common.Security;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.RateLimiting;
using AidlyPms.Api.Common;
using AidlyPms.Api.Common.Database;
using AidlyPms.Api.Modules.Auth.Models;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AidlyPms.Api.Modules.Auth.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class AuthController : BaseController
{
    private readonly IDbConnectionFactory _db;
    private readonly IConfiguration _config;

    public AuthController(IDbConnectionFactory db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    [HttpPost("login")]
    [EnableRateLimiting("login")]
    [BrowserCsrfRequired]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<LoginResponse>>> Login([FromBody] LoginRequest request)
    {
        var identifier = (request.Phone ?? request.Username ?? "").Trim();
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return FailResponse<LoginResponse>("Phone number or username is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            return FailResponse<LoginResponse>("Password is required.");
        }

        var phoneCandidates = LoginIdentifier.PhoneCandidates(identifier);
        if (!HttpContext.RequestServices.GetRequiredService<LoginThrottle>().Permit(phoneCandidates.FirstOrDefault() ?? identifier))
        {
            Response.Headers.RetryAfter = "60";
            return FailResponse<LoginResponse>("Too many sign-in attempts. Try again shortly.", statusCode: 429);
        }
        using var conn = _db.CreateConnection();

        // 1. Check if user exists
        const string userSql = @"
            SELECT u.user_no, u.uuid, u.pharmacy_no, u.branch_no, u.role_no,
                   u.first_name, u.last_name, u.phone, u.email, u.password_hash,
                   u.gender, u.is_active, u.updated_at, r.alias as role_name
            FROM sys_users u
            LEFT JOIN sys_roles r ON u.role_no = r.role_no
            WHERE u.phone = @Identifier 
               OR LOWER(u.email) = LOWER(@Identifier)
               OR regexp_replace(u.phone, '[^0-9]', '', 'g') = ANY(@PhoneIdentifiers)
               OR LOWER(SPLIT_PART(u.email, '@', 1)) = LOWER(@Identifier)
            LIMIT 2;";

        var matches = (await conn.QueryAsync<dynamic>(userSql, new { Identifier = identifier, PhoneIdentifiers = phoneCandidates })).ToList();
        dynamic? user = matches.Count == 1 ? matches[0] : null;

        var passwordVerified = PasswordVerification.Verify(request.Password, user is null ? null : (string?)user.password_hash);
        if (user is null)
            return FailResponse<LoginResponse>("Invalid phone number or password.", statusCode: 401);
        if (user.is_active != true || !passwordVerified)
        {
            return FailResponse<LoginResponse>("Invalid phone number or password.", statusCode: 401);
        }

        // 3. Fetch permissions
        var roleNo = (long)user.role_no;
        // 4. Fetch branch details
        var pharmacyNo = (long)user.pharmacy_no;
        var branchNo = (long)user.branch_no;

        var branch = await conn.QuerySingleOrDefaultAsync<BranchDto>(@"
            SELECT b.branch_no, b.pharmacy_no, b.name,
                   COALESCE(b.contact_phone, p.contact_phone) as contact_phone,
                   COALESCE(b.address, p.address) as address,
                   COALESCE(b.district, p.district) as district,
                   p.drug_license_no, p.contact_email
            FROM sys_branches b
            JOIN sys_pharmacies p ON b.pharmacy_no = p.pharmacy_no
            WHERE b.branch_no = @BranchNo AND b.pharmacy_no = @PharmacyNo;",
            new { BranchNo = branchNo, PharmacyNo = pharmacyNo });

        if (branch == null)
            return FailResponse<LoginResponse>("Invalid phone number or password.", statusCode: 401);

        // 5. Build user menu tree, special permissions, and is_full_access
        var (menuTree, specialPermissions, isFullAccess) = await BuildUserAccessAsync(conn, pharmacyNo, roleNo, (string?)user.role_name);

        // 6. Generate JWT token
        (string Token, DateTime ExpiresAt) issued;
        try
        {
            issued = await HttpContext.RequestServices.GetRequiredService<IAuthSessions>()
                .CreateAsync((long)user.user_no, pharmacyNo, branchNo, HttpContext.RequestAborted, verifiedUserUpdatedAt: (DateTime)user.updated_at);
        }
        catch (UnauthorizedAccessException)
        {
            return FailResponse<LoginResponse>("Invalid phone number or password.", statusCode: 401);
        }
        BrowserSession.Write(HttpContext, issued.Token, issued.ExpiresAt);
        Response.Headers.CacheControl = "no-store";

        var userDto = new AuthUserDto
        {
            UserNo = (long)user.user_no,
            Uuid = (Guid)user.uuid,
            PharmacyNo = pharmacyNo,
            BranchNo = branchNo,
            RoleNo = roleNo,
            RoleName = user.role_name,
            FirstName = user.first_name ?? "",
            LastName = user.last_name ?? "",
            Phone = user.phone ?? "",
            Email = user.email,
            Gender = user.gender,
            IsActive = user.is_active ?? true,
            Permissions = specialPermissions,
            Menu = menuTree,
            IsFullAccess = isFullAccess,
            IsPlatformOwner = await IsPlatformRoleAsync(conn, roleNo)
        };

        var response = new LoginResponse
        {
            Token = "session",
            User = userDto,
            Branch = branch,
            Menu = menuTree,
            Permissions = specialPermissions,
            IsFullAccess = isFullAccess
        };

        return OkResponse(response, $"Welcome back, {userDto.FirstName}!");
    }

    [HttpGet("me")]
    [AuthenticatedOnly]
    public async Task<ActionResult<ApiResponse<AuthUserDto>>> GetCurrentUser()
    {
        if (CurrentUserNo <= 0)
        {
            return FailResponse<AuthUserDto>("Unauthorized", statusCode: 401);
        }

        using var conn = _db.CreateConnection();
        const string userSql = @"
            SELECT u.user_no, u.uuid, u.pharmacy_no, u.branch_no, u.role_no,
                   u.first_name, u.last_name, u.phone, u.email,
                   u.gender, u.is_active, r.alias as role_name
            FROM sys_users u
            LEFT JOIN sys_roles r ON u.role_no = r.role_no
            WHERE u.user_no = @UserNo
            LIMIT 1;";

        var user = await conn.QuerySingleOrDefaultAsync<dynamic>(userSql, new { UserNo = CurrentUserNo });
        if (user == null)
        {
            return NotFoundResponse<AuthUserDto>("User not found.");
        }

        var (menuTree, specialPermissions, isFullAccess) = await BuildUserAccessAsync(
            conn, CurrentPharmacyNo, (long)user.role_no, (string?)user.role_name);

        var userDto = new AuthUserDto
        {
            UserNo = (long)user.user_no,
            Uuid = (Guid)user.uuid,
            PharmacyNo = CurrentPharmacyNo,
            BranchNo = CurrentBranchNo,
            RoleNo = (long)user.role_no,
            RoleName = user.role_name,
            FirstName = user.first_name ?? "",
            LastName = user.last_name ?? "",
            Phone = user.phone ?? "",
            Email = user.email,
            Gender = user.gender,
            IsActive = user.is_active ?? true,
            Permissions = specialPermissions,
            Menu = menuTree,
            IsFullAccess = isFullAccess,
            IsPlatformOwner = IsPlatformOwner
        };

        return OkResponse(userDto);
    }

    private static readonly HashSet<string> SpecialPermissionCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "sale.return", "void", "stock.adjust", "price.edit", "cash.close", "user.manage", "role.manage"
    };

    public static async Task<(List<AidlyPms.Api.Modules.Roles.Models.SysMenu> MenuTree, List<string> Permissions, bool IsFullAccess)> BuildUserAccessAsync(
        System.Data.IDbConnection conn, long pharmacyNo, long roleNo, string? roleName)
    {
        var scope = await conn.QuerySingleOrDefaultAsync<string>(@"
            SELECT c.access_scope FROM sys_role_capabilities c JOIN sys_roles r ON r.role_no = c.role_no
            WHERE r.role_no = @RoleNo AND r.status = TRUE
              AND (c.access_scope = 'PLATFORM_OWNER' OR r.pharmacy_no = @PharmacyNo);", new { RoleNo = roleNo, PharmacyNo = pharmacyNo });
        var isAdmin = scope == "PLATFORM_OWNER";
        var isManager = scope == "STORE_MANAGER";

        var isFullAccess = isAdmin || isManager;

        // 1. Special permissions (the 7 non-menu action codes)
        List<string> specialPermissions;
        if (isFullAccess)
        {
            specialPermissions = SpecialPermissionCodes.OrderBy(x => x).ToList();
        }
        else
        {
            var rawPerms = (await conn.QueryAsync<string>(@"
                SELECT p.code
                FROM sys_role_permissions rp
                JOIN sys_permissions p ON rp.permission_no = p.permission_no
                WHERE rp.role_no = @RoleNo
                  AND rp.pharmacy_no = @PharmacyNo;",
                new { RoleNo = roleNo, PharmacyNo = pharmacyNo })).ToList();

            specialPermissions = rawPerms.Where(p => SpecialPermissionCodes.Contains(p)).Distinct().OrderBy(x => x).ToList();
        }

        // 2. Fetch all active menus
        var allMenus = (await conn.QueryAsync<AidlyPms.Api.Modules.Roles.Models.SysMenu>(@"
            SELECT menu_no, menu_code, title, title_bn, path, route, icon, parent_menu_no, order_no, menu_type, status, created_at, updated_at
            FROM sys_menus
            WHERE status = TRUE
            ORDER BY order_no ASC;")).ToList();

        // 3. Permissions mapping
        Dictionary<long, AidlyPms.Api.Modules.Roles.Models.MenuPermissionsDto> permsMap;
        if (isAdmin)
        {
            permsMap = allMenus.ToDictionary(m => m.MenuNo, m => new AidlyPms.Api.Modules.Roles.Models.MenuPermissionsDto
            {
                View = true,
                Create = true,
                Edit = true,
                Delete = true
            });
        }
        else if (isManager)
        {
            // Store/Pharmacy Manager has full operational access, EXCEPT developer-only menu management screens
            permsMap = allMenus.ToDictionary(m => m.MenuNo, m =>
            {
                var isDevOnly = m.MenuCode == "MENU_1904" || m.MenuCode == "MENU_1905" ||
                                string.Equals(m.Route, "/menus", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(m.Route, "/menus/add", StringComparison.OrdinalIgnoreCase);
                return isDevOnly
                    ? new AidlyPms.Api.Modules.Roles.Models.MenuPermissionsDto { View = false, Create = false, Edit = false, Delete = false }
                    : new AidlyPms.Api.Modules.Roles.Models.MenuPermissionsDto { View = true, Create = true, Edit = true, Delete = true };
            });
        }
        else
        {
            var grants = (await conn.QueryAsync<(long MenuNo, bool CanView, bool CanCreate, bool CanEdit, bool CanDelete)>(@"
                SELECT menu_no, can_view, can_create, can_edit, can_delete
                FROM sys_role_menu_permissions
                WHERE role_no = @RoleNo
                  AND pharmacy_no = @PharmacyNo;",
                new { RoleNo = roleNo, PharmacyNo = pharmacyNo })).ToList();

            permsMap = grants.ToDictionary(g => g.MenuNo, g => new AidlyPms.Api.Modules.Roles.Models.MenuPermissionsDto
            {
                View = g.CanView,
                Create = g.CanCreate,
                Edit = g.CanEdit,
                Delete = g.CanDelete
            });
        }

        // Attach permissions to all menu objects
        foreach (var menu in allMenus)
        {
            if (permsMap.TryGetValue(menu.MenuNo, out var p))
            {
                menu.Permissions = p;
            }
            else
            {
                menu.Permissions = new AidlyPms.Api.Modules.Roles.Models.MenuPermissionsDto { View = false, Create = false, Edit = false, Delete = false };
            }
        }

        // Build tree
        var menuById = allMenus.ToDictionary(m => m.MenuNo, m => new AidlyPms.Api.Modules.Roles.Models.SysMenu
        {
            MenuNo = m.MenuNo,
            MenuCode = m.MenuCode,
            Title = m.Title,
            TitleBn = m.TitleBn,
            Path = m.Path,
            Route = m.Route,
            Icon = m.Icon,
            ParentMenuNo = m.ParentMenuNo,
            OrderNo = m.OrderNo,
            MenuType = m.MenuType,
            Status = m.Status,
            Permissions = m.Permissions,
            CreatedAt = m.CreatedAt,
            UpdatedAt = m.UpdatedAt,
            Children = new List<AidlyPms.Api.Modules.Roles.Models.SysMenu>()
        });

        foreach (var m in allMenus)
        {
            if (m.ParentMenuNo.HasValue && menuById.TryGetValue(m.ParentMenuNo.Value, out var parentNode))
            {
                parentNode.Children!.Add(menuById[m.MenuNo]);
            }
        }

        // Visibility filter: node is visible if it can be viewed or any child is visible
        bool IsNodeVisible(AidlyPms.Api.Modules.Roles.Models.SysMenu node)
        {
            var isDevOnly = node.MenuCode == "MENU_1904" || node.MenuCode == "MENU_1905" ||
                            string.Equals(node.Route, "/menus", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(node.Route, "/menus/add", StringComparison.OrdinalIgnoreCase);
            if (isDevOnly && !isAdmin) return false;

            if (isAdmin) return true;
            if (node.Permissions?.View == true) return true;
            if (node.Children != null && node.Children.Any(IsNodeVisible))
            {
                if (node.Permissions != null) node.Permissions.View = true;
                return true;
            }
            return false;
        }

        void PruneInvisibleChildren(AidlyPms.Api.Modules.Roles.Models.SysMenu node)
        {
            if (node.Children == null) return;
            node.Children = node.Children.Where(IsNodeVisible).OrderBy(c => c.OrderNo).ToList();
            foreach (var child in node.Children)
            {
                PruneInvisibleChildren(child);
            }
        }

        var roots = allMenus
            .Where(m => !m.ParentMenuNo.HasValue)
            .Select(m => menuById[m.MenuNo])
            .Where(IsNodeVisible)
            .OrderBy(m => m.OrderNo)
            .ToList();

        foreach (var root in roots)
        {
            PruneInvisibleChildren(root);
        }

        return (roots, specialPermissions, isFullAccess);
    }

    private static Task<bool> IsPlatformRoleAsync(System.Data.IDbConnection conn, long roleNo) =>
        conn.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM sys_role_capabilities WHERE role_no = @RoleNo AND access_scope = 'PLATFORM_OWNER');", new { RoleNo = roleNo });

    [HttpGet("csrf")]
    [AllowAnonymous]
    public ActionResult<ApiResponse<object>> GetCsrfToken([FromServices] IAntiforgery antiforgery)
    {
        Response.Headers.CacheControl = "no-store";
        return OkResponse<object>(new { requestToken = antiforgery.GetAndStoreTokens(HttpContext).RequestToken });
    }

    [HttpPost("logout")]
    [AuthenticatedOnly]
    public async Task<ActionResult<ApiResponse<bool>>> Logout([FromServices] IAuthSessions sessions)
    {
        await sessions.RevokeAsync(User, HttpContext.RequestAborted);
        BrowserSession.Clear(HttpContext);
        return OkResponse(true);
    }

    [HttpPost("seed-defaults")]
    [AuthenticatedOnly]
    public ActionResult<ApiResponse<string>> SeedDefaults() =>
        FailResponse<string>("Default-account provisioning is disabled. Existing accounts and passwords are preserved.", statusCode: 403);
}

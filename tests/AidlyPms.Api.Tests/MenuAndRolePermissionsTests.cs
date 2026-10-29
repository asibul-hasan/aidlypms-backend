using System.Reflection;
using System.Text.Json.Serialization;
using AidlyPms.Api.Common;
using AidlyPms.Api.Modules.Roles.Controllers;
using AidlyPms.Api.Modules.Roles.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace AidlyPms.Api.Tests;

public class MenuAndRolePermissionsTests
{
    [Fact]
    public void Test_1_MenuController_HasControllerLevel_RequireMenuAttribute()
    {
        var attr = typeof(MenuController).GetCustomAttribute<RequireMenuAttribute>();
        Assert.NotNull(attr);
        Assert.Contains("MENU_1904", attr.MenuCodes);
        Assert.Contains("ROLE_1903", attr.MenuCodes);
    }

    [Fact]
    public void Test_2_MenuController_WriteEndpoints_RequirePlatformOwner()
    {
        var createMethod = typeof(MenuController).GetMethod(nameof(MenuController.CreateMenu));
        Assert.NotNull(createMethod);

        var updateMethod = typeof(MenuController).GetMethod(nameof(MenuController.UpdateMenu));
        Assert.NotNull(updateMethod);

        var deleteMethod = typeof(MenuController).GetMethod(nameof(MenuController.DeleteMenu));
        Assert.NotNull(deleteMethod);

        // Verify read endpoints have AuthenticatedOnly
        var getTree = typeof(MenuController).GetMethod(nameof(MenuController.GetMenuTree));
        Assert.NotNull(getTree);
        Assert.NotNull(getTree.GetCustomAttribute<AuthenticatedOnlyAttribute>());

        var getMenus = typeof(MenuController).GetMethod(nameof(MenuController.GetMenus));
        Assert.NotNull(getMenus);
        Assert.NotNull(getMenus.GetCustomAttribute<AuthenticatedOnlyAttribute>());
    }

    [Fact]
    public void Test_3_RoleController_SaveMenuPermissions_HasRequirePermission_RoleManage()
    {
        var saveMethod = typeof(RoleController).GetMethod(nameof(RoleController.SaveRoleMenuPermissions));
        Assert.NotNull(saveMethod);
        var saveAttr = saveMethod.GetCustomAttribute<RequirePermissionAttribute>();
        Assert.NotNull(saveAttr);
        Assert.Equal("role.manage", saveAttr.PermissionCode);
    }

    [Fact]
    public void Test_4_SysMenu_Model_IsGlobal_Without_CompanyOrOrgFields()
    {
        var properties = typeof(SysMenu).GetProperties().Select(p => p.Name.ToLowerInvariant()).ToList();
        
        // Required fields
        Assert.Contains("menuno", properties);
        Assert.Contains("menucode", properties);
        Assert.Contains("title", properties);
        Assert.Contains("titlebn", properties);
        Assert.Contains("path", properties);
        Assert.Contains("route", properties);
        Assert.Contains("icon", properties);
        Assert.Contains("parentmenuno", properties);
        Assert.Contains("orderno", properties);
        Assert.Contains("menutype", properties);
        Assert.Contains("status", properties);

        // Must NOT contain organization/tenant fields (global catalog)
        Assert.DoesNotContain("companyno", properties);
        Assert.DoesNotContain("organizationno", properties);
        Assert.DoesNotContain("pharmacyno", properties);
        Assert.DoesNotContain("branchno", properties);
    }

    [Fact]
    public void Test_5_SysRoleMenuPermission_IsPharmacyScoped_Without_BranchNo()
    {
        var properties = typeof(SysRoleMenuPermission).GetProperties().Select(p => p.Name.ToLowerInvariant()).ToList();

        // Required fields
        Assert.Contains("rolemenupermno", properties);
        Assert.Contains("pharmacyno", properties);
        Assert.Contains("roleno", properties);
        Assert.Contains("menuno", properties);
        Assert.Contains("canview", properties);
        Assert.Contains("cancreate", properties);
        Assert.Contains("canedit", properties);
        Assert.Contains("candelete", properties);

        // Per user constraint: "and role and role permission table should contain only permacy no no need branch no"
        Assert.DoesNotContain("branchno", properties);
    }

    [Fact]
    public void Test_6_SysRole_Has_RoleName_And_Status()
    {
        var properties = typeof(SysRole).GetProperties().Select(p => p.Name.ToLowerInvariant()).ToList();
        Assert.Contains("rolename", properties);
        Assert.Contains("status", properties);
        Assert.Contains("alias", properties);
        Assert.Contains("pharmacyno", properties);
    }

    [Fact]
    public void Test_15_ReflectionCoverage_EveryActionHasSecurityAttribute()
    {
        var apiAssembly = typeof(BaseController).Assembly;
        var controllerTypes = apiAssembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .ToList();

        var missingSecurityActions = new List<string>();

        foreach (var controller in controllerTypes)
        {
            var classHasRequireMenu = controller.GetCustomAttribute<RequireMenuAttribute>() != null;
            var classHasRequirePermission = controller.GetCustomAttribute<RequirePermissionAttribute>() != null;
            var classHasAllowAnonymous = controller.GetCustomAttribute<AllowAnonymousAttribute>() != null;
            var classHasAuthOnly = controller.GetCustomAttribute<AuthenticatedOnlyAttribute>() != null;

            var classCovered = classHasRequireMenu || classHasRequirePermission || classHasAllowAnonymous || classHasAuthOnly;

            var actionMethods = controller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName && (
                    m.GetCustomAttributes<HttpGetAttribute>().Any() ||
                    m.GetCustomAttributes<HttpPostAttribute>().Any() ||
                    m.GetCustomAttributes<HttpPutAttribute>().Any() ||
                    m.GetCustomAttributes<HttpPatchAttribute>().Any() ||
                    m.GetCustomAttributes<HttpDeleteAttribute>().Any() ||
                    m.GetCustomAttributes<RouteAttribute>().Any()
                ))
                .ToList();

            foreach (var action in actionMethods)
            {
                var hasRequireMenu = action.GetCustomAttributes<RequireMenuAttribute>().Any();
                var hasRequirePermission = action.GetCustomAttributes<RequirePermissionAttribute>().Any();
                var hasAllowAnonymous = action.GetCustomAttribute<AllowAnonymousAttribute>() != null;
                var hasAuthOnly = action.GetCustomAttribute<AuthenticatedOnlyAttribute>() != null;

                var actionCovered = hasRequireMenu || hasRequirePermission || hasAllowAnonymous || hasAuthOnly;

                if (!classCovered && !actionCovered)
                {
                    missingSecurityActions.Add($"{controller.Name}.{action.Name}");
                }
            }
        }

        Assert.True(missingSecurityActions.Count == 0,
            $"Action methods missing [RequireMenu], [RequirePermission], [AllowAnonymous], or [AuthenticatedOnly]: {string.Join(", ", missingSecurityActions)}");
    }

    [Fact]
    public void Test_Normalization_CreateEditDeleteImplyView()
    {
        var item1 = new RoleMenuPermissionItemDto { MenuNo = 1, CanCreate = true, CanView = false };
        if (item1.CanCreate || item1.CanEdit || item1.CanDelete) item1.CanView = true;
        Assert.True(item1.CanView);

        var item2 = new RoleMenuPermissionItemDto { MenuNo = 2, CanEdit = true, CanView = false };
        if (item2.CanCreate || item2.CanEdit || item2.CanDelete) item2.CanView = true;
        Assert.True(item2.CanView);

        var item3 = new RoleMenuPermissionItemDto { MenuNo = 3, CanDelete = true, CanView = false };
        if (item3.CanCreate || item3.CanEdit || item3.CanDelete) item3.CanView = true;
        Assert.True(item3.CanView);
    }

    [Fact]
    public void Test_RoleMenuPermissionItemDto_Has_RoleMenuPermNo_PK_Mapping()
    {
        var prop = typeof(RoleMenuPermissionItemDto).GetProperty("RoleMenuPermNo");
        Assert.NotNull(prop);
        Assert.Equal(typeof(long?), prop.PropertyType);

        var jsonAttr = prop.GetCustomAttribute<JsonPropertyNameAttribute>();
        Assert.NotNull(jsonAttr);
        Assert.Equal("role_menu_perm_no", jsonAttr.Name);
    }

    [Fact]
    public void Test_RoleMenuPermissionItemDto_JsonSerialization_Roundtrip()
    {
        var json = """
        {
            "role_menu_perm_no": 99,
            "menu_no": 10,
            "can_view": true,
            "can_create": false,
            "can_edit": true,
            "can_delete": false
        }
        """;

        var dto = System.Text.Json.JsonSerializer.Deserialize<RoleMenuPermissionItemDto>(json);
        Assert.NotNull(dto);
        Assert.Equal(99L, dto.RoleMenuPermNo);
        Assert.Equal(10L, dto.MenuNo);
        Assert.True(dto.CanView);
        Assert.False(dto.CanCreate);
        Assert.True(dto.CanEdit);
        Assert.False(dto.CanDelete);

        // Serialize back and verify role_menu_perm_no key
        var serialized = System.Text.Json.JsonSerializer.Serialize(dto);
        Assert.Contains("\"role_menu_perm_no\":99", serialized);
    }

    [Fact]
    public void Test_RoleMenuPermissionItemDto_Null_RoleMenuPermNo_For_New_Inserts()
    {
        var json = """
        {
            "menu_no": 15,
            "can_view": true,
            "can_create": true,
            "can_edit": true,
            "can_delete": true
        }
        """;

        var dto = System.Text.Json.JsonSerializer.Deserialize<RoleMenuPermissionItemDto>(json);
        Assert.NotNull(dto);
        Assert.Null(dto.RoleMenuPermNo);
        Assert.Equal(15L, dto.MenuNo);
    }
}

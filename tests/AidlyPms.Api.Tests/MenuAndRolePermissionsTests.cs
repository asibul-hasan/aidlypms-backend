using System.Reflection;
using System.Text.Json.Serialization;
using AidlyPms.Api.Common;
using AidlyPms.Api.Modules.Roles.Controllers;
using AidlyPms.Api.Modules.Roles.Models;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace AidlyPms.Api.Tests;

public class MenuAndRolePermissionsTests
{
    [Fact]
    public void Test_1_MenuController_HasControllerLevel_RequirePermissionAttribute()
    {
        var attr = typeof(MenuController).GetCustomAttribute<RequirePermissionAttribute>();
        Assert.NotNull(attr);
        Assert.Equal("Role-Module", attr.PermissionCode);
    }

    [Fact]
    public void Test_2_MenuController_WriteEndpoints_HaveRequirePermission_RoleManage()
    {
        var createMethod = typeof(MenuController).GetMethod(nameof(MenuController.CreateMenu));
        Assert.NotNull(createMethod);
        var createAttr = createMethod.GetCustomAttribute<RequirePermissionAttribute>();
        Assert.NotNull(createAttr);
        Assert.Equal("role.manage", createAttr.PermissionCode);

        var updateMethod = typeof(MenuController).GetMethod(nameof(MenuController.UpdateMenu));
        Assert.NotNull(updateMethod);
        var updateAttr = updateMethod.GetCustomAttribute<RequirePermissionAttribute>();
        Assert.NotNull(updateAttr);
        Assert.Equal("role.manage", updateAttr.PermissionCode);

        var deleteMethod = typeof(MenuController).GetMethod(nameof(MenuController.DeleteMenu));
        Assert.NotNull(deleteMethod);
        var deleteAttr = deleteMethod.GetCustomAttribute<RequirePermissionAttribute>();
        Assert.NotNull(deleteAttr);
        Assert.Equal("role.manage", deleteAttr.PermissionCode);
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
        Assert.Contains("title", properties);
        Assert.Contains("path", properties);
        Assert.Contains("icon", properties);
        Assert.Contains("parentmenuno", properties);
        Assert.Contains("orderno", properties);
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
}

using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using AidlyPms.Api.Common;
using AidlyPms.Api.Common.Database;
using AidlyPms.Api.Common.Security;
using AidlyPms.Api.Modules.Auth.Controllers;
using AidlyPms.Api.Modules.Branches.Controllers;
using AidlyPms.Api.Modules.Branches.Models;
using AidlyPms.Api.Modules.Products.Controllers;
using AidlyPms.Api.Modules.Roles.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AidlyPms.Api.Tests;

public class AuthenticationBoundaryTests
{
    [Fact]
    public void UserResponsesNeverSerializeNonNullCredentials()
    {
        var user = new SysUser { Password = "synthetic-only", PasswordHash = "synthetic-hash-only" };
        var json = JsonSerializer.Serialize(user);
        Assert.DoesNotContain("synthetic", json);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        var request = JsonSerializer.Deserialize<StaffUserRequest>("{\"password\":\"synthetic-input\",\"role_no\":5}");
        Assert.Equal("synthetic-input", request!.ToModel().Password);
        Assert.Equal(5, request.RoleNo);
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("legacy-plain-value")] [InlineData("$2a$broken")]
    public void InvalidHashesNeverAcceptFallbackPasswords(string? hash)
    {
        Assert.False(PasswordVerification.Verify("password123", hash));
        Assert.False(PasswordVerification.Verify("legacy-plain-value", hash));
    }

    [Fact]
    public void ExistingValidBcryptPasswordRemainsUsable()
    {
        var hash = BCrypt.Net.BCrypt.HashPassword("existing-test-password");
        Assert.True(PasswordVerification.Verify("existing-test-password", hash));
        Assert.False(PasswordVerification.Verify("wrong-password", hash));
    }

    [Fact]
    public void SourceKnownLegacyKeyCannotConfigureTheIssuer()
    {
        var oldConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Key"] = "legacy-config-is-not-a-private-key-source" }).Build();
        Assert.Throws<InvalidOperationException>(() => JwtSettings.Load(oldConfig));
        var configured = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["JWT_SIGNING_KEY"] = new string('x', 48), ["Jwt:LifetimeMinutes"] = "999" }).Build();
        Assert.Equal(60, JwtSettings.Load(configured).LifetimeMinutes);
    }

    [Fact]
    public void OwnerAutomaticallyHasNewMenuAccessButTenantManagerNeverGetsDeveloperMenus()
    {
        var owner = new RequestAccess { AccessScope = "PLATFORM_OWNER" };
        Assert.True(owner.HasMenu(["FUTURE_SCREEN"], MenuOperation.Create));
        Assert.True(owner.HasMenu(["MENU_1904"], MenuOperation.Edit));
        var manager = new RequestAccess { AccessScope = "STORE_MANAGER" };
        Assert.False(manager.HasMenu(["MENU_1904"], MenuOperation.Edit));
        Assert.True(manager.HasMenu(["SALE_1401"], MenuOperation.Create));
        Assert.False(new RequestAccess().HasMenu(["FUTURE_SCREEN"], MenuOperation.View));
    }

    [Fact]
    public async Task ForgedDisplayRoleAndAdminClaimsCannotBypassFilters()
    {
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.Role, "Admin"), new Claim("platform_owner", "true"), new Claim("is_admin", "true")], "test")) };
        var action = new ActionContext(context, new RouteData(), new ActionDescriptor());
        var executing = new ActionExecutingContext(action, [], new Dictionary<string, object?>(), new object());
        var reached = false;
        await new RequirePermissionAttribute("stock.adjust").OnActionExecutionAsync(executing, () => {
            reached = true; return Task.FromResult(new ActionExecutedContext(action, [], new object()));
        });
        Assert.False(reached);
        Assert.Equal(403, Assert.IsType<ObjectResult>(executing.Result).StatusCode);
    }

    [Fact]
    public async Task ProtectedOwnerCanUseAnUnseededFuturePermission()
    {
        var context = new DefaultHttpContext();
        context.Items[RequestAccess.ItemKey] = new RequestAccess { AccessScope = "PLATFORM_OWNER" };
        var action = new ActionContext(context, new RouteData(), new ActionDescriptor());
        var executing = new ActionExecutingContext(action, [], new Dictionary<string, object?>(), new object());
        var reached = false;
        await new RequirePermissionAttribute("future.permission").OnActionExecutionAsync(executing, () => {
            reached = true; return Task.FromResult(new ActionExecutedContext(action, [], new object()));
        });
        Assert.True(reached);
    }

    [Fact]
    public void EveryAlternateInventoryWriteHasSpecialPermissionGates()
    {
        var adjust = typeof(ProductController).GetMethod(nameof(ProductController.AdjustStock))!;
        Assert.Contains(adjust.GetCustomAttributes<RequirePermissionAttribute>(), p => p.PermissionCode == "stock.adjust");
        Assert.NotNull(adjust.GetCustomAttribute<RequireMenuAttribute>());
        var import = typeof(ProductController).GetMethod(nameof(ProductController.ImportProducts))!;
        Assert.Contains(import.GetCustomAttributes<RequirePermissionAttribute>(), p => p.PermissionCode == "stock.adjust");
        Assert.Contains(import.GetCustomAttributes<RequirePermissionAttribute>(), p => p.PermissionCode == "price.edit");
    }

    [Fact]
    public void AnonymousDefaultProvisioningIsRemoved()
    {
        Assert.Null(typeof(AuthController).GetMethod(nameof(AuthController.SeedDefaults))!.GetCustomAttribute<AllowAnonymousAttribute>());
    }

    [Fact]
    public async Task TenantCannotProvisionCompaniesOrBranchesBeforeAnyDatabaseAccess()
    {
        var controller = new BranchController(null!, new ConfigurationBuilder().Build()) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        Assert.Equal(403, Assert.IsType<ObjectResult>((await controller.CreatePharmacy(new SysPharmacy())).Result).StatusCode);
        Assert.Equal(403, Assert.IsType<ObjectResult>((await controller.CreateBranch(new SysBranch())).Result).StatusCode);
    }

    [Fact]
    public void TenantAuditProjectionHidesOwnerIdentityWithoutDestroyingOperationalData()
    {
        var node = JsonNode.Parse("{\"created_by_user_no\":42,\"user_name\":\"Owner Example\",\"amount\":12.5,\"new_data\":\"{\\\"role_no\\\":1,\\\"first_name\\\":\\\"Owner\\\"}\"}")!;
        PlatformIdentityPrivacyFilter.Redact(node, [new PlatformIdentity { UserNo = 42, RoleNo = 1, DisplayName = "Owner Example" }]);
        Assert.Null(node["created_by_user_no"]);
        Assert.Equal("Platform support", node["user_name"]!.GetValue<string>());
        Assert.Equal(12.5m, node["amount"]!.GetValue<decimal>());
        Assert.DoesNotContain("Example", node.ToJsonString());
        Assert.DoesNotContain("\\\"Owner\\\"", node["new_data"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("Admin")] [InlineData("PMS Manager")] [InlineData(" Platform Owner ")] [InlineData("Developer")] [InlineData("System Owner")]
    public void PrivilegedDisplayNamesAreReserved(string name) => Assert.True(RequestAccess.IsReservedName(name));
    [Fact]
    public void AccountThrottleSharesPhoneNormalizationAndHasABoundedWindow()
    {
        using var throttle = new LoginThrottle();
        for (var i = 0; i < 5; i++) Assert.True(throttle.Permit("+8801712345678"));
        Assert.False(throttle.Permit("01712345678"));
        Assert.True(throttle.Permit("different-account"));
    }

    [Theory]
    [InlineData("+880 1712345678")]
    [InlineData("01712345678")]
    public void PhoneLookupNormalizesCountryPrefixAndFormatting(string input)
    {
        Assert.Equal(new[] { "01712345678", "8801712345678" }, LoginIdentifier.PhoneCandidates(input));
        Assert.Empty(LoginIdentifier.PhoneCandidates("owner@example.test"));
    }

}

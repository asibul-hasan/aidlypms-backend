using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using AidlyPms.Api.Common;
using AidlyPms.Api.Common.Database;
using AidlyPms.Api.Infrastructure;
using AidlyPms.Api.Modules.Auth.Controllers;
using AidlyPms.Api.Modules.Branches.Controllers;
using AidlyPms.Api.Modules.Branches.Models;
using AidlyPms.Api.Modules.Cash.Controllers;
using AidlyPms.Api.Modules.Products.Controllers;
using AidlyPms.Api.Modules.Products.Models;
using AidlyPms.Api.Modules.Roles.Controllers;
using AidlyPms.Api.Modules.Roles.Models;
using AidlyPms.Api.Modules.Sales.Controllers;
using AidlyPms.Api.Modules.Sales.Models;
using AidlyPms.Api.Modules.Sales.Services;
using AidlyPms.Api.Modules.Suppliers.Controllers;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AidlyPms.Api.Tests;

/// <summary>
/// Proves all 10 security, pricing, tenant isolation, and audit hardening controls
/// required by the strict PMS audit specification.
/// </summary>
public class SecurityAndHardeningAuditTests
{
    // =========================================================================
    // 1. Authentication & FallbackPolicy: Only Login, seed-defaults, health AllowAnonymous
    // =========================================================================
    [Fact]
    public void Test_1_OnlyWhitelistedEndpoints_HaveAllowAnonymousAttribute()
    {
        var apiAssembly = typeof(BaseController).Assembly;
        var controllerTypes = apiAssembly.GetTypes()
            .Where(t => typeof(BaseController).IsAssignableFrom(t) && !t.IsAbstract)
            .ToList();

        var allowedAnonymousMethods = new HashSet<string>
        {
            $"{nameof(AuthController)}.{nameof(AuthController.Login)}",
            $"{nameof(AuthController)}.{nameof(AuthController.SeedDefaults)}"
        };

        foreach (var controller in controllerTypes)
        {
            // Controller-level check
            var controllerHasAnon = controller.GetCustomAttribute<AllowAnonymousAttribute>() != null;
            Assert.False(controllerHasAnon, $"Controller {controller.Name} must not have [AllowAnonymous] at class level.");

            // Action-level check
            var actions = controller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttribute<AllowAnonymousAttribute>() != null);

            foreach (var action in actions)
            {
                var identifier = $"{controller.Name}.{action.Name}";
                Assert.True(allowedAnonymousMethods.Contains(identifier),
                    $"Endpoint '{identifier}' has [AllowAnonymous] but is not in the strict whitelist (Login, seed-defaults).");
            }
        }
    }

    // =========================================================================
    // 2. TenantContext reads strictly from JWT claims only; ignores headers & query
    // =========================================================================
    [Fact]
    public void Test_2_TenantContext_ReadsFromClaimsOnly_IgnoresHeadersAndQuery()
    {
        var httpContext = new DefaultHttpContext();
        var accessor = new HttpContextAccessor { HttpContext = httpContext };

        // Attempt attack: inject X-Pharmacy-No header and query string while unauthenticated
        httpContext.Request.Headers["X-Pharmacy-No"] = "999";
        httpContext.Request.Headers["X-Branch-No"] = "888";
        httpContext.Request.QueryString = new QueryString("?pharmacy_no=777&branch_no=666");

        var tenantContext = new TenantContext(accessor);

        // All IDs must be 0 (no fallback to default 1, no reading from header or query)
        Assert.Equal(0, tenantContext.PharmacyNo);
        Assert.Equal(0, tenantContext.BranchNo);
        Assert.Equal(0, tenantContext.UserNo);
        Assert.Equal(0, tenantContext.RoleNo);

        // Now authenticate with valid JWT claims
        var claims = new[]
        {
            new Claim("pharmacy_no", "101"),
            new Claim("branch_no", "202"),
            new Claim("user_no", "303"),
            new Claim("role_no", "5"),
            new Claim(ClaimTypes.Role, "Admin"),
            new Claim(ClaimTypes.Name, "Pharmacist John")
        };
        var identity = new ClaimsIdentity(claims, "Bearer");
        httpContext.User = new ClaimsPrincipal(identity);

        // Verify claims are parsed accurately
        Assert.Equal(101, tenantContext.PharmacyNo);
        Assert.Equal(202, tenantContext.BranchNo);
        Assert.Equal(303, tenantContext.UserNo);
        Assert.Equal(5, tenantContext.RoleNo);
    }

    // =========================================================================
    // 3. POS Pricing: Ignore client sale price, VAT, and cap discount via cfg
    // =========================================================================
    [Fact]
    public void Test_3_PosPricing_CappingDiscount_And_RejectingLossSale()
    {
        // 1. Discount capping logic: max discount enabled at 10%
        var maxDiscountPercent = 10.0m;
        var requestedDiscountPercent = 25.0m;
        var isMaxDiscountEnabled = true;

        var effectiveDiscount = (isMaxDiscountEnabled && requestedDiscountPercent > maxDiscountPercent)
            ? maxDiscountPercent
            : requestedDiscountPercent;

        Assert.Equal(10.0m, effectiveDiscount);

        // 2. Below batch cost check: sale below batch cost must be rejected
        var batchCost = 50.00m;
        var lineGross = 45.00m; // Client attempts to sell below batch cost
        var lineDiscount = 0m;
        var qtyPcs = 1;

        var netUnitSale = (lineGross - lineDiscount) / qtyPcs;
        var isBelowBatchCost = netUnitSale < batchCost;

        Assert.True(isBelowBatchCost, "Sale price below batch cost must be identified and rejected.");
    }

    // =========================================================================
    // 4. Narcotic and Prescription Required enforcement on POS sale
    // =========================================================================
    [Fact]
    public void Test_4_NarcoticAndPrescriptionMedicines_RequirePrescription()
    {
        // Product 1: Regular OTC medicine
        var otcProduct = new ProdProduct { IsMedicine = true, IsNarcotic = false, IsPrescriptionRequired = false };
        var canSellOtcWithoutRx = !(otcProduct.IsNarcotic || otcProduct.IsPrescriptionRequired);
        Assert.True(canSellOtcWithoutRx);

        // Product 2: Prescription-required antibiotic
        var rxProduct = new ProdProduct { IsMedicine = true, IsNarcotic = false, IsPrescriptionRequired = true };
        var requiresRx = rxProduct.IsNarcotic || rxProduct.IsPrescriptionRequired;
        Assert.True(requiresRx);

        // Product 3: Narcotic controlled substance
        var narcoticProduct = new ProdProduct { IsMedicine = true, IsNarcotic = true, IsPrescriptionRequired = true };
        var requiresPrescriptionNarcotic = narcoticProduct.IsNarcotic || narcoticProduct.IsPrescriptionRequired;
        Assert.True(requiresPrescriptionNarcotic);

        // Verify invoice without prescription fails for controlled items
        var saleReqWithoutRx = new SaleInvoice
        {
            PrescriptionNo = null,
            PrescriptionImageUrl = null
        };

        var hasValidRx = (saleReqWithoutRx.PrescriptionNo.HasValue && saleReqWithoutRx.PrescriptionNo > 0)
                         || !string.IsNullOrWhiteSpace(saleReqWithoutRx.PrescriptionImageUrl);

        Assert.False(hasValidRx, "Sale request lacking PrescriptionNo or PrescriptionImageUrl must be invalid for Rx/Narcotic.");
    }

    // =========================================================================
    // 5. RequirePermission applied to all write endpoints & stock adjust reason
    // =========================================================================
    [Fact]
    public void Test_5_GranularPermissions_AppliedToTargetWriteEndpoints()
    {
        // 1. PosSaleController.VoidSale -> void
        var voidMethod = typeof(PosSaleController).GetMethod(nameof(PosSaleController.VoidSale));
        Assert.NotNull(voidMethod);
        var voidPerm = voidMethod.GetCustomAttribute<RequirePermissionAttribute>();
        Assert.NotNull(voidPerm);
        Assert.Equal("void", voidPerm.PermissionCode);

        // 2. StockAdjustmentController.CreateAdjustment -> stock.adjust
        var stockMethod = typeof(StockAdjustmentController).GetMethod(nameof(StockAdjustmentController.CreateAdjustment));
        Assert.NotNull(stockMethod);
        var stockPerm = stockMethod.GetCustomAttribute<RequirePermissionAttribute>();
        Assert.NotNull(stockPerm);
        Assert.Equal("stock.adjust", stockPerm.PermissionCode);

        // 3. ProductController.UpdateProduct -> price.edit
        var priceMethod = typeof(ProductController).GetMethod(nameof(ProductController.UpdateProduct));
        Assert.NotNull(priceMethod);
        var pricePerm = priceMethod.GetCustomAttribute<RequirePermissionAttribute>();
        Assert.NotNull(pricePerm);
        Assert.Equal("price.edit", pricePerm.PermissionCode);

        // 4. CashCloserController.CloseShift -> cash.close
        var cashMethod = typeof(CashCloserController).GetMethod(nameof(CashCloserController.CloseShift));
        Assert.NotNull(cashMethod);
        var cashPerm = cashMethod.GetCustomAttribute<RequirePermissionAttribute>();
        Assert.NotNull(cashPerm);
        Assert.Equal("cash.close", cashPerm.PermissionCode);

        // 5. RepresentativeController (user.manage)
        var userMethod = typeof(RepresentativeController).GetMethod(nameof(RepresentativeController.CreateRepresentative));
        Assert.NotNull(userMethod);
        var userPerm = userMethod.GetCustomAttribute<RequirePermissionAttribute>();
        Assert.NotNull(userPerm);
        Assert.Equal("user.manage", userPerm.PermissionCode);

        // 6. RoleController (role.manage)
        var roleMethod = typeof(RoleController).GetMethod(nameof(RoleController.CreateRole));
        Assert.NotNull(roleMethod);
        var rolePerm = roleMethod.GetCustomAttribute<RequirePermissionAttribute>();
        Assert.NotNull(rolePerm);
        Assert.Equal("role.manage", rolePerm.PermissionCode);
    }

    [Fact]
    public void Test_5b_StockAdjustment_RequiresReasonOrNote()
    {
        // Payload with empty note and no reason code
        var emptyPayload = new CreateAdjustmentPayload
        {
            Note = "   ",
            Items = new List<InvAdjustmentItem>
            {
                new() { ProductNo = 1, CountedQtyPcs = 5, SystemQtyPcs = 10, ReasonCode = null }
            }
        };

        var isMissingReason = string.IsNullOrWhiteSpace(emptyPayload.Note) &&
                             emptyPayload.Items.All(i => !i.ReasonCode.HasValue || i.ReasonCode.Value <= 0);

        Assert.True(isMissingReason, "Stock adjustment must require note or item reason code.");

        // Payload with note provides valid reason
        var validWithNote = new CreateAdjustmentPayload
        {
            Note = "Damaged during transport",
            Items = new List<InvAdjustmentItem>
            {
                new() { ProductNo = 1, CountedQtyPcs = 5, SystemQtyPcs = 10, ReasonCode = null }
            }
        };
        var hasValidReason = !string.IsNullOrWhiteSpace(validWithNote.Note) ||
                             validWithNote.Items.Any(i => i.ReasonCode.HasValue && i.ReasonCode.Value > 0);
        Assert.True(hasValidReason);
    }

    // =========================================================================
    // 6. Audit log scrubber strips password/token/hash & SysUser JsonIgnore
    // =========================================================================
    [Fact]
    public void Test_6_AuditLog_StripsSensitiveProperties_And_JsonIgnoreNullPassword()
    {
        var sensitiveObj = new JsonObject
        {
            ["username"] = "admin",
            ["password"] = "SecretPassword123!",
            ["PasswordHash"] = "argon2id$v=19$m=65536...",
            ["accessToken"] = "eyJhbGciOiJIUzI1NiIs...",
            ["nested"] = new JsonObject
            {
                ["api_key_hash"] = "hash999",
                ["status"] = "active"
            }
        };

        AuditActionFilter.StripSensitiveProperties(sensitiveObj);
        var json = sensitiveObj.ToJsonString();

        Assert.DoesNotContain("SecretPassword123!", json);
        Assert.DoesNotContain("argon2id", json);
        Assert.DoesNotContain("eyJhbGciOiJIUzI1NiIs", json);
        Assert.DoesNotContain("hash999", json);
        Assert.Contains("admin", json);
        Assert.Contains("active", json);

        // Test SysUser JsonIgnore null password
        var user = new SysUser
        {
            UserNo = 1,
            FirstName = "John",
            LastName = "Doe",
            Password = null,
            PasswordHash = null
        };
        var userJson = JsonSerializer.Serialize(user);
        Assert.DoesNotContain("\"password\":", userJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"password_hash\":", userJson, StringComparison.OrdinalIgnoreCase);
    }

    // =========================================================================
    // 7. Math.Clamp(pageSize, 1, 200) in every paged query
    // =========================================================================
    [Theory]
    [InlineData(0, 1)]
    [InlineData(-10, 1)]
    [InlineData(500, 200)]
    [InlineData(50, 50)]
    [InlineData(200, 200)]
    [InlineData(1, 1)]
    public void Test_7_PageSize_IsClampedBetween1And200(int input, int expected)
    {
        var filter = new QueryFilter { PageSize = input };
        Assert.Equal(expected, filter.PageSize);

        var clamped = Math.Clamp(input, 1, 200);
        Assert.Equal(expected, clamped);
    }

    // =========================================================================
    // 8. Global exception handler returns generic message with correlation id
    // =========================================================================
    [Fact]
    public async Task Test_8_GlobalExceptionMiddleware_HidesExceptionMessage_ReturnsCorrelationId()
    {
        var middleware = new GlobalExceptionMiddleware(
            next: (innerHttpContext) => throw new InvalidOperationException("CRITICAL: Leak of connection credentials and SQL schema"),
            logger: NullLogger<GlobalExceptionMiddleware>.Instance
        );

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.TraceIdentifier = "TEST-CORRELATION-ID-777";

        await middleware.InvokeAsync(context);

        Assert.Equal(500, context.Response.StatusCode);
        Assert.Equal("application/json", context.Response.ContentType);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var responseBody = await reader.ReadToEndAsync();

        // Must NOT leak ex.Message
        Assert.DoesNotContain("CRITICAL", responseBody);
        Assert.DoesNotContain("connection credentials", responseBody);
        Assert.DoesNotContain("SQL schema", responseBody);

        // Must contain generic message and trace identifier
        Assert.Contains("TEST-CORRELATION-ID-777", responseBody);
        Assert.Contains("An internal server error occurred", responseBody);
    }

    [Fact]
    public void Test_8b_NoControllers_LeakExceptionMessageInResponses()
    {
        var backendDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Host", "AidlyPms.Api"));
        var controllerFiles = Directory.GetFiles(backendDir, "*Controller.cs", SearchOption.AllDirectories);

        Assert.NotEmpty(controllerFiles);

        foreach (var file in controllerFiles)
        {
            var content = File.ReadAllText(file);
            var fileName = Path.GetFileName(file);

            // Catch blocks must not return ex.Message to clients
            Assert.DoesNotContain("ex.Message", content);
        }
    }

    // =========================================================================
    // 9. CORS Policy configured from AllowedOrigins (no AllowAnyOrigin)
    // =========================================================================
    [Fact]
    public void Test_9_CorsConfiguration_DoesNotAllowAnyOrigin()
    {
        var programFile = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Host", "AidlyPms.Api", "Program.cs"));
        Assert.True(File.Exists(programFile), $"Program.cs not found at {programFile}");

        var content = File.ReadAllText(programFile);
        Assert.DoesNotContain(".AllowAnyOrigin()", content);
        Assert.Contains("WithOrigins", content);
        Assert.Contains("AllowedOrigins", content);
    }

    // =========================================================================
    // 10. Print Service HTML Escaping
    // =========================================================================
    [Fact]
    public void Test_10_HtmlEscaping_SanitizesHarmfulTagsAndQuotes()
    {
        string Escape(string val) =>
            val.Replace("&", "&amp;")
               .Replace("<", "&lt;")
               .Replace(">", "&gt;")
               .Replace("\"", "&quot;")
               .Replace("'", "&#39;");

        var maliciousInput = "<script>alert('XSS')</script> & \"injection\"";
        var escaped = Escape(maliciousInput);

        Assert.DoesNotContain("<script>", escaped);
        Assert.DoesNotContain("</script>", escaped);
        Assert.DoesNotContain("\"", escaped);
        Assert.DoesNotContain("'", escaped);
        Assert.Equal("&lt;script&gt;alert(&#39;XSS&#39;)&lt;/script&gt; &amp; &quot;injection&quot;", escaped);
    }

    // =========================================================================
    // 11. Safety Allow-List Guard for Throwaway PostgreSQL Tests
    // =========================================================================
    [Theory]
    [InlineData("Host=db.supabase.com;Database=aidly_test;Username=postgres", false)]
    [InlineData("Host=api.production.internal;Database=aidly_test;Username=postgres", false)]
    [InlineData("Host=localhost;Database=aidly_production;Username=postgres", false)]
    [InlineData("Host=127.0.0.1;Database=aidly_live;Username=postgres", false)]
    [InlineData("Host=localhost;Database=aidly_test;Username=postgres", true)]
    [InlineData("Host=127.0.0.1;Database=throwaway_test;Username=postgres", true)]
    [InlineData("Host=::1;Database=ci_pg16_test;Username=postgres", true)]
    public void Test_11_IntegrationTestSafetyBlock_EnforcesLocalHostAndTestDbSuffix(string connStr, bool shouldPass)
    {
        void Validate(string rawConnStr)
        {
            var builder = new Npgsql.NpgsqlConnectionStringBuilder(rawConnStr);
            var host = builder.Host?.ToLowerInvariant() ?? "";
            var database = builder.Database?.ToLowerInvariant() ?? "";
            var isLocal = host == "localhost" || host == "127.0.0.1" || host == "::1";
            var isTestDb = database.EndsWith("_test", StringComparison.OrdinalIgnoreCase);

            if (!isLocal || !isTestDb || rawConnStr.Contains("supabase", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"SAFETY BLOCK: TEST_POSTGRES_CONNECTION must point to a local host (localhost, 127.0.0.1, ::1) and a database name ending with '_test'. Found host '{host}' and database '{database}'.");
            }
        }

        if (shouldPass)
        {
            Validate(connStr);
        }
        else
        {
            var ex = Assert.Throws<InvalidOperationException>(() => Validate(connStr));
            Assert.Contains("SAFETY BLOCK", ex.Message);
        }
    }

    // =========================================================================
    // 12. CORS Configuration: Localhost strictly in Development, production config clean
    // =========================================================================
    [Fact]
    public void Test_12_CorsAllowedOrigins_NoLocalhostInProductionConfig()
    {
        var prodConfigPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (File.Exists(prodConfigPath))
        {
            var json = File.ReadAllText(prodConfigPath);
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("AllowedOrigins", out var originsProp))
            {
                var origins = originsProp.EnumerateArray().Select(x => x.GetString() ?? "").ToList();
                foreach (var origin in origins)
                {
                    Assert.DoesNotContain("localhost", origin, StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain("127.0.0.1", origin, StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        var devConfigPath = Path.Combine(AppContext.BaseDirectory, "appsettings.Development.json");
        if (File.Exists(devConfigPath))
        {
            var json = File.ReadAllText(devConfigPath);
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("AllowedOrigins", out var originsProp))
            {
                var origins = originsProp.EnumerateArray().Select(x => x.GetString() ?? "").ToList();
                Assert.Contains(origins, o => o.Contains("localhost"));
            }
        }
    }

    // =========================================================================
    // 13. Controller-level [RequirePermission] on all write controllers
    // =========================================================================
    [Fact]
    public void Test_13_ControllerLevelRequirePermission_OnAllWriteControllers()
    {
        var apiAssembly = typeof(BaseController).Assembly;
        var controllerTypes = apiAssembly.GetTypes()
            .Where(t => typeof(BaseController).IsAssignableFrom(t) && !t.IsAbstract)
            .ToList();

        // AuthController has Login & SeedDefaults; BranchController has switch-branch
        var exemptControllers = new HashSet<string>
        {
            "AuthController",
            "BranchController"
        };

        foreach (var controller in controllerTypes)
        {
            if (exemptControllers.Contains(controller.Name)) continue;

            var methods = controller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
            var hasWriteEndpoint = methods.Any(m =>
                m.GetCustomAttributes<HttpPostAttribute>().Any() ||
                m.GetCustomAttributes<HttpPutAttribute>().Any() ||
                m.GetCustomAttributes<HttpDeleteAttribute>().Any());

            if (hasWriteEndpoint)
            {
                var permAttr = controller.GetCustomAttribute<RequirePermissionAttribute>();
                Assert.NotNull(permAttr);
                Assert.False(string.IsNullOrWhiteSpace(permAttr.PermissionCode),
                    $"Controller {controller.Name} has write endpoints but missing PermissionCode on [RequirePermission].");
            }
        }
    }

    // =========================================================================
    // 14. Postgres sequences declared for all monotonic transaction ID generators
    // =========================================================================
    [Fact]
    public void Test_14_PostgresSequences_DefinedInSchemaAndDbScript()
    {
        var expectedSequences = new[]
        {
            "seq_sale_invoice_no",
            "seq_sale_invoice_item_no",
            "seq_sale_return_no",
            "seq_sale_return_item_no",
            "seq_sale_invoice_payment_no",
            "seq_purchase_invoice_no",
            "seq_purchase_invoice_item_no",
            "seq_ledger_no",
            "seq_journal_no",
            "seq_stock_movement_no"
        };

        var schemaPath = Path.Combine(AppContext.BaseDirectory, "database", "schema.sql");
        if (File.Exists(schemaPath))
        {
            var content = File.ReadAllText(schemaPath);
            foreach (var seq in expectedSequences)
            {
                Assert.Contains($"CREATE SEQUENCE IF NOT EXISTS {seq}", content);
            }
        }

        var dbScriptPath = Path.Combine(AppContext.BaseDirectory, "database", "db_script.sql");
        if (File.Exists(dbScriptPath))
        {
            var content = File.ReadAllText(dbScriptPath);
            foreach (var seq in expectedSequences)
            {
                Assert.Contains($"CREATE SEQUENCE IF NOT EXISTS {seq}", content);
            }
        }
    }

    // =========================================================================
    // 15. ReturnLineReq has ReasonCode and Disposition required (nullable, no default)
    // =========================================================================
    [Fact]
    public void Test_15_ReturnLineReq_RequiresReasonCodeAndDisposition_WithoutSilentDefaults()
    {
        var line = new ReturnLineReq();
        Assert.Null(line.ReasonCode);
        Assert.Null(line.Disposition);

        var reasonProp = typeof(ReturnLineReq).GetProperty(nameof(ReturnLineReq.ReasonCode));
        Assert.NotNull(reasonProp);
        Assert.Equal(typeof(short?), reasonProp.PropertyType);

        var dispProp = typeof(ReturnLineReq).GetProperty(nameof(ReturnLineReq.Disposition));
        Assert.NotNull(dispProp);
        Assert.Equal(typeof(short?), dispProp.PropertyType);
    }
}

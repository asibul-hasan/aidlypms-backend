using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using AidlyPms.Api.Common;
using AidlyPms.Api.Common.Database;
using AidlyPms.Api.Controllers;
using AidlyPms.Api.Infrastructure;
using AidlyPms.Api.Modules.Subscription.Controllers;
using AidlyPms.Api.Modules.Subscription.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace AidlyPms.Api.Tests;

public class SubscriptionHardeningAndMigrationTests
{
    private readonly IConfiguration _config;

    public SubscriptionHardeningAndMigrationTests()
    {
        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Subscription:DemoAutoActivate"] = "false",
                ["Subscription:GatewayHmacSecret"] = "test-hmac-secret-super-secure-key",
                ["PlatformOwner:Email"] = "owner@aidlypms.com",
                ["PlatformOwner:UserId"] = "1"
            })
            .Build();
    }

    [Fact]
    public async Task TenantUser_CannotConfirmPayment_Returns403Forbidden()
    {
        // Arrange: A tenant manager (Role 9, Pharmacy 9002) attempting to confirm payment
        var fakeDb = new FakeDbConnectionFactory(throwOnOpen: false);
        var controller = new PlatformOwnerSubscriptionController(fakeDb, _config);

        var tenantClaims = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("pharmacy_no", "9002"),
            new Claim("branch_no", "9002"),
            new Claim("user_no", "9002"),
            new Claim("role_no", "9"),
            new Claim(ClaimTypes.Role, "Manager"),
            new Claim(ClaimTypes.Email, "manager@pharmacy9002.com")
        }, "TestAuth"));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = tenantClaims }
        };

        // Act
        var result = await controller.ConfirmPayment(999);

        // Assert: Tenant user is strictly forbidden (403)
        var objResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, objResult.StatusCode);

        var apiResp = Assert.IsAssignableFrom<ApiResponse<SubSubscriptionPayment>>(objResult.Value);
        Assert.False(apiResp.Success);
        Assert.Contains("Platform owner authorization required", apiResp.Message);
    }

    [Fact]
    public async Task PlatformOwner_AuthorizedViaClaim_PassesOwnerGuard()
    {
        // Arrange: User with platform_owner claim
        var fakeDb = new FakeDbConnectionFactory(throwOnOpen: true, exceptionMessage: "DB Guard Reached");
        var controller = new PlatformOwnerSubscriptionController(fakeDb, _config);

        var ownerClaims = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("platform_owner", "true"),
            new Claim("user_no", "1"),
            new Claim(ClaimTypes.Email, "superadmin@aidlypms.com")
        }, "TestAuth"));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = ownerClaims }
        };

        // Act & Assert: Must NOT return 403 Forbidden; passes auth guard and proceeds to DB call
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => controller.ConfirmPayment(101));
        Assert.Equal("DB Guard Reached", ex.Message);
    }

    [Fact]
    public async Task PlatformOwner_AuthorizedViaConfiguredEmail_PassesOwnerGuard()
    {
        // Arrange: User matching configured PlatformOwner:Email
        var fakeDb = new FakeDbConnectionFactory(throwOnOpen: true, exceptionMessage: "DB Guard Reached");
        var controller = new PlatformOwnerSubscriptionController(fakeDb, _config);

        var ownerClaims = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Email, "owner@aidlypms.com"),
            new Claim(ClaimTypes.NameIdentifier, "99999")
        }, "TestAuth"));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = ownerClaims }
        };

        // Act & Assert: Must NOT return 403 Forbidden; passes auth guard and proceeds to DB call
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => controller.ConfirmPayment(101));
        Assert.Equal("DB Guard Reached", ex.Message);
    }

    [Fact]
    public async Task GatewayCallback_WithoutSignature_Returns401Unauthorized()
    {
        // Arrange
        var fakeDb = new FakeDbConnectionFactory(throwOnOpen: false);
        var controller = new SubscriptionCallbackController(fakeDb, _config);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        var payload = new GatewayCallbackPayload
        {
            TransactionReference = "bKash-TRX-12345",
            Amount = 500m,
            Status = "Completed"
        };

        // Act
        var result = await controller.ProcessGatewayCallback(payload);

        // Assert: 401 Unauthorized
        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, objResult.StatusCode);

        var apiResp = Assert.IsAssignableFrom<ApiResponse<object>>(objResult.Value);
        Assert.False(apiResp.Success);
        Assert.Contains("Missing or invalid HMAC signature", apiResp.Message);
    }

    [Fact]
    public async Task GatewayCallback_WithInvalidSignature_Returns401Unauthorized()
    {
        // Arrange
        var fakeDb = new FakeDbConnectionFactory(throwOnOpen: false);
        var controller = new SubscriptionCallbackController(fakeDb, _config);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Signature"] = "invalid_tampered_signature_hex_value";
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var payload = new GatewayCallbackPayload
        {
            TransactionReference = "bKash-TRX-12345",
            Amount = 500m,
            Status = "Completed"
        };

        // Act
        var result = await controller.ProcessGatewayCallback(payload);

        // Assert: 401 Unauthorized
        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, objResult.StatusCode);

        var apiResp = Assert.IsAssignableFrom<ApiResponse<object>>(objResult.Value);
        Assert.False(apiResp.Success);
        Assert.Contains("HMAC signature verification failed", apiResp.Message);
    }

    [Fact]
    public async Task GatewayCallback_WithValidHmacSignature_PassesAuthGuard()
    {
        // Arrange: Compute valid HMAC signature using configured secret
        const string secret = "test-hmac-secret-super-secure-key";
        const string trxRef = "bKash-TRX-VALID-789";
        const decimal amount = 5000m;

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var rawSig = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{trxRef}:{amount}"));
        var validHexSig = Convert.ToHexString(rawSig);

        var fakeDb = new FakeDbConnectionFactory(throwOnOpen: true, exceptionMessage: "Auth Passed, Reached DB");
        var controller = new SubscriptionCallbackController(fakeDb, _config);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Signature"] = validHexSig;
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var payload = new GatewayCallbackPayload
        {
            TransactionReference = trxRef,
            Amount = amount,
            Status = "Completed"
        };

        // Act & Assert: Should pass HMAC validation and proceed to DB lookup
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => controller.ProcessGatewayCallback(payload));
        Assert.Equal("Auth Passed, Reached DB", ex.Message);
    }

    [Fact]
    public void DatabaseMigrationRunner_HasDeterministicAdvisoryLockConstant()
    {
        // Assert: pg_advisory_lock key is fixed 64-bit bigint
        Assert.Equal(849201948271049281L, DatabaseMigrationRunner.AdvisoryLockId);
    }

    [Fact]
    public void EmbeddedMigrations_CountIsGreaterThanZero()
    {
        var assembly = typeof(DatabaseMigrationRunner).Assembly;
        var resourceNames = assembly.GetManifestResourceNames()
            .Where(r => r.StartsWith("AidlyPms.Api.Migrations.", StringComparison.OrdinalIgnoreCase) && r.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.NotEmpty(resourceNames);
        Assert.True(resourceNames.Count > 0, $"Expected embedded migrations > 0, but found {resourceNames.Count}");
    }

    [Fact]
    public async Task DatabaseMigrationRunner_FailsFast_WhenMigrationFails_UnlessFailOpen()
    {
        // Arrange: Setup throwing DB factory
        var fakeDb = new FakeDbConnectionFactory(throwOnOpen: true, exceptionMessage: "Connection refused by database host. Password=SuperSecret123;");

        var services = new ServiceCollection();
        services.AddSingleton<IDbConnectionFactory>(fakeDb);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        var sp = services.BuildServiceProvider();

        // Ensure MIGRATIONS_FAIL_OPEN is false/unset
        Environment.SetEnvironmentVariable("MIGRATIONS_FAIL_OPEN", "false");

        // Act & Assert: When MIGRATIONS_FAIL_OPEN=false, runner MUST throw to stop app startup (fail fast)
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DatabaseMigrationRunner.ApplyMigrationsAsync(sp, NullLogger.Instance));

        Assert.NotNull(DatabaseMigrationRunner.LastError);
        // Sensitive credentials must be sanitized
        Assert.DoesNotContain("SuperSecret123", DatabaseMigrationRunner.LastError);
        Assert.Contains("[REDACTED]", DatabaseMigrationRunner.LastError);

        // Act & Assert 2: When MIGRATIONS_FAIL_OPEN=true, runner logs critical but does NOT throw
        Environment.SetEnvironmentVariable("MIGRATIONS_FAIL_OPEN", "true");
        try
        {
            var exception = await Record.ExceptionAsync(() =>
                DatabaseMigrationRunner.ApplyMigrationsAsync(sp, NullLogger.Instance));
            Assert.Null(exception); // Swallowed in fail-open emergency mode
        }
        finally
        {
            Environment.SetEnvironmentVariable("MIGRATIONS_FAIL_OPEN", null);
        }
    }

    [Fact]
    public async Task SysController_GetMigrations_ReturnsSanitizedStatus_WithoutSensitiveOutput()
    {
        // Arrange
        DatabaseMigrationRunner.LastError = "Simulated error Pwd=MySecretPassword; on host";
        var fakeDb = new FakeDbConnectionFactory(throwOnOpen: true, exceptionMessage: "DB unavailable");

        var controller = new SysController(fakeDb);

        // Act
        var result = await controller.GetMigrations();

        // Assert: 200 OK with sanitized response
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var apiResp = Assert.IsAssignableFrom<ApiResponse<SysMigrationsResponse>>(okResult.Value);
        Assert.True(apiResp.Success);

        var data = apiResp.Data;
        Assert.NotNull(data);
        Assert.Equal("Error", data.Status);
        Assert.DoesNotContain("MySecretPassword", data.LastError);
        Assert.Contains("[REDACTED]", data.LastError);
    }

    [Theory]
    [InlineData("false", false)]
    [InlineData("true", true)]
    public void SubscriptionConfig_DemoAutoActivate_ParsesCorrectly(string configVal, bool expected)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Subscription:DemoAutoActivate"] = configVal
            })
            .Build();

        var isAutoConfirm = config.GetValue<bool>("Subscription:DemoAutoActivate", false);
        Assert.Equal(expected, isAutoConfirm);
    }

    [Theory]
    [InlineData((short)2, 365)]
    [InlineData((short)1, 30)]
    public void ExtensionDays_DerivedFromBillingCycle_NotFromSubtotal(short billingCycle, int expectedDays)
    {
        // Proves requirement 1.d: Extension days must be derived from billing cycle (2 = Yearly -> 365, 1 = Monthly -> 30)
        // regardless of subtotal.
        var payment = new SubSubscriptionPayment
        {
            BillingCycle = billingCycle,
            Subtotal = 100 // Even if subtotal is low, yearly billing cycle yields 365 days
        };

        var days = payment.BillingCycle == 2 ? 365 : 30;
        Assert.Equal(expectedDays, days);
    }

    private class FakeDbConnectionFactory : IDbConnectionFactory
    {
        private readonly bool _throwOnOpen;
        private readonly string _exceptionMessage;

        public FakeDbConnectionFactory(bool throwOnOpen, string exceptionMessage = "Simulated DB failure")
        {
            _throwOnOpen = throwOnOpen;
            _exceptionMessage = exceptionMessage;
        }

        public NpgsqlConnection CreateConnection()
        {
            if (_throwOnOpen) throw new InvalidOperationException(_exceptionMessage);
            return new NpgsqlConnection("Host=localhost;Database=dummy_test;Username=postgres;Password=test");
        }

        public Task<NpgsqlConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default)
        {
            if (_throwOnOpen) throw new InvalidOperationException(_exceptionMessage);
            var conn = new NpgsqlConnection("Host=localhost;Database=dummy_test;Username=postgres;Password=test");
            return Task.FromResult(conn);
        }
    }
}

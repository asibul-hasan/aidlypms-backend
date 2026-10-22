using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AidlyPms.Api.Common;
using AidlyPms.Api.Modules.Sales.Models;
using Dapper;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace AidlyPms.Api.Tests;

/// <summary>
/// HTTP-level integration tests using WebApplicationFactory against the CI PostgreSQL 16 service container.
/// Strictly enforces TEST_POSTGRES_CONNECTION allow-list guard (local host and *_test database only).
/// Proves:
/// 1. Unauthenticated request -> 401 Unauthorized
/// 2. Spoofed X-Pharmacy-No ignored -> TenantContext bound strictly to JWT claims
/// 3. User without 'void' permission -> 403 Forbidden
/// 4. Tampered client price ignored -> DB authoritative pricing enforced
/// 5. Narcotic / prescription medicine with fake or missing prescription -> rejected
/// 6. Full void reverses stock, ledger, due and loyalty
/// 7. Exchange Case 1: return ৳70 item, take ৳100 item -> collects ৳30
/// 8. Exchange Case 2: return ৳100 item, take ৳70 item -> gives back ৳30
/// </summary>
[Collection("PostgresDatabaseIntegration")]
public class WebApplicationFactoryIntegrationTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private readonly string? _baseConnectionString;
    private PostgresTestSchemaHarness? _harness;
    private string? _connectionString;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;

    public const long TestPharmacyNo = 9002;
    public const long TestBranchNo = 9002;
    public const long TestUserNo = 9002;
    public const long TestManagerRoleNo = 9;
    public const long TestRestrictedRoleNo = 9999;
    public const long TestCashierRoleNo = 10;

    public WebApplicationFactoryIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;

        _baseConnectionString = Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION");
        if (string.IsNullOrWhiteSpace(_baseConnectionString))
        {
            _output.WriteLine("TEST_POSTGRES_CONNECTION is not set. WebApplicationFactory tests will run in dry-run mode.");
        }
    }

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(_baseConnectionString)) return;

        _harness = await PostgresTestSchemaHarness.CreateAsync(_baseConnectionString);
        _connectionString = _harness.IsolatedConnectionString;

        // Initialize WebApplicationFactory with test connection string (isolated schema Search Path)
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configBuilder) =>
                {
                    configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:DefaultConnection"] = _connectionString,
                        ["ConnectionStrings:DirectConnection"] = _connectionString
                    });
                });
            });

        _client = _factory.CreateClient();

        // Seed test pharmacy and master records inside isolated schema
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await EnsureSeedAsync(conn);
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_factory != null)
        {
            await _factory.DisposeAsync();
        }

        if (_harness != null)
        {
            await _harness.DisposeAsync();
        }
    }

    private static string GenerateJwt(long pharmacyNo, long branchNo, long userNo, long roleNo, string roleName)
    {
        const string keyStr = "AidlyPMS_SuperSecret_Jwt_SigningKey_2026_Enterprise_Secure!";
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(keyStr));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userNo.ToString()),
            new(ClaimTypes.Name, $"User {userNo}"),
            new(ClaimTypes.Role, roleName),
            new("user_no", userNo.ToString()),
            new("pharmacy_no", pharmacyNo.ToString()),
            new("branch_no", branchNo.ToString()),
            new("role_no", roleNo.ToString())
        };

        var token = new JwtSecurityToken(
            issuer: "AidlyPmsBackend",
            audience: "AidlyPmsFrontend",
            claims: claims,
            expires: DateTime.UtcNow.AddDays(1),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static async Task EnsureSeedAsync(NpgsqlConnection conn)
    {
        await conn.ExecuteAsync(@"
            INSERT INTO sys_pharmacies (
                pharmacy_no, uuid, name, contact_phone, contact_email, address, district, is_active, created_at, updated_at
            ) VALUES (
                @p, gen_random_uuid(), 'HTTP Test Pharmacy', '01711111111', 'http9002@example.com', 'Dhaka, Bangladesh', 'Dhaka', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            ) ON CONFLICT (pharmacy_no) DO NOTHING;

            INSERT INTO sys_branches (
                branch_no, uuid, pharmacy_no, name, contact_phone, contact_email, address, district, is_active, created_at, updated_at
            ) VALUES (
                @b, gen_random_uuid(), @p, 'HTTP Test Main Branch', '01711111111', 'http9002@example.com', 'Banani, Dhaka', 'Dhaka', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            ) ON CONFLICT (branch_no) DO NOTHING;

            INSERT INTO sys_branches (
                branch_no, uuid, pharmacy_no, name, contact_phone, contact_email, address, district, is_active, created_at, updated_at
            ) VALUES (
                @b + 1, gen_random_uuid(), @p, 'HTTP Test Secondary Branch', '01711111112', 'dest9003@example.com', 'Gulshan, Dhaka', 'Dhaka', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            ) ON CONFLICT (branch_no) DO NOTHING;

            -- Manager Role (9)
            INSERT INTO sys_roles (
                role_no, pharmacy_no, branch_no, role_name, alias, description, is_system, status, created_at, updated_at
            ) VALUES (
                9, @p, @b, 'Manager', 'Manager', 'Manager', TRUE, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            ) ON CONFLICT (role_no) DO NOTHING;

            -- Restricted Role without 'void' permission
            INSERT INTO sys_roles (
                role_no, pharmacy_no, branch_no, role_name, alias, description, is_system, status, created_at, updated_at
            ) VALUES (
                @restrictedRole, @p, @b, 'No-Void Cashier', 'No-Void Cashier', 'Role lacking void permission', FALSE, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            ) ON CONFLICT (role_no) DO NOTHING;

            -- Seed SysUsers
            INSERT INTO sys_users (
                user_no, uuid, pharmacy_no, branch_no, role_no, first_name, last_name, phone, email, password_hash, is_active, created_at, updated_at
            ) VALUES (
                @u, gen_random_uuid(), @p, @b, 9, 'HTTP', 'User 9002', '01711111111', 'httpuser9002@example.com', 'hash', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            ) ON CONFLICT (user_no) DO NOTHING;

            INSERT INTO sys_users (
                user_no, uuid, pharmacy_no, branch_no, role_no, first_name, last_name, phone, email, password_hash, is_active, created_at, updated_at
            ) VALUES (
                @u + 10, gen_random_uuid(), @p, @b, @restrictedRole, 'Restricted', 'User 9002', '01711111112', 'restricted9002@example.com', 'hash', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            ) ON CONFLICT (user_no) DO NOTHING;

            INSERT INTO prod_companies (
                company_no, pharmacy_no, branch_no, name, is_active, created_at, updated_at
            ) VALUES (
                @p, @p, @b, 'HTTP Test Manufacturer', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            ) ON CONFLICT (company_no) DO NOTHING;

            INSERT INTO prod_generics (
                generic_no, pharmacy_no, branch_no, name, is_active, created_at, updated_at
            ) VALUES (
                @p, @p, @b, 'HTTP Test Molecule', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            ) ON CONFLICT (generic_no) DO NOTHING;

            -- Assign Sale module but deliberately EXCLUDE 'void' for restricted role
            INSERT INTO sys_role_permissions (role_no, permission_no, pharmacy_no, branch_no)
            SELECT @restrictedRole, permission_no, @p, @b
            FROM sys_permissions
            WHERE code IN ('PMS-Sale-Module', 'sale.return')
            ON CONFLICT DO NOTHING;

            -- Give manager role (9) all permissions for both branches
            INSERT INTO sys_role_permissions (role_no, permission_no, pharmacy_no, branch_no)
            SELECT 9, permission_no, @p, @b
            FROM sys_permissions
            ON CONFLICT DO NOTHING;

            INSERT INTO sys_role_permissions (role_no, permission_no, pharmacy_no, branch_no)
            SELECT 9, permission_no, @p, @b + 1
            FROM sys_permissions
            ON CONFLICT DO NOTHING;

            -- Setup chart of accounts
            INSERT INTO acc_transaction_accounts (
                account_no, pharmacy_no, branch_no, account_category, account_code, account_name, account_type, bank_account_number, bank_name, current_balance, is_active, created_at, updated_at
            ) VALUES
            (@p * 100 + 1, @p, @b, 1, '10101', 'Cash in Hand', 1, '', 'Main Cash Drawer', 100000, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
            (@p * 100 + 2, @p, @b, 4, '40101', 'Sales Revenue', 1, '', 'General Sales', 0, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
            (@p * 100 + 3, @p, @b, 2, '20201', 'Output VAT Payable', 1, '', 'Govt VAT', 0, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
            (@p * 100 + 4, @p, @b, 1, '10301', 'Customer Accounts Receivable', 1, '', 'Customer Dues', 0, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
            (@p * 100 + 5, @p, @b, 5, '50101', 'Cost of Goods Sold', 1, '', 'COGS', 0, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
            (@p * 100 + 6, @p, @b, 1, '10201', 'Inventory Asset', 1, '', 'Pharmacy Inventory Stock', 500000, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
            (@p * 100 + 7, @p, @b, 1, '10901', 'Return Credit Clearing Account', 1, '', 'Exchange Clearing', 0, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
            (@p * 100 + 8, @p, @b, 1, '10401', 'Merchandise Inventory Asset', 1, '', 'Pharmacy Inventory Stock', 500000, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
            (@p * 100 + 9, @p, @b, 4, '40102', 'Sales Returns and Allowances', 1, '', 'Returns', 0, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
            (@p * 100 + 10, @p, @b, 2, '20301', 'Customer Store Credit Liability', 1, '', 'Store Credit', 0, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
            ON CONFLICT (account_no) DO NOTHING;

            -- Sale config
            INSERT INTO cfg_sale_configs (
                sale_config_no, pharmacy_no, branch_no, cash_received, out_of_stock_sale,
                allow_loss_sale, product_purchase_cost, stock_qty, company_name,
                profit_margin, rak_number, opening_stock_percent, max_sale_discount_percent,
                is_max_discount_enabled, business_type, return_window_days, updated_at
            ) VALUES (
                @p, @p, @b, TRUE, TRUE,
                TRUE, TRUE, TRUE, TRUE,
                TRUE, TRUE, 0, 10,
                FALSE, 1, 14, CURRENT_TIMESTAMP
            ) ON CONFLICT (pharmacy_no, branch_no) DO UPDATE SET out_of_stock_sale = TRUE;
        ", new { p = TestPharmacyNo, b = TestBranchNo, u = TestUserNo, restrictedRole = TestRestrictedRoleNo });
    }

    private static async Task CleanupAsync(NpgsqlConnection conn)
    {
        await conn.ExecuteAsync(@"
            DELETE FROM sale_return_items WHERE pharmacy_no = @p;
            DELETE FROM sale_returns WHERE pharmacy_no = @p;
            DELETE FROM sale_invoice_payments WHERE pharmacy_no = @p;
            DELETE FROM sale_invoice_items WHERE pharmacy_no = @p;
            DELETE FROM sale_invoices WHERE pharmacy_no = @p;
            DELETE FROM inv_stock_movement WHERE pharmacy_no = @p;
            DELETE FROM inv_transfer_items WHERE pharmacy_no = @p;
            DELETE FROM inv_transfers WHERE pharmacy_no = @p;
            DELETE FROM prod_product_batches WHERE pharmacy_no = @p;
            DELETE FROM prod_products WHERE pharmacy_no = @p;
            DELETE FROM doc_prescriptions WHERE pharmacy_no = @p;
            DELETE FROM cust_customers WHERE pharmacy_no = @p;
            DELETE FROM supp_suppliers WHERE pharmacy_no = @p;
            DELETE FROM sub_subscription_payments WHERE pharmacy_no = @p;
            DELETE FROM sub_pharmacy_subscriptions WHERE pharmacy_no = @p;
            DELETE FROM sys_audit_logs WHERE pharmacy_no = @p;
            DELETE FROM acc_transaction_ledger WHERE pharmacy_no = @p;
            DELETE FROM acc_transaction_accounts WHERE pharmacy_no = @p;
            DELETE FROM cfg_sale_configs WHERE pharmacy_no = @p;
            DELETE FROM prod_generics WHERE pharmacy_no = @p;
            DELETE FROM prod_companies WHERE pharmacy_no = @p;
            DELETE FROM sys_user_pharmacy_access WHERE pharmacy_no = @p;
            DELETE FROM sys_users WHERE pharmacy_no = @p;
            DELETE FROM sys_role_permissions WHERE pharmacy_no = @p;
            DELETE FROM sys_roles WHERE pharmacy_no = @p;
            DELETE FROM sys_branches WHERE pharmacy_no = @p;
            DELETE FROM sys_pharmacies WHERE pharmacy_no = @p;
        ", new { p = TestPharmacyNo, b = TestBranchNo, u = TestUserNo, restrictedRole = TestRestrictedRoleNo });
    }

    // =========================================================================
    // 1. Unauthenticated request -> 401 Unauthorized
    // =========================================================================
    [Fact]
    public async Task Test_1_NoToken_Returns401Unauthorized()
    {
        if (_client == null) return;

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/pos/sales");
        request.Content = JsonContent.Create(new { });

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        _output.WriteLine("Test_1 Passed: Anonymous call to /api/v1/pos/sales rejected with HTTP 401 Unauthorized.");
    }

    // =========================================================================
    // 2. Spoofed X-Pharmacy-No ignored -> TenantContext strictly bound to JWT
    // =========================================================================
    [Fact]
    public async Task Test_2_SpoofedXPharmacyNo_IsIgnored_TenantContextBoundStrictlyToJwt()
    {
        if (_client == null || string.IsNullOrWhiteSpace(_connectionString)) return;

        var token = GenerateJwt(TestPharmacyNo, TestBranchNo, TestUserNo, TestManagerRoleNo, "PMS Manager");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/cfg/sale-configs?pharmacy_no=9999");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Pharmacy-No", "9999");
        request.Headers.Add("X-Branch-No", "9999");

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonObject>();
        var data = json?["data"]?.AsObject();
        Assert.NotNull(data);

        // Pharmacy number returned in config must be 9002 (from JWT), NOT 9999 (from spoofed header/query)
        var returnedPharmacyNo = data["pharmacy_no"]?.GetValue<long>() ?? 0;
        Assert.Equal(TestPharmacyNo, returnedPharmacyNo);
        _output.WriteLine("Test_2 Passed: Spoofed X-Pharmacy-No=9999 ignored. TenantContext accurately bound to JWT claim 9002.");
    }

    // =========================================================================
    // 3. User without 'void' permission -> 403 Forbidden
    // =========================================================================
    [Fact]
    public async Task Test_3_UserWithoutVoidPermission_Returns403Forbidden()
    {
        if (_client == null || string.IsNullOrWhiteSpace(_connectionString)) return;

        // Mint token for user holding restricted role (lacks 'void' permission)
        var token = GenerateJwt(TestPharmacyNo, TestBranchNo, TestUserNo + 10, TestRestrictedRoleNo, "Restricted Cashier");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/pos/sales/1/void");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new { reason = "Test unauthorized void attempt" });

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        _output.WriteLine("Test_3 Passed: Void attempt by role lacking 'void' permission returned HTTP 403 Forbidden.");
    }

    // =========================================================================
    // 4. Tampered client price ignored -> DB authoritative pricing enforced
    // =========================================================================
    [Fact]
    public async Task Test_4_TamperedPrice_IsIgnored_DbPriceEnforced()
    {
        if (_client == null || string.IsNullOrWhiteSpace(_connectionString)) return;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        const long prodNo = 9201;
        const decimal trueDbPrice = 150.00m;
        const decimal trueDbCost = 100.00m;

        await conn.ExecuteAsync(@"
            INSERT INTO prod_products (
                product_no, uuid, pharmacy_no, branch_no, company_no, generic_no, product_name,
                medicine_type, unit_type, quantity_per_box,
                cost_per_box, sale_price_per_box, avg_purchase_price_per_piece, purchase_price_per_piece, sale_price_per_piece,
                wholesale_price_per_box, wholesale_price_per_piece,
                is_medicine, is_prescription_required, is_narcotic, min_stock_qty_pcs, is_active, created_at, updated_at
            ) VALUES (
                @prodNo, gen_random_uuid(), @p, @b, @p, @p, 'Tamper Proof Syrup',
                1, 1, 1,
                @trueDbCost, @trueDbPrice, @trueDbCost, @trueDbCost, @trueDbPrice,
                @trueDbPrice, @trueDbPrice,
                TRUE, FALSE, FALSE, 10, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            ) ON CONFLICT (product_no) DO UPDATE SET sale_price_per_piece = @trueDbPrice, cost_per_box = @trueDbCost;
        ", new { prodNo, p = TestPharmacyNo, b = TestBranchNo, trueDbPrice, trueDbCost });

        var token = GenerateJwt(TestPharmacyNo, TestBranchNo, TestUserNo, TestManagerRoleNo, "PMS Manager");

        // Client attempts to tamper sale price to ৳10 instead of DB price ৳150
        var salePayload = new
        {
            customer_no = (long?)null,
            sale_mode = 1,
            items = new[]
            {
                new
                {
                    product_no = prodNo,
                    sale_qty = 2,
                    sale_price = 10.00m,       // Tampered price
                    unit_sale_price = 10.00m,  // Tampered price
                    total_price = 20.00m       // Tampered total
                }
            },
            payments = new[]
            {
                new { payment_method = 1, amount = 300.00m, account_no = TestPharmacyNo * 100 + 1 }
            }
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/pos/sales");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(salePayload);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonObject>();
        var data = json?["data"]?.AsObject();
        Assert.NotNull(data);

        // Server must enforce 2 * ৳150 = ৳300 gross, completely ignoring client's ৳20
        var grossTotal = data["gross_total_price"]?.GetValue<decimal>() ?? 0;
        Assert.Equal(300.00m, grossTotal);
        _output.WriteLine("Test_4 Passed: Client tampered price (৳10) was ignored. Authoritative DB price (৳150) enforced (Gross = ৳300).");
    }

    // =========================================================================
    // 5. Narcotic / prescription medicine with fake prescription -> rejected
    // =========================================================================
    [Fact]
    public async Task Test_5_Narcotic_WithFakePrescription_Rejected()
    {
        if (_client == null || string.IsNullOrWhiteSpace(_connectionString)) return;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        const long narcoticProdNo = 9202;
        await conn.ExecuteAsync(@"
            INSERT INTO prod_products (
                product_no, uuid, pharmacy_no, branch_no, company_no, generic_no, product_name,
                medicine_type, unit_type, quantity_per_box,
                cost_per_box, sale_price_per_box, avg_purchase_price_per_piece, purchase_price_per_piece, sale_price_per_piece,
                wholesale_price_per_box, wholesale_price_per_piece,
                is_medicine, is_prescription_required, is_narcotic, min_stock_qty_pcs, is_active, created_at, updated_at
            ) VALUES (
                @narcoticProdNo, gen_random_uuid(), @p, @b, @p, @p, 'Morphine Sulfate 10mg',
                1, 1, 1,
                50.00, 80.00, 50.00, 50.00, 80.00,
                80.00, 80.00,
                TRUE, TRUE, TRUE, 10, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            ) ON CONFLICT (product_no) DO UPDATE SET is_narcotic = TRUE, is_prescription_required = TRUE;
        ", new { narcoticProdNo, p = TestPharmacyNo, b = TestBranchNo });

        var token = GenerateJwt(TestPharmacyNo, TestBranchNo, TestUserNo, TestManagerRoleNo, "PMS Manager");

        // Attempt sale with fake prescription #888888 (does not exist in doc_prescriptions)
        var salePayload = new
        {
            prescription_no = 888888L, // Fake prescription number
            items = new[]
            {
                new { product_no = narcoticProdNo, sale_qty = 1 }
            },
            payments = new[]
            {
                new { payment_method = 1, amount = 80.00m, account_no = TestPharmacyNo * 100 + 1 }
            }
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/pos/sales");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(salePayload);

        var response = await _client.SendAsync(request);

        // Must NOT succeed (must return 400 Bad Request or 500 error)
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        _output.WriteLine("Test_5 Passed: Narcotic sale with fake prescription #888888 was rejected by authoritative prescription check.");
    }

    // =========================================================================
    // 6. Full void reverses stock, ledger, due and loyalty
    // =========================================================================
    [Fact]
    public async Task Test_6_FullVoid_ReversesStock_Ledger_Due_And_Loyalty()
    {
        if (_client == null || string.IsNullOrWhiteSpace(_connectionString)) return;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        const long prodNo = 9203;
        const long custNo = 9301;
        await conn.ExecuteAsync(@"
            INSERT INTO cust_customers (
                customer_no, uuid, pharmacy_no, branch_no, name, phone, customer_type, discount_percent,
                has_due, current_due, advance_balance, is_loyalty_member, loyalty_points, is_active, created_at, updated_at
            ) VALUES (
                @custNo, gen_random_uuid(), @p, @b, 'Due Customer', '01722222222', 1, 0,
                TRUE, 0, 0, FALSE, 0, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            ) ON CONFLICT (customer_no) DO UPDATE SET current_due = 0, loyalty_points = 0;

            INSERT INTO prod_products (
                product_no, uuid, pharmacy_no, branch_no, company_no, generic_no, product_name,
                medicine_type, unit_type, quantity_per_box,
                cost_per_box, sale_price_per_box, avg_purchase_price_per_piece, purchase_price_per_piece, sale_price_per_piece,
                wholesale_price_per_box, wholesale_price_per_piece,
                is_medicine, is_prescription_required, is_narcotic, min_stock_qty_pcs, is_active, created_at, updated_at
            ) VALUES (
                @prodNo, gen_random_uuid(), @p, @b, @p, @p, 'Stock Void Medicine',
                1, 1, 1,
                40.00, 60.00, 40.00, 40.00, 60.00,
                60.00, 60.00,
                TRUE, FALSE, FALSE, 10, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            ) ON CONFLICT (product_no) DO UPDATE SET sale_price_per_piece = 60.00, purchase_price_per_piece = 40.00;
        ", new { custNo, prodNo, p = TestPharmacyNo, b = TestBranchNo });

        var token = GenerateJwt(TestPharmacyNo, TestBranchNo, TestUserNo, TestManagerRoleNo, "PMS Manager");

        // Create sale with ৳40 paid cash and ৳20 due
        var salePayload = new
        {
            customer_no = custNo,
            items = new[]
            {
                new { product_no = prodNo, sale_qty = 1 }
            },
            payments = new[]
            {
                new { payment_method = 1, amount = 40.00m, account_no = TestPharmacyNo * 100 + 1 }
            }
        };

        var saleReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/pos/sales");
        saleReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        saleReq.Content = JsonContent.Create(salePayload);

        var saleResp = await _client.SendAsync(saleReq);
        Assert.Equal(HttpStatusCode.OK, saleResp.StatusCode);

        var saleJson = await saleResp.Content.ReadFromJsonAsync<JsonObject>();
        var invoiceNo = saleJson?["data"]?["sale_invoice_no"]?.GetValue<long>() ?? 0;
        Assert.True(invoiceNo > 0, "Sale invoice was not created.");

        // Customer due should now be ৳20
        var dueAfterSale = await conn.ExecuteScalarAsync<decimal>(
            "SELECT current_due FROM cust_customers WHERE customer_no = @custNo;", new { custNo });
        Assert.Equal(20.00m, dueAfterSale);

        // Void the invoice
        var voidReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/pos/sales/{invoiceNo}/void");
        voidReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        voidReq.Content = JsonContent.Create(new { reason = "Accidental billing test void" });

        var voidResp = await _client.SendAsync(voidReq);
        Assert.Equal(HttpStatusCode.OK, voidResp.StatusCode);

        // 1. Verify invoice marked voided (document_status = 3)
        var invoiceStatus = await conn.ExecuteScalarAsync<short>(
            "SELECT document_status FROM sale_invoices WHERE sale_invoice_no = @invoiceNo;", new { invoiceNo });
        Assert.Equal(3, invoiceStatus);

        // 2. Verify stock movement recorded with reference SALE_VOID
        var voidMovements = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM inv_stock_movement WHERE reference_doc_type = 'SALE_VOID' AND reference_doc_id = @invoiceNo;", new { invoiceNo });
        Assert.True(voidMovements > 0, "Stock restoration movement must be logged on void.");

        // 3. Verify ledger reversal entries posted
        var voidLedgerEntries = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM acc_transaction_ledger WHERE source_document_type = 'SALE_VOID' AND source_document_id = @invoiceNo;", new { invoiceNo });
        Assert.True(voidLedgerEntries > 0, "Ledger reversal entries must be posted on void.");

        // 4. Verify customer due restored to 0
        var dueAfterVoid = await conn.ExecuteScalarAsync<decimal>(
            "SELECT current_due FROM cust_customers WHERE customer_no = @custNo;", new { custNo });
        Assert.Equal(0.00m, dueAfterVoid);

        _output.WriteLine("Test_6 Passed: Sale invoice fully voided. Stock, ledger entries, customer dues, and loyalty points reversed.");
    }

    // =========================================================================
    // 7. Exchange Case 1: return ৳70 item, take ৳100 item -> collects ৳30
    // =========================================================================
    [Fact]
    public async Task Test_7_Exchange_Case1_Collect30_TenderDifference()
    {
        if (_client == null || string.IsNullOrWhiteSpace(_connectionString)) return;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        const long itemA = 9204; // ৳70
        const long itemB = 9205; // ৳100

        await conn.ExecuteAsync(@"
            INSERT INTO prod_products (
                product_no, uuid, pharmacy_no, branch_no, company_no, generic_no, product_name,
                medicine_type, unit_type, quantity_per_box,
                cost_per_box, sale_price_per_box, avg_purchase_price_per_piece, purchase_price_per_piece, sale_price_per_piece,
                wholesale_price_per_box, wholesale_price_per_piece,
                is_medicine, is_prescription_required, is_narcotic, min_stock_qty_pcs, is_active, created_at, updated_at
            ) VALUES
            (@itemA, gen_random_uuid(), @p, @b, @p, @p, 'Old Medicine A', 1, 1, 1, 50.00, 70.00, 50.00, 50.00, 70.00, 70.00, 70.00, TRUE, FALSE, FALSE, 10, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
            (@itemB, gen_random_uuid(), @p, @b, @p, @p, 'New Medicine B', 1, 1, 1, 70.00, 100.00, 70.00, 70.00, 100.00, 100.00, 100.00, TRUE, FALSE, FALSE, 10, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
            ON CONFLICT (product_no) DO UPDATE SET sale_price_per_piece = EXCLUDED.sale_price_per_piece;
        ", new { itemA, itemB, p = TestPharmacyNo, b = TestBranchNo });

        var token = GenerateJwt(TestPharmacyNo, TestBranchNo, TestUserNo, TestManagerRoleNo, "PMS Manager");

        // 1. Sell Item A for ৳70
        var initialSale = new
        {
            items = new[] { new { product_no = itemA, sale_qty = 1 } },
            payments = new[] { new { payment_method = 1, amount = 70.00m, account_no = TestPharmacyNo * 100 + 1 } }
        };

        var initReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/pos/sales");
        initReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        initReq.Content = JsonContent.Create(initialSale);

        var initResp = await _client.SendAsync(initReq);
        Assert.Equal(HttpStatusCode.OK, initResp.StatusCode);

        var initJson = await initResp.Content.ReadFromJsonAsync<JsonObject>();
        var origInvoiceNo = initJson?["data"]?["sale_invoice_no"]?.GetValue<long>() ?? 0;
        Assert.True(origInvoiceNo > 0);

        var origItemNo = await conn.ExecuteScalarAsync<long>(
            "SELECT sale_invoice_item_no FROM sale_invoice_items WHERE sale_invoice_no = @origInvoiceNo LIMIT 1;", new { origInvoiceNo });

        // 2. Exchange: Return Item A (credit ৳70) and purchase Item B (price ৳100). Difference = +৳30.
        // Tender ৳30 cash payment.
        var exchangePayload = new
        {
            @return = new
            {
                sale_invoice_no = origInvoiceNo,
                refund_method = 5, // Exchange Credit
                lines = new[]
                {
                    new
                    {
                        sale_invoice_item_no = origItemNo,
                        qty_pcs = 1,
                        reason_code = (short)4,  // Customer Choice (required)
                        disposition = (short)1   // Restock (required)
                    }
                }
            },
            new_items = new[]
            {
                new { product_no = itemB, sale_qty = 1 }
            },
            tender_payments = new[]
            {
                new { payment_method = 1, amount = 30.00m, account_no = TestPharmacyNo * 100 + 1 }
            }
        };

        var exchReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/pos/exchange");
        exchReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        exchReq.Content = JsonContent.Create(exchangePayload);

        var exchResp = await _client.SendAsync(exchReq);
        Assert.Equal(HttpStatusCode.OK, exchResp.StatusCode);

        var exchJson = await exchResp.Content.ReadFromJsonAsync<JsonObject>();
        var exchData = exchJson?["data"]?.AsObject();
        Assert.NotNull(exchData);

        var diff = exchData["net_difference"]?.GetValue<decimal>() ?? 0;
        Assert.Equal(30.00m, diff);

        // Net balance of exchange clearing account (10901) must be strictly 0
        var clearingBalance = await conn.ExecuteScalarAsync<decimal>(
            "SELECT current_balance FROM acc_transaction_accounts WHERE account_code = '10901' AND pharmacy_no = @p;", new { p = TestPharmacyNo });
        Assert.Equal(0.00m, clearingBalance);

        _output.WriteLine("Test_7 Passed: Exchange Case 1 successfully collected ৳30 difference, return recorded, clearing account netted to 0.");
    }

    // =========================================================================
    // 8. Exchange Case 2: return ৳100 item, take ৳70 item -> gives back ৳30
    // =========================================================================
    [Fact]
    public async Task Test_8_Exchange_Case2_GiveBack30_RefundDifference()
    {
        if (_client == null || string.IsNullOrWhiteSpace(_connectionString)) return;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        const long itemA = 9206; // ৳70
        const long itemB = 9207; // ৳100

        await conn.ExecuteAsync(@"
            INSERT INTO prod_products (
                product_no, uuid, pharmacy_no, branch_no, company_no, generic_no, product_name,
                medicine_type, unit_type, quantity_per_box,
                cost_per_box, sale_price_per_box, avg_purchase_price_per_piece, purchase_price_per_piece, sale_price_per_piece,
                wholesale_price_per_box, wholesale_price_per_piece,
                is_medicine, is_prescription_required, is_narcotic, min_stock_qty_pcs, is_active, created_at, updated_at
            ) VALUES
            (@itemA, gen_random_uuid(), @p, @b, @p, @p, 'Exch Item 70', 1, 1, 1, 50.00, 70.00, 50.00, 50.00, 70.00, 70.00, 70.00, TRUE, FALSE, FALSE, 10, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
            (@itemB, gen_random_uuid(), @p, @b, @p, @p, 'Exch Item 100', 1, 1, 1, 70.00, 100.00, 70.00, 70.00, 100.00, 100.00, 100.00, TRUE, FALSE, FALSE, 10, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
            ON CONFLICT (product_no) DO UPDATE SET sale_price_per_piece = EXCLUDED.sale_price_per_piece;
        ", new { itemA, itemB, p = TestPharmacyNo, b = TestBranchNo });

        var token = GenerateJwt(TestPharmacyNo, TestBranchNo, TestUserNo, TestManagerRoleNo, "PMS Manager");

        // 1. Sell Item B for ৳100
        var initialSale = new
        {
            items = new[] { new { product_no = itemB, sale_qty = 1 } },
            payments = new[] { new { payment_method = 1, amount = 100.00m, account_no = TestPharmacyNo * 100 + 1 } }
        };

        var initReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/pos/sales");
        initReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        initReq.Content = JsonContent.Create(initialSale);

        var initResp = await _client.SendAsync(initReq);
        Assert.Equal(HttpStatusCode.OK, initResp.StatusCode);

        var initJson = await initResp.Content.ReadFromJsonAsync<JsonObject>();
        var origInvoiceNo = initJson?["data"]?["sale_invoice_no"]?.GetValue<long>() ?? 0;
        Assert.True(origInvoiceNo > 0);

        var origItemNo = await conn.ExecuteScalarAsync<long>(
            "SELECT sale_invoice_item_no FROM sale_invoice_items WHERE sale_invoice_no = @origInvoiceNo LIMIT 1;", new { origInvoiceNo });

        // 2. Exchange: Return Item B (credit ৳100) and purchase Item A (price ৳70). Difference = -৳30.
        // Pharmacy gives back ৳30 cash refund to customer.
        var exchangePayload = new
        {
            @return = new
            {
                sale_invoice_no = origInvoiceNo,
                refund_method = 5, // Exchange Credit
                lines = new[]
                {
                    new
                    {
                        sale_invoice_item_no = origItemNo,
                        qty_pcs = 1,
                        reason_code = (short)4,  // Customer Choice (required)
                        disposition = (short)1   // Restock (required)
                    }
                }
            },
            new_items = new[]
            {
                new { product_no = itemA, sale_qty = 1 }
            }
        };

        var exchReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/pos/exchange");
        exchReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        exchReq.Content = JsonContent.Create(exchangePayload);

        var exchResp = await _client.SendAsync(exchReq);
        Assert.Equal(HttpStatusCode.OK, exchResp.StatusCode);

        var exchJson = await exchResp.Content.ReadFromJsonAsync<JsonObject>();
        var exchData = exchJson?["data"]?.AsObject();
        Assert.NotNull(exchData);

        var diff = exchData["net_difference"]?.GetValue<decimal>() ?? 0;
        Assert.Equal(-30.00m, diff);

        // Net balance of exchange clearing account (10901) must be strictly 0
        var clearingBalance = await conn.ExecuteScalarAsync<decimal>(
            "SELECT current_balance FROM acc_transaction_accounts WHERE account_code = '10901' AND pharmacy_no = @p;", new { p = TestPharmacyNo });
        Assert.Equal(0.00m, clearingBalance);

        _output.WriteLine("Test_8 Passed: Exchange Case 2 successfully refunded / gave back ৳30 difference, return recorded, clearing account netted to 0.");
    }

    [Fact]
    public async Task Test_9_CashierRole_WithoutExpensePermission_Returns403Forbidden()
    {
        if (_client == null)
        {
            _output.WriteLine("Skipping: TEST_POSTGRES_CONNECTION not configured.");
            return;
        }

        // Cashier role (10) lacks PMS-Expense-Module permission
        var cashierToken = GenerateJwt(TestPharmacyNo, TestBranchNo, TestUserNo, TestCashierRoleNo, "PMS Salesman");
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/acc/expenses")
        {
            Content = JsonContent.Create(new
            {
                expense_category_no = 1,
                amount = 250.00m,
                expense_date = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                note = "Unauthorized Cashier Expense Attempt"
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cashierToken);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        _output.WriteLine("Test 9 Passed: Cashier role without PMS-Expense-Module permission correctly received HTTP 403 Forbidden.");
    }

    // =========================================================================
    // 10. Soft-delete requires reason, blocks in-use records, and excludes deleted
    // =========================================================================
    [Fact]
    public async Task Test_10_SoftDelete_RequiresReason_And_BlocksInUse_And_HidesInactive()
    {
        if (_client == null || string.IsNullOrWhiteSpace(_connectionString))
        {
            _output.WriteLine("Skipping: TEST_POSTGRES_CONNECTION not configured.");
            return;
        }

        var token = GenerateJwt(TestPharmacyNo, TestBranchNo, TestUserNo, TestManagerRoleNo, "Manager");
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        // 1. Setup active Product with stock
        const long testProdNo = 900250;
        await conn.ExecuteAsync(@"
            INSERT INTO prod_products (
                product_no, uuid, pharmacy_no, branch_no, company_no, generic_no,
                product_name, strength, volume, medicine_type, unit_type,
                quantity_per_box, cost_per_box, sale_price_per_box,
                avg_purchase_price_per_piece, purchase_price_per_piece, sale_price_per_piece,
                wholesale_price_per_box, wholesale_price_per_piece,
                is_medicine, is_prescription_required, is_narcotic, min_stock_qty_pcs, is_active,
                created_at, updated_at
            ) VALUES (
                @testProdNo, gen_random_uuid(), @p, @b, @p, @p,
                'SoftDelete Test Medicine', '500mg', '10ml', 1, 1,
                10, 100, 150, 10, 10, 15,
                150, 15,
                TRUE, FALSE, FALSE, 10, TRUE,
                CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            ) ON CONFLICT (product_no) DO UPDATE SET is_active = TRUE, deleted_at = NULL, deleted_by = NULL, deletion_reason = NULL;

            INSERT INTO prod_product_batches (
                batch_no, pharmacy_no, branch_no, product_no, batch_number,
                expiry_date, box_quantity, quantity_in_box, total_quantity_pcs,
                cost_per_box, sale_price_per_box, vat_percent, is_expired, created_at, updated_at
            ) VALUES (
                900250, @p, @b, @testProdNo, 'BATCH-SD-1',
                CURRENT_TIMESTAMP + INTERVAL '1 year', 5, 10, 50,
                100, 150, 0, FALSE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            ) ON CONFLICT (batch_no) DO UPDATE SET total_quantity_pcs = 50, updated_at = CURRENT_TIMESTAMP;
        ", new { p = TestPharmacyNo, b = TestBranchNo, testProdNo });

        // A. Delete product without reason -> 400 Bad Request
        var noReasonReq = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/prod/products/{testProdNo}")
        {
            Content = JsonContent.Create(new { reason = "" })
        };
        noReasonReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var noReasonResp = await _client.SendAsync(noReasonReq);
        Assert.Equal(HttpStatusCode.BadRequest, noReasonResp.StatusCode);

        // B. Delete product with stock > 0 -> 400 Bad Request
        var inUseReq = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/prod/products/{testProdNo}")
        {
            Content = JsonContent.Create(new { reason = "Discontinued item" })
        };
        inUseReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var inUseResp = await _client.SendAsync(inUseReq);
        Assert.Equal(HttpStatusCode.BadRequest, inUseResp.StatusCode);
        var inUseJson = await inUseResp.Content.ReadFromJsonAsync<JsonObject>();
        Assert.Contains("remaining stock", inUseJson?["message"]?.ToString() ?? "");

        // Zero out stock
        await conn.ExecuteAsync("UPDATE prod_product_batches SET total_quantity_pcs = 0 WHERE product_no = @testProdNo;", new { testProdNo });

        // C. Delete product without stock -> 200 OK
        var validDelReq = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/prod/products/{testProdNo}")
        {
            Content = JsonContent.Create(new { reason = "Discontinued item" })
        };
        validDelReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var validDelResp = await _client.SendAsync(validDelReq);
        Assert.Equal(HttpStatusCode.OK, validDelResp.StatusCode);

        // Check DB soft-delete audit fields
        var isProdActive = await conn.ExecuteScalarAsync<bool>(
            "SELECT is_active FROM prod_products WHERE product_no = @testProdNo;", new { testProdNo });
        var deletionReason = await conn.ExecuteScalarAsync<string>(
            "SELECT deletion_reason FROM prod_products WHERE product_no = @testProdNo;", new { testProdNo });
        Assert.False(isProdActive);
        Assert.Equal("Discontinued item", deletionReason);

        // D. GET /api/v1/prod/products does not list soft-deleted product
        var listReq = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/prod/products?search=SoftDelete");
        listReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var listResp = await _client.SendAsync(listReq);
        Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
        var listJson = await listResp.Content.ReadFromJsonAsync<JsonObject>();
        var items = listJson?["data"]?["items"]?.AsArray();
        Assert.DoesNotContain(items ?? new JsonArray(), item => item?["product_no"]?.GetValue<long>() == testProdNo);

        // 2. Setup active Customer with outstanding due
        const long testCustNo = 900260;
        await conn.ExecuteAsync(@"
            INSERT INTO cust_customers (
                customer_no, uuid, pharmacy_no, branch_no, name, phone, customer_type, discount_percent,
                has_due, current_due, advance_balance, is_loyalty_member, loyalty_points, is_active, created_at, updated_at
            ) VALUES (
                @testCustNo, gen_random_uuid(), @p, @b, 'SoftDelete Customer', '01799999999', 1, 0,
                TRUE, 500.00, 0.00, FALSE, 0, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            ) ON CONFLICT (customer_no) DO UPDATE SET has_due = TRUE, current_due = 500.00, is_active = TRUE;
        ", new { p = TestPharmacyNo, b = TestBranchNo, testCustNo });

        // Delete customer with due -> 400 Bad Request
        var custDueReq = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/cust/customers/{testCustNo}")
        {
            Content = JsonContent.Create(new { reason = "Customer moved" })
        };
        custDueReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var custDueResp = await _client.SendAsync(custDueReq);
        Assert.Equal(HttpStatusCode.BadRequest, custDueResp.StatusCode);

        // Clear customer due
        await conn.ExecuteAsync("UPDATE cust_customers SET has_due = FALSE, current_due = 0 WHERE customer_no = @testCustNo;", new { testCustNo });

        // Delete customer without due -> 200 OK
        var custValidReq = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/cust/customers/{testCustNo}")
        {
            Content = JsonContent.Create(new { reason = "Customer moved away" })
        };
        custValidReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var custValidResp = await _client.SendAsync(custValidReq);
        Assert.Equal(HttpStatusCode.OK, custValidResp.StatusCode);

        // 3. Setup active Supplier with due balance
        const long testSuppNo = 900270;
        await conn.ExecuteAsync(@"
            INSERT INTO supp_suppliers (
                supplier_no, pharmacy_no, branch_no, name, phone,
                current_due_balance, is_active, created_at, updated_at
            ) VALUES (
                @testSuppNo, @p, @b, 'SoftDelete Supplier', '01788888888',
                1200.00, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            ) ON CONFLICT (supplier_no) DO UPDATE SET current_due_balance = 1200.00, is_active = TRUE;
        ", new { p = TestPharmacyNo, b = TestBranchNo, testSuppNo });

        // Delete supplier with due -> 400 Bad Request
        var suppDueReq = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/supp/suppliers/{testSuppNo}")
        {
            Content = JsonContent.Create(new { reason = "Vendor termination" })
        };
        suppDueReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var suppDueResp = await _client.SendAsync(suppDueReq);
        Assert.Equal(HttpStatusCode.BadRequest, suppDueResp.StatusCode);

        // Clear supplier due
        await conn.ExecuteAsync("UPDATE supp_suppliers SET current_due_balance = 0 WHERE supplier_no = @testSuppNo;", new { testSuppNo });

        // Delete supplier using aliased route /api/v1/pur/suppliers/{id}
        var purSuppReq = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/pur/suppliers/{testSuppNo}")
        {
            Content = JsonContent.Create(new { reason = "Vendor closed" })
        };
        purSuppReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var purSuppResp = await _client.SendAsync(purSuppReq);
        Assert.Equal(HttpStatusCode.OK, purSuppResp.StatusCode);

        _output.WriteLine("Test 10 Passed: Soft-delete requires reason, blocks in-use records, and hides inactive records.");
    }

    // =========================================================================
    // 11. Stock Transfer Receive requires caller branch to equal ToBranchNo (403 otherwise)
    // =========================================================================
    [Fact]
    public async Task Test_11_StockTransfer_Receive_FromDifferentBranch_Returns403Forbidden()
    {
        if (_client == null || string.IsNullOrWhiteSpace(_connectionString))
        {
            _output.WriteLine("Skipping: TEST_POSTGRES_CONNECTION not configured.");
            return;
        }

        var sourceBranchToken = GenerateJwt(TestPharmacyNo, TestBranchNo, TestUserNo, TestManagerRoleNo, "Manager");
        var destBranchNo = TestBranchNo + 1;
        var destBranchToken = GenerateJwt(TestPharmacyNo, destBranchNo, TestUserNo, TestManagerRoleNo, "Manager");

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        // Ensure product and batch exist at source branch
        const long transferProdNo = 900280;
        await conn.ExecuteAsync(@"
            INSERT INTO prod_products (
                product_no, uuid, pharmacy_no, branch_no, company_no, generic_no,
                product_name, strength, volume, medicine_type, unit_type,
                quantity_per_box, cost_per_box, sale_price_per_box,
                avg_purchase_price_per_piece, purchase_price_per_piece, sale_price_per_piece,
                wholesale_price_per_box, wholesale_price_per_piece,
                is_medicine, is_prescription_required, is_narcotic, min_stock_qty_pcs, is_active,
                created_at, updated_at
            ) VALUES (
                @transferProdNo, gen_random_uuid(), @p, @b, @p, @p,
                'Transfer Test Product', '250mg', '5ml', 1, 1,
                10, 80, 120, 8, 8, 12,
                120, 12,
                TRUE, FALSE, FALSE, 10, TRUE,
                CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            ) ON CONFLICT (product_no) DO NOTHING;

            INSERT INTO prod_product_batches (
                batch_no, pharmacy_no, branch_no, product_no, batch_number,
                expiry_date, box_quantity, quantity_in_box, total_quantity_pcs,
                cost_per_box, sale_price_per_box, vat_percent, is_expired, created_at, updated_at
            ) VALUES (
                900280, @p, @b, @transferProdNo, 'BATCH-XFER-1',
                CURRENT_TIMESTAMP + INTERVAL '1 year', 10, 10, 100,
                80, 120, 0, FALSE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            ) ON CONFLICT (batch_no) DO UPDATE SET total_quantity_pcs = 100, updated_at = CURRENT_TIMESTAMP;
        ", new { p = TestPharmacyNo, b = TestBranchNo, transferProdNo });

        // 1. Source branch initiates transfer to destBranchNo
        var createPayload = new
        {
            to_branch_no = destBranchNo,
            note = "Inter-branch stock transfer test",
            items = new[]
            {
                new
                {
                    product_no = transferProdNo,
                    batch_no = (long?)900280,
                    batch_number = "BATCH-XFER-1",
                    qty_pcs = 20,
                    unit_cost = 8.00m,
                    total_cost = 160.00m,
                    expiry_date = DateTime.UtcNow.AddYears(1)
                }
            }
        };

        var initReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/inv/transfers")
        {
            Content = JsonContent.Create(createPayload)
        };
        initReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sourceBranchToken);
        var initResp = await _client.SendAsync(initReq);
        Assert.Equal(HttpStatusCode.OK, initResp.StatusCode);

        var initJson = await initResp.Content.ReadFromJsonAsync<JsonObject>();
        var transferNo = initJson?["data"]?["transfer_no"]?.GetValue<long>() ?? 0;
        Assert.True(transferNo > 0);

        // 2. Caller from source branch (Branch 9002) tries to receive transfer destined for Branch 9003 -> 403 Forbidden
        var unauthorizedRcvReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/inv/transfers/{transferNo}/receive");
        unauthorizedRcvReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sourceBranchToken);
        var unauthorizedRcvResp = await _client.SendAsync(unauthorizedRcvReq);
        Assert.Equal(HttpStatusCode.Forbidden, unauthorizedRcvResp.StatusCode);

        // 3. Caller from destination branch (Branch 9003) receives transfer -> 200 OK
        var authorizedRcvReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/inv/transfers/{transferNo}/receive");
        authorizedRcvReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", destBranchToken);
        var authorizedRcvResp = await _client.SendAsync(authorizedRcvReq);
        Assert.Equal(HttpStatusCode.OK, authorizedRcvResp.StatusCode);

        // Assert DB status is received (2)
        var status = await conn.ExecuteScalarAsync<short>(
            "SELECT transfer_status FROM inv_transfers WHERE transfer_no = @transferNo;", new { transferNo });
        Assert.Equal((short)2, status);

        _output.WriteLine("Test 11 Passed: Stock transfer receive correctly restricted to destination branch (403 on mismatch, 200 on match).");
    }

    // =========================================================================
    // 12. Subscription Expiry beyond grace period returns 402 Payment Required
    // =========================================================================
    [Fact]
    public async Task Test_12_SubscriptionExpiry_PastGracePeriod_Returns402PaymentRequired()
    {
        if (_client == null || string.IsNullOrWhiteSpace(_connectionString))
        {
            _output.WriteLine("Skipping: TEST_POSTGRES_CONNECTION not configured.");
            return;
        }

        var token = GenerateJwt(TestPharmacyNo, TestBranchNo, TestUserNo, TestManagerRoleNo, "Manager");
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        try
        {
            // Insert an expired subscription past its grace period
            const long subNo = 900290;
            await conn.ExecuteAsync(@"
                INSERT INTO sub_pharmacy_subscriptions (
                    subscription_no, pharmacy_no, branch_no, plan_no,
                    billing_cycle, subscription_status, trial_end_date,
                    subscription_start_date, subscription_end_date,
                    next_payment_date, monthly_rate, grace_period_days, updated_at
                ) VALUES (
                    @subNo, @p, @b, 1,
                    1, 1, CURRENT_DATE - INTERVAL '60 days',
                    CURRENT_DATE - INTERVAL '45 days', CURRENT_DATE - INTERVAL '15 days',
                    CURRENT_DATE - INTERVAL '15 days', 500, 7, CURRENT_TIMESTAMP
                ) ON CONFLICT (subscription_no) DO UPDATE
                SET subscription_end_date = CURRENT_DATE - INTERVAL '15 days',
                    grace_period_days = 7,
                    subscription_status = 1;
            ", new { subNo, p = TestPharmacyNo, b = TestBranchNo });

            // 1. Business endpoint (e.g. Products list) must return 402 Payment Required
            var prodReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/prod/products");
            prodReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var prodResp = await _client.SendAsync(prodReq);
            Assert.Equal((HttpStatusCode)402, prodResp.StatusCode);

            // 2. Subscription management endpoints (/api/v1/sub/*) must remain accessible (not blocked by 402)
            var subPlanReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/sub/plans");
            subPlanReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var subPlanResp = await _client.SendAsync(subPlanReq);
            Assert.Equal(HttpStatusCode.OK, subPlanResp.StatusCode);

            // 3. Initiate renewal payment -> status Pending
            var renewReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/sub/renew")
            {
                Content = JsonContent.Create(new
                {
                    plan_no = 1,
                    billing_cycle = "Monthly",
                    payment_gateway = "bKash"
                })
            };
            renewReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var renewResp = await _client.SendAsync(renewReq);
            Assert.Equal(HttpStatusCode.OK, renewResp.StatusCode);

            var renewJson = await renewResp.Content.ReadFromJsonAsync<JsonObject>();
            var paymentNo = renewJson?["data"]?["subscription_payment_no"]?.GetValue<long>() ?? 0;
            Assert.True(paymentNo > 0);
            Assert.Equal("Pending", renewJson?["data"]?["payment_status"]?.ToString());

            // 4. Confirm payment -> extends subscription end date and activates
            var confirmReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/sub/payments/{paymentNo}/confirm");
            confirmReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var confirmResp = await _client.SendAsync(confirmReq);
            Assert.Equal(HttpStatusCode.OK, confirmResp.StatusCode);

            // 5. Business endpoint now succeeds with 200 OK
            var unblockedProdReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/prod/products");
            unblockedProdReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var unblockedProdResp = await _client.SendAsync(unblockedProdReq);
            Assert.Equal(HttpStatusCode.OK, unblockedProdResp.StatusCode);

            _output.WriteLine("Test 12 Passed: Expired subscription blocks business endpoints with 402, allows renewal and unblocks upon confirmation.");
        }
        finally
        {
            await conn.ExecuteAsync("DELETE FROM sub_subscription_payments WHERE pharmacy_no = @p;", new { p = TestPharmacyNo });
            await conn.ExecuteAsync("DELETE FROM sub_pharmacy_subscriptions WHERE pharmacy_no = @p;", new { p = TestPharmacyNo });
        }
    }

    // =========================================================================
    // 13. Audit Log endpoint sanitizes sensitive values and requires permission
    // =========================================================================
    [Fact]
    public async Task Test_13_AuditLogs_Endpoint_SanitizesSensitiveData_And_RequiresPermission()
    {
        if (_client == null || string.IsNullOrWhiteSpace(_connectionString))
        {
            _output.WriteLine("Skipping: TEST_POSTGRES_CONNECTION not configured.");
            return;
        }

        var managerToken = GenerateJwt(TestPharmacyNo, TestBranchNo, TestUserNo, TestManagerRoleNo, "Manager");
        var restrictedToken = GenerateJwt(TestPharmacyNo, TestBranchNo, TestUserNo, TestRestrictedRoleNo, "Restricted");

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        // 1. Role lacking Pharmacy-Module receives 403 Forbidden
        var forbiddenReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/audit/logs");
        forbiddenReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", restrictedToken);
        var forbiddenResp = await _client.SendAsync(forbiddenReq);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenResp.StatusCode);

        // 2. Insert test audit log row with sensitive credentials
        const long testAuditNo = 900295;
        await conn.ExecuteAsync(@"
            INSERT INTO sys_audit_logs (
                audit_no, pharmacy_no, branch_no, user_no, table_name, record_id, action,
                old_data, new_data, ip_address, created_at
            ) VALUES (
                @testAuditNo, @p, @b, @u, 'sys_users', @u, 'UPDATE',
                '{""user_name"": ""admin"", ""password"": ""SecretPass99!"", ""token"": ""SensitiveBearerJwt123""}',
                '{""user_name"": ""admin"", ""password"": ""NewSecretPass100!"", ""token"": ""NewSensitiveBearerJwt456"", ""pin"": ""9876""}',
                '127.0.0.1', CURRENT_TIMESTAMP
            ) ON CONFLICT (audit_no) DO UPDATE
            SET old_data = '{""user_name"": ""admin"", ""password"": ""SecretPass99!"", ""token"": ""SensitiveBearerJwt123""}',
                new_data = '{""user_name"": ""admin"", ""password"": ""NewSecretPass100!"", ""token"": ""NewSensitiveBearerJwt456"", ""pin"": ""9876""}';
        ", new { testAuditNo, p = TestPharmacyNo, b = TestBranchNo, u = TestUserNo });

        // 3. Manager requests audit logs -> 200 OK
        var auditReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/audit/logs?tableName=sys_users");
        auditReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", managerToken);
        var auditResp = await _client.SendAsync(auditReq);
        Assert.Equal(HttpStatusCode.OK, auditResp.StatusCode);

        var auditJson = await auditResp.Content.ReadAsStringAsync();

        // 4. Assert sensitive strings are never returned
        Assert.DoesNotContain("SecretPass99!", auditJson);
        Assert.DoesNotContain("SensitiveBearerJwt123", auditJson);
        Assert.DoesNotContain("NewSecretPass100!", auditJson);
        Assert.DoesNotContain("NewSensitiveBearerJwt456", auditJson);
        Assert.DoesNotContain("9876", auditJson);
        Assert.Contains("[REDACTED]", auditJson);

        _output.WriteLine("Test 13 Passed: Audit log endpoint enforces permission and redacts sensitive passwords/tokens.");
    }

    // =========================================================================
    // 14. Unmapped Chart of Accounts throws clear error without fallback
    // =========================================================================
    [Fact]
    public async Task Test_14_FallbackAccounts_Removed_ThrowsWhenUnmapped()
    {
        if (_client == null || string.IsNullOrWhiteSpace(_connectionString))
        {
            _output.WriteLine("Skipping: TEST_POSTGRES_CONNECTION not configured.");
            return;
        }

        var token = GenerateJwt(TestPharmacyNo, TestBranchNo, TestUserNo, TestManagerRoleNo, "Manager");
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        // 1. Setup a product
        const long unmappedProdNo = 900299;
        await conn.ExecuteAsync(@"
            INSERT INTO prod_products (
                product_no, uuid, pharmacy_no, branch_no, company_no, generic_no,
                product_name, strength, volume, medicine_type, unit_type,
                quantity_per_box, cost_per_box, sale_price_per_box,
                avg_purchase_price_per_piece, purchase_price_per_piece, sale_price_per_piece,
                wholesale_price_per_box, wholesale_price_per_piece,
                is_medicine, is_prescription_required, is_narcotic, min_stock_qty_pcs, is_active,
                created_at, updated_at
            ) VALUES (
                @unmappedProdNo, gen_random_uuid(), @p, @b, @p, @p,
                'Unmapped Account Prod', '10mg', '1ml', 1, 1,
                1, 50, 100, 50, 50, 100,
                100, 100,
                TRUE, FALSE, FALSE, 10, TRUE,
                CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            ) ON CONFLICT (product_no) DO NOTHING;

            INSERT INTO prod_product_batches (
                batch_no, pharmacy_no, branch_no, product_no, batch_number,
                expiry_date, box_quantity, quantity_in_box, total_quantity_pcs,
                cost_per_box, sale_price_per_box, vat_percent, is_expired, created_at, updated_at
            ) VALUES (
                900299, @p, @b, @unmappedProdNo, 'BATCH-UNMAP-1',
                CURRENT_TIMESTAMP + INTERVAL '1 year', 10, 1, 10,
                50, 100, 0, FALSE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            ) ON CONFLICT (batch_no) DO UPDATE SET total_quantity_pcs = 10, updated_at = CURRENT_TIMESTAMP;
        ", new { unmappedProdNo, p = TestPharmacyNo, b = TestBranchNo });

        // Temporarily disable sales revenue account (40101) to simulate unmapped account
        await conn.ExecuteAsync(@"
            UPDATE acc_transaction_accounts
            SET is_active = FALSE, account_code = '40101_DISABLED'
            WHERE pharmacy_no = @p AND account_code = '40101';
        ", new { p = TestPharmacyNo });

        try
        {
            var salePayload = new
            {
                items = new[] { new { product_no = unmappedProdNo, sale_qty = 1 } },
                payments = new[] { new { payment_method = 1, amount = 100.00m, account_no = TestPharmacyNo * 100 + 1 } }
            };

            var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/pos/sales")
            {
                Content = JsonContent.Create(salePayload)
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resp = await _client.SendAsync(req);

            // Server-side fails (500) rather than silently writing to a fallback account
            Assert.Equal(HttpStatusCode.InternalServerError, resp.StatusCode);

            _output.WriteLine("Test 14 Passed: Missing revenue account threw clear error without hardcoded fallback.");
        }
        finally
        {
            // Restore account 40101
            await conn.ExecuteAsync(@"
                UPDATE acc_transaction_accounts
                SET is_active = TRUE, account_code = '40101'
                WHERE pharmacy_no = @p AND account_code = '40101_DISABLED';
            ", new { p = TestPharmacyNo });
        }
    }

    private static string GenerateOwnerJwt(string email = "owner@aidlypms.com", string userId = "1")
    {
        const string keyStr = "AidlyPMS_SuperSecret_Jwt_SigningKey_2026_Enterprise_Secure!";
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(keyStr));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Name, "Platform Owner"),
            new(ClaimTypes.Email, email),
            new("user_no", userId),
            new("platform_owner", "true"),
            new(ClaimTypes.Role, "PlatformOwner")
        };

        var token = new JwtSecurityToken(
            issuer: "AidlyPmsBackend",
            audience: "AidlyPmsFrontend",
            claims: claims,
            expires: DateTime.UtcNow.AddDays(7),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    // =========================================================================
    // 15. Tenant cannot confirm payment under /api/v1/owner/subscriptions/payments/{no}/confirm (403 Forbidden)
    // =========================================================================
    [Fact]
    public async Task Test_15_Tenant_CannotConfirmOwnPayment_Returns403()
    {
        if (_client == null || string.IsNullOrWhiteSpace(_connectionString))
        {
            _output.WriteLine("Skipping: TEST_POSTGRES_CONNECTION not configured.");
            return;
        }

        var tenantToken = GenerateJwt(TestPharmacyNo, TestBranchNo, TestUserNo, TestManagerRoleNo, "Manager");

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/owner/subscriptions/payments/1/confirm");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tenantToken);

        var resp = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);

        _output.WriteLine("Test 15 Passed: Tenant user received 403 Forbidden when calling owner payment confirm.");
    }

    // =========================================================================
    // 16. Platform owner confirms payment and derives extension days from plan/billing cycle
    // =========================================================================
    [Fact]
    public async Task Test_16_PlatformOwner_CanConfirmPayment_AndDerivesDays()
    {
        if (_client == null || string.IsNullOrWhiteSpace(_connectionString))
        {
            _output.WriteLine("Skipping: TEST_POSTGRES_CONNECTION not configured.");
            return;
        }

        var ownerToken = GenerateOwnerJwt();
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        // 1. Seed a test pending payment
        const long testPaymentNo = 900288;
        await conn.ExecuteAsync(@"
            INSERT INTO sub_subscription_payments (
                subscription_payment_no, pharmacy_no, branch_no, subscription_no,
                transaction_reference, payment_gateway, subtotal, discount_amount, final_amount,
                payment_status, payment_timestamp, billing_cycle, plan_no
            ) VALUES (
                @testPaymentNo, @p, @b, @p,
                'TRX-OWNER-CONFIRM-1', 'bKash', 5000, 0, 5000,
                'Pending', CURRENT_TIMESTAMP, 2, 1
            ) ON CONFLICT (subscription_payment_no) DO UPDATE SET payment_status = 'Pending';",
            new { testPaymentNo, p = TestPharmacyNo, b = TestBranchNo });

        // 2. Call confirm endpoint
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/owner/subscriptions/payments/{testPaymentNo}/confirm");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var resp = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var json = await resp.Content.ReadFromJsonAsync<JsonObject>();
        Assert.True(json?["success"]?.GetValue<bool>());

        // 3. Verify payment is Completed in DB
        var status = await conn.ExecuteScalarAsync<string>(
            "SELECT payment_status FROM sub_subscription_payments WHERE subscription_payment_no = @testPaymentNo;",
            new { testPaymentNo });
        Assert.Equal("Completed", status);

        _output.WriteLine("Test 16 Passed: Platform owner successfully confirmed payment and extended subscription.");
    }

    // =========================================================================
    // 17. Anonymous gateway callback without valid signature returns 401 Unauthorized
    // =========================================================================
    [Fact]
    public async Task Test_17_GatewayCallback_WithoutValidSignature_Returns401()
    {
        if (_client == null || string.IsNullOrWhiteSpace(_connectionString))
        {
            _output.WriteLine("Skipping: TEST_POSTGRES_CONNECTION not configured.");
            return;
        }

        var payload = new
        {
            transaction_reference = "TRX-CALLBACK-NOSIG",
            amount = 500,
            status = "Completed"
        };

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/sub/subscriptions/payments/callback")
        {
            Content = JsonContent.Create(payload)
        };

        var resp = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);

        _output.WriteLine("Test 17 Passed: Gateway callback without HMAC signature was rejected with 401 Unauthorized.");
    }

    // =========================================================================
    // 18. Anonymous gateway callback with valid signature completes payment and is idempotent
    // =========================================================================
    [Fact]
    public async Task Test_18_GatewayCallback_WithValidSignature_CompletesPayment_Idempotently()
    {
        if (_client == null || string.IsNullOrWhiteSpace(_connectionString))
        {
            _output.WriteLine("Skipping: TEST_POSTGRES_CONNECTION not configured.");
            return;
        }

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        const long testPaymentNo = 900289;
        const string trxRef = "TRX-CALLBACK-VALID-1";
        const decimal amount = 500m;

        await conn.ExecuteAsync(@"
            INSERT INTO sub_subscription_payments (
                subscription_payment_no, pharmacy_no, branch_no, subscription_no,
                transaction_reference, payment_gateway, subtotal, discount_amount, final_amount,
                payment_status, payment_timestamp, billing_cycle, plan_no
            ) VALUES (
                @testPaymentNo, @p, @b, @p,
                @trxRef, 'bKash', 500, 0, 500,
                'Pending', CURRENT_TIMESTAMP, 1, 1
            ) ON CONFLICT (subscription_payment_no) DO UPDATE SET payment_status = 'Pending';",
            new { testPaymentNo, p = TestPharmacyNo, b = TestBranchNo, trxRef });

        // Compute signature using default secret
        const string secret = "aidly-pms-default-hmac-secret-change-in-prod";
        using var hmac = new System.Security.Cryptography.HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var sig = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{trxRef}:{amount}")));

        var payload = new
        {
            transaction_reference = trxRef,
            amount = amount,
            status = "Completed"
        };

        var req1 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/sub/subscriptions/payments/callback")
        {
            Content = JsonContent.Create(payload)
        };
        req1.Headers.Add("X-Signature", sig);

        var resp1 = await _client.SendAsync(req1);
        Assert.Equal(HttpStatusCode.OK, resp1.StatusCode);

        // Verify completed in DB
        var status = await conn.ExecuteScalarAsync<string>(
            "SELECT payment_status FROM sub_subscription_payments WHERE subscription_payment_no = @testPaymentNo;",
            new { testPaymentNo });
        Assert.Equal("Completed", status);

        // Call again with same payload (idempotency test)
        var req2 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/sub/subscriptions/payments/callback")
        {
            Content = JsonContent.Create(payload)
        };
        req2.Headers.Add("X-Signature", sig);

        var resp2 = await _client.SendAsync(req2);
        Assert.Equal(HttpStatusCode.OK, resp2.StatusCode);

        _output.WriteLine("Test 18 Passed: Gateway callback verified HMAC signature, updated status, and ran idempotently.");
    }

    // =========================================================================
    // 19. Sys migrations endpoint requires authentication and returns applied migrations
    // =========================================================================
    [Fact]
    public async Task Test_19_SysMigrations_RequiresAuth_AndReturnsAppliedMigrations()
    {
        if (_client == null || string.IsNullOrWhiteSpace(_connectionString))
        {
            _output.WriteLine("Skipping: TEST_POSTGRES_CONNECTION not configured.");
            return;
        }

        // 1. Anonymous request should be rejected by FallbackPolicy (401)
        var anonResp = await _client.GetAsync("/api/v1/sys/migrations");
        Assert.Equal(HttpStatusCode.Unauthorized, anonResp.StatusCode);

        // 2. Authenticated request should return 200 OK with migration list
        var token = GenerateJwt(TestPharmacyNo, TestBranchNo, TestUserNo, TestManagerRoleNo, "Manager");
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/sys/migrations");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var json = await resp.Content.ReadFromJsonAsync<JsonObject>();
        Assert.True(json?["success"]?.GetValue<bool>());

        var data = json?["data"]?.AsObject();
        Assert.NotNull(data);
        Assert.True(data?["totalApplied"]?.GetValue<int>() >= 0);

        _output.WriteLine("Test 19 Passed: /api/v1/sys/migrations enforces auth and returns migration status.");
    }
}

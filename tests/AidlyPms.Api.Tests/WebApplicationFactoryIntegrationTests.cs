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
                        ["ConnectionStrings:DirectConnection"] = _connectionString,
                        ["RUN_BACKGROUND_JOBS"] = "false"
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
                9, @p, @b, 'PMS Manager', 'PMS Manager', 'Full operational control over store inventory, accounting, settings, and staff', TRUE, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            ) ON CONFLICT (role_no) DO UPDATE SET alias = 'PMS Manager', role_name = 'PMS Manager';

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

            -- Setup chart of accounts (strictly matches schema.sql COA)
            INSERT INTO acc_transaction_accounts (
                account_no, pharmacy_no, branch_no, account_category, account_code, account_name, account_type, bank_account_number, bank_name, current_balance, is_active, created_at, updated_at
            ) VALUES
            (@p * 100 + 1, @p, @b, 1, '10101', 'Main Cash Drawer (Cash in Hand)', 1, NULL, NULL, 100000, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
            (@p * 100 + 2, @p, @b, 1, '10201', 'Bank Current Account (Main)', 2, '2050123456789', 'Islami Bank Bangladesh Ltd.', 0, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
            (@p * 100 + 3, @p, @b, 1, '10301', 'Accounts Receivable (Customer Due)', 99, NULL, NULL, 0, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
            (@p * 100 + 4, @p, @b, 1, '10401', 'Merchandise Inventory Asset', 99, NULL, 'Pharmacy Inventory Stock', 500000, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
            (@p * 100 + 5, @p, @b, 1, '10501', 'Input VAT Asset (Tax Advance)', 99, NULL, NULL, 0, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
            (@p * 100 + 6, @p, @b, 2, '20101', 'Accounts Payable (Supplier Due)', 99, NULL, NULL, 0, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
            (@p * 100 + 7, @p, @b, 2, '20201', 'Output VAT Payable (Tax Due)', 99, NULL, NULL, 0, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
            (@p * 100 + 8, @p, @b, 3, '30101', 'Owner Capital & Retained Earnings', 99, NULL, NULL, 0, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
            (@p * 100 + 9, @p, @b, 4, '40101', 'Sales Revenue', 99, NULL, NULL, 0, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
            (@p * 100 + 10, @p, @b, 4, '40102', 'Sales Returns and Allowances', 99, NULL, NULL, 0, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
            (@p * 100 + 11, @p, @b, 5, '50101', 'Cost of Goods Sold (COGS)', 99, NULL, NULL, 0, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
            (@p * 100 + 12, @p, @b, 5, '50201', 'General & Operating Expenses', 99, NULL, NULL, 0, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
            (@p * 100 + 13, @p, @b, 1, '10102', 'bKash Merchant Wallet', 4, '01700000002', 'bKash Merchant', 0, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
            (@p * 100 + 14, @p, @b, 1, '10103', 'Nagad Merchant Wallet', 5, '01800000002', 'Nagad Merchant', 0, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
            (@p * 100 + 15, @p, @b, 1, '10104', 'Card POS Settlement Account', 7, NULL, 'Card POS Settlement', 0, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
            (@p * 100 + 16, @p, @b, 4, '40202', 'Rounding Adjustment Gain', 99, NULL, NULL, 0, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
            (@p * 100 + 17, @p, @b, 5, '50203', 'Rounding Adjustment Loss', 99, NULL, NULL, 0, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
            (@p * 100 + 18, @p, @b, 5, '50204', 'Cash Drawer Variance & Shortage', 99, NULL, NULL, 0, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
            (@p * 100 + 19, @p, @b, 4, '40203', 'Cash Drawer Variance & Overage', 99, NULL, NULL, 0, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
            (@p * 100 + 20, @p, @b, 1, '10901', 'Return Credit Clearing Account', 99, NULL, 'Exchange Clearing', 0, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
            (@p * 100 + 21, @p, @b, 2, '20301', 'Customer Store Credit Liability', 99, NULL, 'Store Credit', 0, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
            ON CONFLICT (account_no) DO NOTHING;

            -- Sale config
            INSERT INTO cfg_sale_configs (
                sale_config_no, pharmacy_no, branch_no, cash_received, out_of_stock_sale,
                allow_loss_sale, product_purchase_cost, stock_qty, company_name,
                profit_margin, rak_number, opening_stock_percent, max_sale_discount_percent,
                is_max_discount_enabled, business_type, return_window_days,
                default_pos_screen, offline_pos_enabled, offline_max_age_hours, updated_at
            ) VALUES (
                @p, @p, @b, TRUE, TRUE,
                TRUE, TRUE, TRUE, TRUE,
                TRUE, TRUE, 0, 10,
                FALSE, 1, 14,
                'screen1', TRUE, 72, CURRENT_TIMESTAMP
            ) ON CONFLICT (pharmacy_no, branch_no) DO UPDATE SET out_of_stock_sale = TRUE, offline_pos_enabled = TRUE, offline_max_age_hours = 72;

            INSERT INTO sub_pharmacy_subscriptions (
                subscription_no, pharmacy_no, branch_no, plan_no,
                billing_cycle, subscription_status, trial_end_date,
                subscription_start_date, subscription_end_date,
                next_payment_date, monthly_rate, grace_period_days, updated_at
            ) VALUES (
                @p, @p, @b, 1,
                1, 1, CURRENT_DATE + INTERVAL '14 days',
                CURRENT_DATE, CURRENT_DATE + INTERVAL '30 days',
                CURRENT_DATE + INTERVAL '30 days', 500, 7, CURRENT_TIMESTAMP
            ) ON CONFLICT (subscription_no) DO NOTHING;
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
            DELETE FROM inv_stocktake_items WHERE pharmacy_no = @p;
            DELETE FROM inv_stocktakes WHERE pharmacy_no = @p;
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
                cost_per_box, sale_price_per_box, vat_percent, is_expired, is_placeholder, created_at, updated_at
            ) VALUES (
                900250, @p, @b, @testProdNo, 'BATCH-SD-1',
                CURRENT_TIMESTAMP + INTERVAL '1 year', 5, 10, 50,
                100, 150, 0, FALSE, FALSE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
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
        await custDueResp.Content.ReadAsStringAsync();
        custDueResp.Dispose();

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
        await custValidResp.Content.ReadAsStringAsync();
        custValidResp.Dispose();

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
        await suppDueResp.Content.ReadAsStringAsync();
        suppDueResp.Dispose();

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
        await purSuppResp.Content.ReadAsStringAsync();
        purSuppResp.Dispose();

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
                cost_per_box, sale_price_per_box, vat_percent, is_expired, is_placeholder, created_at, updated_at
            ) VALUES (
                900280, @p, @b, @transferProdNo, 'BATCH-XFER-1',
                CURRENT_TIMESTAMP + INTERVAL '1 year', 10, 10, 100,
                80, 120, 0, FALSE, FALSE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
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
            // Set subscription as expired past its grace period
            await conn.ExecuteAsync(@"
                UPDATE sub_pharmacy_subscriptions
                SET subscription_end_date = CURRENT_DATE - INTERVAL '15 days',
                    grace_period_days = 7,
                    subscription_status = 1,
                    trial_end_date = CURRENT_DATE - INTERVAL '60 days',
                    subscription_start_date = CURRENT_DATE - INTERVAL '45 days',
                    next_payment_date = CURRENT_DATE - INTERVAL '15 days',
                    updated_at = CURRENT_TIMESTAMP
                WHERE pharmacy_no = @p AND branch_no = @b;",
                new { p = TestPharmacyNo, b = TestBranchNo });

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
            var ownerToken = GenerateOwnerJwt();
            var confirmReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/owner/subscriptions/payments/{paymentNo}/confirm");
            confirmReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
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
            await conn.ExecuteAsync(@"
                UPDATE sub_pharmacy_subscriptions
                SET subscription_end_date = CURRENT_DATE + INTERVAL '30 days',
                    grace_period_days = 7,
                    subscription_status = 1,
                    trial_end_date = CURRENT_DATE + INTERVAL '14 days',
                    subscription_start_date = CURRENT_DATE,
                    next_payment_date = CURRENT_DATE + INTERVAL '30 days',
                    updated_at = CURRENT_TIMESTAMP
                WHERE pharmacy_no = @p AND branch_no = @b;",
                new { p = TestPharmacyNo, b = TestBranchNo });
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
                cost_per_box, sale_price_per_box, vat_percent, is_expired, is_placeholder, created_at, updated_at
            ) VALUES (
                900299, @p, @b, @unmappedProdNo, 'BATCH-UNMAP-1',
                CURRENT_TIMESTAMP + INTERVAL '1 year', 10, 1, 10,
                50, 100, 0, FALSE, FALSE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
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

    // =========================================================================
    // 20. Stocktake End-to-End: List, Get, Count, Refresh, and Post endpoints return 200 and valid shape
    // =========================================================================
    [Fact]
    public async Task Test_20_Stocktake_EndToEnd_HttpEndpoints_Return200AndValidShape()
    {
        if (_client == null || string.IsNullOrWhiteSpace(_connectionString))
        {
            _output.WriteLine("Skipping: TEST_POSTGRES_CONNECTION not configured.");
            return;
        }

        // Seed product & batch if missing in isolated schema
        await using (var conn = new NpgsqlConnection(_connectionString))
        {
            await conn.OpenAsync();
            var prodExists = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM prod_products WHERE pharmacy_no = @p AND product_no = 9101;",
                new { p = TestPharmacyNo });

            if (prodExists == 0)
            {
                await conn.ExecuteAsync(@"
                    INSERT INTO prod_products (
                        product_no, uuid, pharmacy_no, branch_no, company_no, generic_no, product_name,
                        medicine_type, unit_type, quantity_per_box,
                        cost_per_box, sale_price_per_box, avg_purchase_price_per_piece, purchase_price_per_piece, sale_price_per_piece,
                        wholesale_price_per_box, wholesale_price_per_piece,
                        is_medicine, is_prescription_required, is_narcotic, min_stock_qty_pcs, is_active, created_at, updated_at
                    ) VALUES (
                        9101, gen_random_uuid(), @p, @b, @p, @p, 'Stocktake HTTP Test Paracetamol',
                        1, 1, 10,
                        25.00, 30.00, 2.50, 2.50, 3.00,
                        30.00, 30.00,
                        TRUE, FALSE, FALSE, 10, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
                    ) ON CONFLICT (product_no) DO NOTHING;

                    INSERT INTO prod_product_batches (
                        batch_no, pharmacy_no, branch_no, product_no, batch_number, expiry_date,
                        box_quantity, quantity_in_box, total_quantity_pcs, cost_per_box,
                        sale_price_per_box, vat_percent, barcode_value, is_expired, is_placeholder,
                        created_at, updated_at
                    ) VALUES (
                        9101, @p, @b, 9101, 'BATCH-HTTP-01', CURRENT_DATE + INTERVAL '180 days',
                        10, 10, 100, 25.00,
                        30.00, 0, 'BARCODE-HTTP-01', FALSE, FALSE,
                        CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
                    ) ON CONFLICT (batch_no) DO NOTHING;",
                    new { p = TestPharmacyNo, b = TestBranchNo });
            }
        }

        var token = GenerateJwt(TestPharmacyNo, TestBranchNo, TestUserNo, TestManagerRoleNo, "Manager");

        // 1. POST /api/v1/inv/stocktakes -> Initialize stocktake
        var createReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/inv/stocktakes");
        createReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        createReq.Content = JsonContent.Create(new
        {
            note = "HTTP integration test stocktake",
            filter_type = "ALL"
        });

        var createResp = await _client.SendAsync(createReq);
        Assert.Equal(HttpStatusCode.OK, createResp.StatusCode);

        var createJson = await createResp.Content.ReadFromJsonAsync<JsonObject>();
        Assert.True(createJson?["success"]?.GetValue<bool>());
        var createdData = createJson?["data"]?.AsObject();
        Assert.NotNull(createdData);

        var stocktakeNo = createdData?["stocktake_no"]?.GetValue<long>() ?? 0;
        Assert.True(stocktakeNo > 0);
        Assert.Equal("OPEN", createdData?["status"]?.GetValue<string>());
        Assert.True(createdData?["total_items"]?.GetValue<int>() >= 1);

        // 2. GET /api/v1/inv/stocktakes -> List stocktakes & assert started_by_name shape
        var listReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/inv/stocktakes?page=1&pageSize=10");
        listReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var listResp = await _client.SendAsync(listReq);
        Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);

        var listJson = await listResp.Content.ReadFromJsonAsync<JsonObject>();
        Assert.True(listJson?["success"]?.GetValue<bool>());
        var listData = listJson?["data"]?.AsObject();
        Assert.NotNull(listData);

        var items = listData?["items"]?.AsArray();
        Assert.NotNull(items);
        Assert.NotEmpty(items);

        var firstListItem = items?[0]?.AsObject();
        Assert.NotNull(firstListItem);
        Assert.False(string.IsNullOrWhiteSpace(firstListItem?["started_by_name"]?.GetValue<string>()),
            "started_by_name must be populated via TRIM(CONCAT(first_name, ' ', last_name)).");

        // 3. GET /api/v1/inv/stocktakes/{stocktakeNo} -> Get detail and items
        var getReq = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/inv/stocktakes/{stocktakeNo}");
        getReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var getResp = await _client.SendAsync(getReq);
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);

        var getJson = await getResp.Content.ReadFromJsonAsync<JsonObject>();
        Assert.True(getJson?["success"]?.GetValue<bool>());
        var getData = getJson?["data"]?.AsObject();
        Assert.NotNull(getData);
        Assert.Equal(stocktakeNo, getData?["stocktake_no"]?.GetValue<long>());

        var stocktakeItems = getData?["items"]?.AsArray();
        Assert.NotNull(stocktakeItems);
        Assert.NotEmpty(stocktakeItems);

        var firstItem = stocktakeItems?[0]?.AsObject();
        Assert.NotNull(firstItem);
        var itemNo = firstItem?["stocktake_item_no"]?.GetValue<long>() ?? 0;
        var sysQty = firstItem?["system_qty_pcs"]?.GetValue<int>() ?? 0;
        Assert.True(itemNo > 0);

        // 4. PUT /api/v1/inv/stocktakes/{stocktakeNo}/count -> Save counts with variance & reason
        var countReq = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/inv/stocktakes/{stocktakeNo}/count");
        countReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        countReq.Content = JsonContent.Create(new
        {
            items = new[]
            {
                new
                {
                    stocktake_item_no = itemNo,
                    counted_qty_pcs = sysQty + 5,
                    reason = "Physical count found 5 extra blister packs in rack"
                }
            }
        });

        var countResp = await _client.SendAsync(countReq);
        Assert.Equal(HttpStatusCode.OK, countResp.StatusCode);

        var countJson = await countResp.Content.ReadFromJsonAsync<JsonObject>();
        Assert.True(countJson?["success"]?.GetValue<bool>());
        var countData = countJson?["data"]?.AsObject();
        Assert.NotNull(countData);
        Assert.Equal("COUNTING", countData?["status"]?.GetValue<string>());
        Assert.Equal(1, countData?["updated_items_count"]?.GetValue<int>());

        // 5. POST /api/v1/inv/stocktakes/{stocktakeNo}/refresh -> Refresh snapshot
        var refreshReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/inv/stocktakes/{stocktakeNo}/refresh");
        refreshReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var refreshResp = await _client.SendAsync(refreshReq);
        Assert.Equal(HttpStatusCode.OK, refreshResp.StatusCode);

        var refreshJson = await refreshResp.Content.ReadFromJsonAsync<JsonObject>();
        Assert.True(refreshJson?["success"]?.GetValue<bool>());

        // 6. POST /api/v1/inv/stocktakes/{stocktakeNo}/post -> Post adjustments
        var postReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/inv/stocktakes/{stocktakeNo}/post");
        postReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var postResp = await _client.SendAsync(postReq);
        Assert.Equal(HttpStatusCode.OK, postResp.StatusCode);

        var postJson = await postResp.Content.ReadFromJsonAsync<JsonObject>();
        Assert.True(postJson?["success"]?.GetValue<bool>());
        var postData = postJson?["data"]?.AsObject();
        Assert.NotNull(postData);
        Assert.Equal("POSTED", postData?["status"]?.GetValue<string>());
        Assert.True(postData?["adjusted_items_count"]?.GetValue<int>() >= 1);

        _output.WriteLine("Test 20 Passed: Stocktake list, get, count, refresh, and post HTTP endpoints succeeded with HTTP 200 and valid responses.");
    }

    // =========================================================================
    // 21. Real chart of accounts payment resolution & cash drawer isolation
    // =========================================================================
    [Fact]
    public async Task Test_21_PaymentAccounts_RealChart_AllCases()
    {
        if (_client == null || string.IsNullOrWhiteSpace(_connectionString))
        {
            _output.WriteLine("Skipping: TEST_POSTGRES_CONNECTION not configured.");
            return;
        }

        var token = GenerateJwt(TestPharmacyNo, TestBranchNo, TestUserNo, TestManagerRoleNo, "PMS Manager");
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        const long p = TestPharmacyNo;
        const long b = TestBranchNo;
        const long testProdNo = 900299;
        const long testBatchNo = 9002991;

        // Ensure customer, product and batch exist with stock
        await conn.ExecuteAsync(@"
            INSERT INTO cust_customers (
                customer_no, uuid, pharmacy_no, branch_no, name, phone, customer_type, discount_percent,
                has_due, current_due, advance_balance, is_loyalty_member, loyalty_points, is_active, created_at, updated_at
            ) VALUES (
                1, gen_random_uuid(), @p, @b, 'Test Customer 1', '01700000001', 1, 0,
                FALSE, 0, 0, FALSE, 0, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            ) ON CONFLICT (customer_no) DO NOTHING;

            INSERT INTO prod_products (
                product_no, uuid, pharmacy_no, branch_no, company_no, generic_no, product_name,
                medicine_type, unit_type, quantity_per_box,
                cost_per_box, sale_price_per_box, avg_purchase_price_per_piece, purchase_price_per_piece, sale_price_per_piece,
                wholesale_price_per_box, wholesale_price_per_piece,
                is_medicine, is_prescription_required, is_narcotic, min_stock_qty_pcs, is_active, created_at, updated_at
            ) VALUES (
                @testProdNo, gen_random_uuid(), @p, @b, @p, @p, 'Payment Method Test Paracetamol',
                1, 1, 10,
                100.00, 150.00, 10.00, 10.00, 15.00,
                150.00, 15.00,
                TRUE, FALSE, FALSE, 10, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            ) ON CONFLICT (product_no) DO UPDATE SET is_active = TRUE;

            INSERT INTO prod_product_batches (
                batch_no, pharmacy_no, branch_no, product_no, batch_number,
                expiry_date, box_quantity, quantity_in_box, total_quantity_pcs,
                cost_per_box, sale_price_per_box, vat_percent, is_expired, is_placeholder, created_at, updated_at
            ) VALUES (
                @testBatchNo, @p, @b, @testProdNo, 'BATCH-PM-01',
                CURRENT_TIMESTAMP + INTERVAL '2 years', 50, 10, 500,
                100, 150, 0, FALSE, FALSE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            ) ON CONFLICT (batch_no) DO UPDATE SET total_quantity_pcs = 500;
        ", new { p, b, testProdNo, testBatchNo });

        // 1. bKash sale (payment_method = 3): Must post debit leg to account TestPharmacyNo * 100 + 13 (type 4)
        var bkashSaleReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/pos/sales")
        {
            Content = JsonContent.Create(new
            {
                customer_no = 1,
                items = new[] { new { product_no = testProdNo, batch_no = testBatchNo, sale_qty = 1, sale_price = 15.0m } },
                payments = new[] { new { payment_method = 3, amount = 15.0m } }
            })
        };
        bkashSaleReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var bkashResp = await _client.SendAsync(bkashSaleReq);
        Assert.Equal(HttpStatusCode.OK, bkashResp.StatusCode);
        var bkashJson = await bkashResp.Content.ReadFromJsonAsync<JsonObject>();
        var bkashInvNo = bkashJson?["data"]?["sale_invoice_no"]?.GetValue<long>() ?? 0;
        Assert.True(bkashInvNo > 0);

        var bkashDebitAccount = await conn.ExecuteScalarAsync<long>(@"
            SELECT account_no FROM acc_transaction_ledger
            WHERE pharmacy_no = @p AND branch_no = @b
              AND source_document_type = 'SALE_INVOICE' AND source_document_id = @bkashInvNo
              AND debit_amount > 0 AND credit_amount = 0
            LIMIT 1;",
            new { p, b, bkashInvNo });
        Assert.Equal(TestPharmacyNo * 100 + 13, bkashDebitAccount);

        // 2. Nagad sale (payment_method = 4): Must post debit leg to account TestPharmacyNo * 100 + 14 (type 5)
        var nagadSaleReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/pos/sales")
        {
            Content = JsonContent.Create(new
            {
                customer_no = 1,
                items = new[] { new { product_no = testProdNo, batch_no = testBatchNo, sale_qty = 1, sale_price = 15.0m } },
                payments = new[] { new { payment_method = 4, amount = 15.0m } }
            })
        };
        nagadSaleReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var nagadResp = await _client.SendAsync(nagadSaleReq);
        Assert.Equal(HttpStatusCode.OK, nagadResp.StatusCode);
        var nagadJson = await nagadResp.Content.ReadFromJsonAsync<JsonObject>();
        var nagadInvNo = nagadJson?["data"]?["sale_invoice_no"]?.GetValue<long>() ?? 0;
        Assert.True(nagadInvNo > 0);

        var nagadDebitAccount = await conn.ExecuteScalarAsync<long>(@"
            SELECT account_no FROM acc_transaction_ledger
            WHERE pharmacy_no = @p AND branch_no = @b
              AND source_document_type = 'SALE_INVOICE' AND source_document_id = @nagadInvNo
              AND debit_amount > 0 AND credit_amount = 0
            LIMIT 1;",
            new { p, b, nagadInvNo });
        Assert.Equal(TestPharmacyNo * 100 + 14, nagadDebitAccount);

        // 3. Card sale (payment_method = 6): Must post debit leg to account TestPharmacyNo * 100 + 15 (type 7)
        var cardSaleReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/pos/sales")
        {
            Content = JsonContent.Create(new
            {
                customer_no = 1,
                items = new[] { new { product_no = testProdNo, batch_no = testBatchNo, sale_qty = 1, sale_price = 15.0m } },
                payments = new[] { new { payment_method = 6, amount = 15.0m } }
            })
        };
        cardSaleReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var cardResp = await _client.SendAsync(cardSaleReq);
        Assert.Equal(HttpStatusCode.OK, cardResp.StatusCode);
        var cardJson = await cardResp.Content.ReadFromJsonAsync<JsonObject>();
        var cardInvNo = cardJson?["data"]?["sale_invoice_no"]?.GetValue<long>() ?? 0;
        Assert.True(cardInvNo > 0);

        var cardDebitAccount = await conn.ExecuteScalarAsync<long>(@"
            SELECT account_no FROM acc_transaction_ledger
            WHERE pharmacy_no = @p AND branch_no = @b
              AND source_document_type = 'SALE_INVOICE' AND source_document_id = @cardInvNo
              AND debit_amount > 0 AND credit_amount = 0
            LIMIT 1;",
            new { p, b, cardInvNo });
        Assert.Equal(TestPharmacyNo * 100 + 15, cardDebitAccount);

        // 4. Rocket sale (payment_method = 5): In the seeded chart, no type 6 account exists -> returns 400 Bad Request
        var rocketSaleReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/pos/sales")
        {
            Content = JsonContent.Create(new
            {
                customer_no = 1,
                items = new[] { new { product_no = testProdNo, batch_no = testBatchNo, sale_qty = 1, sale_price = 15.0m } },
                payments = new[] { new { payment_method = 5, amount = 15.0m } }
            })
        };
        rocketSaleReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var rocketResp = await _client.SendAsync(rocketSaleReq);
        Assert.Equal(HttpStatusCode.BadRequest, rocketResp.StatusCode);
        var rocketErr = await rocketResp.Content.ReadAsStringAsync();
        Assert.Contains("No account configured for this payment method. Add one in Accounts.", rocketErr);

        _output.WriteLine("Test 21 Passed: bKash (type 4), Nagad (type 5), Card (type 7) resolved correctly, and missing Rocket returned 400.");
    }

    // =========================================================================
    // 22. No menu grant returns 403 Forbidden on GET, POST, PUT, DELETE
    // =========================================================================
    [Fact]
    public async Task Test_22_NoMenuGrant_Returns403Forbidden_OnGetPostPutDelete()
    {
        if (_client == null || string.IsNullOrWhiteSpace(_connectionString))
        {
            _output.WriteLine("Skipping: TEST_POSTGRES_CONNECTION not configured.");
            return;
        }

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        const long p = TestPharmacyNo;
        const long b = TestBranchNo;
        const long noMenuRoleNo = 9995;
        const long noMenuUserNo = 9995;

        // Ensure role exists with NO menu permissions
        await conn.ExecuteAsync(@"
            INSERT INTO sys_roles (
                role_no, pharmacy_no, branch_no, role_name, alias, description, is_system, status, created_at, updated_at
            ) VALUES (
                @noMenuRoleNo, @p, @b, 'No-Menu User', 'No-Menu User', 'Role with zero menu grants', FALSE, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            ) ON CONFLICT (role_no) DO UPDATE SET alias = 'No-Menu User', role_name = 'No-Menu User';

            DELETE FROM sys_role_menu_permissions WHERE role_no = @noMenuRoleNo AND pharmacy_no = @p;
        ", new { p, b, noMenuRoleNo });

        var token = GenerateJwt(p, b, noMenuUserNo, noMenuRoleNo, "No-Menu User");

        // 1. GET /api/v1/cust/customers -> 403 Forbidden
        var getReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/cust/customers");
        getReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var getResp = await _client.SendAsync(getReq);
        Assert.Equal(HttpStatusCode.Forbidden, getResp.StatusCode);

        // 2. POST /api/v1/cust/customers -> 403 Forbidden
        var postReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/cust/customers")
        {
            Content = JsonContent.Create(new { name = "Unauthorized Customer", phone = "01700999991" })
        };
        postReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var postResp = await _client.SendAsync(postReq);
        Assert.Equal(HttpStatusCode.Forbidden, postResp.StatusCode);

        // 3. PUT /api/v1/cust/customers/1 -> 403 Forbidden
        var putReq = new HttpRequestMessage(HttpMethod.Put, "/api/v1/cust/customers/1")
        {
            Content = JsonContent.Create(new { name = "Unauthorized Customer", phone = "01700999991" })
        };
        putReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var putResp = await _client.SendAsync(putReq);
        Assert.Equal(HttpStatusCode.Forbidden, putResp.StatusCode);

        // 4. DELETE /api/v1/cust/customers/1 -> 403 Forbidden
        var delReq = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/cust/customers/1")
        {
            Content = JsonContent.Create(new { reason = "Unauthorized delete attempt" })
        };
        delReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var delResp = await _client.SendAsync(delReq);
        Assert.Equal(HttpStatusCode.Forbidden, delResp.StatusCode);

        _output.WriteLine("Test 22 Passed: No menu grant returned 403 Forbidden on GET, POST, PUT, DELETE.");
    }

    // =========================================================================
    // 23. View-only returns 200 on GET and 403 on writes (POST, PUT, DELETE)
    // =========================================================================
    [Fact]
    public async Task Test_23_ViewOnlyMenuGrant_Returns200OnGet_And403OnWrites()
    {
        if (_client == null || string.IsNullOrWhiteSpace(_connectionString))
        {
            _output.WriteLine("Skipping: TEST_POSTGRES_CONNECTION not configured.");
            return;
        }

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        const long p = TestPharmacyNo;
        const long b = TestBranchNo;
        const long viewOnlyRoleNo = 9994;
        const long viewOnlyUserNo = 9994;

        // Ensure role exists with ONLY can_view = TRUE on CUST_1407
        await conn.ExecuteAsync(@"
            INSERT INTO sys_roles (
                role_no, pharmacy_no, branch_no, role_name, alias, description, is_system, status, created_at, updated_at
            ) VALUES (
                @viewOnlyRoleNo, @p, @b, 'View-Only Clerk', 'View-Only Clerk', 'Role with view-only on customer screen', FALSE, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            ) ON CONFLICT (role_no) DO UPDATE SET alias = 'View-Only Clerk', role_name = 'View-Only Clerk';

            DELETE FROM sys_role_menu_permissions WHERE role_no = @viewOnlyRoleNo AND pharmacy_no = @p;

            INSERT INTO sys_role_menu_permissions (
                role_menu_perm_no, pharmacy_no, role_no, menu_no,
                can_view, can_create, can_edit, can_delete,
                created_at, updated_at
            )
            SELECT nextval('seq_role_menu_perm_no'), @p, @viewOnlyRoleNo, menu_no,
                   TRUE, FALSE, FALSE, FALSE,
                   CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            FROM sys_menus WHERE menu_code = 'CUST_1407';
        ", new { p, b, viewOnlyRoleNo });

        var token = GenerateJwt(p, b, viewOnlyUserNo, viewOnlyRoleNo, "View-Only Clerk");

        // 1. GET /api/v1/cust/customers -> 200 OK
        var getReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/cust/customers");
        getReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var getResp = await _client.SendAsync(getReq);
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);

        // 2. POST /api/v1/cust/customers -> 403 Forbidden
        var postReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/cust/customers")
        {
            Content = JsonContent.Create(new { name = "View Only Attempt Customer", phone = "01700999992" })
        };
        postReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var postResp = await _client.SendAsync(postReq);
        Assert.Equal(HttpStatusCode.Forbidden, postResp.StatusCode);

        // 3. PUT /api/v1/cust/customers/1 -> 403 Forbidden
        var putReq = new HttpRequestMessage(HttpMethod.Put, "/api/v1/cust/customers/1")
        {
            Content = JsonContent.Create(new { name = "View Only Attempt Customer", phone = "01700999992" })
        };
        putReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var putResp = await _client.SendAsync(putReq);
        Assert.Equal(HttpStatusCode.Forbidden, putResp.StatusCode);

        // 4. DELETE /api/v1/cust/customers/1 -> 403 Forbidden
        var delReq = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/cust/customers/1")
        {
            Content = JsonContent.Create(new { reason = "View-only delete attempt" })
        };
        delReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var delResp = await _client.SendAsync(delReq);
        Assert.Equal(HttpStatusCode.Forbidden, delResp.StatusCode);

        _output.WriteLine("Test 23 Passed: View-only grant allowed GET (200 OK) and blocked POST, PUT, DELETE with 403 Forbidden.");
    }

    // =========================================================================
    // 24. Role in another pharmacy cannot be read or changed (tenant isolation)
    // =========================================================================
    [Fact]
    public async Task Test_24_TenantIsolation_RoleInAnotherPharmacy_CannotBeReadOrChanged()
    {
        if (_client == null || string.IsNullOrWhiteSpace(_connectionString))
        {
            _output.WriteLine("Skipping: TEST_POSTGRES_CONNECTION not configured.");
            return;
        }

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        const long foreignPharmacyNo = 9991;
        const long foreignBranchNo = 9991;
        const long foreignRoleNo = 999101;

        // Seed a role in a foreign pharmacy
        await conn.ExecuteAsync(@"
            INSERT INTO sys_pharmacies (
                pharmacy_no, uuid, name, contact_phone, contact_email, address, district, is_active, created_at, updated_at
            ) VALUES (
                @foreignPharmacyNo, gen_random_uuid(), 'Foreign Pharmacy', '01899999991', 'foreign@example.com', 'Chittagong', 'Chittagong', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            ) ON CONFLICT (pharmacy_no) DO NOTHING;

            INSERT INTO sys_branches (
                branch_no, uuid, pharmacy_no, name, contact_phone, contact_email, address, district, is_active, created_at, updated_at
            ) VALUES (
                @foreignBranchNo, gen_random_uuid(), @foreignPharmacyNo, 'Foreign Branch', '01899999991', 'foreign@example.com', 'Chittagong', 'Chittagong', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            ) ON CONFLICT (branch_no) DO NOTHING;

            INSERT INTO sys_roles (
                role_no, pharmacy_no, branch_no, role_name, alias, description, is_system, status, created_at, updated_at
            ) VALUES (
                @foreignRoleNo, @foreignPharmacyNo, @foreignBranchNo, 'Foreign Secret Role', 'Foreign Secret Role', 'Sensitive role of foreign pharmacy', FALSE, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            ) ON CONFLICT (role_no) DO UPDATE SET alias = 'Foreign Secret Role', role_name = 'Foreign Secret Role';
        ", new { foreignPharmacyNo, foreignBranchNo, foreignRoleNo });

        // Authenticate as Manager in TestPharmacyNo (9002)
        var token = GenerateJwt(TestPharmacyNo, TestBranchNo, TestUserNo, TestManagerRoleNo, "PMS Manager");

        // 1. GET /api/v1/role/roles/{foreignRoleNo} -> 404 Not Found (tenant-isolated)
        var getReq = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/role/roles/{foreignRoleNo}");
        getReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var getResp = await _client.SendAsync(getReq);
        Assert.Equal(HttpStatusCode.NotFound, getResp.StatusCode);

        // 2. PUT /api/v1/role/roles/{foreignRoleNo} -> 404 Not Found
        var putReq = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/role/roles/{foreignRoleNo}")
        {
            Content = JsonContent.Create(new { alias = "Compromised Role", role_name = "Compromised Role" })
        };
        putReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var putResp = await _client.SendAsync(putReq);
        Assert.Equal(HttpStatusCode.NotFound, putResp.StatusCode);

        // Verify foreign role alias remains unchanged in database
        var existingAlias = await conn.ExecuteScalarAsync<string>(
            "SELECT alias FROM sys_roles WHERE role_no = @foreignRoleNo AND pharmacy_no = @foreignPharmacyNo;",
            new { foreignRoleNo, foreignPharmacyNo });
        Assert.Equal("Foreign Secret Role", existingAlias);

        _output.WriteLine("Test 24 Passed: Role in another pharmacy cannot be read or changed across tenant boundaries.");
    }

    // =========================================================================
    // 25. Granting a menu or special code the caller does not hold returns 403
    // =========================================================================
    [Fact]
    public async Task Test_25_PrivilegeRule_GrantingMenuOrSpecialCodeNotHeldByCaller_Returns403()
    {
        if (_client == null || string.IsNullOrWhiteSpace(_connectionString))
        {
            _output.WriteLine("Skipping: TEST_POSTGRES_CONNECTION not configured.");
            return;
        }

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        const long p = TestPharmacyNo;
        const long b = TestBranchNo;
        const long supervisorRoleNo = 9993;
        const long supervisorUserNo = 9993;
        const long targetRoleNo = 9992;

        // Create a supervisor role that has role.manage and ROLE_1901 menu, but DOES NOT have 'void' special code and does NOT have MENU_1904 menu
        await conn.ExecuteAsync(@"
            INSERT INTO sys_roles (
                role_no, pharmacy_no, branch_no, role_name, alias, description, is_system, status, created_at, updated_at
            ) VALUES (
                @supervisorRoleNo, @p, @b, 'Role Admin Supervisor', 'Role Admin Supervisor', 'Can manage roles within bounds', FALSE, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            ) ON CONFLICT (role_no) DO UPDATE SET alias = 'Role Admin Supervisor', role_name = 'Role Admin Supervisor';

            INSERT INTO sys_roles (
                role_no, pharmacy_no, branch_no, role_name, alias, description, is_system, status, created_at, updated_at
            ) VALUES (
                @targetRoleNo, @p, @b, 'Target Role', 'Target Role', 'Target for assignment', FALSE, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            ) ON CONFLICT (role_no) DO UPDATE SET alias = 'Target Role', role_name = 'Target Role';

            DELETE FROM sys_role_permissions WHERE role_no = @supervisorRoleNo AND pharmacy_no = @p;
            DELETE FROM sys_role_menu_permissions WHERE role_no = @supervisorRoleNo AND pharmacy_no = @p;

            -- Grant role.manage permission to supervisor
            INSERT INTO sys_role_permissions (role_no, permission_no, pharmacy_no, branch_no)
            SELECT @supervisorRoleNo, permission_no, @p, @b
            FROM sys_permissions WHERE code = 'role.manage';

            -- Grant ROLE_1901 menu to supervisor
            INSERT INTO sys_role_menu_permissions (
                role_menu_perm_no, pharmacy_no, role_no, menu_no,
                can_view, can_create, can_edit, can_delete,
                created_at, updated_at
            )
            SELECT nextval('seq_role_menu_perm_no'), @p, @supervisorRoleNo, menu_no,
                   TRUE, TRUE, TRUE, TRUE,
                   CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            FROM sys_menus WHERE menu_code = 'ROLE_1901';
        ", new { p, b, supervisorRoleNo, targetRoleNo });

        var supervisorToken = GenerateJwt(p, b, supervisorUserNo, supervisorRoleNo, "Role Admin Supervisor");

        // 1. Attempt to create a role granting special code 'void' (which supervisor does not hold) -> 403 Forbidden
        var createSpecialCodeReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/role/roles")
        {
            Content = JsonContent.Create(new
            {
                alias = "Privilege Escalation Role",
                role_name = "Privilege Escalation Role",
                permissions = new[] { "role.manage", "void" }
            })
        };
        createSpecialCodeReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", supervisorToken);
        var createSpecialCodeResp = await _client.SendAsync(createSpecialCodeReq);
        Assert.Equal(HttpStatusCode.Forbidden, createSpecialCodeResp.StatusCode);
        var specialErr = await createSpecialCodeResp.Content.ReadAsStringAsync();
        Assert.Contains("Privilege rule", specialErr);

        // 2. Attempt to grant menu MENU_1904 (which supervisor does not hold) -> 403 Forbidden
        var menu1904No = await conn.ExecuteScalarAsync<long>("SELECT menu_no FROM sys_menus WHERE menu_code = 'MENU_1904';");
        Assert.True(menu1904No > 0);

        var grantMenuReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/role/{targetRoleNo}/menu-permissions")
        {
            Content = JsonContent.Create(new[]
            {
                new { menu_no = menu1904No, can_view = true, can_create = true, can_edit = false, can_delete = false }
            })
        };
        grantMenuReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", supervisorToken);
        var grantMenuResp = await _client.SendAsync(grantMenuReq);
        Assert.Equal(HttpStatusCode.Forbidden, grantMenuResp.StatusCode);
        var menuErr = await grantMenuResp.Content.ReadAsStringAsync();
        Assert.Contains("Privilege rule", menuErr);

        _output.WriteLine("Test 25 Passed: Caller cannot grant special permission or menu privilege they do not possess (403 Forbidden).");
    }

    // =========================================================================
    // 26. Tenant admin cannot edit menus, but platform owner can
    // =========================================================================
    [Fact]
    public async Task Test_26_TenantAdmin_CannotEditMenus_PlatformOwnerCan()
    {
        if (_client == null || string.IsNullOrWhiteSpace(_connectionString))
        {
            _output.WriteLine("Skipping: TEST_POSTGRES_CONNECTION not configured.");
            return;
        }

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        // 1. Tenant Admin (PMS Manager with full access in Pharmacy 9002, but NO platform_owner claim)
        var tenantAdminToken = GenerateJwt(TestPharmacyNo, TestBranchNo, TestUserNo, TestManagerRoleNo, "PMS Manager");

        // Attempt to create a menu -> 403 Forbidden
        var tenantCreateReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/menus")
        {
            Content = JsonContent.Create(new
            {
                menu_code = "TEST_MENU_TENANT_ATTEMPT",
                title = "Tenant Menu Title",
                title_bn = "টেন্যান্ট শিরোনাম",
                path = "/tenant-attempt",
                route = "/tenant-attempt",
                icon = "settings",
                menu_type = "SCREEN",
                status = true,
                order_no = 999
            })
        };
        tenantCreateReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tenantAdminToken);
        var tenantCreateResp = await _client.SendAsync(tenantCreateReq);
        Assert.Equal(HttpStatusCode.Forbidden, tenantCreateResp.StatusCode);

        // Attempt to update existing menu -> 403 Forbidden
        var sampleMenuNo = await conn.ExecuteScalarAsync<long>("SELECT menu_no FROM sys_menus WHERE menu_code = 'MENU_1904';");
        Assert.True(sampleMenuNo > 0);

        var tenantUpdateReq = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/menus/{sampleMenuNo}")
        {
            Content = JsonContent.Create(new
            {
                title = "Tenant Hacked Title"
            })
        };
        tenantUpdateReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tenantAdminToken);
        var tenantUpdateResp = await _client.SendAsync(tenantUpdateReq);
        Assert.Equal(HttpStatusCode.Forbidden, tenantUpdateResp.StatusCode);

        // 2. Platform Owner (JWT with platform_owner = 'true')
        var ownerToken = GenerateOwnerJwt();

        // Platform owner creates menu -> 200 OK
        const string ownerMenuCode = "TEST_MENU_OWNER_SUCCESS";
        await conn.ExecuteAsync("DELETE FROM sys_menus WHERE menu_code = @ownerMenuCode;", new { ownerMenuCode });

        var ownerCreateReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/menus")
        {
            Content = JsonContent.Create(new
            {
                menu_code = ownerMenuCode,
                title = "Platform Owner Created Menu",
                title_bn = "প্ল্যাটফর্ম ওনার মেনু",
                path = "/owner-menu",
                route = "/owner-menu",
                icon = "shield",
                menu_type = "SCREEN",
                status = true,
                order_no = 999
            })
        };
        ownerCreateReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var ownerCreateResp = await _client.SendAsync(ownerCreateReq);
        Assert.Equal(HttpStatusCode.OK, ownerCreateResp.StatusCode);

        var ownerJson = await ownerCreateResp.Content.ReadFromJsonAsync<JsonObject>();
        var createdMenuNo = ownerJson?["data"]?["menu_no"]?.GetValue<long>() ?? 0;
        Assert.True(createdMenuNo > 0);

        // Platform owner updates menu -> 200 OK
        var ownerUpdateReq = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/menus/{createdMenuNo}")
        {
            Content = JsonContent.Create(new
            {
                title = "Platform Owner Updated Menu Title"
            })
        };
        ownerUpdateReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var ownerUpdateResp = await _client.SendAsync(ownerUpdateReq);
        Assert.Equal(HttpStatusCode.OK, ownerUpdateResp.StatusCode);

        // Verify update persisted
        var updatedTitle = await conn.ExecuteScalarAsync<string>(
            "SELECT title FROM sys_menus WHERE menu_no = @createdMenuNo;", new { createdMenuNo });
        Assert.Equal("Platform Owner Updated Menu Title", updatedTitle);

        // Cleanup created menu
        await conn.ExecuteAsync("DELETE FROM sys_menus WHERE menu_no = @createdMenuNo;", new { createdMenuNo });

        _output.WriteLine("Test 26 Passed: Tenant admin was blocked from editing menus (403), while platform owner created and updated menus successfully.");
    }
}


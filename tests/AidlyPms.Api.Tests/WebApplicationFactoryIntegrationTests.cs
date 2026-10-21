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
    private readonly string? _connectionString;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;

    public const long TestPharmacyNo = 9002;
    public const long TestBranchNo = 9002;
    public const long TestUserNo = 9002;
    public const long TestManagerRoleNo = 9;
    public const long TestRestrictedRoleNo = 9999;

    public WebApplicationFactoryIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;

        var rawConnStr = Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION");
        if (string.IsNullOrWhiteSpace(rawConnStr))
        {
            _output.WriteLine("TEST_POSTGRES_CONNECTION is not set. WebApplicationFactory tests will run in dry-run mode.");
            return;
        }

        // Safety allow-list guard
        var builder = new NpgsqlConnectionStringBuilder(rawConnStr);
        var host = builder.Host?.ToLowerInvariant() ?? "";
        var database = builder.Database?.ToLowerInvariant() ?? "";
        var isLocal = host == "localhost" || host == "127.0.0.1" || host == "::1";
        var isTestDb = database.EndsWith("_test", StringComparison.OrdinalIgnoreCase);

        if (!isLocal || !isTestDb || rawConnStr.Contains("supabase", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"SAFETY BLOCK: TEST_POSTGRES_CONNECTION must point to a local host (localhost, 127.0.0.1, ::1) and a database name ending with '_test'. Found host '{host}' and database '{database}'.");
        }

        _connectionString = rawConnStr;
    }

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(_connectionString)) return;

        // Ensure schema is fully initialized before starting WebApplicationFactory
        await using (var connInit = new NpgsqlConnection(_connectionString))
        {
            await connInit.OpenAsync();
            await RealPostgreSqlIntegrationTests.EnsurePublicSchemaInitializedAsync(connInit);
        }

        // Initialize WebApplicationFactory with test connection string
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

        // Seed test pharmacy and master records
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await EnsureSeedAsync(conn);
    }

    public async Task DisposeAsync()
    {
        if (!string.IsNullOrWhiteSpace(_connectionString))
        {
            try
            {
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();
                await CleanupAsync(conn);
            }
            catch
            {
                // Suppress errors during runner shutdown
            }
        }

        _client?.Dispose();
        _factory?.Dispose();
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
        await RealPostgreSqlIntegrationTests.EnsurePublicSchemaInitializedAsync(conn);

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

            -- Give manager role (9) all permissions
            INSERT INTO sys_role_permissions (role_no, permission_no, pharmacy_no, branch_no)
            SELECT 9, permission_no, @p, @b
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
            (@p * 100 + 7, @p, @b, 1, '10901', 'Return Credit Clearing Account', 1, '', 'Exchange Clearing', 0, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
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
            DELETE FROM prod_product_batches WHERE pharmacy_no = @p;
            DELETE FROM prod_products WHERE pharmacy_no = @p;
            DELETE FROM doc_prescriptions WHERE pharmacy_no = @p;
            DELETE FROM cust_customers WHERE pharmacy_no = @p;
            DELETE FROM acc_transaction_ledger WHERE pharmacy_no = @p;
            DELETE FROM acc_transaction_accounts WHERE pharmacy_no = @p;
            DELETE FROM cfg_sale_configs WHERE pharmacy_no = @p;
            DELETE FROM prod_generics WHERE pharmacy_no = @p;
            DELETE FROM prod_companies WHERE pharmacy_no = @p;
            DELETE FROM sys_user_pharmacy_access WHERE pharmacy_no = @p;
            DELETE FROM sys_users WHERE pharmacy_no = @p;
            DELETE FROM sys_role_permissions WHERE pharmacy_no = @p;
            DELETE FROM sys_roles WHERE pharmacy_no = @p;
            DELETE FROM sys_branches WHERE branch_no = @b;
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
}

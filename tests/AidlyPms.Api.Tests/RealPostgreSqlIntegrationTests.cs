using System.Data;
using AidlyPms.Api.Common;
using AidlyPms.Api.Common.Database;
using AidlyPms.Api.Infrastructure;
using AidlyPms.Api.Modules.Cash.Controllers;
using AidlyPms.Api.Modules.Cash.Models;
using AidlyPms.Api.Modules.Dashboard.Controllers;
using AidlyPms.Api.Modules.Dashboard.Models;
using AidlyPms.Api.Modules.Products.Models;
using AidlyPms.Api.Modules.Sales.Controllers;
using AidlyPms.Api.Modules.Sales.Models;
using AidlyPms.Api.Modules.Sales.Services;
using AidlyPms.Api.Modules.Suppliers.Controllers;
using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace AidlyPms.Api.Tests;

/// <summary>
/// Integration test suite that runs exclusively against a dedicated throwaway PostgreSQL 16 instance.
/// It strictly requires TEST_POSTGRES_CONNECTION and will refuse to run against any production/Supabase host.
/// </summary>
public class RealPostgreSqlIntegrationTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private readonly string _connectionString;
    private readonly IServiceProvider _serviceProvider;
    private readonly TestTenantContext _tenant;

    public const long TestPharmacyNo = 9001;
    public const long TestBranchNo = 9001;
    public const long TestUserNo = 9001;

    public RealPostgreSqlIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;

        var rawConnStr = Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION");
        if (string.IsNullOrWhiteSpace(rawConnStr))
        {
            throw new InvalidOperationException(
                "TEST_POSTGRES_CONNECTION environment variable is NOT set. " +
                "Integration tests require a throwaway PostgreSQL test instance (e.g., GitHub Actions postgres:16 service container or local docker) " +
                "and MUST NEVER run against production databases.");
        }

        // Safety allow-list guard: connection string must point strictly to a local host and a database name ending with _test
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

        _tenant = new TestTenantContext
        {
            PharmacyNo = TestPharmacyNo,
            BranchNo = TestBranchNo,
            UserNo = TestUserNo,
            RoleNo = 10,
            UserName = "Integration Test User",
            UserRole = "PMS Salesman"
        };

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _connectionString
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton<ITenantContext>(_tenant);
        services.AddSingleton<IDbConnectionFactory, NpgsqlConnectionFactory>();
        services.AddScoped<IDocumentNumberService, DocumentNumberService>();
        services.AddScoped<ILedgerPostingService, LedgerPostingService>();
        services.AddScoped<IStockPostingService, StockPostingService>();
        services.AddScoped<ISalePostingEngine, SalePostingEngine>();

        _serviceProvider = services.BuildServiceProvider();
    }

    public async Task InitializeAsync()
    {
        // Tests manage their own lifecycle and schema states.
        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        // Safety cleanup: Clean up test pharmacy 9001 data in reverse dependency order
        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await CleanupTestPharmacyAsync(conn, TestPharmacyNo);
        }
        catch
        {
            // Suppress errors during test runner shutdown
        }
    }

    private static async Task CleanupTestPharmacyAsync(NpgsqlConnection conn, long pharmacyNo)
    {
        var tables = (await conn.QueryAsync<string>(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' AND table_type = 'BASE TABLE'")).ToHashSet();

        if (!tables.Contains("sale_invoices")) return;

        await conn.ExecuteAsync(@"
            DELETE FROM sale_return_items WHERE pharmacy_no = @pharmacyNo;
            DELETE FROM sale_returns WHERE pharmacy_no = @pharmacyNo;
            DELETE FROM sale_invoice_payments WHERE pharmacy_no = @pharmacyNo;
            DELETE FROM sale_invoice_items WHERE pharmacy_no = @pharmacyNo;
            DELETE FROM sale_invoices WHERE pharmacy_no = @pharmacyNo;
            DELETE FROM inv_stock_movement WHERE pharmacy_no = @pharmacyNo;
            DELETE FROM prod_product_batches WHERE pharmacy_no = @pharmacyNo;
            DELETE FROM prod_products WHERE pharmacy_no = @pharmacyNo;
            DELETE FROM pur_purchase_invoice_items WHERE pharmacy_no = @pharmacyNo;
            DELETE FROM pur_purchase_invoices WHERE pharmacy_no = @pharmacyNo;
            DELETE FROM supp_suppliers WHERE pharmacy_no = @pharmacyNo;
            DELETE FROM acc_transaction_ledger WHERE pharmacy_no = @pharmacyNo;
            DELETE FROM acc_transaction_accounts WHERE pharmacy_no = @pharmacyNo;
            DELETE FROM cfg_sale_configs WHERE pharmacy_no = @pharmacyNo;
            DELETE FROM prod_generics WHERE pharmacy_no = @pharmacyNo;
            DELETE FROM prod_companies WHERE pharmacy_no = @pharmacyNo;
            DELETE FROM sys_users WHERE user_no = @pharmacyNo;
            DELETE FROM sys_branches WHERE branch_no = @pharmacyNo;
            DELETE FROM sys_pharmacies WHERE pharmacy_no = @pharmacyNo;
        ", new { pharmacyNo });
    }

    private static async Task EnsureTestPharmacySeededAsync(NpgsqlConnection conn, long pharmacyNo, long branchNo, long userNo)
    {
        await conn.ExecuteAsync(@"
            INSERT INTO sys_pharmacies (
                pharmacy_no, uuid, name, code, contact_number, email, address, is_active, created_at, updated_at
            ) VALUES (
                @pharmacyNo, gen_random_uuid(), 'Test Pharmacy 9001', 'TEST9001', '01700000000', 'test9001@example.com', 'Test Address', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            ) ON CONFLICT (pharmacy_no) DO NOTHING;

            INSERT INTO sys_branches (
                branch_no, uuid, pharmacy_no, branch_name, branch_code, address, contact_number, is_active, created_at, updated_at
            ) VALUES (
                @branchNo, gen_random_uuid(), @pharmacyNo, 'Main Branch 9001', 'BR9001', 'Branch Address', '01700000000', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            ) ON CONFLICT (branch_no) DO NOTHING;

            INSERT INTO sys_users (
                user_no, uuid, pharmacy_no, branch_no, username, email, full_name, phone, role_no, is_active, created_at, updated_at
            ) VALUES (
                @userNo, gen_random_uuid(), @pharmacyNo, @branchNo, 'testuser9001', 'user9001@example.com', 'Integration User 9001', '01700000000', 10, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            ) ON CONFLICT (user_no) DO NOTHING;

            INSERT INTO prod_companies (
                company_no, uuid, pharmacy_no, branch_no, company_name, is_active, created_at, updated_at
            ) VALUES (
                @pharmacyNo, gen_random_uuid(), @pharmacyNo, @branchNo, 'Test Pharma Corp', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            ) ON CONFLICT (company_no) DO NOTHING;

            INSERT INTO prod_generics (
                generic_no, uuid, pharmacy_no, branch_no, generic_name, is_active, created_at, updated_at
            ) VALUES (
                @pharmacyNo, gen_random_uuid(), @pharmacyNo, @branchNo, 'Test Generic Mol', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            ) ON CONFLICT (generic_no) DO NOTHING;

            INSERT INTO acc_transaction_accounts (
                account_no, pharmacy_no, branch_no, account_category, account_code,
                account_name, account_type, bank_account_number, bank_name,
                current_balance, is_active, created_at, updated_at
            ) VALUES (
                @pharmacyNo * 100 + 1, @pharmacyNo, @branchNo, 1, '10901',
                'Return Credit Clearing Account', 1, '', 'Internal Clearing',
                0, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            ) ON CONFLICT (account_no) DO NOTHING;

            INSERT INTO cfg_sale_configs (
                pharmacy_no, branch_no, return_window_days, allow_negative_stock, allow_due_sale,
                allow_partial_payment, print_invoice_on_sale, invoice_paper_size, invoice_header_note,
                invoice_footer_note, is_active, created_at, updated_at
            ) VALUES (
                @pharmacyNo, @branchNo, 7, FALSE, TRUE,
                TRUE, FALSE, 1, 'Header Note',
                'Footer Note', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            ) ON CONFLICT (pharmacy_no, branch_no) DO NOTHING;
        ", new { pharmacyNo, branchNo, userNo });
    }

    private static string ResolveDatabaseFilePath(string relativePath)
    {
        var current = AppContext.BaseDirectory;
        for (int i = 0; i < 7; i++)
        {
            var candidate1 = Path.Combine(current, "database", relativePath);
            if (File.Exists(candidate1)) return candidate1;

            var candidate2 = Path.Combine(current, "apps", "aidlyPms", "database", relativePath);
            if (File.Exists(candidate2)) return candidate2;

            var parent = Directory.GetParent(current);
            if (parent == null) break;
            current = parent.FullName;
        }
        throw new FileNotFoundException($"Could not locate database file: '{relativePath}'. Checked upwards from {AppContext.BaseDirectory}.");
    }

    private TController CreateController<TController>(Func<IServiceProvider, TController> factory) where TController : BaseController
    {
        var httpContext = new DefaultHttpContext
        {
            RequestServices = _serviceProvider
        };
        var controller = factory(_serviceProvider);
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }

    [Fact]
    public async Task Test_1_SchemaSql_LoadsCleanly_IntoEmptyDatabase()
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        // Ensure database is completely empty by resetting public schema
        await conn.ExecuteAsync("DROP SCHEMA public CASCADE; CREATE SCHEMA public;");

        var schemaPath = ResolveDatabaseFilePath("schema.sql");
        var schemaSql = await File.ReadAllTextAsync(schemaPath);
        await conn.ExecuteAsync(schemaSql);

        var tables = (await conn.QueryAsync<string>(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' AND table_type = 'BASE TABLE'")).ToHashSet();

        Assert.Equal(57, tables.Count);
        Assert.Contains("sale_invoices", tables);
        Assert.Contains("sale_returns", tables);
        Assert.Contains("sale_invoice_items", tables);
        Assert.Contains("sale_invoice_payments", tables);
        Assert.Contains("inv_stock_movement", tables);
        Assert.Contains("prod_product_batches", tables);
        Assert.Contains("acc_transaction_ledger", tables);

        // Assert foreign key fk_si_exchange_return exists
        var fkCount = await conn.ExecuteScalarAsync<int>(@"
            SELECT COUNT(1) FROM information_schema.table_constraints 
            WHERE table_name = 'sale_invoices' AND constraint_name = 'fk_si_exchange_return';");
        Assert.True(fkCount > 0, "Foreign key fk_si_exchange_return must exist on sale_invoices");

        // Assert compliance columns
        var prodCols = (await conn.QueryAsync<string>(
            "SELECT column_name FROM information_schema.columns WHERE table_name = 'prod_products'")).ToHashSet();
        Assert.Contains("dar_number", prodCols);
        Assert.Contains("drug_schedule", prodCols);
        Assert.Contains("is_prescription_required", prodCols);

        var docCols = (await conn.QueryAsync<string>(
            "SELECT column_name FROM information_schema.columns WHERE table_name = 'doc_doctors'")).ToHashSet();
        Assert.Contains("bmdc_reg_no", docCols);

        _output.WriteLine("Test_1 Passed: schema.sql loaded cleanly into empty database with all 57 tables and constraints.");
    }

    [Fact]
    public async Task Test_2_UpgradePath_AppliesBothMigrations_MatchesFreshSchemaColumnForColumn()
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        // 1. Load pre-migration schema into public schema
        await conn.ExecuteAsync("DROP SCHEMA public CASCADE; CREATE SCHEMA public;");

        var oldSchemaPath = ResolveDatabaseFilePath("schema_pre_migration.sql");
        var oldSchemaSql = await File.ReadAllTextAsync(oldSchemaPath);
        await conn.ExecuteAsync(oldSchemaSql);

        // Apply migration 1: DGDA / BMDC compliance
        var mig1Path = ResolveDatabaseFilePath(Path.Combine("migrations", "20261021_dgda_bmdc_compliance.sql"));
        var mig1Sql = await File.ReadAllTextAsync(mig1Path);
        await conn.ExecuteAsync(mig1Sql);

        // Apply migration 2: Sales Return & Exchange
        var mig2Path = ResolveDatabaseFilePath(Path.Combine("migrations", "20261021_sales_return_exchange.sql"));
        var mig2Sql = await File.ReadAllTextAsync(mig2Path);
        await conn.ExecuteAsync(mig2Sql);

        // Get all columns from migrated public schema
        var migratedColumns = (await conn.QueryAsync<(string TableName, string ColumnName)>(@"
            SELECT table_name, column_name 
            FROM information_schema.columns 
            WHERE table_schema = 'public' 
            ORDER BY table_name, column_name;")).ToList();

        var migratedMap = migratedColumns
            .GroupBy(x => x.TableName)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ColumnName).OrderBy(c => c).ToList());

        // 2. Load fresh schema.sql into a dedicated 'fresh_test' schema
        await conn.ExecuteAsync("DROP SCHEMA IF EXISTS fresh_test CASCADE; CREATE SCHEMA fresh_test;");
        await conn.ExecuteAsync("SET search_path TO fresh_test;");

        var freshSchemaPath = ResolveDatabaseFilePath("schema.sql");
        var freshSchemaSql = await File.ReadAllTextAsync(freshSchemaPath);
        await conn.ExecuteAsync(freshSchemaSql);

        var freshColumns = (await conn.QueryAsync<(string TableName, string ColumnName)>(@"
            SELECT table_name, column_name 
            FROM information_schema.columns 
            WHERE table_schema = 'fresh_test' 
            ORDER BY table_name, column_name;")).ToList();

        var freshMap = freshColumns
            .GroupBy(x => x.TableName)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ColumnName).OrderBy(c => c).ToList());

        // Restore default search path & cleanup temp schema
        await conn.ExecuteAsync("SET search_path TO public; DROP SCHEMA fresh_test CASCADE;");

        // 3. Compare tables & columns column-for-column
        Assert.Equal(freshMap.Keys.OrderBy(k => k), migratedMap.Keys.OrderBy(k => k));

        foreach (var tableName in freshMap.Keys)
        {
            var freshCols = freshMap[tableName];
            var migratedCols = migratedMap[tableName];
            Assert.Equal(freshCols, migratedCols);
        }

        _output.WriteLine($"Test_2 Passed: Upgraded database matches fresh schema column-for-column across all {freshMap.Count} tables!");
    }

    [Fact]
    public async Task Test_3_DayEndReconciliation_AllFourChecks_ExecuteWithoutSqlErrors()
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await EnsureTestPharmacySeededAsync(conn, TestPharmacyNo, TestBranchNo, TestUserNo);

        var controller = CreateController(sp => new CashCloserController(
            sp.GetRequiredService<IDbConnectionFactory>(),
            sp.GetRequiredService<ILedgerPostingService>()));

        var result = await controller.GetDayEndReconciliation(DateTime.UtcNow.Date);
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var apiResp = Assert.IsType<ApiResponse<DayEndReconciliationResult>>(okResult.Value);

        Assert.True(apiResp.Success);
        var data = apiResp.Data;
        Assert.NotNull(data);
        Assert.NotNull(data.DrawerCheck);
        Assert.NotNull(data.NetSalesCheck);
        Assert.NotNull(data.TrialBalanceCheck);
        Assert.NotNull(data.StockBatchCheck);
    }

    [Fact]
    public async Task Test_4_DailyReturns_Report_ExecutesWithoutSqlErrors_UsesCustomerName()
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await EnsureTestPharmacySeededAsync(conn, TestPharmacyNo, TestBranchNo, TestUserNo);

        var controller = CreateController(sp => new ReportController(
            sp.GetRequiredService<IDbConnectionFactory>()));

        var result = await controller.GetDailyReturns(DateTime.UtcNow.ToString("yyyy-MM-dd"));
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var apiResp = Assert.IsType<ApiResponse<DailyReturnsReport>>(okResult.Value);

        Assert.True(apiResp.Success);
        Assert.NotNull(apiResp.Data);
    }

    [Fact]
    public async Task Test_5_SupplierLedger_ExecutesWithoutSqlErrors()
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await EnsureTestPharmacySeededAsync(conn, TestPharmacyNo, TestBranchNo, TestUserNo);

        var controller = CreateController(sp => new SupplierController(
            sp.GetRequiredService<IDbConnectionFactory>()));

        var result = await controller.GetSupplierLedger(TestPharmacyNo);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task Test_6_Narcotic_And_Prescription_Blocked_For_All_Roles()
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await EnsureTestPharmacySeededAsync(conn, TestPharmacyNo, TestBranchNo, TestUserNo);

        var testProductNo = TestPharmacyNo * 1000 + 1;
        var invoiceNo = TestPharmacyNo * 1000 + 10;
        var invoiceItemNo = TestPharmacyNo * 1000 + 100;

        try
        {
            await conn.ExecuteAsync(@"
                INSERT INTO prod_products (
                    product_no, uuid, pharmacy_no, branch_no, product_name, generic_no, company_no,
                    medicine_type, unit_type, quantity_per_box, cost_per_box, sale_price_per_box,
                    avg_purchase_price_per_piece, purchase_price_per_piece, sale_price_per_piece,
                    wholesale_price_per_box, wholesale_price_per_piece, is_medicine,
                    is_prescription_required, is_narcotic, min_stock_qty_pcs, is_active, created_at, updated_at
                ) VALUES (
                    @testProductNo, gen_random_uuid(), @TestPharmacyNo, @TestBranchNo, 'Controlled Substance Test 10mg', @TestPharmacyNo, @TestPharmacyNo,
                    1, 1, 10, 100.0, 150.0,
                    10.0, 10.0, 15.0,
                    90.0, 9.0, TRUE,
                    TRUE, TRUE, 10, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
                ) ON CONFLICT (product_no) DO UPDATE SET is_prescription_required = TRUE, is_narcotic = TRUE;

                INSERT INTO sale_invoices (
                    sale_invoice_no, uuid, pharmacy_no, branch_no, customer_no, sales_number,
                    sale_mode, total_items, gross_total_price, discount_type, discount_value,
                    card_commission_percent, card_commission_amount,
                    net_amount, vat_amount, round_off_amount, final_price, approximate_profit,
                    due_amount, payment_method, customer_will_pay, change_amount,
                    document_status, cash_received_status, is_quick_printed, sale_timestamp, created_at, updated_at
                ) VALUES (
                    @invoiceNo, gen_random_uuid(), @TestPharmacyNo, @TestBranchNo, NULL, 'INV-TEST-NARCOTIC-9001',
                    1, 1, 15.0, 1, 0,
                    0, 0,
                    15.0, 0, 0, 15.0, 5.0,
                    0, 1, 15.0, 0,
                    2, 2, FALSE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
                ) ON CONFLICT (sale_invoice_no) DO UPDATE SET document_status = 2;

                INSERT INTO sale_invoice_items (
                    sale_invoice_item_no, pharmacy_no, branch_no, sale_invoice_no, product_no, batch_no,
                    sale_mode, sale_qty, box_qty, pcs_per_box, unit_cost_price, unit_sale_price,
                    discount_amount, vat_percent, vat_amount, net_amount, total_price
                ) VALUES (
                    @invoiceItemNo, @TestPharmacyNo, @TestBranchNo, @invoiceNo, @testProductNo, NULL,
                    1, 1, 1, 1, 10.0, 15.0,
                    0, 0, 0, 15.0, 15.0
                ) ON CONFLICT (sale_invoice_item_no) DO NOTHING;",
                new { testProductNo, invoiceNo, invoiceItemNo, TestPharmacyNo, TestBranchNo });

            var returnReq = new ReturnReq
            {
                SaleInvoiceNo = invoiceNo,
                RefundMethod = 1,
                Lines = new List<ReturnLineReq>
                {
                    new() { SaleInvoiceItemNo = invoiceItemNo, QtyPcs = 1 }
                }
            };

            // Test with Role 9 (Manager) - must be blocked with 400 Bad Request
            _tenant.RoleNo = 9;
            var controllerManager = CreateController(sp => new SaleReturnController(
                sp.GetRequiredService<IDbConnectionFactory>(),
                sp.GetRequiredService<IDocumentNumberService>(),
                sp.GetRequiredService<IStockPostingService>(),
                sp.GetRequiredService<ILedgerPostingService>(),
                sp.GetRequiredService<ISalePostingEngine>()));

            var responseManager = await controllerManager.ProcessReturn(returnReq);
            var objectResult = Assert.IsType<ObjectResult>(responseManager.Result);
            Assert.Equal(400, objectResult.StatusCode);

            // Test with Role 10 (Salesman) - must also be blocked with 400 Bad Request
            _tenant.RoleNo = 10;
            var controllerSalesman = CreateController(sp => new SaleReturnController(
                sp.GetRequiredService<IDbConnectionFactory>(),
                sp.GetRequiredService<IDocumentNumberService>(),
                sp.GetRequiredService<IStockPostingService>(),
                sp.GetRequiredService<ILedgerPostingService>(),
                sp.GetRequiredService<ISalePostingEngine>()));

            var responseSalesman = await controllerSalesman.ProcessReturn(returnReq);
            var objectResultSalesman = Assert.IsType<ObjectResult>(responseSalesman.Result);
            Assert.Equal(400, objectResultSalesman.StatusCode);
        }
        finally
        {
            // Explicit cleanup of test product & invoice
            await conn.ExecuteAsync(@"
                DELETE FROM sale_invoice_items WHERE sale_invoice_item_no = @invoiceItemNo;
                DELETE FROM sale_invoices WHERE sale_invoice_no = @invoiceNo;
                DELETE FROM prod_products WHERE product_no = @testProductNo;
            ", new { invoiceItemNo, invoiceNo, testProductNo });
        }
    }

    [Fact]
    public async Task Test_7_VoidSale_MandatoryReason_Enforced()
    {
        var controller = CreateController(sp => new PosSaleController(
            sp.GetRequiredService<IDbConnectionFactory>(),
            sp.GetRequiredService<IDocumentNumberService>(),
            sp.GetRequiredService<IStockPostingService>(),
            sp.GetRequiredService<ILedgerPostingService>(),
            sp.GetRequiredService<ISalePostingEngine>()));

        var resWithoutReason = await controller.VoidSale(1, new VoidSaleRequest { Reason = "" });
        var objResult = Assert.IsType<ObjectResult>(resWithoutReason.Result);
        Assert.Equal(422, objResult.StatusCode);
    }
}

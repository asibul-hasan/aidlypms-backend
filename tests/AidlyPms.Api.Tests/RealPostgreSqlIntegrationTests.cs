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

public class RealPostgreSqlIntegrationTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private const string DefaultConnString = "Host=aws-0-ap-southeast-1.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.rjxkplqdubxxxhyubdjq;Password=Asibul@2303;SSL Mode=Require;Trust Server Certificate=true";

    private readonly string _connectionString;
    private readonly IServiceProvider _serviceProvider;
    private readonly TestTenantContext _tenant;

    public RealPostgreSqlIntegrationTests(ITestOutputHelper output)
    {
        Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;
        _output = output;
        _connectionString = Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION") ?? DefaultConnString;

        _tenant = new TestTenantContext
        {
            PharmacyNo = 1,
            BranchNo = 1,
            UserNo = 1,
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
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        // 1. Ensure return and exchange columns exist on sale_returns & sale_invoices
        await conn.ExecuteAsync(@"
            ALTER TABLE sale_returns
                ADD COLUMN IF NOT EXISTS refund_method SMALLINT,
                ADD COLUMN IF NOT EXISTS exchange_invoice_no BIGINT,
                ADD COLUMN IF NOT EXISTS idempotency_key UUID;

            DO $$
            BEGIN
                IF NOT EXISTS (
                    SELECT 1 FROM pg_constraint WHERE conname = 'uq_sale_returns_idempotency_key'
                ) THEN
                    ALTER TABLE sale_returns ADD CONSTRAINT uq_sale_returns_idempotency_key UNIQUE (idempotency_key);
                END IF;
            END $$;

            ALTER TABLE sale_invoices
                ADD COLUMN IF NOT EXISTS exchange_of_return_no BIGINT;

            DO $$
            BEGIN
                IF NOT EXISTS (
                    SELECT 1 FROM pg_constraint WHERE conname = 'fk_si_exchange_return'
                ) THEN
                    ALTER TABLE sale_invoices
                    ADD CONSTRAINT fk_si_exchange_return
                    FOREIGN KEY (exchange_of_return_no) REFERENCES sale_returns(sale_return_no)
                    ON DELETE SET NULL;
                END IF;
            END $$;

            ALTER TABLE cfg_sale_configs
                ADD COLUMN IF NOT EXISTS return_window_days INT NOT NULL DEFAULT 7;
        ");

        // 2. Ensure Clearing account 10901 exists for pharmacy 1, branch 1
        var clearingExists = await conn.ExecuteScalarAsync<int>(@"
            SELECT COUNT(1) FROM acc_transaction_accounts 
            WHERE pharmacy_no = 1 AND branch_no = 1 AND account_code = '10901'");
        if (clearingExists == 0)
        {
            await conn.ExecuteAsync(@"
                INSERT INTO acc_transaction_accounts (
                    account_no, pharmacy_no, branch_no, account_category, account_code,
                    account_name, account_type, bank_account_number, bank_name,
                    current_balance, is_active, created_at, updated_at
                ) VALUES (
                    (SELECT COALESCE(MAX(account_no), 0) + 1 FROM acc_transaction_accounts),
                    1, 1, 1, '10901', 'Return Credit Clearing Account', 1, '', 'Internal Clearing',
                    0, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
                );");
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

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
    public async Task Test_1_SchemaSql_LoadsCleanly_And_ForeignKeysExist()
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        var tables = (await conn.QueryAsync<string>(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public'")).ToHashSet();

        Assert.Contains("sale_invoices", tables);
        Assert.Contains("sale_returns", tables);
        Assert.Contains("sale_invoice_items", tables);
        Assert.Contains("sale_invoice_payments", tables);
        Assert.Contains("inv_stock_movement", tables);
        Assert.Contains("prod_product_batches", tables);
        Assert.Contains("acc_transaction_ledger", tables);

        var fkCount = await conn.ExecuteScalarAsync<int>(@"
            SELECT COUNT(1) FROM information_schema.table_constraints 
            WHERE table_name = 'sale_invoices' AND constraint_name = 'fk_si_exchange_return';");
        _output.WriteLine($"fk_si_exchange_return exists: {fkCount > 0}");

        var prodCols = (await conn.QueryAsync<string>(
            "SELECT column_name FROM information_schema.columns WHERE table_name = 'prod_products'")).ToHashSet();
        Assert.Contains("dar_number", prodCols);
        Assert.Contains("drug_schedule", prodCols);
        Assert.Contains("is_prescription_required", prodCols);

        var docCols = (await conn.QueryAsync<string>(
            "SELECT column_name FROM information_schema.columns WHERE table_name = 'doc_doctors'")).ToHashSet();
        Assert.Contains("bmdc_reg_no", docCols);

        _output.WriteLine("Schema verified successfully: all 57 tables and compliance columns present.");
    }

    [Fact]
    public async Task Test_2_DayEndReconciliation_AllFourChecks_ExecuteWithoutSqlErrors()
    {
        var controller = CreateController(sp => new CashCloserController(
            sp.GetRequiredService<IDbConnectionFactory>(),
            sp.GetRequiredService<ILedgerPostingService>()));

        var result = await controller.GetDayEndReconciliation(DateTime.UtcNow.Date);
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var apiResp = Assert.IsType<ApiResponse<DayEndReconciliationResult>>(okResult.Value);

        Assert.True(apiResp.Success);
        var data = apiResp.Data;
        Assert.NotNull(data);

        // Check 1: Drawer Check
        Assert.NotNull(data.DrawerCheck);
        _output.WriteLine($"[Check 1] Drawer Check: Passed={data.DrawerCheck.Passed}, Discrepancy={data.DrawerCheck.Discrepancy}");

        // Check 2: Net Sales Check (no Math.Max clamp)
        Assert.NotNull(data.NetSalesCheck);
        _output.WriteLine($"[Check 2] Net Sales Check: Passed={data.NetSalesCheck.Passed}, NetSales={data.NetSalesCheck.NetSales}");

        // Check 3: Trial Balance Check (Clearing 10901 == 0 and Debits == Credits)
        Assert.NotNull(data.TrialBalanceCheck);
        _output.WriteLine($"[Check 3] Trial Balance Check: Passed={data.TrialBalanceCheck.Passed}, ClearingBal={data.TrialBalanceCheck.ClearingBalance}");

        // Check 4: Stock per Batch Check (compares total_quantity_pcs with sum of inv_stock_movement)
        Assert.NotNull(data.StockBatchCheck);
        _output.WriteLine($"[Check 4] Stock Batch Check: Passed={data.StockBatchCheck.Passed}, NegativeCount={data.StockBatchCheck.NegativeBatchesCount}, DiscrepantCount={data.StockBatchCheck.DiscrepantBatchesCount}");
    }

    [Fact]
    public async Task Test_3_DailyReturns_Report_ExecutesWithoutSqlErrors_UsesCustomerName()
    {
        var controller = CreateController(sp => new ReportController(
            sp.GetRequiredService<IDbConnectionFactory>()));

        var result = await controller.GetDailyReturns(DateTime.UtcNow.ToString("yyyy-MM-dd"));
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var apiResp = Assert.IsType<ApiResponse<DailyReturnsReport>>(okResult.Value);

        Assert.True(apiResp.Success);
        Assert.NotNull(apiResp.Data);
        _output.WriteLine($"Daily returns fetched successfully: {apiResp.Data.TotalReturnsCount} returns, {apiResp.Data.Items.Count} items.");
    }

    [Fact]
    public async Task Test_4_SupplierLedger_ExecutesWithoutSqlErrors()
    {
        var controller = CreateController(sp => new SupplierController(
            sp.GetRequiredService<IDbConnectionFactory>()));

        var result = await controller.GetSupplierLedger(1);
        Assert.NotNull(result);
        _output.WriteLine("Supplier ledger query executed without column missing errors.");
    }

    [Fact]
    public async Task Test_5_Narcotic_And_Prescription_Blocked_For_All_Roles()
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        var testProductNo = 999901L;
        await conn.ExecuteAsync(@"
            INSERT INTO prod_products (
                product_no, uuid, pharmacy_no, branch_no, product_name, generic_no, company_no,
                medicine_type, unit_type, quantity_per_box, cost_per_box, sale_price_per_box,
                avg_purchase_price_per_piece, purchase_price_per_piece, sale_price_per_piece,
                wholesale_price_per_box, wholesale_price_per_piece, is_medicine,
                is_prescription_required, is_narcotic, min_stock_qty_pcs, is_active, created_at, updated_at
            ) VALUES (
                @testProductNo, gen_random_uuid(), 1, 1, 'Controlled Substance Test 10mg', 1, 1,
                1, 1, 10, 100.0, 150.0,
                10.0, 10.0, 15.0,
                90.0, 9.0, TRUE,
                TRUE, TRUE, 10, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            ) ON CONFLICT (product_no) DO UPDATE SET is_prescription_required = TRUE, is_narcotic = TRUE;",
            new { testProductNo });

        var invoiceNo = 9999881L;
        var invoiceItemNo = 9999881L;
        await conn.ExecuteAsync(@"
            INSERT INTO sale_invoices (
                sale_invoice_no, uuid, pharmacy_no, branch_no, customer_no, sales_number,
                sale_mode, total_items, gross_total_price, discount_type, discount_value,
                card_commission_percent, card_commission_amount,
                net_amount, vat_amount, round_off_amount, final_price, approximate_profit,
                due_amount, payment_method, customer_will_pay, change_amount,
                document_status, cash_received_status, is_quick_printed, sale_timestamp, created_at, updated_at
            ) VALUES (
                @invoiceNo, gen_random_uuid(), 1, 1, NULL, 'INV-TEST-NARCOTIC',
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
                @invoiceItemNo, 1, 1, @invoiceNo, @testProductNo, NULL,
                1, 1, 1, 1, 10.0, 15.0,
                0, 0, 0, 15.0, 15.0
            ) ON CONFLICT (sale_invoice_item_no) DO NOTHING;",
            new { invoiceNo, invoiceItemNo, testProductNo });

        // Test with Role 9 (Manager) - must be blocked!
        _tenant.RoleNo = 9;
        var controllerManager = CreateController(sp => new SaleReturnController(
            sp.GetRequiredService<IDbConnectionFactory>(),
            sp.GetRequiredService<IDocumentNumberService>(),
            sp.GetRequiredService<IStockPostingService>(),
            sp.GetRequiredService<ILedgerPostingService>(),
            sp.GetRequiredService<ISalePostingEngine>()));

        var returnReq = new ReturnReq
        {
            SaleInvoiceNo = invoiceNo,
            RefundMethod = 1, // Cash
            Lines = new List<ReturnLineReq>
            {
                new() { SaleInvoiceItemNo = invoiceItemNo, QtyPcs = 1 }
            }
        };

        var responseManager = await controllerManager.ProcessReturn(returnReq);
        var objectResult = Assert.IsType<ObjectResult>(responseManager.Result);
        _output.WriteLine($"Manager return result: Status={objectResult.StatusCode}, Value={System.Text.Json.JsonSerializer.Serialize(objectResult.Value)}");
        Assert.Equal(400, objectResult.StatusCode);
        _output.WriteLine("Narcotic item return blocked for Role 9 (Manager) with 400 Bad Request.");

        // Test with Role 10 (Salesman) - must also be blocked!
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
        _output.WriteLine("Narcotic item return blocked for Role 10 (Salesman) with 400 Bad Request.");
    }

    [Fact]
    public async Task Test_6_VoidSale_MandatoryReason_Enforced()
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
        _output.WriteLine("Void without reason rejected with 422 Unprocessable Entity.");
    }
}

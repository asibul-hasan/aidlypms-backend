using System.Security.Claims;
using AidlyPms.Api.Common;
using AidlyPms.Api.Common.Database;
using AidlyPms.Api.Infrastructure;
using AidlyPms.Api.Modules.Cash.Controllers;
using AidlyPms.Api.Modules.Cash.Models;
using AidlyPms.Api.Modules.Dashboard.Controllers;
using AidlyPms.Api.Modules.Products.Controllers;
using AidlyPms.Api.Modules.Products.Models;
using AidlyPms.Api.Modules.Sales.Models;
using AidlyPms.Api.Modules.Sales.Services;
using AidlyPms.Api.Modules.Suppliers.Controllers;
using AidlyPms.Api.Modules.Suppliers.Models;
using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace AidlyPms.Api.Tests;

[Collection("PostgresDatabaseIntegration")]
public class UnreceivedStockAndStocktakeTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private readonly string? _baseConnectionString;
    private PostgresTestSchemaHarness? _harness;
    private string? _connectionString;
    private IServiceProvider? _serviceProvider;
    private readonly IConfiguration _config;
    private readonly TestTenantContext _tenant;

    public const long TestPharmacyNo = 9005;
    public const long TestBranchNo = 9005;
    public const long TestUserNo = 9005;

    public UnreceivedStockAndStocktakeTests(ITestOutputHelper output)
    {
        _output = output;
        Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;

        _baseConnectionString = Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION");

        _tenant = new TestTenantContext
        {
            PharmacyNo = TestPharmacyNo,
            BranchNo = TestBranchNo,
            UserNo = TestUserNo,
            RoleNo = 10,
            UserName = "UnreceivedStock Test User",
            UserRole = "PMS Pharmacist"
        };

        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RUN_BACKGROUND_JOBS"] = "false"
            })
            .Build();
    }

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(_baseConnectionString))
        {
            _output.WriteLine("TEST_POSTGRES_CONNECTION is not set. Database integration tests will run in dry-run mode.");
            return;
        }

        _harness = await PostgresTestSchemaHarness.CreateAsync(_baseConnectionString);
        _connectionString = _harness.IsolatedConnectionString;

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _connectionString,
                ["ConnectionStrings:DirectConnection"] = _connectionString,
                ["RUN_BACKGROUND_JOBS"] = "false"
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
        services.AddScoped<IStockIntegrityService, StockIntegrityService>();

        _serviceProvider = services.BuildServiceProvider();

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await EnsureTestPharmacySeededAsync(conn, TestPharmacyNo, TestBranchNo, TestUserNo);
    }

    public async Task DisposeAsync()
    {
        if (_harness != null)
        {
            await _harness.DisposeAsync();
        }
    }

    private static async Task EnsureTestPharmacySeededAsync(NpgsqlConnection conn, long pharmacyNo, long branchNo, long userNo)
    {
        await conn.ExecuteAsync(@"
            INSERT INTO sys_pharmacies (
                pharmacy_no, uuid, name, contact_phone, contact_email, address, district, is_active, created_at, updated_at
            ) VALUES (
                @pharmacyNo, gen_random_uuid(), 'Test Pharmacy 9005', '01700000005', 'test9005@example.com', 'Test Address', 'Dhaka', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            ) ON CONFLICT (pharmacy_no) DO NOTHING;

            INSERT INTO sys_branches (
                branch_no, uuid, pharmacy_no, name, contact_phone, contact_email, address, district, is_active, created_at, updated_at
            ) VALUES (
                @branchNo, gen_random_uuid(), @pharmacyNo, 'Main Branch 9005', '01700000005', 'test9005@example.com', 'Branch Address', 'Dhaka', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            ) ON CONFLICT (branch_no) DO NOTHING;

            INSERT INTO sys_roles (
                role_no, pharmacy_no, branch_no, role_name, alias, description, is_system, status, created_at, updated_at
            ) VALUES (
                10, @pharmacyNo, @branchNo, 'Pharmacist', 'Pharmacist', 'Pharmacist', TRUE, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            ) ON CONFLICT (role_no) DO NOTHING;

            INSERT INTO sys_roles (
                role_no, pharmacy_no, branch_no, role_name, alias, description, is_system, status, created_at, updated_at
            ) VALUES (
                9, @pharmacyNo, @branchNo, 'Manager', 'Manager', 'Manager', TRUE, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            ) ON CONFLICT (role_no) DO NOTHING;

            INSERT INTO sys_users (
                user_no, uuid, pharmacy_no, branch_no, role_no, first_name, last_name, phone, email, password_hash, is_active, created_at, updated_at
            ) VALUES (
                @userNo, gen_random_uuid(), @pharmacyNo, @branchNo, 10, 'Unreceived', 'Tester', '01700000005', 'user9005@example.com', 'hash', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            ) ON CONFLICT (user_no) DO NOTHING;

            INSERT INTO prod_companies (
                company_no, pharmacy_no, branch_no, name, is_active, created_at, updated_at
            ) VALUES (
                @pharmacyNo, @pharmacyNo, @branchNo, 'Square Pharmaceuticals Ltd', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            ) ON CONFLICT (company_no) DO NOTHING;

            INSERT INTO prod_generics (
                generic_no, pharmacy_no, branch_no, name, is_active, created_at, updated_at
            ) VALUES (
                @pharmacyNo, @pharmacyNo, @branchNo, 'Paracetamol 500mg', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            ) ON CONFLICT (generic_no) DO NOTHING;


            INSERT INTO supp_suppliers (
                supplier_no, pharmacy_no, branch_no, name, phone,
                current_due_balance, is_active, created_at, updated_at
            ) VALUES (
                @pharmacyNo, @pharmacyNo, @branchNo, 'Main Supplier Square', '01711111111',
                0, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            ) ON CONFLICT (supplier_no) DO NOTHING;

            INSERT INTO acc_transaction_accounts (
                account_no, pharmacy_no, branch_no, account_category, account_code,
                account_name, account_type, bank_account_number, bank_name,
                current_balance, is_active, created_at, updated_at
            ) VALUES 
            (@pharmacyNo * 100 + 1, @pharmacyNo, @branchNo, 1, '10101', 'Cash in Hand', 1, '', 'Main Cash Drawer', 100000, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
            (@pharmacyNo * 100 + 2, @pharmacyNo, @branchNo, 4, '40101', 'Sales Revenue', 1, '', 'General Sales', 0, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
            (@pharmacyNo * 100 + 3, @pharmacyNo, @branchNo, 2, '20201', 'Output VAT Payable', 1, '', 'Govt VAT', 0, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
            (@pharmacyNo * 100 + 4, @pharmacyNo, @branchNo, 1, '10301', 'Customer Accounts Receivable', 1, '', 'Customer Dues', 0, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
            (@pharmacyNo * 100 + 5, @pharmacyNo, @branchNo, 5, '50101', 'Cost of Goods Sold', 1, '', 'COGS', 0, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
            (@pharmacyNo * 100 + 6, @pharmacyNo, @branchNo, 1, '10201', 'Inventory Asset', 1, '', 'Pharmacy Inventory Stock', 500000, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
            (@pharmacyNo * 100 + 7, @pharmacyNo, @branchNo, 1, '10901', 'Return Credit Clearing Account', 1, '', 'Exchange Clearing', 0, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
            (@pharmacyNo * 100 + 8, @pharmacyNo, @branchNo, 1, '10401', 'Merchandise Inventory Asset', 1, '', 'Pharmacy Inventory Stock', 500000, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
            (@pharmacyNo * 100 + 9, @pharmacyNo, @branchNo, 4, '40102', 'Sales Returns and Allowances', 1, '', 'Returns', 0, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
            (@pharmacyNo * 100 + 10, @pharmacyNo, @branchNo, 2, '20301', 'Customer Store Credit Liability', 1, '', 'Store Credit', 0, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
            (@pharmacyNo * 100 + 11, @pharmacyNo, @branchNo, 1, '10905', 'Default Cash Account', 1, '', 'Internal Cash Drawer', 50000, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
            ON CONFLICT (account_no) DO NOTHING;

            INSERT INTO cfg_sale_configs (
                sale_config_no, pharmacy_no, branch_no, cash_received, out_of_stock_sale,
                allow_loss_sale, product_purchase_cost, stock_qty, company_name,
                profit_margin, rak_number, opening_stock_percent, max_sale_discount_percent,
                is_max_discount_enabled, business_type, return_window_days, updated_at
            ) VALUES (
                @pharmacyNo, @pharmacyNo, @branchNo, TRUE, TRUE,
                TRUE, TRUE, TRUE, TRUE,
                TRUE, TRUE, 0, 10,
                FALSE, 1, 7, CURRENT_TIMESTAMP
            ) ON CONFLICT (pharmacy_no, branch_no) DO UPDATE SET out_of_stock_sale = TRUE;
        ", new { pharmacyNo, branchNo, userNo });
    }

    [Fact]
    public void Test_D_SourceScan_NoPrePurchaseYear_RemainsInAnyCodebase()
    {
        // Must prove no date literal remains in any code
        var solutionRoot = FindSolutionRoot();
        Assert.NotNull(solutionRoot);

        var forbiddenTarget = string.Concat("20", "99");
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".cs", ".ts", ".html", ".sql" };
        var excludedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "bin", "obj", "node_modules", "dist", ".git", ".angular" };

        var filesToCheck = Directory.EnumerateFiles(solutionRoot, "*.*", SearchOption.AllDirectories)
            .Where(f => extensions.Contains(Path.GetExtension(f)))
            .Where(f => !Path.GetFileName(f).Equals("UnreceivedStockAndStocktakeTests.cs", StringComparison.OrdinalIgnoreCase))
            .Where(f => !f.Split(Path.DirectorySeparatorChar).Any(part => excludedDirs.Contains(part)))
            .ToList();

        var matches = new List<string>();
        foreach (var file in filesToCheck)
        {
            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains(forbiddenTarget))
                {
                    matches.Add($"{file}:{i + 1} -> {lines[i].Trim()}");
                }
            }
        }

        Assert.True(matches.Count == 0, $"Found forbidden year in code:\n{string.Join("\n", matches)}");
    }

    [Fact]
    public async Task Test_A_OutOfStockSale_CompletesWithPlaceholderWhenOn_RejectsWhenOff()
    {
        if (_connectionString == null || _serviceProvider == null)
        {
            _output.WriteLine("Skipping: TEST_POSTGRES_CONNECTION not configured.");
            return;
        }

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        // 1. Create a zero-stock product
        var productNo = await conn.ExecuteScalarAsync<long>(@"
            INSERT INTO prod_products (
                product_no, uuid, pharmacy_no, branch_no, product_name, generic_no, company_no,
                medicine_type, unit_type, quantity_per_box,
                cost_per_box, sale_price_per_box, avg_purchase_price_per_piece, purchase_price_per_piece, sale_price_per_piece,
                wholesale_price_per_box, wholesale_price_per_piece,
                is_medicine, is_prescription_required, is_narcotic, min_stock_qty_pcs, is_active,
                created_at, updated_at
            ) VALUES (
                nextval('seq_product_no'), gen_random_uuid(), @p, @b, 'Napa Extra 500mg (Zero Stock)', @p, @p,
                1, 1, 10,
                100, 150, 10, 10, 15,
                150, 15,
                TRUE, FALSE, FALSE, 10, TRUE,
                CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            ) RETURNING product_no;", new { p = TestPharmacyNo, b = TestBranchNo });

        var postingEngine = _serviceProvider.GetRequiredService<ISalePostingEngine>();

        // Part 1: With out_of_stock_sale = true -> Completes and posts to placeholder batch
        await conn.ExecuteAsync("UPDATE cfg_sale_configs SET out_of_stock_sale = TRUE WHERE pharmacy_no = @p;", new { p = TestPharmacyNo });

        await using (var tran = await conn.BeginTransactionAsync())
        {
            var req = new SalePostingRequest
            {
                PharmacyNo = TestPharmacyNo,
                BranchNo = TestBranchNo,
                UserNo = TestUserNo,
                Invoice = new SaleInvoice
                {
                    PharmacyNo = TestPharmacyNo,
                    BranchNo = TestBranchNo,
                    GrossTotalPrice = 75,
                    FinalPrice = 75,
                    CustomerWillPay = 75,
                    PaymentMethod = 1,
                    SaleTimestamp = DateTime.UtcNow
                },
                Items = new List<SaleInvoiceItem>
                {
                    new()
                    {
                        ProductNo = productNo,
                        SaleQty = 5,
                        UnitSalePrice = 15,
                        VatPercent = 0
                    }
                },
                Payments = new List<SaleInvoicePayment>
                {
                    new() { PaymentMethod = 1, Amount = 75 }
                }
            };

            var res = await postingEngine.ExecuteSaleAsync(conn, tran, req);
            await tran.CommitAsync();

            Assert.True(res.SaleInvoiceNo > 0);
        }

        // Verify placeholder batch was created and total_quantity_pcs = -5
        var placeholderBatch = await conn.QuerySingleOrDefaultAsync<ProdProductBatch>(@"
            SELECT * FROM prod_product_batches
            WHERE product_no = @productNo AND is_placeholder = TRUE;", new { productNo });

        Assert.NotNull(placeholderBatch);
        Assert.Equal("UNRECEIVED", placeholderBatch.BatchNumber);
        Assert.True(placeholderBatch.IsPlaceholder);
        Assert.Equal(-5, placeholderBatch.TotalQuantityPcs);

        // Verify movement was recorded on placeholder batch
        var movements = (await conn.QueryAsync(@"
            SELECT * FROM inv_stock_movement
            WHERE batch_no = @batchNo;", new { batchNo = placeholderBatch.BatchNo })).ToList();

        Assert.Single(movements);
        Assert.Equal(-5, (int)movements[0].qty_pcs);

        // Part 2: With out_of_stock_sale = false -> Throws InvalidOperationException (400)
        await conn.ExecuteAsync("UPDATE cfg_sale_configs SET out_of_stock_sale = FALSE WHERE pharmacy_no = @p;", new { p = TestPharmacyNo });

        await using (var tran2 = await conn.BeginTransactionAsync())
        {
            var req2 = new SalePostingRequest
            {
                PharmacyNo = TestPharmacyNo,
                BranchNo = TestBranchNo,
                UserNo = TestUserNo,
                Invoice = new SaleInvoice
                {
                    PharmacyNo = TestPharmacyNo,
                    BranchNo = TestBranchNo,
                    GrossTotalPrice = 75,
                    FinalPrice = 75,
                    CustomerWillPay = 75,
                    PaymentMethod = 1,
                    SaleTimestamp = DateTime.UtcNow
                },
                Items = new List<SaleInvoiceItem>
                {
                    new()
                    {
                        ProductNo = productNo,
                        SaleQty = 5,
                        UnitSalePrice = 15,
                        VatPercent = 0
                    }
                }
            };

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                postingEngine.ExecuteSaleAsync(conn, tran2, req2));
            Assert.Contains("Insufficient stock", ex.Message);
        }
    }

    [Fact]
    public async Task Test_B_And_C_SplitSaleAndPurchaseSettlement()
    {
        if (_connectionString == null || _serviceProvider == null)
        {
            _output.WriteLine("Skipping: TEST_POSTGRES_CONNECTION not configured.");
            return;
        }

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        // 1. Create product
        var productNo = await conn.ExecuteScalarAsync<long>(@"
            INSERT INTO prod_products (
                product_no, uuid, pharmacy_no, branch_no, product_name, generic_no, company_no,
                medicine_type, unit_type, quantity_per_box,
                cost_per_box, sale_price_per_box, avg_purchase_price_per_piece, purchase_price_per_piece, sale_price_per_piece,
                wholesale_price_per_box, wholesale_price_per_piece,
                is_medicine, is_prescription_required, is_narcotic, min_stock_qty_pcs, is_active,
                created_at, updated_at
            ) VALUES (
                nextval('seq_product_no'), gen_random_uuid(), @p, @b, 'Ace 500mg Split Test', @p, @p,
                1, 1, 10,
                100, 150, 10, 10, 15,
                150, 15,
                TRUE, FALSE, FALSE, 10, TRUE,
                CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            ) RETURNING product_no;", new { p = TestPharmacyNo, b = TestBranchNo });

        // 2. Insert real batch with stock 5
        var realBatchNo = await conn.ExecuteScalarAsync<long>(@"
            INSERT INTO prod_product_batches (
                batch_no, pharmacy_no, branch_no, product_no, batch_number,
                expiry_date, box_quantity, quantity_in_box, total_quantity_pcs,
                cost_per_box, sale_price_per_box, vat_percent, is_expired, is_placeholder,
                created_at, updated_at
            ) VALUES (
                nextval('seq_prod_batch_no'), @p, @b, @productNo, 'BATCH-REAL-5',
                CURRENT_DATE + INTERVAL '1 year', 1, 10, 5,
                100, 150, 0, FALSE, FALSE,
                CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            ) RETURNING batch_no;", new { p = TestPharmacyNo, b = TestBranchNo, productNo });

        // Record initial ledger movement for the real batch (+5)
        await conn.ExecuteAsync(@"
            INSERT INTO inv_stock_movement (
                movement_no, pharmacy_no, branch_no, product_no, batch_no,
                movement_type, reference_doc_type, reference_doc_no, reference_doc_id,
                qty_pcs, unit_cost, balance_after_pcs, remarks, created_by_user_no, created_at
            ) VALUES (
                nextval('seq_inv_movement_no'), @p, @b, @productNo, @realBatchNo,
                1, 'PURCHASE', 'PO-INIT', 1,
                5, 10, 5, 'Initial stock', @u, CURRENT_TIMESTAMP
            );", new { p = TestPharmacyNo, b = TestBranchNo, productNo, realBatchNo, u = TestUserNo });

        // Ensure out_of_stock_sale is enabled
        await conn.ExecuteAsync("UPDATE cfg_sale_configs SET out_of_stock_sale = TRUE WHERE pharmacy_no = @p;", new { p = TestPharmacyNo });

        // --- Test B: Sell 8 units (5 from real batch, 3 from placeholder) ---
        var postingEngine = _serviceProvider.GetRequiredService<ISalePostingEngine>();
        await using (var tran = await conn.BeginTransactionAsync())
        {
            var req = new SalePostingRequest
            {
                PharmacyNo = TestPharmacyNo,
                BranchNo = TestBranchNo,
                UserNo = TestUserNo,
                Invoice = new SaleInvoice
                {
                    PharmacyNo = TestPharmacyNo,
                    BranchNo = TestBranchNo,
                    GrossTotalPrice = 120,
                    FinalPrice = 120,
                    CustomerWillPay = 120,
                    PaymentMethod = 1,
                    SaleTimestamp = DateTime.UtcNow
                },
                Items = new List<SaleInvoiceItem>
                {
                    new()
                    {
                        ProductNo = productNo,
                        SaleQty = 8,
                        UnitSalePrice = 15,
                        VatPercent = 0
                    }
                },
                Payments = new List<SaleInvoicePayment>
                {
                    new() { PaymentMethod = 1, Amount = 120 }
                }
            };

            var res = await postingEngine.ExecuteSaleAsync(conn, tran, req);
            await tran.CommitAsync();
            Assert.True(res.SaleInvoiceNo > 0);
        }

        // Verify Real Batch: 0 pcs remaining
        var realBatchQty = await conn.ExecuteScalarAsync<int>(
            "SELECT total_quantity_pcs FROM prod_product_batches WHERE batch_no = @realBatchNo;", new { realBatchNo });
        Assert.Equal(0, realBatchQty);

        // Verify Placeholder Batch: -3 pcs
        var placeholder = await conn.QuerySingleOrDefaultAsync<ProdProductBatch>(@"
            SELECT * FROM prod_product_batches
            WHERE product_no = @productNo AND is_placeholder = TRUE;", new { productNo });
        Assert.NotNull(placeholder);
        Assert.Equal(-3, placeholder.TotalQuantityPcs);

        // --- Test C: Purchase 10 units afterwards -> Settles placeholder (-3 + 3 = 0, new batch 10 - 3 = 7) ---
        var purchaseController = new PurchaseInvoiceController(
            _serviceProvider.GetRequiredService<IDbConnectionFactory>(),
            _serviceProvider.GetRequiredService<IDocumentNumberService>(),
            _serviceProvider.GetRequiredService<IStockPostingService>(),
            _serviceProvider.GetRequiredService<ILedgerPostingService>());

        purchaseController.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                RequestServices = _serviceProvider,
                User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim("pharmacy_no", TestPharmacyNo.ToString()),
                    new Claim("branch_no", TestBranchNo.ToString()),
                    new Claim("user_no", TestUserNo.ToString()),
                    new Claim("role_no", "10")
                }, "TestAuth"))
            }
        };

        var purchaseReq = new CreatePurchasePayload
        {
            SupplierNo = TestPharmacyNo,
            Invoice = new PurPurchaseInvoice
            {
                SupplierNo = TestPharmacyNo,
                GrossTotalPrice = 100,
                NetAmount = 100,
                FinalPrice = 100,
                PaidAmount = 100
            },
            Items = new List<PurPurchaseInvoiceItem>
            {
                new()
                {
                    ProductNo = productNo,
                    BatchNumber = "BATCH-NEW-10",
                    ExpiryDate = DateTime.UtcNow.AddYears(2),
                    BoxQty = 1,
                    QtyInBox = 10,
                    TotalQtyPcs = 10,
                    UnitCost = 10,
                    SalePrice = 15,
                    TotalPrice = 100,
                    Btp = 100
                }
            }
        };

        var purRes = await purchaseController.CreatePurchase(purchaseReq);
        var okPur = Assert.IsType<OkObjectResult>(purRes.Result);
        var purApiResp = Assert.IsType<ApiResponse<PurPurchaseInvoice>>(okPur.Value);
        Assert.True(purApiResp.Success);

        // Check Placeholder Batch quantity is settled back to 0!
        var settledPlaceholderQty = await conn.ExecuteScalarAsync<int>(
            "SELECT total_quantity_pcs FROM prod_product_batches WHERE batch_no = @pBatchNo;",
            new { pBatchNo = placeholder.BatchNo });
        Assert.Equal(0, settledPlaceholderQty);

        // Check New Batch quantity is 7 (10 received minus 3 settled)!
        var newBatch = await conn.QuerySingleOrDefaultAsync<ProdProductBatch>(@"
            SELECT * FROM prod_product_batches
            WHERE product_no = @productNo AND batch_number = 'BATCH-NEW-10';", new { productNo });
        Assert.NotNull(newBatch);
        Assert.Equal(7, newBatch.TotalQuantityPcs);

        // Check movements integrity: sum of ledger movements strictly equals total_quantity_pcs
        var newBatchLedgerSum = await conn.ExecuteScalarAsync<int>(
            "SELECT COALESCE(SUM(qty_pcs), 0) FROM inv_stock_movement WHERE batch_no = @batchNo;",
            new { batchNo = newBatch.BatchNo });
        Assert.Equal(newBatch.TotalQuantityPcs, newBatchLedgerSum);

        var placeholderLedgerSum = await conn.ExecuteScalarAsync<int>(
            "SELECT COALESCE(SUM(qty_pcs), 0) FROM inv_stock_movement WHERE batch_no = @batchNo;",
            new { batchNo = placeholder.BatchNo });
        Assert.Equal(settledPlaceholderQty, placeholderLedgerSum);
    }

    [Fact]
    public async Task Test_D_ExpiryQueries_And_Valuation_OmitPlaceholder()
    {
        if (_connectionString == null || _serviceProvider == null)
        {
            _output.WriteLine("Skipping: TEST_POSTGRES_CONNECTION not configured.");
            return;
        }

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        var batchController = new BatchController(_serviceProvider.GetRequiredService<IDbConnectionFactory>());
        batchController.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                RequestServices = _serviceProvider,
                User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim("pharmacy_no", TestPharmacyNo.ToString()),
                    new Claim("branch_no", TestBranchNo.ToString()),
                    new Claim("user_no", TestUserNo.ToString()),
                    new Claim("role_no", "9")
                }, "TestAuth"))
            }
        };

        // Get expired and near-expiry batches
        var expRes = await batchController.GetExpiredBatches();
        var okExp = Assert.IsType<OkObjectResult>(expRes.Result);
        var expData = Assert.IsType<ApiResponse<List<ProdProductBatch>>>(okExp.Value).Data;

        var nearExpRes = await batchController.GetNearExpiryBatches(90);
        var okNearExp = Assert.IsType<OkObjectResult>(nearExpRes.Result);
        var nearExpData = Assert.IsType<ApiResponse<List<ProdProductBatch>>>(okNearExp.Value).Data;

        // Verify no placeholder batch appears in expired or near-expiry batches
        Assert.DoesNotContain(expData ?? new(), b => b.IsPlaceholder || b.BatchNumber == "UNRECEIVED");
        Assert.DoesNotContain(nearExpData ?? new(), b => b.IsPlaceholder || b.BatchNumber == "UNRECEIVED");
    }

    [Fact]
    public async Task Test_E_Stocktake_Snapshot_Count_VariancePost_And_PermissionChecks()
    {
        if (_connectionString == null || _serviceProvider == null)
        {
            _output.WriteLine("Skipping: TEST_POSTGRES_CONNECTION not configured.");
            return;
        }

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        // 1. Create product and batch with 20 pcs
        var prodNo = await conn.ExecuteScalarAsync<long>(@"
            INSERT INTO prod_products (
                product_no, uuid, pharmacy_no, branch_no, product_name, generic_no, company_no,
                medicine_type, unit_type, quantity_per_box,
                cost_per_box, sale_price_per_box, avg_purchase_price_per_piece, purchase_price_per_piece, sale_price_per_piece,
                wholesale_price_per_box, wholesale_price_per_piece,
                is_medicine, is_prescription_required, is_narcotic, min_stock_qty_pcs, is_active,
                created_at, updated_at
            ) VALUES (
                nextval('seq_product_no'), gen_random_uuid(), @p, @b, 'Stocktake Test Product', @p, @p,
                1, 1, 10,
                100, 150, 10, 10, 15,
                150, 15,
                TRUE, FALSE, FALSE, 10, TRUE,
                CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            ) RETURNING product_no;", new { p = TestPharmacyNo, b = TestBranchNo });

        var batchNo = await conn.ExecuteScalarAsync<long>(@"
            INSERT INTO prod_product_batches (
                batch_no, pharmacy_no, branch_no, product_no, batch_number,
                expiry_date, box_quantity, quantity_in_box, total_quantity_pcs,
                cost_per_box, sale_price_per_box, vat_percent, is_expired, is_placeholder,
                created_at, updated_at
            ) VALUES (
                nextval('seq_prod_batch_no'), @p, @b, @prodNo, 'BATCH-ST-20',
                CURRENT_DATE + INTERVAL '1 year', 2, 10, 20,
                100, 150, 0, FALSE, FALSE,
                CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            ) RETURNING batch_no;", new { p = TestPharmacyNo, b = TestBranchNo, prodNo });

        await conn.ExecuteAsync(@"
            INSERT INTO inv_stock_movement (
                movement_no, pharmacy_no, branch_no, product_no, batch_no,
                movement_type, reference_doc_type, reference_doc_no, reference_doc_id,
                qty_pcs, unit_cost, balance_after_pcs, remarks, created_by_user_no, created_at
            ) VALUES (
                nextval('seq_stock_movement_no'), @p, @b, @prodNo, @batchNo,
                1, 'PURCHASE', 'PO-ST-1', 1,
                20, 10, 20, 'Initial stock for ST', @u, CURRENT_TIMESTAMP
            );", new { p = TestPharmacyNo, b = TestBranchNo, prodNo, batchNo, u = TestUserNo });

        var stocktakeController = new StocktakeController(
            _serviceProvider.GetRequiredService<IDbConnectionFactory>(),
            _serviceProvider.GetRequiredService<IStockPostingService>(),
            _serviceProvider.GetRequiredService<IDocumentNumberService>());

        // Create claims for testing permissions
        var authorizedUser = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("pharmacy_no", TestPharmacyNo.ToString()),
            new Claim("branch_no", TestBranchNo.ToString()),
            new Claim("user_no", TestUserNo.ToString()),
            new Claim("role_no", "9"),
            new Claim(ClaimTypes.Role, "Manager"),
            new Claim("permissions", "stock.adjust")
        }, "TestAuth"));

        var unauthorizedUser = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("pharmacy_no", TestPharmacyNo.ToString()),
            new Claim("branch_no", TestBranchNo.ToString()),
            new Claim("user_no", TestUserNo.ToString()),
            new Claim("role_no", "10"),
            new Claim(ClaimTypes.Role, "Pharmacist")
        }, "TestAuth"));

        // 1. Create Stocktake
        stocktakeController.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                RequestServices = _serviceProvider,
                User = authorizedUser
            }
        };

        var createRes = await stocktakeController.CreateStocktake(new CreateStocktakeRequest
        {
            FilterType = "PRODUCT",
            ProductIds = new List<long> { prodNo },
            Note = "Cycle Count Test"
        });

        var okCreate = Assert.IsType<OkObjectResult>(createRes.Result);
        var stResp = Assert.IsType<ApiResponse<InvStocktake>>(okCreate.Value);
        Assert.True(stResp.Success);
        var stocktakeNo = stResp.Data!.StocktakeNo;

        // Retrieve items to find stocktake_item_no
        var getRes = await stocktakeController.GetStocktake(stocktakeNo);
        var okGet = Assert.IsType<OkObjectResult>(getRes.Result);
        var getResp = Assert.IsType<ApiResponse<InvStocktake>>(okGet.Value);
        var item = Assert.Single(getResp.Data!.Items!);
        Assert.Equal(20, item.SystemQtyPcs);

        // 2. Counted = 25 (Variance = +5). Save Count without reason.
        await stocktakeController.SaveCounts(stocktakeNo, new SaveStocktakeCountsRequest
        {
            Items = new List<StocktakeCountEntry>
            {
                new() { StocktakeItemNo = item.StocktakeItemNo, CountedQtyPcs = 25, Reason = "" }
            }
        });

        // 3. Post without reason on variance item -> rejected 400
        var postNoReasonRes = await stocktakeController.PostStocktake(stocktakeNo);
        var badReq = Assert.IsType<ObjectResult>(postNoReasonRes.Result);
        Assert.Equal(400, badReq.StatusCode);

        // 4. Save Count with valid reason
        await stocktakeController.SaveCounts(stocktakeNo, new SaveStocktakeCountsRequest
        {
            Items = new List<StocktakeCountEntry>
            {
                new() { StocktakeItemNo = item.StocktakeItemNo, CountedQtyPcs = 25, Reason = "Shelf recount surplus" }
            }
        });

        // 5. Post without permission -> 403 Forbidden
        stocktakeController.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                RequestServices = _serviceProvider,
                User = unauthorizedUser
            }
        };
        var postNoPermRes = await stocktakeController.PostStocktake(stocktakeNo);
        var forbiddenReq = Assert.IsType<ObjectResult>(postNoPermRes.Result);
        Assert.Equal(403, forbiddenReq.StatusCode);

        // 6. Post with authorized user and valid reason -> 200 OK
        stocktakeController.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                RequestServices = _serviceProvider,
                User = authorizedUser
            }
        };
        var postSuccessRes = await stocktakeController.PostStocktake(stocktakeNo);
        var okPost = Assert.IsType<OkObjectResult>(postSuccessRes.Result);
        var postResp = Assert.IsType<ApiResponse<object>>(okPost.Value);
        Assert.True(postResp.Success);

        // Verify Batch total_quantity_pcs is now 25!
        var updatedBatchQty = await conn.ExecuteScalarAsync<int>(
            "SELECT total_quantity_pcs FROM prod_product_batches WHERE batch_no = @batchNo;", new { batchNo });
        Assert.Equal(25, updatedBatchQty);

        // Verify ledger sum is 20 + 5 = 25!
        var ledgerSum = await conn.ExecuteScalarAsync<int>(
            "SELECT SUM(qty_pcs) FROM inv_stock_movement WHERE batch_no = @batchNo;", new { batchNo });
        Assert.Equal(25, ledgerSum);

        // 7. Second post attempt -> rejected (400)
        var secondPostRes = await stocktakeController.PostStocktake(stocktakeNo);
        var secondBadReq = Assert.IsType<ObjectResult>(secondPostRes.Result);
        Assert.Equal(400, secondBadReq.StatusCode);
    }

    [Fact]
    public async Task Test_F_ExpiryMaintenanceJob_FlipsRealBatch_IgnoresPlaceholder()
    {
        if (_connectionString == null || _serviceProvider == null)
        {
            _output.WriteLine("Skipping: TEST_POSTGRES_CONNECTION not configured.");
            return;
        }

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        var productNo = await conn.ExecuteScalarAsync<long>(@"
            INSERT INTO prod_products (
                product_no, uuid, pharmacy_no, branch_no, product_name, generic_no, company_no,
                medicine_type, unit_type, quantity_per_box,
                cost_per_box, sale_price_per_box, avg_purchase_price_per_piece, purchase_price_per_piece, sale_price_per_piece,
                wholesale_price_per_box, wholesale_price_per_piece,
                is_medicine, is_prescription_required, is_narcotic, min_stock_qty_pcs, is_active,
                created_at, updated_at
            ) VALUES (
                nextval('seq_product_no'), gen_random_uuid(), @p, @b, 'Expiry Sweep Test Product', @p, @p,
                1, 1, 10,
                100, 150, 10, 10, 15,
                150, 15,
                TRUE, FALSE, FALSE, 10, TRUE,
                CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            ) RETURNING product_no;", new { p = TestPharmacyNo, b = TestBranchNo });

        // Insert expired real batch (yesterday)
        var realBatchNo = await conn.ExecuteScalarAsync<long>(@"
            INSERT INTO prod_product_batches (
                batch_no, pharmacy_no, branch_no, product_no, batch_number,
                expiry_date, box_quantity, quantity_in_box, total_quantity_pcs,
                cost_per_box, sale_price_per_box, vat_percent, is_expired, is_placeholder,
                created_at, updated_at
            ) VALUES (
                nextval('seq_prod_batch_no'), @p, @b, @productNo, 'BATCH-EXP-YESTERDAY',
                CURRENT_DATE - INTERVAL '1 day', 1, 10, 10,
                100, 150, 0, FALSE, FALSE,
                CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            ) RETURNING batch_no;", new { p = TestPharmacyNo, b = TestBranchNo, productNo });

        // Insert placeholder batch (also has past/today date)
        var placeholderBatchNo = await conn.ExecuteScalarAsync<long>(@"
            INSERT INTO prod_product_batches (
                batch_no, pharmacy_no, branch_no, product_no, batch_number,
                expiry_date, box_quantity, quantity_in_box, total_quantity_pcs,
                cost_per_box, sale_price_per_box, vat_percent, is_expired, is_placeholder,
                created_at, updated_at
            ) VALUES (
                nextval('seq_prod_batch_no'), @p, @b, @productNo, 'UNRECEIVED',
                CURRENT_DATE - INTERVAL '1 day', 0, 10, -2,
                100, 150, 0, FALSE, TRUE,
                CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            ) RETURNING batch_no;", new { p = TestPharmacyNo, b = TestBranchNo, productNo });

        var maintenanceService = new ExpiryMaintenanceService(
            _serviceProvider.GetRequiredService<IDbConnectionFactory>(),
            NullLogger<ExpiryMaintenanceService>.Instance,
            _config);

        var sweepCount = await maintenanceService.RunExpirySweepAsync(CancellationToken.None);
        Assert.True(sweepCount >= 1);

        // Real batch: is_expired is TRUE
        var realIsExpired = await conn.ExecuteScalarAsync<bool>(
            "SELECT is_expired FROM prod_product_batches WHERE batch_no = @realBatchNo;", new { realBatchNo });
        Assert.True(realIsExpired);

        // Placeholder batch: is_expired remains FALSE!
        var placeholderIsExpired = await conn.ExecuteScalarAsync<bool>(
            "SELECT is_expired FROM prod_product_batches WHERE batch_no = @placeholderBatchNo;", new { placeholderBatchNo });
        Assert.False(placeholderIsExpired);
    }

    [Fact]
    public async Task Test_G_StockLedgerIntegrity_ReportsMismatch_DayEndCheck4UsesIt()
    {
        if (_connectionString == null || _serviceProvider == null)
        {
            _output.WriteLine("Skipping: TEST_POSTGRES_CONNECTION not configured.");
            return;
        }

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        var integrityService = _serviceProvider.GetRequiredService<IStockIntegrityService>();
        var cleanResult = await integrityService.CheckIntegrityAsync(conn, TestPharmacyNo, TestBranchNo);

        Assert.True(cleanResult.IsClean);
        Assert.Equal(0, cleanResult.TotalDiscrepancies);

        var prodNo = await conn.ExecuteScalarAsync<long>(@"
            INSERT INTO prod_products (
                product_no, uuid, pharmacy_no, branch_no, product_name, generic_no, company_no,
                medicine_type, unit_type, quantity_per_box,
                cost_per_box, sale_price_per_box, avg_purchase_price_per_piece, purchase_price_per_piece, sale_price_per_piece,
                wholesale_price_per_box, wholesale_price_per_piece,
                is_medicine, is_prescription_required, is_narcotic, min_stock_qty_pcs, is_active,
                created_at, updated_at
            ) VALUES (
                nextval('seq_product_no'), gen_random_uuid(), @p, @b, 'Tamper Test Product', @p, @p,
                1, 1, 10,
                100, 150, 10, 10, 15,
                150, 15,
                TRUE, FALSE, FALSE, 10, TRUE,
                CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            ) RETURNING product_no;", new { p = TestPharmacyNo, b = TestBranchNo });

        // Deliberately introduce a tamper/mismatch: create a batch with 999 pcs but only 10 in ledger
        var tamperedBatchNo = await conn.ExecuteScalarAsync<long>(@"
            INSERT INTO prod_product_batches (
                batch_no, pharmacy_no, branch_no, product_no, batch_number,
                expiry_date, box_quantity, quantity_in_box, total_quantity_pcs,
                cost_per_box, sale_price_per_box, vat_percent, is_expired, is_placeholder,
                created_at, updated_at
            ) VALUES (
                nextval('seq_prod_batch_no'), @p, @b, @prodNo, 'BATCH-TAMPERED',
                CURRENT_DATE + INTERVAL '1 year', 10, 10, 999,
                100, 150, 0, FALSE, FALSE,
                CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            ) RETURNING batch_no;", new { p = TestPharmacyNo, b = TestBranchNo, prodNo });

        await conn.ExecuteAsync(@"
            INSERT INTO inv_stock_movement (
                movement_no, pharmacy_no, branch_no, product_no, batch_no,
                movement_type, reference_doc_type, reference_doc_no, reference_doc_id,
                qty_pcs, unit_cost, balance_after_pcs, remarks, created_by_user_no, created_at
            ) VALUES (
                nextval('seq_stock_movement_no'), @p, @b, @prodNo, @tamperedBatchNo,
                1, 'PURCHASE', 'PO-TAMP', 1,
                10, 10, 10, 'Only 10 recorded', @u, CURRENT_TIMESTAMP
            );", new { p = TestPharmacyNo, b = TestBranchNo, prodNo, tamperedBatchNo, u = TestUserNo });

        var dirtyResult = await integrityService.CheckIntegrityAsync(conn, TestPharmacyNo, TestBranchNo);
        Assert.False(dirtyResult.IsClean);
        Assert.True(dirtyResult.DiscrepantLedgerCount >= 1);
        Assert.Contains(dirtyResult.OffendingBatches, d => d.BatchNo == tamperedBatchNo && d.IssueType == "LEDGER_MISMATCH");

        // Day-End Check 4 verification
        var closerController = new CashCloserController(
            _serviceProvider.GetRequiredService<IDbConnectionFactory>(),
            _serviceProvider.GetRequiredService<ILedgerPostingService>(),
            integrityService);

        closerController.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                RequestServices = _serviceProvider,
                User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim("pharmacy_no", TestPharmacyNo.ToString()),
                    new Claim("branch_no", TestBranchNo.ToString()),
                    new Claim("user_no", TestUserNo.ToString()),
                    new Claim("role_no", "9"),
                    new Claim(ClaimTypes.Role, "Manager")
                }, "TestAuth"))
            }
        };

        var checkRes = await closerController.GetDayEndReconciliation(DateTime.UtcNow.Date);
        var okRecon = Assert.IsType<OkObjectResult>(checkRes.Result);
        var reconResp = Assert.IsType<ApiResponse<DayEndReconciliationResult>>(okRecon.Value);
        Assert.NotNull(reconResp.Data);
        Assert.False(reconResp.Data.StockBatchCheck.Passed);
        Assert.False(reconResp.Data.AllChecksPassed);
    }

    private static string? FindSolutionRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "AGENTS.md")))
            {
                return dir;
            }
            dir = Directory.GetParent(dir)?.FullName;
        }
        return null;
    }
}

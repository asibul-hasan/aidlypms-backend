using AidlyPms.Api.Common;
using AidlyPms.Api.Common.Database;
using Dapper;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace AidlyPms.Api.Tests;

[Collection("PostgresDatabaseIntegration")]
public class PaymentAccountResolverTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private readonly string? _baseConnectionString;
    private PostgresTestSchemaHarness? _harness;
    private string? _connectionString;

    public const long TestPharmacyNo = 9005;
    public const long TestBranchNo = 9005;

    private sealed class TestDbConnectionFactory : IDbConnectionFactory
    {
        private readonly string _connectionString;
        public TestDbConnectionFactory(string connectionString) => _connectionString = connectionString;
        public NpgsqlConnection CreateConnection() => new NpgsqlConnection(_connectionString);
        public async Task<NpgsqlConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default)
        {
            var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);
            return conn;
        }
    }

    public PaymentAccountResolverTests(ITestOutputHelper output)
    {
        _output = output;
        Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;
        _baseConnectionString = Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION");
    }

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(_baseConnectionString))
        {
            _output.WriteLine("TEST_POSTGRES_CONNECTION is not set. PaymentAccountResolverTests will run in dry-run mode.");
            return;
        }

        _harness = await PostgresTestSchemaHarness.CreateAsync(_baseConnectionString);
        _connectionString = _harness.IsolatedConnectionString;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await SeedRealChartAsync(conn);
    }

    public async Task DisposeAsync()
    {
        if (_harness != null)
        {
            await _harness.DisposeAsync();
        }
    }

    private static async Task SeedRealChartAsync(NpgsqlConnection conn)
    {
        const long p = TestPharmacyNo;
        const long b = TestBranchNo;

        // Seed pharmacy & branch
        await conn.ExecuteAsync(@"
            INSERT INTO sys_pharmacies (pharmacy_no, pharmacy_name, bin_number, drug_license_no, is_active, created_at, updated_at)
            VALUES (@p, 'Resolver Test Pharmacy', '123456789', 'DL-TEST-001', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
            ON CONFLICT (pharmacy_no) DO NOTHING;

            INSERT INTO sys_branches (branch_no, pharmacy_no, branch_name, is_head_office, is_active, created_at, updated_at)
            VALUES (@b, @p, 'Resolver Main Branch', TRUE, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
            ON CONFLICT (branch_no) DO NOTHING;
        ", new { p, b });

        // Seed the REAL standard chart matching schema.sql (accounts 1..21)
        await conn.ExecuteAsync(@"
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
        ", new { p, b });
    }

    [Fact]
    public async Task ResolveAccountNo_BkashSale_ResolvesToType4Account()
    {
        if (string.IsNullOrWhiteSpace(_connectionString)) return;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        var resolver = new PaymentAccountResolver(new TestDbConnectionFactory(_connectionString));
        // PaymentMethod 3 = bKash -> should resolve account_type 4 (bKash Merchant Wallet: account TestPharmacyNo * 100 + 13)
        var accountNo = await resolver.ResolveAccountNoAsync(conn, null, TestPharmacyNo, TestBranchNo, 3);

        Assert.Equal(TestPharmacyNo * 100 + 13, accountNo);
    }

    [Fact]
    public async Task ResolveAccountNo_NagadSale_ResolvesToType5Account()
    {
        if (string.IsNullOrWhiteSpace(_connectionString)) return;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        var resolver = new PaymentAccountResolver(new TestDbConnectionFactory(_connectionString));
        // PaymentMethod 4 = Nagad -> should resolve account_type 5 (Nagad Merchant Wallet: account TestPharmacyNo * 100 + 14)
        var accountNo = await resolver.ResolveAccountNoAsync(conn, null, TestPharmacyNo, TestBranchNo, 4);

        Assert.Equal(TestPharmacyNo * 100 + 14, accountNo);
    }

    [Fact]
    public async Task ResolveAccountNo_CardSale_ResolvesToType7Account()
    {
        if (string.IsNullOrWhiteSpace(_connectionString)) return;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        var resolver = new PaymentAccountResolver(new TestDbConnectionFactory(_connectionString));
        // PaymentMethod 6 = Card -> should resolve account_type 7 (Card POS Settlement: account TestPharmacyNo * 100 + 15)
        var accountNo6 = await resolver.ResolveAccountNoAsync(conn, null, TestPharmacyNo, TestBranchNo, 6);
        Assert.Equal(TestPharmacyNo * 100 + 15, accountNo6);

        // PaymentMethod 7 = Card alias -> also resolves account_type 7
        var accountNo7 = await resolver.ResolveAccountNoAsync(conn, null, TestPharmacyNo, TestBranchNo, 7);
        Assert.Equal(TestPharmacyNo * 100 + 15, accountNo7);
    }

    [Fact]
    public async Task ResolveAccountNo_RocketWithNoAccount_Throws400PosSaleException()
    {
        if (string.IsNullOrWhiteSpace(_connectionString)) return;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        var resolver = new PaymentAccountResolver(new TestDbConnectionFactory(_connectionString));

        // PaymentMethod 5 = Rocket -> No account_type = 6 is in the seeded chart
        var ex = await Assert.ThrowsAsync<PosSaleException>(async () =>
        {
            await resolver.ResolveAccountNoAsync(conn, null, TestPharmacyNo, TestBranchNo, 5);
        });

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal("No account configured for this payment method. Add one in Accounts.", ex.ErrorMessage);
    }

    [Fact]
    public async Task ResolveAccountNo_NeverFallsBackToCash()
    {
        if (string.IsNullOrWhiteSpace(_connectionString)) return;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        var resolver = new PaymentAccountResolver(new TestDbConnectionFactory(_connectionString));

        // Ensure Cash Drawer exists (account TestPharmacyNo * 100 + 1)
        var cashAcc = await conn.ExecuteScalarAsync<long>(
            "SELECT account_no FROM acc_transaction_accounts WHERE pharmacy_no = @p AND account_type = 1 LIMIT 1;",
            new { p = TestPharmacyNo });
        Assert.True(cashAcc > 0);

        // Deactivate bKash account so no type 4 or 3 exists
        await conn.ExecuteAsync(
            "UPDATE acc_transaction_accounts SET is_active = FALSE WHERE pharmacy_no = @p AND account_type IN (3, 4);",
            new { p = TestPharmacyNo });

        try
        {
            // Now attempt to resolve bKash (3) -> Must NOT fall back to cashAcc!
            var ex = await Assert.ThrowsAsync<PosSaleException>(async () =>
            {
                await resolver.ResolveAccountNoAsync(conn, null, TestPharmacyNo, TestBranchNo, 3);
            });

            Assert.Equal(400, ex.StatusCode);
            Assert.Equal("No account configured for this payment method. Add one in Accounts.", ex.ErrorMessage);
        }
        finally
        {
            // Restore bKash account
            await conn.ExecuteAsync(
                "UPDATE acc_transaction_accounts SET is_active = TRUE WHERE pharmacy_no = @p AND account_type IN (3, 4);",
                new { p = TestPharmacyNo });
        }
    }

    [Fact]
    public async Task ResolveAccountNo_BkashPersonalFallback_ResolvesType3WhenType4Missing()
    {
        if (string.IsNullOrWhiteSpace(_connectionString)) return;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        var resolver = new PaymentAccountResolver(new TestDbConnectionFactory(_connectionString));
        const long p = TestPharmacyNo;
        const long b = TestBranchNo;

        // Insert bKash Personal account (account_type = 3)
        const long personalBkashAcc = p * 100 + 30;
        await conn.ExecuteAsync(@"
            INSERT INTO acc_transaction_accounts (
                account_no, pharmacy_no, branch_no, account_category, account_code, account_name, account_type, bank_account_number, bank_name, current_balance, is_active, created_at, updated_at
            ) VALUES (
                @personalBkashAcc, @p, @b, 1, '10106', 'bKash Personal Agent', 3, '01700000003', 'bKash Personal', 0, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            ) ON CONFLICT (account_no) DO UPDATE SET is_active = TRUE;",
            new { personalBkashAcc, p, b });

        // Temporarily deactivate bKash Merchant (type 4)
        await conn.ExecuteAsync(
            "UPDATE acc_transaction_accounts SET is_active = FALSE WHERE pharmacy_no = @p AND account_type = 4;",
            new { p });

        try
        {
            // Resolver should fall back to type 3 (bKash Personal)
            var resolved = await resolver.ResolveAccountNoAsync(conn, null, p, b, 3);
            Assert.Equal(personalBkashAcc, resolved);
        }
        finally
        {
            // Restore type 4 and clean up type 3
            await conn.ExecuteAsync(
                "UPDATE acc_transaction_accounts SET is_active = TRUE WHERE pharmacy_no = @p AND account_type = 4;",
                new { p });
            await conn.ExecuteAsync(
                "DELETE FROM acc_transaction_accounts WHERE account_no = @personalBkashAcc;",
                new { personalBkashAcc });
        }
    }

    [Fact]
    public async Task CashDrawerTotals_ContainOnlyCash_ExcludesOtherPaymentMethods()
    {
        if (string.IsNullOrWhiteSpace(_connectionString)) return;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        const long p = TestPharmacyNo;
        const long b = TestBranchNo;

        // Seed customer
        await conn.ExecuteAsync(@"
            INSERT INTO cust_customers (customer_no, pharmacy_no, branch_no, name, phone, is_active, created_at, updated_at)
            VALUES (@p * 10, @p, @b, 'Drawer Test Customer', '01911111111', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
            ON CONFLICT (customer_no) DO NOTHING;
        ", new { p, b });

        // Insert two sale invoices today: one paid in Cash (৳500), one paid in bKash (৳300)
        var saleNo1 = await conn.ExecuteScalarAsync<long>("SELECT nextval('seq_sale_invoice_no');");
        var saleNo2 = await conn.ExecuteScalarAsync<long>("SELECT nextval('seq_sale_invoice_no');");

        await conn.ExecuteAsync(@"
            INSERT INTO sale_invoices (
                sale_invoice_no, pharmacy_no, branch_no, customer_no, sales_number,
                gross_total_price, final_price, total_tax, total_discount,
                customer_will_pay, paid_amount, due_amount, change_amount,
                payment_method, document_status, sale_timestamp, created_at, updated_at
            ) VALUES
            (@saleNo1, @p, @b, @p * 10, 'INV-CASH-001', 500, 500, 0, 0, 500, 500, 0, 0, 1, 2, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
            (@saleNo2, @p, @b, @p * 10, 'INV-BKASH-001', 300, 300, 0, 0, 300, 300, 0, 0, 3, 2, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);

            INSERT INTO sale_invoice_payments (sale_invoice_payment_no, sale_invoice_no, payment_method, amount, account_no)
            VALUES
            (nextval('seq_sale_invoice_payment_no'), @saleNo1, 1, 500, @p * 100 + 1),
            (nextval('seq_sale_invoice_payment_no'), @saleNo2, 3, 300, @p * 100 + 13);
        ", new { saleNo1, saleNo2, p, b });

        // Query the cash sales payment sum used by CashCloserController
        var payments = (await conn.QueryAsync<(short PaymentMethod, decimal Amount)>(@"
            SELECT p.payment_method, COALESCE(SUM(p.amount), 0) AS total_collected
            FROM sale_invoice_payments p
            INNER JOIN sale_invoices s ON p.sale_invoice_no = s.sale_invoice_no
            WHERE s.pharmacy_no = @p AND s.branch_no = @b
              AND s.document_status = 2
              AND s.sale_invoice_no IN (@saleNo1, @saleNo2)
            GROUP BY p.payment_method;",
            new { p, b, saleNo1, saleNo2 })).ToDictionary(x => x.PaymentMethod, x => x.Amount);

        decimal cashSales = payments.GetValueOrDefault((short)1, 0m);
        decimal bkashSales = payments.GetValueOrDefault((short)3, 0m);

        // Assert strictly: Cash drawer totals contain ONLY cash (৳500), not bKash (৳300)
        Assert.Equal(500m, cashSales);
        Assert.Equal(300m, bkashSales);
    }

    [Fact]
    public async Task PharmacyChartSeeder_SeedsCardAndRocketForNewPharmacy_SkipsExistingPharmacy()
    {
        if (string.IsNullOrWhiteSpace(_connectionString)) return;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        var seeder = new PharmacyChartSeeder();

        // 1. Existing pharmacy (TestPharmacyNo) already has accounts -> no new accounts inserted
        var existingCountBefore = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM acc_transaction_accounts WHERE pharmacy_no = @p;",
            new { p = TestPharmacyNo });
        await seeder.SeedDefaultChartForPharmacyAsync(conn, null, TestPharmacyNo, TestBranchNo);
        var existingCountAfter = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM acc_transaction_accounts WHERE pharmacy_no = @p;",
            new { p = TestPharmacyNo });
        Assert.Equal(existingCountBefore, existingCountAfter);

        // 2. New pharmacy (TestPharmacyNo + 88) with 0 accounts
        const long newP = TestPharmacyNo + 88;
        const long newB = TestBranchNo + 88;

        await conn.ExecuteAsync(@"
            INSERT INTO sys_pharmacies (pharmacy_no, pharmacy_name, bin_number, drug_license_no, is_active, created_at, updated_at)
            VALUES (@newP, 'Brand New Pharmacy', '999999999', 'DL-NEW-001', TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
            ON CONFLICT (pharmacy_no) DO NOTHING;

            INSERT INTO sys_branches (branch_no, pharmacy_no, branch_name, is_head_office, is_active, created_at, updated_at)
            VALUES (@newB, @newP, 'Brand New Main Branch', TRUE, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
            ON CONFLICT (branch_no) DO NOTHING;
        ", new { newP, newB });

        await seeder.SeedDefaultChartForPharmacyAsync(conn, null, newP, newB);
        var newCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM acc_transaction_accounts WHERE pharmacy_no = @newP;",
            new { newP });
        Assert.True(newCount > 0, "Seeder should have inserted accounts for new pharmacy");

        // Verify Card POS Machine (account_type = 7) and Rocket (account_type = 6) were seeded
        var cardAccount = await conn.QuerySingleOrDefaultAsync<dynamic>(@"
            SELECT account_no, account_code, account_name, account_type
            FROM acc_transaction_accounts
            WHERE pharmacy_no = @newP AND account_type = 7;",
            new { newP });

        Assert.NotNull(cardAccount);
        Assert.Equal("10104", (string)cardAccount!.account_code);

        var rocketAccount = await conn.QuerySingleOrDefaultAsync<dynamic>(@"
            SELECT account_no, account_code, account_name, account_type
            FROM acc_transaction_accounts
            WHERE pharmacy_no = @newP AND account_type = 6;",
            new { newP });

        Assert.NotNull(rocketAccount);
        Assert.Equal("10105", (string)rocketAccount!.account_code);
    }
}

using Dapper;
using Npgsql;

namespace AidlyPms.Api.Common;

public interface IPharmacyChartSeeder
{
    Task SeedDefaultChartForPharmacyAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction? tran,
        long pharmacyNo,
        long branchNo);
}

public class PharmacyChartSeeder : IPharmacyChartSeeder
{
    public record DefaultAccountTemplate(
        string AccountCode,
        string AccountName,
        short AccountCategory,
        short AccountType,
        string? BankName = null,
        string? PhoneNumber = null);

    public static readonly IReadOnlyList<DefaultAccountTemplate> DefaultAccounts = new List<DefaultAccountTemplate>
    {
        new("10101", "Main Cash Drawer (Cash in Hand)", 1, 1),
        new("10201", "Bank Current Account (Main)", 1, 2, "Main Commercial Bank"),
        new("10301", "Accounts Receivable (Customer Due)", 1, 99),
        new("10401", "Merchandise Inventory Asset", 1, 99),
        new("10501", "Input VAT Asset (Tax Advance)", 1, 99),
        new("20101", "Accounts Payable (Supplier Due)", 2, 99),
        new("20201", "Output VAT Payable (Tax Due)", 2, 99),
        new("30101", "Owner Capital & Retained Earnings", 3, 99),
        new("40101", "Sales Revenue", 4, 99),
        new("40102", "Sales Returns and Allowances", 4, 99),
        new("50101", "Cost of Goods Sold (COGS)", 5, 99),
        new("50201", "General & Operating Expenses", 5, 99),
        new("10102", "bKash Merchant Wallet", 1, 4, "bKash Merchant"),
        new("10103", "Nagad Merchant Wallet", 1, 5, "Nagad Merchant"),
        new("10104", "Card POS Machine", 1, 7, "Card POS Settlement"),
        new("10105", "Rocket", 1, 6, "Rocket Merchant"),
        new("40202", "Rounding Adjustment Gain", 4, 99),
        new("50203", "Rounding Adjustment Loss", 5, 99),
        new("50204", "Cash Drawer Variance & Shortage", 5, 99),
        new("40203", "Cash Drawer Variance & Overage", 4, 99),
        new("10901", "Return Credit Clearing Account", 1, 99, "Internal Clearing")
    };

    public async Task SeedDefaultChartForPharmacyAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction? tran,
        long pharmacyNo,
        long branchNo)
    {
        // For new pharmacies only. Does not auto-create on existing pharmacies where accounts already exist.
        var hasExistingAccounts = await conn.ExecuteScalarAsync<bool>(@"
            SELECT EXISTS(
                SELECT 1 FROM acc_transaction_accounts
                WHERE pharmacy_no = @pharmacyNo AND branch_no = @branchNo
            );",
            new { pharmacyNo, branchNo },
            transaction: tran);

        if (hasExistingAccounts)
        {
            return;
        }

        const string insertSql = @"
            INSERT INTO acc_transaction_accounts (
                account_no, pharmacy_no, branch_no, account_category, account_code,
                account_name, phone_number, account_type, bank_account_number, bank_name,
                current_balance, is_active, created_at, updated_at
            ) VALUES (
                nextval('seq_account_no'), @PharmacyNo, @BranchNo, @AccountCategory, @AccountCode,
                @AccountName, @PhoneNumber, @AccountType, NULL, @BankName,
                0.0000, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            );";

        foreach (var acc in DefaultAccounts)
        {
            await conn.ExecuteAsync(insertSql, new
            {
                PharmacyNo = pharmacyNo,
                BranchNo = branchNo,
                acc.AccountCategory,
                acc.AccountCode,
                acc.AccountName,
                acc.PhoneNumber,
                acc.AccountType,
                acc.BankName
            }, transaction: tran);
        }
    }
}

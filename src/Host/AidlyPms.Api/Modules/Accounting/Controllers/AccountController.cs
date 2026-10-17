using AidlyPms.Api.Common;
using AidlyPms.Api.Common.Database;
using AidlyPms.Api.Modules.Accounting.Models;
using Dapper;
using Microsoft.AspNetCore.Mvc;

namespace AidlyPms.Api.Modules.Accounting.Controllers;

[Route("api/v1/acc/accounts")]
public class AccountController : BaseController
{
    private readonly IDbConnectionFactory _db;

    public AccountController(IDbConnectionFactory db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<AccTransactionAccount>>>> GetAccounts(
        [FromQuery] short? accountCategory,
        [FromQuery] short? accountType,
        [FromQuery] string? search)
    {
        using var conn = _db.CreateConnection();
        var sql = @"
            SELECT a.*,
                   (
                       (SELECT COUNT(1) FROM acc_transaction_ledger l WHERE l.account_no = a.account_no AND l.pharmacy_no = @CurrentPharmacyNo AND l.branch_no = @CurrentBranchNo)
                       + (SELECT COUNT(1) FROM acc_expenses e WHERE e.account_no = a.account_no AND e.pharmacy_no = @CurrentPharmacyNo AND e.branch_no = @CurrentBranchNo)
                       + (SELECT COUNT(1) FROM acc_incomes i WHERE i.account_no = a.account_no AND i.pharmacy_no = @CurrentPharmacyNo AND i.branch_no = @CurrentBranchNo)
                       + (SELECT COUNT(1) FROM acc_additional_balances b WHERE b.account_no = a.account_no AND b.pharmacy_no = @CurrentPharmacyNo AND b.branch_no = @CurrentBranchNo)
                       + (SELECT COUNT(1) FROM due_collections d WHERE d.account_no = a.account_no AND d.pharmacy_no = @CurrentPharmacyNo AND d.branch_no = @CurrentBranchNo)
                       + (SELECT COUNT(1) FROM sale_invoice_payments p WHERE p.account_no = a.account_no AND p.pharmacy_no = @CurrentPharmacyNo AND p.branch_no = @CurrentBranchNo)
                       + (SELECT COUNT(1) FROM pur_supplier_payments sp WHERE sp.account_no = a.account_no AND sp.pharmacy_no = @CurrentPharmacyNo AND sp.branch_no = @CurrentBranchNo)
                       + (SELECT COUNT(1) FROM acc_reconciliations r WHERE (r.payment_from_account_no = a.account_no OR r.payment_to_account_no = a.account_no) AND r.pharmacy_no = @CurrentPharmacyNo AND r.branch_no = @CurrentBranchNo)
                   ) AS transaction_count
            FROM acc_transaction_accounts a
            WHERE a.pharmacy_no = @CurrentPharmacyNo AND a.branch_no = @CurrentBranchNo";

        var pms = new DynamicParameters();
        pms.Add("CurrentPharmacyNo", CurrentPharmacyNo);
        pms.Add("CurrentBranchNo", CurrentBranchNo);

        if (accountCategory.HasValue)
        {
            sql += " AND a.account_category = @AccountCategory";
            pms.Add("AccountCategory", accountCategory.Value);
        }

        if (accountType.HasValue)
        {
            sql += " AND a.account_type = @AccountType";
            pms.Add("AccountType", accountType.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            sql += " AND (a.account_name ILIKE @Search OR a.account_code ILIKE @Search OR a.bank_account_number ILIKE @Search)";
            pms.Add("Search", $"%{search.Trim()}%");
        }

        sql += " ORDER BY a.account_category ASC, a.account_code ASC, a.account_name ASC;";
        var list = (await conn.QueryAsync<AccTransactionAccount>(sql, pms)).ToList();

        return OkResponse(list);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<AccTransactionAccount>>> GetAccountById(long id)
    {
        using var conn = _db.CreateConnection();
        var account = await conn.QuerySingleOrDefaultAsync<AccTransactionAccount>(@"
            SELECT a.*,
                   (
                       (SELECT COUNT(1) FROM acc_transaction_ledger l WHERE l.account_no = a.account_no AND l.pharmacy_no = @CurrentPharmacyNo AND l.branch_no = @CurrentBranchNo)
                       + (SELECT COUNT(1) FROM acc_expenses e WHERE e.account_no = a.account_no AND e.pharmacy_no = @CurrentPharmacyNo AND e.branch_no = @CurrentBranchNo)
                       + (SELECT COUNT(1) FROM acc_incomes i WHERE i.account_no = a.account_no AND i.pharmacy_no = @CurrentPharmacyNo AND i.branch_no = @CurrentBranchNo)
                       + (SELECT COUNT(1) FROM acc_additional_balances b WHERE b.account_no = a.account_no AND b.pharmacy_no = @CurrentPharmacyNo AND b.branch_no = @CurrentBranchNo)
                       + (SELECT COUNT(1) FROM due_collections d WHERE d.account_no = a.account_no AND d.pharmacy_no = @CurrentPharmacyNo AND d.branch_no = @CurrentBranchNo)
                       + (SELECT COUNT(1) FROM sale_invoice_payments p WHERE p.account_no = a.account_no AND p.pharmacy_no = @CurrentPharmacyNo AND p.branch_no = @CurrentBranchNo)
                       + (SELECT COUNT(1) FROM pur_supplier_payments sp WHERE sp.account_no = a.account_no AND sp.pharmacy_no = @CurrentPharmacyNo AND sp.branch_no = @CurrentBranchNo)
                       + (SELECT COUNT(1) FROM acc_reconciliations r WHERE (r.payment_from_account_no = a.account_no OR r.payment_to_account_no = a.account_no) AND r.pharmacy_no = @CurrentPharmacyNo AND r.branch_no = @CurrentBranchNo)
                   ) AS transaction_count
            FROM acc_transaction_accounts a
            WHERE a.account_no = @Id AND a.pharmacy_no = @CurrentPharmacyNo AND a.branch_no = @CurrentBranchNo;",
            new { Id = id, CurrentPharmacyNo, CurrentBranchNo });

        if (account == null)
            return FailResponse<AccTransactionAccount>("Account not found.", statusCode: 404);

        return OkResponse(account);
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<AccTransactionAccount>>> CreateAccount([FromBody] AccTransactionAccount model)
    {
        if (string.IsNullOrWhiteSpace(model.AccountName))
            return FailResponse<AccTransactionAccount>("Account name is required.");

        using var conn = _db.CreateConnection();

        var exists = await conn.ExecuteScalarAsync<bool>(@"
            SELECT EXISTS(
                SELECT 1 FROM acc_transaction_accounts
                WHERE account_name = @AccountName
                  AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
            );",
            new { model.AccountName, CurrentPharmacyNo, CurrentBranchNo });

        if (exists)
            return FailResponse<AccTransactionAccount>($"Account '{model.AccountName}' already exists in this pharmacy branch.");

        var nextNo = await conn.ExecuteScalarAsync<long>(
            "SELECT COALESCE(MAX(account_no), 0) + 1 FROM acc_transaction_accounts;");

        model.AccountNo = nextNo;
        model.PharmacyNo = CurrentPharmacyNo;
        model.BranchNo = CurrentBranchNo;
        model.IsActive = true;
        model.CreatedAt = DateTime.UtcNow;
        model.UpdatedAt = DateTime.UtcNow;

        await conn.ExecuteAsync(@"
            INSERT INTO acc_transaction_accounts (
                account_no, pharmacy_no, branch_no, account_category, account_code,
                account_name, phone_number, account_type, bank_account_number,
                bank_name, current_balance, is_active, created_at, updated_at
            ) VALUES (
                @AccountNo, @PharmacyNo, @BranchNo, @AccountCategory, @AccountCode,
                @AccountName, @PhoneNumber, @AccountType, @BankAccountNumber,
                @BankName, @CurrentBalance, @IsActive, @CreatedAt, @UpdatedAt
            );", model);

        return OkResponse(model, $"Account '{model.AccountName}' created successfully.");
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<ApiResponse<AccTransactionAccount>>> UpdateAccount(long id, [FromBody] AccTransactionAccount model)
    {
        using var conn = _db.CreateConnection();
        var existing = await conn.QuerySingleOrDefaultAsync<AccTransactionAccount>(@"
            SELECT * FROM acc_transaction_accounts
            WHERE account_no = @Id AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
            new { Id = id, CurrentPharmacyNo, CurrentBranchNo });

        if (existing == null)
            return FailResponse<AccTransactionAccount>("Account not found.", statusCode: 404);

        if (!string.IsNullOrWhiteSpace(model.AccountName) && model.AccountName != existing.AccountName)
        {
            var nameExists = await conn.ExecuteScalarAsync<bool>(@"
                SELECT EXISTS(
                    SELECT 1 FROM acc_transaction_accounts
                    WHERE account_name = @AccountName AND account_no <> @Id
                      AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
                );",
                new { model.AccountName, Id = id, CurrentPharmacyNo, CurrentBranchNo });

            if (nameExists)
                return FailResponse<AccTransactionAccount>($"Account name '{model.AccountName}' is already taken.");
        }

        existing.AccountName = !string.IsNullOrWhiteSpace(model.AccountName) ? model.AccountName : existing.AccountName;
        existing.PhoneNumber = model.PhoneNumber ?? existing.PhoneNumber;
        existing.BankAccountNumber = model.BankAccountNumber ?? existing.BankAccountNumber;
        existing.BankName = model.BankName ?? existing.BankName;
        existing.IsActive = model.IsActive;
        existing.UpdatedAt = DateTime.UtcNow;

        await conn.ExecuteAsync(@"
            UPDATE acc_transaction_accounts
            SET account_name = @AccountName, phone_number = @PhoneNumber,
                bank_account_number = @BankAccountNumber, bank_name = @BankName,
                is_active = @IsActive, updated_at = @UpdatedAt
            WHERE account_no = @AccountNo AND pharmacy_no = @PharmacyNo AND branch_no = @BranchNo;",
            existing);

        return OkResponse(existing, $"Account '{existing.AccountName}' updated successfully.");
    }

    [HttpGet("{id}/statement")]
    [HttpGet("{id}/ledger")]
    public async Task<ActionResult<ApiResponse<object>>> GetAccountStatement(long id)
    {
        using var conn = _db.CreateConnection();
        var account = await conn.QuerySingleOrDefaultAsync<AccTransactionAccount>(@"
            SELECT * FROM acc_transaction_accounts
            WHERE account_no = @id AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
            new { id, CurrentPharmacyNo, CurrentBranchNo });

        if (account == null)
            return FailResponse<object>("Account not found.", statusCode: 404);

        var transactions = (await conn.QueryAsync(@"
            SELECT ledger_no, journal_no, transaction_date, transaction_type,
                   debit_amount, credit_amount, balance_after,
                   source_document_type, source_document_no, narration, created_at
            FROM acc_transaction_ledger
            WHERE account_no = @id AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
            ORDER BY transaction_date DESC, ledger_no DESC
            LIMIT 200;",
            new { id, CurrentPharmacyNo, CurrentBranchNo })).ToList();

        return OkResponse<object>(new
        {
            account,
            transactions
        });
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteAccount(long id)
    {
        using var conn = _db.CreateConnection();
        var account = await conn.QuerySingleOrDefaultAsync<AccTransactionAccount>(@"
            SELECT * FROM acc_transaction_accounts
            WHERE account_no = @id AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
            new { id, CurrentPharmacyNo, CurrentBranchNo });

        if (account == null)
            return FailResponse<bool>("Account not found.", statusCode: 404);

        if (!string.IsNullOrWhiteSpace(account.AccountCode) && account.AccountCode.Length == 5 &&
            (account.AccountCode.StartsWith("1") || account.AccountCode.StartsWith("2") ||
             account.AccountCode.StartsWith("3") || account.AccountCode.StartsWith("4") ||
             account.AccountCode.StartsWith("5")))
        {
            return FailResponse<bool>("Default system chart of accounts cannot be deleted.");
        }

        if (account.CurrentBalance != 0)
        {
            return FailResponse<bool>($"Cannot delete account '{account.AccountName}' because it has an active balance of ৳ {account.CurrentBalance:N2}. Balance must be 0.00.");
        }

        var hasTransactions = await conn.ExecuteScalarAsync<bool>(@"
            SELECT (
                EXISTS(SELECT 1 FROM acc_transaction_ledger WHERE account_no = @id AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo)
                OR EXISTS(SELECT 1 FROM acc_expenses WHERE account_no = @id AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo)
                OR EXISTS(SELECT 1 FROM acc_incomes WHERE account_no = @id AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo)
                OR EXISTS(SELECT 1 FROM acc_additional_balances WHERE account_no = @id AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo)
                OR EXISTS(SELECT 1 FROM due_collections WHERE account_no = @id AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo)
                OR EXISTS(SELECT 1 FROM sale_invoice_payments WHERE account_no = @id AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo)
                OR EXISTS(SELECT 1 FROM pur_supplier_payments WHERE account_no = @id AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo)
                OR EXISTS(SELECT 1 FROM acc_reconciliations WHERE (payment_from_account_no = @id OR payment_to_account_no = @id) AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo)
            );", new { id, CurrentPharmacyNo, CurrentBranchNo });

        if (hasTransactions)
        {
            return FailResponse<bool>($"Cannot delete account '{account.AccountName}' because transactions have already been recorded against it. Financial records must be preserved.");
        }

        var rows = await conn.ExecuteAsync(@"
            DELETE FROM acc_transaction_accounts
            WHERE account_no = @id AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
            new { id, CurrentPharmacyNo, CurrentBranchNo });

        if (rows == 0)
            return FailResponse<bool>("Account not found.", statusCode: 404);

        return OkResponse(true, $"Account '{account.AccountName}' deleted successfully.");
    }
}

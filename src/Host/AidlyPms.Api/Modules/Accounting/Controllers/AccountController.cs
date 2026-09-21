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
            SELECT * FROM acc_transaction_accounts
            WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo";

        var pms = new DynamicParameters();
        pms.Add("CurrentPharmacyNo", CurrentPharmacyNo);
        pms.Add("CurrentBranchNo", CurrentBranchNo);

        if (accountCategory.HasValue)
        {
            sql += " AND account_category = @AccountCategory";
            pms.Add("AccountCategory", accountCategory.Value);
        }

        if (accountType.HasValue)
        {
            sql += " AND account_type = @AccountType";
            pms.Add("AccountType", accountType.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            sql += " AND (account_name ILIKE @Search OR account_code ILIKE @Search OR bank_account_number ILIKE @Search)";
            pms.Add("Search", $"%{search.Trim()}%");
        }

        sql += " ORDER BY account_category ASC, account_code ASC, account_name ASC;";
        var list = (await conn.QueryAsync<AccTransactionAccount>(sql, pms)).ToList();

        return OkResponse(list);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<AccTransactionAccount>>> GetAccountById(long id)
    {
        using var conn = _db.CreateConnection();
        var account = await conn.QuerySingleOrDefaultAsync<AccTransactionAccount>(@"
            SELECT * FROM acc_transaction_accounts
            WHERE account_no = @Id AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
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
}

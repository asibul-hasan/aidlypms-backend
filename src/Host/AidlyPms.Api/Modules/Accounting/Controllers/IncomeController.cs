using AidlyPms.Api.Common;
using AidlyPms.Api.Common.Database;
using AidlyPms.Api.Infrastructure;
using AidlyPms.Api.Modules.Accounting.Models;
using Dapper;
using Microsoft.AspNetCore.Mvc;

namespace AidlyPms.Api.Modules.Accounting.Controllers;

[Route("api/v1/acc")]
public class IncomeController : BaseController
{
    private readonly IDbConnectionFactory _db;
    private readonly IDocumentNumberService _docNumService;
    private readonly ILedgerPostingService _ledgerPosting;

    public IncomeController(
        IDbConnectionFactory db,
        IDocumentNumberService docNumService,
        ILedgerPostingService ledgerPosting)
    {
        _db = db;
        _docNumService = docNumService;
        _ledgerPosting = ledgerPosting;
    }

    [HttpGet("income-categories")]
    public async Task<ActionResult<ApiResponse<List<AccIncomeCategory>>>> GetCategories()
    {
        using var conn = _db.CreateConnection();
        var sql = @"
            SELECT * FROM acc_income_categories
            WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
            ORDER BY name ASC;";

        var list = (await conn.QueryAsync<AccIncomeCategory>(sql, new { CurrentPharmacyNo, CurrentBranchNo })).ToList();
        return OkResponse(list);
    }

    [HttpPost("income-categories")]
    public async Task<ActionResult<ApiResponse<AccIncomeCategory>>> CreateCategory([FromBody] AccIncomeCategory model)
    {
        if (string.IsNullOrWhiteSpace(model.Name))
            return FailResponse<AccIncomeCategory>("Category name is required.");

        using var conn = _db.CreateConnection();
        var exists = await conn.ExecuteScalarAsync<bool>(@"
            SELECT EXISTS(
                SELECT 1 FROM acc_income_categories
                WHERE name = @Name AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
            );",
            new { model.Name, CurrentPharmacyNo, CurrentBranchNo });

        if (exists)
            return FailResponse<AccIncomeCategory>($"Category '{model.Name}' already exists.");

        var nextNo = await conn.ExecuteScalarAsync<long>(
            "SELECT COALESCE(MAX(income_category_no), 0) + 1 FROM acc_income_categories;");

        model.IncomeCategoryNo = nextNo;
        model.PharmacyNo = CurrentPharmacyNo;
        model.BranchNo = CurrentBranchNo;
        model.IsActive = true;
        model.CreatedAt = DateTime.UtcNow;

        await conn.ExecuteAsync(@"
            INSERT INTO acc_income_categories (
                income_category_no, pharmacy_no, branch_no, name, is_active, created_at
            ) VALUES (
                @IncomeCategoryNo, @PharmacyNo, @BranchNo, @Name, @IsActive, @CreatedAt
            );", model);

        return OkResponse(model, $"Category '{model.Name}' created successfully.");
    }

    [HttpGet("incomes")]
    [HttpGet("income")]
    public async Task<ActionResult<ApiResponse<PagedResult<AccIncome>>>> GetIncomes(
        [FromQuery] QueryFilter filter,
        [FromQuery] long? categoryNo,
        [FromQuery] long? accountNo,
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        [FromQuery] string? search)
    {
        using var conn = _db.CreateConnection();
        var sql = @"
            SELECT i.*, c.name AS category_name, a.account_name
            FROM acc_incomes i
            JOIN acc_income_categories c ON i.income_category_no = c.income_category_no
            LEFT JOIN acc_transaction_accounts a ON i.account_no = a.account_no
            WHERE i.pharmacy_no = @CurrentPharmacyNo AND i.branch_no = @CurrentBranchNo";

        var countSql = @"
            SELECT COUNT(1)
            FROM acc_incomes i
            JOIN acc_income_categories c ON i.income_category_no = c.income_category_no
            WHERE i.pharmacy_no = @CurrentPharmacyNo AND i.branch_no = @CurrentBranchNo";

        var pms = new DynamicParameters();
        pms.Add("CurrentPharmacyNo", CurrentPharmacyNo);
        pms.Add("CurrentBranchNo", CurrentBranchNo);

        if (categoryNo.HasValue)
        {
            sql += " AND i.income_category_no = @CategoryNo";
            countSql += " AND i.income_category_no = @CategoryNo";
            pms.Add("CategoryNo", categoryNo.Value);
        }

        if (accountNo.HasValue)
        {
            sql += " AND i.account_no = @AccountNo";
            countSql += " AND i.account_no = @AccountNo";
            pms.Add("AccountNo", accountNo.Value);
        }

        if (fromDate.HasValue)
        {
            sql += " AND i.income_date >= @FromDate";
            countSql += " AND i.income_date >= @FromDate";
            pms.Add("FromDate", fromDate.Value.ToUniversalTime());
        }

        if (toDate.HasValue)
        {
            sql += " AND i.income_date <= @ToDate";
            countSql += " AND i.income_date <= @ToDate";
            pms.Add("ToDate", toDate.Value.ToUniversalTime());
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            sql += " AND (i.receipt_no ILIKE @Search OR i.note ILIKE @Search OR i.received_from ILIKE @Search)";
            countSql += " AND (i.receipt_no ILIKE @Search OR i.note ILIKE @Search OR i.received_from ILIKE @Search)";
            pms.Add("Search", $"%{search.Trim()}%");
        }

        sql += " ORDER BY i.income_no DESC LIMIT @PageSize OFFSET @Offset;";
        pms.Add("PageSize", filter.PageSize);
        pms.Add("Offset", (filter.PageIndex - 1) * filter.PageSize);

        var total = await conn.ExecuteScalarAsync<int>(countSql, pms);
        var items = (await conn.QueryAsync<AccIncome>(sql, pms)).ToList();

        var result = new PagedResult<AccIncome>(items, total, filter.PageIndex, filter.PageSize);
        return PagedResponse(result);
    }

    [HttpPost("incomes")]
    [HttpPost("income")]
    public async Task<ActionResult<ApiResponse<AccIncome>>> CreateIncome([FromBody] AccIncome model)
    {
        if (model.IncomeCategoryNo <= 0)
            return FailResponse<AccIncome>("A valid income category is required.");

        if (model.Amount <= 0)
            return FailResponse<AccIncome>("Income amount must be greater than zero.");

        using var conn = _db.CreateConnection();
        conn.Open();
        using var tran = conn.BeginTransaction();

        try
        {
            var accountNo = model.AccountNo > 0 ? model.AccountNo : 1;
            var receiptNo = await _docNumService.GetNextDocumentNumberAsync(conn, tran, CurrentPharmacyNo, CurrentBranchNo, "INCOME");

            var nextNo = await conn.ExecuteScalarAsync<long>(
                "SELECT COALESCE(MAX(income_no), 0) + 1 FROM acc_incomes;", transaction: tran);

            model.IncomeNo = nextNo;
            model.PharmacyNo = CurrentPharmacyNo;
            model.BranchNo = CurrentBranchNo;
            model.ReceiptNo = receiptNo;
            model.AccountNo = accountNo;
            model.CreatedByUserNo = CurrentUserNo;
            model.IncomeDate = model.IncomeDate == default ? DateTime.UtcNow : model.IncomeDate;
            model.CreatedAt = DateTime.UtcNow;

            await conn.ExecuteAsync(@"
                INSERT INTO acc_incomes (
                    income_no, pharmacy_no, branch_no, receipt_no, income_category_no,
                    account_no, amount, payment_type, received_from, note,
                    created_by_user_no, income_date, created_at
                ) VALUES (
                    @IncomeNo, @PharmacyNo, @BranchNo, @ReceiptNo, @IncomeCategoryNo,
                    @AccountNo, @Amount, @PaymentType, @ReceivedFrom, @Note,
                    @CreatedByUserNo, @IncomeDate, @CreatedAt
                );", model, transaction: tran);

            // Synchronous Universal Financial Ledger Posting:
            // Dr Cash / Bank Account (Funds received)
            // Cr 40201 Miscellaneous Revenue
            var revenueAcc = await conn.QuerySingleOrDefaultAsync<long?>(
                "SELECT account_no FROM acc_transaction_accounts WHERE account_code = '40101' AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
                new { CurrentPharmacyNo, CurrentBranchNo }, transaction: tran) ?? 1;

            var journalLegs = new List<LedgerLeg>
            {
                new LedgerLeg
                {
                    AccountNo = accountNo,
                    DebitAmount = model.Amount,
                    CreditAmount = 0,
                    Narration = $"Receipt for income {receiptNo}"
                },
                new LedgerLeg
                {
                    AccountNo = revenueAcc,
                    DebitAmount = 0,
                    CreditAmount = model.Amount,
                    Narration = $"Miscellaneous income {receiptNo}: {model.Note}"
                }
            };

            await _ledgerPosting.PostJournalAsync(conn, tran, new CompoundJournalEntry
            {
                PharmacyNo = CurrentPharmacyNo,
                BranchNo = CurrentBranchNo,
                TransactionType = 6, // Income
                SourceDocumentType = "INCOME",
                SourceDocumentNo = receiptNo,
                SourceDocumentId = nextNo,
                CreatedByUserNo = CurrentUserNo,
                Legs = journalLegs
            });

            tran.Commit();

            return OkResponse(model, $"Income receipt {receiptNo} posted successfully.");
        }
        catch (Exception ex)
        {
            tran.Rollback();
            return FailResponse<AccIncome>($"Posting failed: {ex.Message}");
        }
    }

    [HttpDelete("incomes/{incomeNo}")]
    [HttpDelete("income/{incomeNo}")]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteIncome(long incomeNo)
    {
        using var conn = _db.CreateConnection();
        var rows = await conn.ExecuteAsync(@"
            DELETE FROM acc_incomes
            WHERE income_no = @incomeNo AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
            new { incomeNo, CurrentPharmacyNo, CurrentBranchNo });

        if (rows == 0) return NotFoundResponse<bool>("Income record not found.");
        return OkResponse(true, "Income record deleted successfully.");
    }
}

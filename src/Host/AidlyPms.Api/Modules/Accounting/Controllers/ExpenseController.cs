using AidlyPms.Api.Common;
using AidlyPms.Api.Common.Database;
using AidlyPms.Api.Infrastructure;
using AidlyPms.Api.Modules.Accounting.Models;
using Dapper;
using Microsoft.AspNetCore.Mvc;

namespace AidlyPms.Api.Modules.Accounting.Controllers;

[Route("api/v1/acc")]
public class ExpenseController : BaseController
{
    private readonly IDbConnectionFactory _db;
    private readonly IDocumentNumberService _docNumService;
    private readonly ILedgerPostingService _ledgerPosting;

    public ExpenseController(
        IDbConnectionFactory db,
        IDocumentNumberService docNumService,
        ILedgerPostingService ledgerPosting)
    {
        _db = db;
        _docNumService = docNumService;
        _ledgerPosting = ledgerPosting;
    }

    [HttpGet("expense-categories")]
    public async Task<ActionResult<ApiResponse<List<AccExpenseCategory>>>> GetCategories()
    {
        using var conn = _db.CreateConnection();
        var sql = @"
            SELECT * FROM acc_expense_categories
            WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
            ORDER BY name ASC;";

        var list = (await conn.QueryAsync<AccExpenseCategory>(sql, new { CurrentPharmacyNo, CurrentBranchNo })).ToList();
        return OkResponse(list);
    }

    [HttpPost("expense-categories")]
    public async Task<ActionResult<ApiResponse<AccExpenseCategory>>> CreateCategory([FromBody] AccExpenseCategory model)
    {
        if (string.IsNullOrWhiteSpace(model.Name))
            return FailResponse<AccExpenseCategory>("Category name is required.");

        using var conn = _db.CreateConnection();
        var exists = await conn.ExecuteScalarAsync<bool>(@"
            SELECT EXISTS(
                SELECT 1 FROM acc_expense_categories
                WHERE name = @Name AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
            );",
            new { model.Name, CurrentPharmacyNo, CurrentBranchNo });

        if (exists)
            return FailResponse<AccExpenseCategory>($"Category '{model.Name}' already exists.");

        var nextNo = await conn.ExecuteScalarAsync<long>(
            "SELECT COALESCE(MAX(expense_category_no), 0) + 1 FROM acc_expense_categories;");

        model.ExpenseCategoryNo = nextNo;
        model.PharmacyNo = CurrentPharmacyNo;
        model.BranchNo = CurrentBranchNo;
        model.IsActive = true;
        model.CreatedAt = DateTime.UtcNow;

        await conn.ExecuteAsync(@"
            INSERT INTO acc_expense_categories (
                expense_category_no, pharmacy_no, branch_no, name, is_active, created_at
            ) VALUES (
                @ExpenseCategoryNo, @PharmacyNo, @BranchNo, @Name, @IsActive, @CreatedAt
            );", model);

        return OkResponse(model, $"Category '{model.Name}' created successfully.");
    }

    [HttpGet("expenses")]
    public async Task<ActionResult<ApiResponse<PagedResult<AccExpense>>>> GetExpenses(
        [FromQuery] QueryFilter filter,
        [FromQuery] long? categoryNo,
        [FromQuery] long? accountNo,
        [FromQuery] string? paymentType,
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        [FromQuery] string? search)
    {
        using var conn = _db.CreateConnection();
        var sql = @"
            SELECT e.*, c.name AS category_name, a.account_name
            FROM acc_expenses e
            JOIN acc_expense_categories c ON e.expense_category_no = c.expense_category_no
            LEFT JOIN acc_transaction_accounts a ON e.account_no = a.account_no
            WHERE e.pharmacy_no = @CurrentPharmacyNo AND e.branch_no = @CurrentBranchNo";

        var countSql = @"
            SELECT COUNT(1)
            FROM acc_expenses e
            JOIN acc_expense_categories c ON e.expense_category_no = c.expense_category_no
            WHERE e.pharmacy_no = @CurrentPharmacyNo AND e.branch_no = @CurrentBranchNo";

        var pms = new DynamicParameters();
        pms.Add("CurrentPharmacyNo", CurrentPharmacyNo);
        pms.Add("CurrentBranchNo", CurrentBranchNo);

        if (categoryNo.HasValue)
        {
            sql += " AND e.expense_category_no = @CategoryNo";
            countSql += " AND e.expense_category_no = @CategoryNo";
            pms.Add("CategoryNo", categoryNo.Value);
        }

        if (accountNo.HasValue)
        {
            sql += " AND e.account_no = @AccountNo";
            countSql += " AND e.account_no = @AccountNo";
            pms.Add("AccountNo", accountNo.Value);
        }

        if (!string.IsNullOrWhiteSpace(paymentType))
        {
            sql += " AND e.payment_type ILIKE @PaymentType";
            countSql += " AND e.payment_type ILIKE @PaymentType";
            pms.Add("PaymentType", paymentType.Trim());
        }

        if (fromDate.HasValue)
        {
            sql += " AND e.expense_date >= @FromDate";
            countSql += " AND e.expense_date >= @FromDate";
            pms.Add("FromDate", fromDate.Value.ToUniversalTime());
        }

        if (toDate.HasValue)
        {
            sql += " AND e.expense_date <= @ToDate";
            countSql += " AND e.expense_date <= @ToDate";
            pms.Add("ToDate", toDate.Value.ToUniversalTime());
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            sql += " AND (e.voucher_no ILIKE @Search OR e.note ILIKE @Search OR c.name ILIKE @Search)";
            countSql += " AND (e.voucher_no ILIKE @Search OR e.note ILIKE @Search OR c.name ILIKE @Search)";
            pms.Add("Search", $"%{search.Trim()}%");
        }

        sql += " ORDER BY e.expense_no DESC LIMIT @PageSize OFFSET @Offset;";
        pms.Add("PageSize", filter.PageSize);
        pms.Add("Offset", (filter.PageIndex - 1) * filter.PageSize);

        var total = await conn.ExecuteScalarAsync<int>(countSql, pms);
        var items = (await conn.QueryAsync<AccExpense>(sql, pms)).ToList();

        var result = new PagedResult<AccExpense>(items, total, filter.PageIndex, filter.PageSize);
        return PagedResponse(result);
    }

    [HttpPost("expenses")]
    public async Task<ActionResult<ApiResponse<AccExpense>>> CreateExpense([FromBody] AccExpense model)
    {
        if (model.ExpenseCategoryNo <= 0)
            return FailResponse<AccExpense>("A valid expense category is required.");

        if (model.Amount <= 0)
            return FailResponse<AccExpense>("Expense amount must be greater than zero.");

        using var conn = _db.CreateConnection();
        conn.Open();
        using var tran = conn.BeginTransaction();

        try
        {
            var accountNo = model.AccountNo > 0 ? model.AccountNo : 1;
            var voucherNo = await _docNumService.GetNextDocumentNumberAsync(conn, tran, CurrentPharmacyNo, CurrentBranchNo, "EXPENSE");

            var nextNo = await conn.ExecuteScalarAsync<long>(
                "SELECT COALESCE(MAX(expense_no), 0) + 1 FROM acc_expenses;", transaction: tran);

            model.ExpenseNo = nextNo;
            model.PharmacyNo = CurrentPharmacyNo;
            model.BranchNo = CurrentBranchNo;
            model.VoucherNo = voucherNo;
            model.AccountNo = accountNo;
            model.CreatedByUserNo = CurrentUserNo;
            model.ExpenseDate = model.ExpenseDate == default ? DateTime.UtcNow : model.ExpenseDate;
            model.CreatedAt = DateTime.UtcNow;

            await conn.ExecuteAsync(@"
                INSERT INTO acc_expenses (
                    expense_no, pharmacy_no, branch_no, voucher_no, expense_category_no,
                    account_no, amount, payment_type, note, created_by_user_no,
                    expense_date, created_at
                ) VALUES (
                    @ExpenseNo, @PharmacyNo, @BranchNo, @VoucherNo, @ExpenseCategoryNo,
                    @AccountNo, @Amount, @PaymentType, @Note, @CreatedByUserNo,
                    @ExpenseDate, @CreatedAt
                );", model, transaction: tran);

            // Synchronous Universal Financial Ledger Posting:
            // Dr 50201 Operating Expenses
            // Cr Cash / Bank Account
            var expenseAcc = await conn.QuerySingleOrDefaultAsync<long?>(
                "SELECT account_no FROM acc_transaction_accounts WHERE account_code = '50201' AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
                new { CurrentPharmacyNo, CurrentBranchNo }, transaction: tran) ?? 1;

            var journalLegs = new List<LedgerLeg>
            {
                new LedgerLeg
                {
                    AccountNo = expenseAcc,
                    DebitAmount = model.Amount,
                    CreditAmount = 0,
                    Narration = $"Operating expense {voucherNo}: {model.Note}"
                },
                new LedgerLeg
                {
                    AccountNo = accountNo,
                    DebitAmount = 0,
                    CreditAmount = model.Amount,
                    Narration = $"Disbursement for expense {voucherNo}"
                }
            };

            await _ledgerPosting.PostJournalAsync(conn, tran, new CompoundJournalEntry
            {
                PharmacyNo = CurrentPharmacyNo,
                BranchNo = CurrentBranchNo,
                TransactionType = 5, // Expense
                SourceDocumentType = "EXPENSE",
                SourceDocumentNo = voucherNo,
                SourceDocumentId = nextNo,
                CreatedByUserNo = CurrentUserNo,
                Legs = journalLegs
            });

            tran.Commit();

            return OkResponse(model, $"Expense voucher {voucherNo} posted successfully.");
        }
        catch (Exception ex)
        {
            tran.Rollback();
            return FailResponse<AccExpense>($"Posting failed: {ex.Message}");
        }
    }
}

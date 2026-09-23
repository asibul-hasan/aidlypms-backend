using AidlyPms.Api.Common;
using AidlyPms.Api.Common.Database;
using AidlyPms.Api.Infrastructure;
using AidlyPms.Api.Modules.Accounting.Models;
using Dapper;
using Microsoft.AspNetCore.Mvc;

namespace AidlyPms.Api.Modules.Accounting.Controllers;

[Route("api/v1/acc/additional-balances")]
[Route("api/v1/acc/additional-balance")]
public class AdditionalBalanceController : BaseController
{
    private readonly IDbConnectionFactory _db;
    private readonly ILedgerPostingService _ledgerPosting;

    public AdditionalBalanceController(
        IDbConnectionFactory db,
        ILedgerPostingService ledgerPosting)
    {
        _db = db;
        _ledgerPosting = ledgerPosting;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<AccAdditionalBalance>>>> GetAdditionalBalances(
        [FromQuery] QueryFilter filter)
    {
        using var conn = _db.CreateConnection();
        var sql = @"
            SELECT b.*, a.account_name
            FROM acc_additional_balances b
            LEFT JOIN acc_transaction_accounts a ON b.account_no = a.account_no
            WHERE b.pharmacy_no = @CurrentPharmacyNo AND b.branch_no = @CurrentBranchNo
            ORDER BY b.additional_balance_no DESC
            LIMIT @PageSize OFFSET @Offset;";

        var countSql = @"
            SELECT COUNT(1) FROM acc_additional_balances
            WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;";

        var pms = new DynamicParameters();
        pms.Add("CurrentPharmacyNo", CurrentPharmacyNo);
        pms.Add("CurrentBranchNo", CurrentBranchNo);
        pms.Add("PageSize", filter.PageSize);
        pms.Add("Offset", (filter.PageIndex - 1) * filter.PageSize);

        var total = await conn.ExecuteScalarAsync<int>(countSql, pms);
        var items = (await conn.QueryAsync<AccAdditionalBalance>(sql, pms)).ToList();

        var result = new PagedResult<AccAdditionalBalance>(items, total, filter.PageIndex, filter.PageSize);
        return PagedResponse(result);
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<AccAdditionalBalance>>> AddCapitalBalance([FromBody] AccAdditionalBalance model)
    {
        if (model.Amount <= 0)
            return FailResponse<AccAdditionalBalance>("Capital injection amount must be greater than zero.");

        using var conn = _db.CreateConnection();
        conn.Open();
        using var tran = conn.BeginTransaction();

        try
        {
            var accountNo = model.AccountNo.HasValue && model.AccountNo.Value > 0 ? model.AccountNo.Value : 1;

            var nextNo = await conn.ExecuteScalarAsync<long>(
                "SELECT COALESCE(MAX(additional_balance_no), 0) + 1 FROM acc_additional_balances;", transaction: tran);

            model.AdditionalBalanceNo = nextNo;
            model.PharmacyNo = CurrentPharmacyNo;
            model.BranchNo = CurrentBranchNo;
            model.AccountNo = accountNo;
            model.CreatedByUserNo = CurrentUserNo;
            model.EntryDate = model.EntryDate == default ? DateTime.UtcNow.Date : model.EntryDate;
            model.CreatedAt = DateTime.UtcNow;

            await conn.ExecuteAsync(@"
                INSERT INTO acc_additional_balances (
                    additional_balance_no, pharmacy_no, branch_no, account_no,
                    amount, entry_date, note, created_by_user_no, created_at
                ) VALUES (
                    @AdditionalBalanceNo, @PharmacyNo, @BranchNo, @AccountNo,
                    @Amount, @EntryDate, @Note, @CreatedByUserNo, @CreatedAt
                );", model, transaction: tran);

            // Synchronous Universal Financial Ledger Posting:
            // Dr Cash / Bank Account (Funds invested)
            // Cr 30101 Owner Equity / Capital
            var equityAcc = await conn.QuerySingleOrDefaultAsync<long?>(
                "SELECT account_no FROM acc_transaction_accounts WHERE account_code = '30101' AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
                new { CurrentPharmacyNo, CurrentBranchNo }, transaction: tran) ?? 1;

            var journalLegs = new List<LedgerLeg>
            {
                new LedgerLeg
                {
                    AccountNo = accountNo,
                    DebitAmount = model.Amount,
                    CreditAmount = 0,
                    Narration = $"Capital deposit: {model.Note}"
                },
                new LedgerLeg
                {
                    AccountNo = equityAcc,
                    DebitAmount = 0,
                    CreditAmount = model.Amount,
                    Narration = $"Owner capital infusion: {model.Note}"
                }
            };

            await _ledgerPosting.PostJournalAsync(conn, tran, new CompoundJournalEntry
            {
                PharmacyNo = CurrentPharmacyNo,
                BranchNo = CurrentBranchNo,
                TransactionType = 10, // Capital Infusion
                SourceDocumentType = "CAPITAL_BALANCE",
                SourceDocumentNo = $"CAP-{nextNo:D6}",
                SourceDocumentId = nextNo,
                CreatedByUserNo = CurrentUserNo,
                Legs = journalLegs
            });

            tran.Commit();

            return OkResponse(model, $"Capital balance of ৳{model.Amount} recorded successfully.");
        }
        catch (Exception ex)
        {
            tran.Rollback();
            return FailResponse<AccAdditionalBalance>($"Capital injection failed: {ex.Message}");
        }
    }

    [HttpDelete("{additionalBalanceNo}")]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteCapitalBalance(long additionalBalanceNo)
    {
        using var conn = _db.CreateConnection();
        var rows = await conn.ExecuteAsync(@"
            DELETE FROM acc_additional_balances
            WHERE additional_balance_no = @additionalBalanceNo AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
            new { additionalBalanceNo, CurrentPharmacyNo, CurrentBranchNo });

        if (rows == 0) return NotFoundResponse<bool>("Capital balance record not found.");
        return OkResponse(true, "Capital balance record deleted successfully.");
    }
}

using AidlyPms.Api.Common;
using AidlyPms.Api.Common.Database;
using AidlyPms.Api.Infrastructure;
using AidlyPms.Api.Modules.Accounting.Models;
using Dapper;
using Microsoft.AspNetCore.Mvc;

namespace AidlyPms.Api.Modules.Accounting.Controllers;

[Route("api/v1")]
public class ReconciliationController : BaseController
{
    private readonly IDbConnectionFactory _db;
    private readonly IDocumentNumberService _docNumService;
    private readonly ILedgerPostingService _ledgerPosting;

    public ReconciliationController(
        IDbConnectionFactory db,
        IDocumentNumberService docNumService,
        ILedgerPostingService ledgerPosting)
    {
        _db = db;
        _docNumService = docNumService;
        _ledgerPosting = ledgerPosting;
    }

    [HttpGet("acc/reconciliations")]
    [HttpGet("cash/reconciliations")]
    public async Task<ActionResult<ApiResponse<PagedResult<AccReconciliation>>>> GetReconciliations(
        [FromQuery] QueryFilter filter,
        [FromQuery] long? fromAccountNo,
        [FromQuery] long? toAccountNo)
    {
        using var conn = _db.CreateConnection();
        var sql = @"
            SELECT r.*, f.account_name AS from_account_name, t.account_name AS to_account_name
            FROM acc_reconciliations r
            JOIN acc_transaction_accounts f ON r.payment_from_account_no = f.account_no
            JOIN acc_transaction_accounts t ON r.payment_to_account_no = t.account_no
            WHERE r.pharmacy_no = @CurrentPharmacyNo AND r.branch_no = @CurrentBranchNo";

        var countSql = @"
            SELECT COUNT(1)
            FROM acc_reconciliations r
            WHERE r.pharmacy_no = @CurrentPharmacyNo AND r.branch_no = @CurrentBranchNo";

        var pms = new DynamicParameters();
        pms.Add("CurrentPharmacyNo", CurrentPharmacyNo);
        pms.Add("CurrentBranchNo", CurrentBranchNo);

        if (fromAccountNo.HasValue)
        {
            sql += " AND r.payment_from_account_no = @FromAccountNo";
            countSql += " AND r.payment_from_account_no = @FromAccountNo";
            pms.Add("FromAccountNo", fromAccountNo.Value);
        }

        if (toAccountNo.HasValue)
        {
            sql += " AND r.payment_to_account_no = @ToAccountNo";
            countSql += " AND r.payment_to_account_no = @ToAccountNo";
            pms.Add("ToAccountNo", toAccountNo.Value);
        }

        sql += " ORDER BY r.reconciliation_no DESC LIMIT @PageSize OFFSET @Offset;";
        pms.Add("PageSize", filter.PageSize);
        pms.Add("Offset", (filter.PageIndex - 1) * filter.PageSize);

        var total = await conn.ExecuteScalarAsync<int>(countSql, pms);
        var items = (await conn.QueryAsync<AccReconciliation>(sql, pms)).ToList();

        var result = new PagedResult<AccReconciliation>(items, total, filter.PageIndex, filter.PageSize);
        return PagedResponse(result);
    }

    [HttpPost("acc/reconciliations")]
    [HttpPost("cash/reconciliations")]
    public async Task<ActionResult<ApiResponse<AccReconciliation>>> TransferFunds([FromBody] AccReconciliation model)
    {
        if (model.PaymentFromAccountNo <= 0 || model.PaymentToAccountNo <= 0)
            return FailResponse<AccReconciliation>("Both source and destination accounts are required.");

        if (model.PaymentFromAccountNo == model.PaymentToAccountNo)
            return FailResponse<AccReconciliation>("Source and destination accounts must be different.");

        if (model.Amount <= 0)
            return FailResponse<AccReconciliation>("Transfer amount must be greater than zero.");

        using var conn = _db.CreateConnection();
        conn.Open();
        using var tran = conn.BeginTransaction();

        try
        {
            var voucherNo = await _docNumService.GetNextDocumentNumberAsync(conn, tran, CurrentPharmacyNo, CurrentBranchNo, "TRANSFER");

            var nextNo = await conn.ExecuteScalarAsync<long>(
                "SELECT COALESCE(MAX(reconciliation_no), 0) + 1 FROM acc_reconciliations;", transaction: tran);

            model.ReconciliationNo = nextNo;
            model.PharmacyNo = CurrentPharmacyNo;
            model.BranchNo = CurrentBranchNo;
            model.VoucherNo = voucherNo;
            model.CreatedByUserNo = CurrentUserNo;
            model.TransferDate = model.TransferDate == default ? DateTime.UtcNow : model.TransferDate;
            model.CreatedAt = DateTime.UtcNow;

            await conn.ExecuteAsync(@"
                INSERT INTO acc_reconciliations (
                    reconciliation_no, pharmacy_no, branch_no, voucher_no,
                    payment_from_account_no, payment_to_account_no, amount,
                    note, created_by_user_no, transfer_date, created_at
                ) VALUES (
                    @ReconciliationNo, @PharmacyNo, @BranchNo, @VoucherNo,
                    @PaymentFromAccountNo, @PaymentToAccountNo, @Amount,
                    @Note, @CreatedByUserNo, @TransferDate, @CreatedAt
                );", model, transaction: tran);

            // Synchronous Universal Financial Ledger Posting:
            // Dr Destination Account (Receiving funds)
            // Cr Source Account (Disbursing funds)
            var journalLegs = new List<LedgerLeg>
            {
                new LedgerLeg
                {
                    AccountNo = model.PaymentToAccountNo,
                    DebitAmount = model.Amount,
                    CreditAmount = 0,
                    Narration = $"Fund transfer receipt {voucherNo}"
                },
                new LedgerLeg
                {
                    AccountNo = model.PaymentFromAccountNo,
                    DebitAmount = 0,
                    CreditAmount = model.Amount,
                    Narration = $"Fund transfer remittance {voucherNo}"
                }
            };

            await _ledgerPosting.PostJournalAsync(conn, tran, new CompoundJournalEntry
            {
                PharmacyNo = CurrentPharmacyNo,
                BranchNo = CurrentBranchNo,
                TransactionType = 9, // Inter-Account Transfer
                SourceDocumentType = "RECONCILIATION",
                SourceDocumentNo = voucherNo,
                SourceDocumentId = nextNo,
                CreatedByUserNo = CurrentUserNo,
                Legs = journalLegs
            });

            tran.Commit();

            return OkResponse(model, $"Fund transfer of ৳{model.Amount} completed successfully ({voucherNo}).");
        }
        catch (Exception ex)
        {
            tran.Rollback();
            return FailResponse<AccReconciliation>($"Fund transfer failed: {ex.Message}");
        }
    }

    [HttpDelete("acc/reconciliations/{reconciliationNo}")]
    [HttpDelete("cash/reconciliations/{reconciliationNo}")]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteReconciliation(long reconciliationNo)
    {
        using var conn = _db.CreateConnection();
        var rows = await conn.ExecuteAsync(@"
            DELETE FROM acc_reconciliations
            WHERE reconciliation_no = @reconciliationNo AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
            new { reconciliationNo, CurrentPharmacyNo, CurrentBranchNo });

        if (rows == 0) return NotFoundResponse<bool>("Reconciliation record not found.");
        return OkResponse(true, "Reconciliation voucher deleted successfully.");
    }
}

using AidlyPms.Api.Common;
using AidlyPms.Api.Common.Database;
using AidlyPms.Api.Infrastructure;
using AidlyPms.Api.Modules.Suppliers.Models;
using Dapper;
using Microsoft.AspNetCore.Mvc;

namespace AidlyPms.Api.Modules.Suppliers.Controllers;

[Route("api/v1/pur")]
public class SupplierPaymentController : BaseController
{
    private readonly IDbConnectionFactory _db;
    private readonly IDocumentNumberService _docNumService;
    private readonly ILedgerPostingService _ledgerPosting;

    public SupplierPaymentController(
        IDbConnectionFactory db,
        IDocumentNumberService docNumService,
        ILedgerPostingService ledgerPosting)
    {
        _db = db;
        _docNumService = docNumService;
        _ledgerPosting = ledgerPosting;
    }

    [HttpGet("supplier-payments")]
    [HttpGet("payments")]
    public async Task<ActionResult<ApiResponse<PagedResult<PurSupplierPayment>>>> GetPayments(
        [FromQuery] QueryFilter filter,
        [FromQuery] long? supplierNo,
        [FromQuery] string? search)
    {
        using var conn = _db.CreateConnection();
        var sql = @"
            SELECT p.*, s.name AS supplier_name, a.account_name
            FROM pur_supplier_payments p
            JOIN supp_suppliers s ON p.supplier_no = s.supplier_no
            LEFT JOIN acc_transaction_accounts a ON p.account_no = a.account_no
            WHERE p.pharmacy_no = @CurrentPharmacyNo AND p.branch_no = @CurrentBranchNo";

        var countSql = @"
            SELECT COUNT(1)
            FROM pur_supplier_payments p
            JOIN supp_suppliers s ON p.supplier_no = s.supplier_no
            WHERE p.pharmacy_no = @CurrentPharmacyNo AND p.branch_no = @CurrentBranchNo";

        var pms = new DynamicParameters();
        pms.Add("CurrentPharmacyNo", CurrentPharmacyNo);
        pms.Add("CurrentBranchNo", CurrentBranchNo);

        if (!string.IsNullOrWhiteSpace(search))
        {
            sql += " AND (p.payment_number ILIKE @Search OR s.name ILIKE @Search OR p.reference_no ILIKE @Search)";
            countSql += " AND (p.payment_number ILIKE @Search OR s.name ILIKE @Search OR p.reference_no ILIKE @Search)";
            pms.Add("Search", $"%{search.Trim()}%");
        }

        if (supplierNo.HasValue)
        {
            sql += " AND p.supplier_no = @SupplierNo";
            countSql += " AND p.supplier_no = @SupplierNo";
            pms.Add("SupplierNo", supplierNo.Value);
        }

        sql += " ORDER BY p.supplier_payment_no DESC LIMIT @PageSize OFFSET @Offset;";
        pms.Add("PageSize", filter.PageSize);
        pms.Add("Offset", (filter.PageIndex - 1) * filter.PageSize);

        var total = await conn.ExecuteScalarAsync<int>(countSql, pms);
        var items = (await conn.QueryAsync<PurSupplierPayment>(sql, pms)).ToList();

        var result = new PagedResult<PurSupplierPayment>(items, total, filter.PageIndex, filter.PageSize);
        return PagedResponse(result);
    }

    [HttpPost("supplier-payments")]
    [HttpPost("payments")]
    public async Task<ActionResult<ApiResponse<PurSupplierPayment>>> CreatePayment([FromBody] PurSupplierPayment payload)
    {
        if (payload.SupplierNo <= 0)
            return FailResponse<PurSupplierPayment>("A valid supplier must be selected.");

        if (payload.Amount <= 0)
            return FailResponse<PurSupplierPayment>("Payment amount must be greater than zero.");

        using var conn = _db.CreateConnection();
        conn.Open();
        using var tran = conn.BeginTransaction();

        try
        {
            var supplier = await conn.QuerySingleOrDefaultAsync<SuppSupplier>(@"
                SELECT * FROM supp_suppliers
                WHERE supplier_no = @SupplierNo AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
                FOR UPDATE;",
                new { payload.SupplierNo, CurrentPharmacyNo, CurrentBranchNo },
                transaction: tran);

            if (supplier == null)
                throw new InvalidOperationException($"Supplier #{payload.SupplierNo} not found in this pharmacy branch.");

            var accountNo = payload.AccountNo > 0 ? payload.AccountNo : 1; // Default Cash Drawer 10101
            var paymentNumber = await _docNumService.GetNextDocumentNumberAsync(conn, tran, CurrentPharmacyNo, CurrentBranchNo, "SUPP_PAY");

            var nextPaymentNo = await conn.ExecuteScalarAsync<long>(
                "SELECT COALESCE(MAX(supplier_payment_no), 0) + 1 FROM pur_supplier_payments;", transaction: tran);

            payload.SupplierPaymentNo = nextPaymentNo;
            payload.PharmacyNo = CurrentPharmacyNo;
            payload.BranchNo = CurrentBranchNo;
            payload.PaymentNumber = paymentNumber;
            payload.AccountNo = accountNo;
            payload.PaidByUserNo = CurrentUserNo;
            payload.PaymentDate = DateTime.UtcNow;
            payload.CreatedAt = DateTime.UtcNow;

            await conn.ExecuteAsync(@"
                INSERT INTO pur_supplier_payments (
                    supplier_payment_no, pharmacy_no, branch_no, supplier_no,
                    account_no, payment_number, payment_method, amount,
                    reference_no, note, paid_by_user_no, payment_date, created_at
                ) VALUES (
                    @SupplierPaymentNo, @PharmacyNo, @BranchNo, @SupplierNo,
                    @AccountNo, @PaymentNumber, @PaymentMethod, @Amount,
                    @ReferenceNo, @Note, @PaidByUserNo, @PaymentDate, @CreatedAt
                );",
                payload, transaction: tran);

            // Deduct supplier AP balance
            await conn.ExecuteAsync(@"
                UPDATE supp_suppliers
                SET current_due_balance = GREATEST(0, current_due_balance - @Amount),
                    updated_at = NOW()
                WHERE supplier_no = @SupplierNo;",
                new { payload.Amount, payload.SupplierNo },
                transaction: tran);

            // Universal Financial Ledger Posting
            var apAcc = await conn.QuerySingleOrDefaultAsync<long?>(
                "SELECT account_no FROM acc_transaction_accounts WHERE account_code = '20101' AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
                new { CurrentPharmacyNo, CurrentBranchNo }, transaction: tran) ?? 1;

            var journalLegs = new List<LedgerLeg>
            {
                // Dr 20101 Accounts Payable (Debt reduced)
                new LedgerLeg
                {
                    AccountNo = apAcc,
                    DebitAmount = payload.Amount,
                    CreditAmount = 0,
                    PartyType = 2, // Supplier
                    PartyNo = payload.SupplierNo,
                    Narration = $"Debt payment to supplier {supplier.Name} ({paymentNumber})"
                },
                // Cr Cash / Bank Account
                new LedgerLeg
                {
                    AccountNo = accountNo,
                    DebitAmount = 0,
                    CreditAmount = payload.Amount,
                    PartyType = 2,
                    PartyNo = payload.SupplierNo,
                    Narration = $"Payment disbursed from account ({paymentNumber})"
                }
            };

            await _ledgerPosting.PostJournalAsync(conn, tran, new CompoundJournalEntry
            {
                PharmacyNo = CurrentPharmacyNo,
                BranchNo = CurrentBranchNo,
                TransactionType = 8, // Supplier Payment
                SourceDocumentType = "SUPP_PAY",
                SourceDocumentNo = paymentNumber,
                SourceDocumentId = nextPaymentNo,
                CreatedByUserNo = CurrentUserNo,
                Legs = journalLegs
            });

            tran.Commit();

            payload.SupplierName = supplier.Name;
            return OkResponse(payload, $"Supplier payment of ৳{payload.Amount} recorded successfully.");
        }
        catch (Exception ex)
        {
            tran.Rollback();
            return FailResponse<PurSupplierPayment>($"Payment failed: {ex.Message}");
        }
    }
}

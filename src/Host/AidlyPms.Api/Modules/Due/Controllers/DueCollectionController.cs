using AidlyPms.Api.Common;
using AidlyPms.Api.Common.Database;
using AidlyPms.Api.Infrastructure;
using AidlyPms.Api.Modules.Cash.Models;
using AidlyPms.Api.Modules.Customers.Models;
using Dapper;
using Microsoft.AspNetCore.Mvc;

namespace AidlyPms.Api.Modules.Due.Controllers;

[Route("api/v1")]
public class DueCollectionController : BaseController
{
    private readonly IDbConnectionFactory _db;
    private readonly IDocumentNumberService _docNumService;
    private readonly ILedgerPostingService _ledgerPosting;

    public DueCollectionController(
        IDbConnectionFactory db,
        IDocumentNumberService docNumService,
        ILedgerPostingService ledgerPosting)
    {
        _db = db;
        _docNumService = docNumService;
        _ledgerPosting = ledgerPosting;
    }

    [HttpGet("cust/dues")]
    public async Task<ActionResult<ApiResponse<List<CustCustomer>>>> GetCustomerDues([FromQuery] string? search)
    {
        using var conn = _db.CreateConnection();
        var sql = @"
            SELECT * FROM cust_customers
            WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
              AND current_due > 0";

        var pms = new DynamicParameters();
        pms.Add("CurrentPharmacyNo", CurrentPharmacyNo);
        pms.Add("CurrentBranchNo", CurrentBranchNo);

        if (!string.IsNullOrWhiteSpace(search))
        {
            sql += " AND (name ILIKE @Search OR phone ILIKE @Search)";
            pms.Add("Search", $"%{search.Trim()}%");
        }

        sql += " ORDER BY current_due DESC LIMIT 100;";
        var customers = (await conn.QueryAsync<CustCustomer>(sql, pms)).ToList();

        return OkResponse(customers);
    }

    [HttpGet("due/collections")]
    [HttpGet("cust/dues/collections")]
    public async Task<ActionResult<ApiResponse<PagedResult<DueCollection>>>> GetDueCollections(
        [FromQuery] QueryFilter filter,
        [FromQuery] long? customerNo,
        [FromQuery] string? search)
    {
        using var conn = _db.CreateConnection();
        var sql = @"
            SELECT d.*, c.name AS customer_name, c.phone AS customer_phone, a.account_name
            FROM due_collections d
            JOIN cust_customers c ON d.customer_no = c.customer_no
            LEFT JOIN acc_transaction_accounts a ON d.account_no = a.account_no
            WHERE d.pharmacy_no = @CurrentPharmacyNo AND d.branch_no = @CurrentBranchNo";

        var countSql = @"
            SELECT COUNT(1)
            FROM due_collections d
            JOIN cust_customers c ON d.customer_no = c.customer_no
            WHERE d.pharmacy_no = @CurrentPharmacyNo AND d.branch_no = @CurrentBranchNo";

        var pms = new DynamicParameters();
        pms.Add("CurrentPharmacyNo", CurrentPharmacyNo);
        pms.Add("CurrentBranchNo", CurrentBranchNo);

        if (!string.IsNullOrWhiteSpace(search))
        {
            sql += " AND (d.payment_number ILIKE @Search OR c.name ILIKE @Search OR c.phone ILIKE @Search)";
            countSql += " AND (d.payment_number ILIKE @Search OR c.name ILIKE @Search OR c.phone ILIKE @Search)";
            pms.Add("Search", $"%{search.Trim()}%");
        }

        if (customerNo.HasValue)
        {
            sql += " AND d.customer_no = @CustomerNo";
            countSql += " AND d.customer_no = @CustomerNo";
            pms.Add("CustomerNo", customerNo.Value);
        }

        sql += " ORDER BY d.due_collection_no DESC LIMIT @PageSize OFFSET @Offset;";
        pms.Add("PageSize", filter.PageSize);
        pms.Add("Offset", (filter.PageIndex - 1) * filter.PageSize);

        var total = await conn.ExecuteScalarAsync<int>(countSql, pms);
        var items = (await conn.QueryAsync<DueCollection>(sql, pms)).ToList();

        var result = new PagedResult<DueCollection>(items, total, filter.PageIndex, filter.PageSize);
        return PagedResponse(result);
    }

    [HttpPost("due/collect")]
    [HttpPost("cust/dues/collect")]
    public async Task<ActionResult<ApiResponse<DueCollection>>> CollectDue([FromBody] CollectDuePayload payload)
    {
        if (payload.CustomerNo <= 0)
            return FailResponse<DueCollection>("A valid customer must be selected.");

        if (payload.Amount <= 0)
            return FailResponse<DueCollection>("Collection amount must be greater than zero.");

        using var conn = _db.CreateConnection();
        conn.Open();
        using var tran = conn.BeginTransaction();

        try
        {
            var customer = await conn.QuerySingleOrDefaultAsync<CustCustomer>(@"
                SELECT * FROM cust_customers
                WHERE customer_no = @CustomerNo AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
                FOR UPDATE;",
                new { payload.CustomerNo, CurrentPharmacyNo, CurrentBranchNo },
                transaction: tran);

            if (customer == null)
                throw new InvalidOperationException($"Customer #{payload.CustomerNo} not found.");

            var accountNo = payload.AccountNo.HasValue && payload.AccountNo.Value > 0 ? payload.AccountNo.Value : 1;
            var paymentNumber = await _docNumService.GetNextDocumentNumberAsync(conn, tran, CurrentPharmacyNo, CurrentBranchNo, "DUE_COLL");

            var nextDueCollNo = await conn.ExecuteScalarAsync<long>(
                "SELECT COALESCE(MAX(due_collection_no), 0) + 1 FROM due_collections;", transaction: tran);

            var collection = new DueCollection
            {
                DueCollectionNo = nextDueCollNo,
                PharmacyNo = CurrentPharmacyNo,
                BranchNo = CurrentBranchNo,
                CustomerNo = payload.CustomerNo,
                AccountNo = accountNo,
                SaleInvoiceNo = payload.SaleInvoiceNo,
                PaymentNumber = paymentNumber,
                PaymentMethod = payload.PaymentMethod,
                Amount = payload.Amount,
                Note = payload.Note,
                ReceivedByUserNo = CurrentUserNo,
                PaymentDate = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };

            await conn.ExecuteAsync(@"
                INSERT INTO due_collections (
                    due_collection_no, pharmacy_no, branch_no, customer_no,
                    account_no, sale_invoice_no, payment_number, payment_method,
                    amount, note, received_by_user_no, payment_date, created_at
                ) VALUES (
                    @DueCollectionNo, @PharmacyNo, @BranchNo, @CustomerNo,
                    @AccountNo, @SaleInvoiceNo, @PaymentNumber, @PaymentMethod,
                    @Amount, @Note, @ReceivedByUserNo, @PaymentDate, @CreatedAt
                );", collection, transaction: tran);

            // Deduct customer current_due
            await conn.ExecuteAsync(@"
                UPDATE cust_customers
                SET current_due = GREATEST(0, current_due - @Amount),
                    has_due = CASE WHEN current_due - @Amount > 0 THEN TRUE ELSE FALSE END,
                    updated_at = NOW()
                WHERE customer_no = @CustomerNo;",
                new { payload.Amount, payload.CustomerNo },
                transaction: tran);

            // Universal Financial Ledger Posting:
            // Dr Cash / Bank Account (Funds received)
            // Cr 10301 Accounts Receivable (AR liability reduced)
            var arAcc = await conn.QuerySingleOrDefaultAsync<long?>(
                "SELECT account_no FROM acc_transaction_accounts WHERE account_code = '10301' AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
                new { CurrentPharmacyNo, CurrentBranchNo }, transaction: tran) ?? 1;

            var journalLegs = new List<LedgerLeg>
            {
                new LedgerLeg
                {
                    AccountNo = accountNo,
                    DebitAmount = payload.Amount,
                    CreditAmount = 0,
                    PartyType = 1, // Customer
                    PartyNo = payload.CustomerNo,
                    Narration = $"Customer due payment received from {customer.Name} ({paymentNumber})"
                },
                new LedgerLeg
                {
                    AccountNo = arAcc,
                    DebitAmount = 0,
                    CreditAmount = payload.Amount,
                    PartyType = 1, // Customer
                    PartyNo = payload.CustomerNo,
                    Narration = $"AR reduction for due collection ({paymentNumber})"
                }
            };

            await _ledgerPosting.PostJournalAsync(conn, tran, new CompoundJournalEntry
            {
                PharmacyNo = CurrentPharmacyNo,
                BranchNo = CurrentBranchNo,
                TransactionType = 7, // Customer Due Collection
                SourceDocumentType = "DUE_COLLECTION",
                SourceDocumentNo = paymentNumber,
                SourceDocumentId = nextDueCollNo,
                CreatedByUserNo = CurrentUserNo,
                Legs = journalLegs
            });

            tran.Commit();

            collection.CustomerName = customer.Name;
            collection.CustomerPhone = customer.Phone;
            return OkResponse(collection, $"Due payment of ৳{payload.Amount} collected successfully.");
        }
        catch (Exception ex)
        {
            tran.Rollback();
            return FailResponse<DueCollection>($"Due collection failed: {ex.Message}");
        }
    }
}

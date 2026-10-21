using System.Text.Json.Serialization;
using AidlyPms.Api.Common;
using AidlyPms.Api.Common.Database;
using AidlyPms.Api.Infrastructure;
using AidlyPms.Api.Modules.Cash.Models;
using AidlyPms.Api.Modules.Customers.Models;
using Dapper;
using Microsoft.AspNetCore.Mvc;

namespace AidlyPms.Api.Modules.Due.Controllers;

public class CustDueRecordDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("customer")]
    public string Customer { get; set; } = string.Empty;

    [JsonPropertyName("phone")]
    public string Phone { get; set; } = string.Empty;

    [JsonPropertyName("inv")]
    public string Inv { get; set; } = string.Empty;

    [JsonPropertyName("saleDate")]
    public string SaleDate { get; set; } = string.Empty;

    [JsonPropertyName("dueDate")]
    public string DueDate { get; set; } = string.Empty;

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }
}

public class CustDueHistoryDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("customer")]
    public string Customer { get; set; } = string.Empty;

    [JsonPropertyName("phone")]
    public string Phone { get; set; } = string.Empty;

    [JsonPropertyName("receipt")]
    public string Receipt { get; set; } = string.Empty;

    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("note")]
    public string Note { get; set; } = string.Empty;

    [JsonPropertyName("paymentDate")]
    public string? PaymentDate { get; set; }
}

public class CollectDueRequestDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("customer_no")]
    public long? CustomerNo { get; set; }

    [JsonPropertyName("customer")]
    public string? Customer { get; set; }

    [JsonPropertyName("phone")]
    public string? Phone { get; set; }

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("method")]
    public string Method { get; set; } = "Cash";

    [JsonPropertyName("payment_method")]
    public object? PaymentMethodRaw { get; set; }

    [JsonPropertyName("account_no")]
    public long? AccountNo { get; set; }

    [JsonPropertyName("note")]
    public string? Note { get; set; }
}

[Route("api/v1")]
[RequirePermission("PMS-Customer-Module")]
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
    public async Task<ActionResult<ApiResponse<List<CustDueRecordDto>>>> GetCustomerDues(
        [FromQuery] string? search = null,
        [FromQuery] string? fromDate = null,
        [FromQuery] string? toDate = null)
    {
        await using var conn = await _db.CreateOpenConnectionAsync();

        var (parsedFromDate, parsedToDate) = DateHelper.NormalizeDateRange(fromDate, toDate);

        var pms = new DynamicParameters();
        pms.Add("CurrentPharmacyNo", CurrentPharmacyNo);
        pms.Add("CurrentBranchNo", CurrentBranchNo);

        var dateFilter = "";
        if (parsedFromDate.HasValue)
        {
            dateFilter += " AND s.sale_timestamp >= @FromDate";
            pms.Add("FromDate", parsedFromDate.Value);
        }
        if (parsedToDate.HasValue)
        {
            dateFilter += " AND s.sale_timestamp <= @ToDate";
            pms.Add("ToDate", parsedToDate.Value);
        }

        var searchFilterSales = "";
        var searchFilterCust = "";
        if (!string.IsNullOrWhiteSpace(search))
        {
            pms.Add("SearchPattern", $"%{search.Trim()}%");
            searchFilterSales = " AND (c.name ILIKE @SearchPattern OR c.phone ILIKE @SearchPattern OR s.sales_number ILIKE @SearchPattern)";
            searchFilterCust = " AND (c.name ILIKE @SearchPattern OR c.phone ILIKE @SearchPattern)";
        }

        var sql = $@"
            SELECT 
                s.sale_invoice_no::text AS id,
                COALESCE(c.name, 'Walking Customer') AS customer,
                COALESCE(c.phone, '') AS phone,
                s.sales_number AS inv,
                TO_CHAR(s.sale_timestamp, 'DD-MM-YYYY') AS saledate,
                TO_CHAR(s.sale_timestamp + INTERVAL '7 days', 'DD-MM-YYYY') AS duedate,
                s.due_amount AS amount
            FROM sale_invoices s
            LEFT JOIN cust_customers c ON s.customer_no = c.customer_no
            WHERE s.pharmacy_no = @CurrentPharmacyNo AND s.branch_no = @CurrentBranchNo
              AND s.due_amount > 0
              {dateFilter}
              {searchFilterSales}

            UNION ALL

            SELECT 
                CONCAT('CUST-', c.customer_no) AS id,
                c.name AS customer,
                COALESCE(c.phone, '') AS phone,
                'OPENING-DUE' AS inv,
                TO_CHAR(c.created_at, 'DD-MM-YYYY') AS saledate,
                TO_CHAR(c.created_at + INTERVAL '7 days', 'DD-MM-YYYY') AS duedate,
                c.current_due AS amount
            FROM cust_customers c
            WHERE c.pharmacy_no = @CurrentPharmacyNo AND c.branch_no = @CurrentBranchNo
              AND c.current_due > 0
              AND NOT EXISTS (
                  SELECT 1 FROM sale_invoices s 
                  WHERE s.customer_no = c.customer_no 
                    AND s.pharmacy_no = @CurrentPharmacyNo 
                    AND s.branch_no = @CurrentBranchNo 
                    AND s.due_amount > 0
              )
              {searchFilterCust}

            ORDER BY saledate DESC;";

        var list = (await conn.QueryAsync<CustDueRecordDto>(sql, pms)).AsList();
        return OkResponse(list);
    }

    [HttpGet("cust/dues/history")]
    public async Task<ActionResult<ApiResponse<List<CustDueHistoryDto>>>> GetDueHistory(
        [FromQuery] string? search = null,
        [FromQuery] string? fromDate = null,
        [FromQuery] string? toDate = null)
    {
        await using var conn = await _db.CreateOpenConnectionAsync();

        var (parsedFromDate, parsedToDate) = DateHelper.NormalizeDateRange(fromDate, toDate);

        var pms = new DynamicParameters();
        pms.Add("CurrentPharmacyNo", CurrentPharmacyNo);
        pms.Add("CurrentBranchNo", CurrentBranchNo);

        var dateFilter = "";
        if (parsedFromDate.HasValue)
        {
            dateFilter += " AND d.payment_date >= @FromDate";
            pms.Add("FromDate", parsedFromDate.Value);
        }
        if (parsedToDate.HasValue)
        {
            dateFilter += " AND d.payment_date <= @ToDate";
            pms.Add("ToDate", parsedToDate.Value);
        }

        var searchFilter = "";
        if (!string.IsNullOrWhiteSpace(search))
        {
            pms.Add("SearchPattern", $"%{search.Trim()}%");
            searchFilter = " AND (c.name ILIKE @SearchPattern OR c.phone ILIKE @SearchPattern OR d.payment_number ILIKE @SearchPattern)";
        }

        var sql = $@"
            SELECT 
                d.due_collection_no::text AS id,
                COALESCE(c.name, 'Customer') AS customer,
                COALESCE(c.phone, '') AS phone,
                COALESCE(d.payment_number, CONCAT('RCT-', d.due_collection_no)) AS receipt,
                CASE d.payment_method 
                    WHEN 3 THEN 'bKash'
                    WHEN 6 THEN 'Card'
                    WHEN 8 THEN 'Split'
                    ELSE 'Cash'
                END AS method,
                d.amount AS amount,
                COALESCE(d.note, 'Due collection settlement') AS note,
                TO_CHAR(d.payment_date, 'DD-MM-YYYY') AS paymentdate
            FROM due_collections d
            LEFT JOIN cust_customers c ON d.customer_no = c.customer_no
            WHERE d.pharmacy_no = @CurrentPharmacyNo AND d.branch_no = @CurrentBranchNo
              {dateFilter}
              {searchFilter}
            ORDER BY d.payment_date DESC
            LIMIT 100;";

        var list = (await conn.QueryAsync<CustDueHistoryDto>(sql, pms)).AsList();
        return OkResponse(list);
    }

    [HttpGet("due/collections")]
    [HttpGet("cust/dues/collections")]
    public async Task<ActionResult<ApiResponse<PagedResult<DueCollection>>>> GetDueCollections(
        [FromQuery] QueryFilter filter,
        [FromQuery] long? customerNo,
        [FromQuery] string? search)
    {
        await using var conn = await _db.CreateOpenConnectionAsync();
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
    public async Task<ActionResult<ApiResponse<object>>> CollectDue([FromBody] CollectDueRequestDto req)
    {
        if (req.Amount <= 0)
        {
            return FailResponse<object>("Collection amount must be greater than zero.");
        }

        await using var conn = await _db.CreateOpenConnectionAsync();
        await using var tran = await conn.BeginTransactionAsync();

        try
        {
            long? saleInvoiceNo = null;
            long customerNo = req.CustomerNo.HasValue && req.CustomerNo.Value > 0 ? req.CustomerNo.Value : 0;
            string customerName = req.Customer ?? string.Empty;
            string customerPhone = req.Phone ?? string.Empty;

            // 1. Resolve sale_invoice_no and customer from ID
            if (!string.IsNullOrWhiteSpace(req.Id))
            {
                if (long.TryParse(req.Id, out var parsedInvoiceNo) && parsedInvoiceNo > 0)
                {
                    saleInvoiceNo = parsedInvoiceNo;
                    var inv = await conn.QuerySingleOrDefaultAsync<(long customer_no, string? customer_name, string? customer_phone)>(@"
                        SELECT s.customer_no, c.name AS customer_name, c.phone AS customer_phone
                        FROM sale_invoices s
                        LEFT JOIN cust_customers c ON s.customer_no = c.customer_no
                        WHERE s.sale_invoice_no = @saleInvoiceNo AND s.pharmacy_no = @CurrentPharmacyNo;",
                        new { saleInvoiceNo, CurrentPharmacyNo }, transaction: tran);

                    if (inv.customer_no > 0)
                    {
                        customerNo = inv.customer_no;
                        if (string.IsNullOrWhiteSpace(customerName)) customerName = inv.customer_name ?? "";
                        if (string.IsNullOrWhiteSpace(customerPhone)) customerPhone = inv.customer_phone ?? "";
                    }
                }
                else if (req.Id.StartsWith("CUST-", StringComparison.OrdinalIgnoreCase) && 
                         long.TryParse(req.Id.Substring(5), out var parsedCustNo))
                {
                    customerNo = parsedCustNo;
                }
            }

            // 2. Resolve customer by phone or ID if not yet resolved
            if (customerNo <= 0 && !string.IsNullOrWhiteSpace(customerPhone))
            {
                var cust = await conn.QuerySingleOrDefaultAsync<CustCustomer>(@"
                    SELECT * FROM cust_customers 
                    WHERE phone = @customerPhone AND pharmacy_no = @CurrentPharmacyNo
                    LIMIT 1;",
                    new { customerPhone, CurrentPharmacyNo }, transaction: tran);

                if (cust != null)
                {
                    customerNo = cust.CustomerNo;
                    if (string.IsNullOrWhiteSpace(customerName)) customerName = cust.Name;
                }
            }

            if (customerNo <= 0)
            {
                customerNo = 1;
            }

            // 3. Update sale invoice due if linked
            if (saleInvoiceNo.HasValue && saleInvoiceNo.Value > 0)
            {
                await conn.ExecuteAsync(@"
                    UPDATE sale_invoices 
                    SET due_amount = GREATEST(0, due_amount - @Amount),
                        customer_will_pay = customer_will_pay + @Amount,
                        updated_at = CURRENT_TIMESTAMP
                    WHERE sale_invoice_no = @saleInvoiceNo AND pharmacy_no = @CurrentPharmacyNo;",
                    new { saleInvoiceNo = saleInvoiceNo.Value, CurrentPharmacyNo, req.Amount }, transaction: tran);
            }

            // 4. Update customer balance
            if (customerNo > 0)
            {
                await conn.ExecuteAsync(@"
                    UPDATE cust_customers
                    SET current_due = GREATEST(0, current_due - @Amount),
                        has_due = CASE WHEN current_due - @Amount > 0 THEN TRUE ELSE FALSE END,
                        updated_at = CURRENT_TIMESTAMP
                    WHERE customer_no = @customerNo AND pharmacy_no = @CurrentPharmacyNo;",
                    new { customerNo, CurrentPharmacyNo, req.Amount }, transaction: tran);
            }

            // 5. Generate Payment Number and Record due_collections
            var accountNo = req.AccountNo.HasValue && req.AccountNo.Value > 0 ? req.AccountNo.Value : 1;
            var paymentNumber = await _docNumService.GetNextDocumentNumberAsync(conn, tran, CurrentPharmacyNo, CurrentBranchNo, "DUE_COLL");
            if (string.IsNullOrWhiteSpace(paymentNumber))
            {
                paymentNumber = $"RCT-{CurrentBranchNo:D2}-{DateTime.UtcNow.Year}-{DateTime.UtcNow.Ticks % 100000:D5}";
            }

            var nextDueCollNo = await conn.ExecuteScalarAsync<long>(
                "SELECT COALESCE(MAX(due_collection_no), 0) + 1 FROM due_collections;", transaction: tran);

            short methodCode = 1; // Default 1: Cash
            if (!string.IsNullOrWhiteSpace(req.Method))
            {
                var m = req.Method.Trim().ToLowerInvariant();
                if (m.Contains("card")) methodCode = 6;
                else if (m.Contains("bkash") || m.Contains("mfs") || m.Contains("nagad")) methodCode = 3;
                else if (short.TryParse(req.Method, out var parsedMethod)) methodCode = parsedMethod;
            }
            else if (req.PaymentMethodRaw != null && short.TryParse(req.PaymentMethodRaw.ToString(), out var parsedRaw))
            {
                methodCode = parsedRaw;
            }

            await conn.ExecuteAsync(@"
                INSERT INTO due_collections (
                    due_collection_no, pharmacy_no, branch_no, customer_no,
                    account_no, sale_invoice_no, payment_number, payment_method,
                    amount, note, received_by_user_no, payment_date, created_at
                ) VALUES (
                    @nextDueCollNo, @CurrentPharmacyNo, @CurrentBranchNo, @customerNo,
                    @accountNo, @saleInvoiceNo, @paymentNumber, @methodCode,
                    @Amount, @Note, @CurrentUserNo, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
                );",
                new
                {
                    nextDueCollNo,
                    CurrentPharmacyNo,
                    CurrentBranchNo,
                    customerNo,
                    accountNo,
                    saleInvoiceNo,
                    paymentNumber,
                    methodCode,
                    req.Amount,
                    Note = req.Note ?? "Due collection settlement",
                    CurrentUserNo
                }, transaction: tran);

            // 6. Universal Financial Ledger Posting:
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
                    DebitAmount = req.Amount,
                    CreditAmount = 0,
                    PartyType = 1, // Customer
                    PartyNo = customerNo,
                    Narration = $"Customer due payment received from {customerName} ({paymentNumber})"
                },
                new LedgerLeg
                {
                    AccountNo = arAcc,
                    DebitAmount = 0,
                    CreditAmount = req.Amount,
                    PartyType = 1, // Customer
                    PartyNo = customerNo,
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

            await tran.CommitAsync();

            return OkResponse<object>(new
            {
                receipt = paymentNumber,
                collected = req.Amount,
                customer = customerName,
                date = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
            }, $"Due payment of ৳{req.Amount} collected successfully.");
        }
        catch
        {
            await tran.RollbackAsync();
            throw;
        }
    }
}

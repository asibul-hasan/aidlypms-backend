using AidlyPms.Api.Common;
using AidlyPms.Api.Common.Database;
using AidlyPms.Api.Infrastructure;
using AidlyPms.Api.Modules.Cash.Models;
using Dapper;
using Microsoft.AspNetCore.Mvc;

namespace AidlyPms.Api.Modules.Cash.Controllers;

[Route("api/v1/cash")]
public class CashCloserController : BaseController
{
    private readonly IDbConnectionFactory _db;
    private readonly ILedgerPostingService _ledgerPosting;

    public CashCloserController(
        IDbConnectionFactory db,
        ILedgerPostingService ledgerPosting)
    {
        _db = db;
        _ledgerPosting = ledgerPosting;
    }

    [HttpGet("closers")]
    public async Task<ActionResult<ApiResponse<PagedResult<CashCloser>>>> GetClosers(
        [FromQuery] QueryFilter filter,
        [FromQuery] DateTime? date)
    {
        using var conn = _db.CreateConnection();
        var sql = @"
            SELECT c.*, CONCAT(u.first_name, ' ', u.last_name) AS closed_by_user_name
            FROM cash_closers c
            LEFT JOIN sys_users u ON c.closed_by_user_no = u.user_no
            WHERE c.pharmacy_no = @CurrentPharmacyNo AND c.branch_no = @CurrentBranchNo";

        var countSql = @"
            SELECT COUNT(1) FROM cash_closers
            WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo";

        var pms = new DynamicParameters();
        pms.Add("CurrentPharmacyNo", CurrentPharmacyNo);
        pms.Add("CurrentBranchNo", CurrentBranchNo);

        if (date.HasValue)
        {
            sql += " AND c.closing_date = @ClosingDate";
            countSql += " AND c.closing_date = @ClosingDate";
            pms.Add("ClosingDate", date.Value.Date);
        }

        sql += " ORDER BY c.cash_closer_no DESC LIMIT @PageSize OFFSET @Offset;";
        pms.Add("PageSize", filter.PageSize);
        pms.Add("Offset", (filter.PageIndex - 1) * filter.PageSize);

        var total = await conn.ExecuteScalarAsync<int>(countSql, pms);
        var items = (await conn.QueryAsync<CashCloser>(sql, pms)).ToList();

        var result = new PagedResult<CashCloser>(items, total, filter.PageIndex, filter.PageSize);
        return PagedResponse(result);
    }

    [HttpGet("current-shift-summary")]
    public async Task<ActionResult<ApiResponse<CashCloser>>> GetCurrentShiftSummary()
    {
        using var conn = _db.CreateConnection();
        var today = DateTime.UtcNow.Date;

        // Cash sales collected today
        var cashSales = await conn.ExecuteScalarAsync<decimal?>(@"
            SELECT COALESCE(SUM(customer_will_pay - change_amount), 0)
            FROM sale_invoices
            WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
              AND DATE(sale_timestamp) = @Today AND payment_method = 1 AND document_status = 2;",
            new { CurrentPharmacyNo, CurrentBranchNo, Today = today }) ?? 0m;

        // Digital collections
        var bkash = await conn.ExecuteScalarAsync<decimal?>(@"
            SELECT COALESCE(SUM(customer_will_pay), 0) FROM sale_invoices
            WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
              AND DATE(sale_timestamp) = @Today AND payment_method = 3 AND document_status = 2;",
            new { CurrentPharmacyNo, CurrentBranchNo, Today = today }) ?? 0m;

        var nagad = await conn.ExecuteScalarAsync<decimal?>(@"
            SELECT COALESCE(SUM(customer_will_pay), 0) FROM sale_invoices
            WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
              AND DATE(sale_timestamp) = @Today AND payment_method = 4 AND document_status = 2;",
            new { CurrentPharmacyNo, CurrentBranchNo, Today = today }) ?? 0m;

        var card = await conn.ExecuteScalarAsync<decimal?>(@"
            SELECT COALESCE(SUM(customer_will_pay), 0) FROM sale_invoices
            WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
              AND DATE(sale_timestamp) = @Today AND payment_method = 6 AND document_status = 2;",
            new { CurrentPharmacyNo, CurrentBranchNo, Today = today }) ?? 0m;

        // Due collections today
        var dueColl = await conn.ExecuteScalarAsync<decimal?>(@"
            SELECT COALESCE(SUM(amount), 0) FROM due_collections
            WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
              AND DATE(payment_date) = @Today AND payment_method = 1;",
            new { CurrentPharmacyNo, CurrentBranchNo, Today = today }) ?? 0m;

        // Cash returns paid today
        var returns = await conn.ExecuteScalarAsync<decimal?>(@"
            SELECT COALESCE(SUM(cash_paid_refund), 0) FROM sale_returns
            WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
              AND DATE(return_date) = @Today AND return_status = 2;",
            new { CurrentPharmacyNo, CurrentBranchNo, Today = today }) ?? 0m;

        // Petty cash expenses paid today
        var expenses = await conn.ExecuteScalarAsync<decimal?>(@"
            SELECT COALESCE(SUM(amount), 0) FROM acc_expenses
            WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
              AND DATE(expense_date) = @Today AND payment_type ILIKE 'Cash';",
            new { CurrentPharmacyNo, CurrentBranchNo, Today = today }) ?? 0m;

        var summary = new CashCloser
        {
            PharmacyNo = CurrentPharmacyNo,
            BranchNo = CurrentBranchNo,
            CashSalesCollected = cashSales,
            BkashCollected = bkash,
            NagadCollected = nagad,
            CardCollected = card,
            DueCollections = dueColl,
            CashReturnsPaid = returns,
            ExpensesPaid = expenses,
            ClosingDate = today
        };

        return OkResponse(summary);
    }

    [HttpPost("close-shift")]
    public async Task<ActionResult<ApiResponse<CashCloser>>> CloseShift([FromBody] CloseShiftPayload payload)
    {
        using var conn = _db.CreateConnection();
        conn.Open();
        using var tran = conn.BeginTransaction();

        try
        {
            var closingDate = (payload.ClosingDate ?? DateTime.UtcNow).Date;

            // Compute live figures on server
            var cashSales = await conn.ExecuteScalarAsync<decimal?>(@"
                SELECT COALESCE(SUM(customer_will_pay - change_amount), 0)
                FROM sale_invoices
                WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
                  AND DATE(sale_timestamp) = @ClosingDate AND payment_method = 1 AND document_status = 2;",
                new { CurrentPharmacyNo, CurrentBranchNo, ClosingDate = closingDate },
                transaction: tran) ?? 0m;

            var card = await conn.ExecuteScalarAsync<decimal?>(@"
                SELECT COALESCE(SUM(customer_will_pay), 0) FROM sale_invoices
                WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
                  AND DATE(sale_timestamp) = @ClosingDate AND payment_method = 6 AND document_status = 2;",
                new { CurrentPharmacyNo, CurrentBranchNo, ClosingDate = closingDate },
                transaction: tran) ?? 0m;

            var bkash = await conn.ExecuteScalarAsync<decimal?>(@"
                SELECT COALESCE(SUM(customer_will_pay), 0) FROM sale_invoices
                WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
                  AND DATE(sale_timestamp) = @ClosingDate AND payment_method = 3 AND document_status = 2;",
                new { CurrentPharmacyNo, CurrentBranchNo, ClosingDate = closingDate },
                transaction: tran) ?? 0m;

            var nagad = await conn.ExecuteScalarAsync<decimal?>(@"
                SELECT COALESCE(SUM(customer_will_pay), 0) FROM sale_invoices
                WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
                  AND DATE(sale_timestamp) = @ClosingDate AND payment_method = 4 AND document_status = 2;",
                new { CurrentPharmacyNo, CurrentBranchNo, ClosingDate = closingDate },
                transaction: tran) ?? 0m;

            var dueColl = await conn.ExecuteScalarAsync<decimal?>(@"
                SELECT COALESCE(SUM(amount), 0) FROM due_collections
                WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
                  AND DATE(payment_date) = @ClosingDate AND payment_method = 1;",
                new { CurrentPharmacyNo, CurrentBranchNo, ClosingDate = closingDate },
                transaction: tran) ?? 0m;

            var returns = await conn.ExecuteScalarAsync<decimal?>(@"
                SELECT COALESCE(SUM(cash_paid_refund), 0) FROM sale_returns
                WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
                  AND DATE(return_date) = @ClosingDate AND return_status = 2;",
                new { CurrentPharmacyNo, CurrentBranchNo, ClosingDate = closingDate },
                transaction: tran) ?? 0m;

            var expenses = await conn.ExecuteScalarAsync<decimal?>(@"
                SELECT COALESCE(SUM(amount), 0) FROM acc_expenses
                WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
                  AND DATE(expense_date) = @ClosingDate AND payment_type ILIKE 'Cash';",
                new { CurrentPharmacyNo, CurrentBranchNo, ClosingDate = closingDate },
                transaction: tran) ?? 0m;

            // Server Calculation Authority for expected cash in drawer
            var calculatedClosing = payload.OpeningBalance + cashSales + dueColl - returns - expenses;
            var discrepancy = payload.ActualCountedBalance - calculatedClosing;

            var nextCloserNo = await conn.ExecuteScalarAsync<long>(
                "SELECT COALESCE(MAX(cash_closer_no), 0) + 1 FROM cash_closers;", transaction: tran);

            var nextShiftNo = await conn.ExecuteScalarAsync<int>(@"
                SELECT COALESCE(MAX(shift_no), 0) + 1 FROM cash_closers
                WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo AND closing_date = @ClosingDate;",
                new { CurrentPharmacyNo, CurrentBranchNo, ClosingDate = closingDate },
                transaction: tran);

            var shiftNo = payload.ShiftNo > 0 ? payload.ShiftNo : nextShiftNo;

            var closer = new CashCloser
            {
                CashCloserNo = nextCloserNo,
                PharmacyNo = CurrentPharmacyNo,
                BranchNo = CurrentBranchNo,
                ClosedByUserNo = CurrentUserNo,
                RegisterId = payload.RegisterId ?? "REG-MAIN-01",
                ShiftNo = shiftNo,
                OpeningBalance = payload.OpeningBalance,
                CashSalesCollected = cashSales,
                CardCollected = card,
                BkashCollected = bkash,
                NagadCollected = nagad,
                RocketCollected = 0m,
                SplitCollected = 0m,
                DueCollections = dueColl,
                CashReturnsPaid = returns,
                ExpensesPaid = expenses,
                CalculatedClosingBalance = calculatedClosing,
                ActualCountedBalance = payload.ActualCountedBalance,
                DiscrepancyAmount = discrepancy,
                Note = payload.Note,
                ClosingDate = closingDate,
                CreatedAt = DateTime.UtcNow
            };

            await conn.ExecuteAsync(@"
                INSERT INTO cash_closers (
                    cash_closer_no, pharmacy_no, branch_no, closed_by_user_no, register_id,
                    shift_no, opening_balance, cash_sales_collected, card_collected,
                    bkash_collected, nagad_collected, rocket_collected, split_collected,
                    due_collections, cash_returns_paid, expenses_paid,
                    calculated_closing_balance, actual_counted_balance, discrepancy_amount,
                    note, closing_date, created_at
                ) VALUES (
                    @CashCloserNo, @PharmacyNo, @BranchNo, @ClosedByUserNo, @RegisterId,
                    @ShiftNo, @OpeningBalance, @CashSalesCollected, @CardCollected,
                    @BkashCollected, @NagadCollected, @RocketCollected, @SplitCollected,
                    @DueCollections, @CashReturnsPaid, @ExpensesPaid,
                    @CalculatedClosingBalance, @ActualCountedBalance, @DiscrepancyAmount,
                    @Note, @ClosingDate, @CreatedAt
                );", closer, transaction: tran);

            // If discrepancy exists, record variance to General Ledger
            if (discrepancy != 0)
            {
                var cashDrawerAcc = await conn.QuerySingleOrDefaultAsync<long?>(
                    "SELECT account_no FROM acc_transaction_accounts WHERE account_code = '10101' AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
                    new { CurrentPharmacyNo, CurrentBranchNo }, transaction: tran) ?? 1;

                var legs = new List<LedgerLeg>();
                if (discrepancy < 0)
                {
                    // Shortage: Dr 50204 Shortage Expense, Cr 10101 Cash Drawer
                    var shortageAcc = await conn.QuerySingleOrDefaultAsync<long?>(
                        "SELECT account_no FROM acc_transaction_accounts WHERE account_code = '50201' AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
                        new { CurrentPharmacyNo, CurrentBranchNo }, transaction: tran) ?? 1;

                    var absDiff = Math.Abs(discrepancy);
                    legs.Add(new LedgerLeg
                    {
                        AccountNo = shortageAcc,
                        DebitAmount = absDiff,
                        CreditAmount = 0,
                        Narration = $"Cash drawer closing shortage variance (Shift #{shiftNo})"
                    });
                    legs.Add(new LedgerLeg
                    {
                        AccountNo = cashDrawerAcc,
                        DebitAmount = 0,
                        CreditAmount = absDiff,
                        Narration = $"Adjustment for cash drawer shortage (Shift #{shiftNo})"
                    });
                }
                else
                {
                    // Overage: Dr 10101 Cash Drawer, Cr 40201 Overage Revenue
                    var overageAcc = await conn.QuerySingleOrDefaultAsync<long?>(
                        "SELECT account_no FROM acc_transaction_accounts WHERE account_code = '40101' AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
                        new { CurrentPharmacyNo, CurrentBranchNo }, transaction: tran) ?? 1;

                    legs.Add(new LedgerLeg
                    {
                        AccountNo = cashDrawerAcc,
                        DebitAmount = discrepancy,
                        CreditAmount = 0,
                        Narration = $"Adjustment for cash drawer overage (Shift #{shiftNo})"
                    });
                    legs.Add(new LedgerLeg
                    {
                        AccountNo = overageAcc,
                        DebitAmount = 0,
                        CreditAmount = discrepancy,
                        Narration = $"Cash drawer closing overage surplus (Shift #{shiftNo})"
                    });
                }

                await _ledgerPosting.PostJournalAsync(conn, tran, new CompoundJournalEntry
                {
                    PharmacyNo = CurrentPharmacyNo,
                    BranchNo = CurrentBranchNo,
                    TransactionType = 12, // Stock/Cash Adjustment
                    SourceDocumentType = "CASH_CLOSER",
                    SourceDocumentNo = $"CC-{closingDate:yyyyMMdd}-{shiftNo}",
                    SourceDocumentId = nextCloserNo,
                    CreatedByUserNo = CurrentUserNo,
                    Legs = legs
                });
            }

            tran.Commit();

            return OkResponse(closer, $"Shift #{shiftNo} closed successfully. Discrepancy: ৳{discrepancy:N2}");
        }
        catch (Exception ex)
        {
            tran.Rollback();
            return FailResponse<CashCloser>($"Shift closing failed: {ex.Message}");
        }
    }

    [HttpGet("receive")]
    public async Task<ActionResult<ApiResponse<List<CashHandoverItemDto>>>> GetPendingHandovers()
    {
        using var conn = _db.CreateConnection();
        const string sql = @"
            SELECT 
                s.sale_invoice_no::text AS id,
                s.sales_number AS inv,
                TO_CHAR(s.sale_timestamp, 'DD-MM-YYYY HH24:MI') AS date,
                COALESCE(u.first_name || ' ' || u.last_name, 'Admin') AS salesman,
                (s.customer_will_pay - s.change_amount) AS paid,
                s.final_price AS total
            FROM sale_invoices s
            LEFT JOIN sys_users u ON s.salesman_user_no = u.user_no
            WHERE s.pharmacy_no = @CurrentPharmacyNo AND s.branch_no = @CurrentBranchNo
              AND s.cash_received_status = 1
              AND s.document_status = 2
            ORDER BY s.sale_invoice_no DESC;";

        var items = (await conn.QueryAsync<CashHandoverItemDto>(sql, new
        {
            CurrentPharmacyNo,
            CurrentBranchNo
        })).AsList();

        return OkResponse(items);
    }

    [HttpPost("receive/reconcile")]
    public async Task<ActionResult<ApiResponse<object>>> ReconcileHandovers([FromBody] ReconcileHandoverPayload? payload)
    {
        using var conn = _db.CreateConnection();
        var pms = new DynamicParameters();
        pms.Add("CurrentPharmacyNo", CurrentPharmacyNo);
        pms.Add("CurrentBranchNo", CurrentBranchNo);
        pms.Add("ReceivedByUserNo", CurrentUserNo);

        string sql;
        if (payload?.InvoiceNos != null && payload.InvoiceNos.Any())
        {
            sql = @"
                UPDATE sale_invoices
                SET cash_received_status = 2,
                    received_by_user_no = @ReceivedByUserNo,
                    updated_at = NOW()
                WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
                  AND sale_invoice_no = ANY(@InvoiceNos)
                  AND cash_received_status = 1;";
            pms.Add("InvoiceNos", payload.InvoiceNos);
        }
        else
        {
            sql = @"
                UPDATE sale_invoices
                SET cash_received_status = 2,
                    received_by_user_no = @ReceivedByUserNo,
                    updated_at = NOW()
                WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
                  AND cash_received_status = 1;";
        }

        var affected = await conn.ExecuteAsync(sql, pms);
        return OkResponse<object>(new { count = affected }, $"Reconciled {affected} transactions into cash drawer.");
    }
}


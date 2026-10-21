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
        var dhakaToday = DateHelper.GetDhakaToday();
        var metrics = await ComputeShiftMetricsAsync(conn, null, CurrentPharmacyNo, CurrentBranchNo, dhakaToday);

        var summary = new CashCloser
        {
            PharmacyNo = CurrentPharmacyNo,
            BranchNo = CurrentBranchNo,
            CashSalesCollected = metrics.CashSales,
            CardCollected = metrics.Card,
            BkashCollected = metrics.Bkash,
            NagadCollected = metrics.Nagad,
            RocketCollected = metrics.Rocket,
            SplitCollected = 0m,
            DueCollections = metrics.DueCollections,
            CashReturnsPaid = metrics.CashReturnsPaid,
            ExpensesPaid = metrics.ExpensesPaid,
            ClosingDate = dhakaToday.ToDateTime(TimeOnly.MinValue)
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
            var dhakaDate = payload.ClosingDate.HasValue
                ? DateOnly.FromDateTime(payload.ClosingDate.Value)
                : DateHelper.GetDhakaToday();

            var metrics = await ComputeShiftMetricsAsync(conn, tran, CurrentPharmacyNo, CurrentBranchNo, dhakaDate);

            // Server Calculation Authority for expected cash in drawer
            var calculatedClosing = payload.OpeningBalance + metrics.CashSales + metrics.DueCollections - metrics.CashReturnsPaid - metrics.ExpensesPaid;
            var discrepancy = payload.ActualCountedBalance - calculatedClosing;

            var nextCloserNo = await conn.ExecuteScalarAsync<long>(
                "SELECT COALESCE(MAX(cash_closer_no), 0) + 1 FROM cash_closers;", transaction: tran);

            var nextShiftNo = await conn.ExecuteScalarAsync<int>(@"
                SELECT COALESCE(MAX(shift_no), 0) + 1 FROM cash_closers
                WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo AND closing_date = @ClosingDate;",
                new { CurrentPharmacyNo, CurrentBranchNo, ClosingDate = dhakaDate.ToDateTime(TimeOnly.MinValue) },
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
                CashSalesCollected = metrics.CashSales,
                CardCollected = metrics.Card,
                BkashCollected = metrics.Bkash,
                NagadCollected = metrics.Nagad,
                RocketCollected = metrics.Rocket,
                SplitCollected = 0m,
                DueCollections = metrics.DueCollections,
                CashReturnsPaid = metrics.CashReturnsPaid,
                ExpensesPaid = metrics.ExpensesPaid,
                CalculatedClosingBalance = calculatedClosing,
                ActualCountedBalance = payload.ActualCountedBalance,
                DiscrepancyAmount = discrepancy,
                Note = payload.Note,
                ClosingDate = dhakaDate.ToDateTime(TimeOnly.MinValue),
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
                    SourceDocumentNo = $"CC-{dhakaDate:yyyyMMdd}-{shiftNo}",
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

    [HttpGet("day-end-reconciliation")]
    public async Task<ActionResult<ApiResponse<DayEndReconciliationDto>>> GetDayEndReconciliation([FromQuery] string? date)
    {
        using var conn = _db.CreateConnection();
        var dhakaDate = DateHelper.GetDhakaToday();
        if (!string.IsNullOrWhiteSpace(date) && DateHelper.TryParse(date, out var parsedDate))
        {
            dhakaDate = DateOnly.FromDateTime(parsedDate);
        }

        var metrics = await ComputeShiftMetricsAsync(conn, null, CurrentPharmacyNo, CurrentBranchNo, dhakaDate);

        var openingBal = await conn.ExecuteScalarAsync<decimal?>(@"
            SELECT opening_balance FROM cash_closers
            WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
              AND closing_date = @ClosingDate
            ORDER BY shift_no ASC LIMIT 1;",
            new { CurrentPharmacyNo, CurrentBranchNo, ClosingDate = dhakaDate.ToDateTime(TimeOnly.MinValue) }) ?? 0m;

        var expectedCash = openingBal + metrics.CashSales + metrics.DueCollections - metrics.CashReturnsPaid - metrics.ExpensesPaid;

        var dto = new DayEndReconciliationDto
        {
            Date = dhakaDate.ToString("yyyy-MM-dd"),
            OpeningBalance = openingBal,
            GrossSales = metrics.GrossSales,
            NetSales = metrics.NetSales,
            CashSalesCollected = metrics.CashSales,
            DigitalCollected = metrics.Card + metrics.Bkash + metrics.Nagad + metrics.Rocket + metrics.Bank,
            BkashCollected = metrics.Bkash,
            NagadCollected = metrics.Nagad,
            CardCollected = metrics.Card,
            BankCollected = metrics.Bank,
            DueSales = metrics.DueSales,
            DueCollections = metrics.DueCollections,
            CashReturnsPaid = metrics.CashReturnsPaid,
            TotalReturnsAmount = metrics.TotalReturnsAmount,
            ExpensesPaid = metrics.ExpensesPaid,
            ExpectedDrawerCash = expectedCash,
            TotalSalesCount = metrics.SalesCount,
            TotalReturnsCount = metrics.TotalReturnsCount,
            ExchangesCount = metrics.ExchangesCount
        };

        return OkResponse(dto);
    }

    private async Task<ShiftMetrics> ComputeShiftMetricsAsync(
        System.Data.IDbConnection conn,
        System.Data.IDbTransaction? tran,
        long pharmacyNo,
        long branchNo,
        DateOnly dhakaDate)
    {
        var (startUtc, endUtc) = DateHelper.GetDhakaDayUtcRange(dhakaDate);

        // 1. Payment collections from sale_invoice_payments for completed invoices (document_status = 2)
        // Exclude ReturnCredit (9) as that is an internal exchange settlement, not real tender
        var payments = (await conn.QueryAsync<(short PaymentMethod, decimal Amount)>(@"
            SELECT p.payment_method, COALESCE(SUM(p.amount), 0) AS total_collected
            FROM sale_invoice_payments p
            INNER JOIN sale_invoices s ON p.sale_invoice_no = s.sale_invoice_no
            WHERE s.pharmacy_no = @pharmacyNo AND s.branch_no = @branchNo
              AND s.document_status = 2
              AND s.sale_timestamp >= @startUtc AND s.sale_timestamp <= @endUtc
              AND p.payment_method != 9
            GROUP BY p.payment_method;",
            new { pharmacyNo, branchNo, startUtc, endUtc },
            transaction: tran)).ToDictionary(x => x.PaymentMethod, x => x.Amount);

        decimal cashSales = payments.GetValueOrDefault((short)1, 0m);
        decimal bank = payments.GetValueOrDefault((short)2, 0m);
        decimal bkash = payments.GetValueOrDefault((short)3, 0m);
        decimal nagad = payments.GetValueOrDefault((short)4, 0m);
        decimal rocket = payments.GetValueOrDefault((short)5, 0m);
        decimal card = payments.GetValueOrDefault((short)6, 0m);

        // 2. Sales totals
        var salesStats = await conn.QueryFirstOrDefaultAsync<(decimal GrossSales, decimal NetSales, decimal DueSales, int TotalCount)>(@"
            SELECT 
                COALESCE(SUM(gross_total_price), 0) AS GrossSales,
                COALESCE(SUM(final_price), 0) AS NetSales,
                COALESCE(SUM(due_amount), 0) AS DueSales,
                COUNT(1) AS TotalCount
            FROM sale_invoices
            WHERE pharmacy_no = @pharmacyNo AND branch_no = @branchNo
              AND document_status = 2
              AND sale_timestamp >= @startUtc AND sale_timestamp <= @endUtc;",
            new { pharmacyNo, branchNo, startUtc, endUtc },
            transaction: tran);

        // 3. Due collections today
        var dueColl = await conn.ExecuteScalarAsync<decimal?>(@"
            SELECT COALESCE(SUM(amount), 0) FROM due_collections
            WHERE pharmacy_no = @pharmacyNo AND branch_no = @branchNo
              AND payment_date >= @startUtc AND payment_date <= @endUtc
              AND payment_method = 1;",
            new { pharmacyNo, branchNo, startUtc, endUtc },
            transaction: tran) ?? 0m;

        // 4. Returns: cash paid refunds and total return amount from completed returns (return_status = 2)
        var returnStats = await conn.QueryFirstOrDefaultAsync<(decimal CashRefunds, decimal TotalReturns, int TotalCount)>(@"
            SELECT 
                COALESCE(SUM(cash_paid_refund), 0) AS CashRefunds,
                COALESCE(SUM(final_refund_price), 0) AS TotalReturns,
                COUNT(1) AS TotalCount
            FROM sale_returns
            WHERE pharmacy_no = @pharmacyNo AND branch_no = @branchNo
              AND return_date >= @startUtc AND return_date <= @endUtc
              AND return_status = 2;",
            new { pharmacyNo, branchNo, startUtc, endUtc },
            transaction: tran);

        // 5. Exchanges count
        var exchangeCount = await conn.ExecuteScalarAsync<int>(@"
            SELECT COUNT(1) FROM sale_invoices
            WHERE pharmacy_no = @pharmacyNo AND branch_no = @branchNo
              AND document_status = 2
              AND exchange_of_return_no IS NOT NULL
              AND sale_timestamp >= @startUtc AND sale_timestamp <= @endUtc;",
            new { pharmacyNo, branchNo, startUtc, endUtc },
            transaction: tran);

        // 6. Petty cash expenses paid today
        var expenses = await conn.ExecuteScalarAsync<decimal?>(@"
            SELECT COALESCE(SUM(amount), 0) FROM acc_expenses
            WHERE pharmacy_no = @pharmacyNo AND branch_no = @branchNo
              AND expense_date >= @startUtc AND expense_date <= @endUtc
              AND payment_type ILIKE 'Cash';",
            new { pharmacyNo, branchNo, startUtc, endUtc },
            transaction: tran) ?? 0m;

        return new ShiftMetrics
        {
            CashSales = cashSales,
            Bank = bank,
            Bkash = bkash,
            Nagad = nagad,
            Rocket = rocket,
            Card = card,
            DueCollections = dueColl,
            CashReturnsPaid = returnStats.CashRefunds,
            TotalReturnsAmount = returnStats.TotalReturns,
            TotalReturnsCount = returnStats.TotalCount,
            ExpensesPaid = expenses,
            GrossSales = salesStats.GrossSales,
            NetSales = salesStats.NetSales,
            DueSales = salesStats.DueSales,
            SalesCount = salesStats.TotalCount,
            ExchangesCount = exchangeCount
        };
    }

    private sealed class ShiftMetrics
    {
        public decimal CashSales { get; set; }
        public decimal Bank { get; set; }
        public decimal Bkash { get; set; }
        public decimal Nagad { get; set; }
        public decimal Rocket { get; set; }
        public decimal Card { get; set; }
        public decimal DueCollections { get; set; }
        public decimal CashReturnsPaid { get; set; }
        public decimal TotalReturnsAmount { get; set; }
        public int TotalReturnsCount { get; set; }
        public decimal ExpensesPaid { get; set; }
        public decimal GrossSales { get; set; }
        public decimal NetSales { get; set; }
        public decimal DueSales { get; set; }
        public int SalesCount { get; set; }
        public int ExchangesCount { get; set; }
    }
}


using AidlyPms.Api.Common;
using AidlyPms.Api.Common.Database;
using AidlyPms.Api.Modules.Dashboard.Models;
using Dapper;
using Microsoft.AspNetCore.Mvc;

namespace AidlyPms.Api.Modules.Dashboard.Controllers;

[Route("api/v1/dashboard")]
public class DashboardController : BaseController
{
    private readonly IDbConnectionFactory _db;

    public DashboardController(IDbConnectionFactory db)
    {
        _db = db;
    }

    [HttpGet("summary")]
    public async Task<ActionResult<ApiResponse<ExecutiveDashboardSummary>>> GetSummary()
    {
        using var conn = _db.CreateConnection();
        var today = DateTime.UtcNow.Date;

        var salesStats = await conn.QuerySingleOrDefaultAsync<(decimal TotalSales, int SalesCount, decimal TotalProfit, decimal CashCollected)>(@"
            SELECT COALESCE(SUM(final_price), 0) AS TotalSales,
                   COUNT(1) AS SalesCount,
                   COALESCE(SUM(approximate_profit), 0) AS TotalProfit,
                   COALESCE(SUM(CASE WHEN payment_method = 1 THEN customer_will_pay - change_amount ELSE 0 END), 0) AS CashCollected
            FROM sale_invoices
            WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
              AND DATE(sale_timestamp) = @Today AND document_status = 2;",
            new { CurrentPharmacyNo, CurrentBranchNo, Today = today });

        var purchasesToday = await conn.ExecuteScalarAsync<decimal?>(@"
            SELECT COALESCE(SUM(final_price), 0)
            FROM pur_purchase_invoices
            WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
              AND DATE(purchase_date) = @Today AND document_status = 2;",
            new { CurrentPharmacyNo, CurrentBranchNo, Today = today }) ?? 0m;

        var expensesToday = await conn.ExecuteScalarAsync<decimal?>(@"
            SELECT COALESCE(SUM(amount), 0)
            FROM acc_expenses
            WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
              AND DATE(expense_date) = @Today;",
            new { CurrentPharmacyNo, CurrentBranchNo, Today = today }) ?? 0m;

        var totalCustomerDue = await conn.ExecuteScalarAsync<decimal?>(@"
            SELECT COALESCE(SUM(current_due), 0)
            FROM cust_customers
            WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
            new { CurrentPharmacyNo, CurrentBranchNo }) ?? 0m;

        var totalSupplierPayable = await conn.ExecuteScalarAsync<decimal?>(@"
            SELECT COALESCE(SUM(current_due_balance), 0)
            FROM supp_suppliers
            WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
            new { CurrentPharmacyNo, CurrentBranchNo }) ?? 0m;

        var totalInventoryValuation = await conn.ExecuteScalarAsync<decimal?>(@"
            SELECT COALESCE(SUM(b.total_quantity_pcs * p.purchase_price_per_piece), 0)
            FROM prod_product_batches b
            JOIN prod_products p ON b.product_no = p.product_no
            WHERE b.pharmacy_no = @CurrentPharmacyNo AND b.branch_no = @CurrentBranchNo
              AND b.total_quantity_pcs > 0;",
            new { CurrentPharmacyNo, CurrentBranchNo }) ?? 0m;

        var totalProducts = await conn.ExecuteScalarAsync<int>(@"
            SELECT COUNT(1) FROM prod_products
            WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo AND is_active = TRUE;",
            new { CurrentPharmacyNo, CurrentBranchNo });

        var lowStockCount = await conn.ExecuteScalarAsync<int>(@"
            SELECT COUNT(1) FROM (
                SELECT p.product_no, p.min_stock_qty_pcs, COALESCE(SUM(b.total_quantity_pcs), 0) AS current_stock
                FROM prod_products p
                LEFT JOIN prod_product_batches b ON p.product_no = b.product_no
                WHERE p.pharmacy_no = @CurrentPharmacyNo AND p.branch_no = @CurrentBranchNo AND p.is_active = TRUE
                GROUP BY p.product_no, p.min_stock_qty_pcs
                HAVING COALESCE(SUM(b.total_quantity_pcs), 0) <= p.min_stock_qty_pcs
            ) sub;",
            new { CurrentPharmacyNo, CurrentBranchNo });

        var expiredCount = await conn.ExecuteScalarAsync<int>(@"
            SELECT COUNT(1) FROM prod_product_batches
            WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
              AND expiry_date < CURRENT_DATE AND total_quantity_pcs > 0;",
            new { CurrentPharmacyNo, CurrentBranchNo });

        var expiryDays = await conn.ExecuteScalarAsync<int?>(@"
            SELECT expired_notification_days FROM cfg_product_configs
            WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
            new { CurrentPharmacyNo, CurrentBranchNo }) ?? 90;

        var nearExpiryThreshold = DateTime.UtcNow.AddDays(expiryDays > 0 ? expiryDays : 90).Date;
        var nearExpiryCount = await conn.ExecuteScalarAsync<int>(@"
            SELECT COUNT(1) FROM prod_product_batches
            WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
              AND expiry_date >= CURRENT_DATE AND expiry_date <= @NearExpiryThreshold
              AND total_quantity_pcs > 0;",
            new { CurrentPharmacyNo, CurrentBranchNo, NearExpiryThreshold = nearExpiryThreshold });

        var bankBalance = await conn.ExecuteScalarAsync<decimal?>(@"
            SELECT COALESCE(SUM(current_balance), 0)
            FROM acc_transaction_accounts
            WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
              AND account_type = 2 AND is_active = TRUE;",
            new { CurrentPharmacyNo, CurrentBranchNo }) ?? 0m;

        var mfsBalance = await conn.ExecuteScalarAsync<decimal?>(@"
            SELECT COALESCE(SUM(current_balance), 0)
            FROM acc_transaction_accounts
            WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
              AND account_type IN (4, 5) AND is_active = TRUE;",
            new { CurrentPharmacyNo, CurrentBranchNo }) ?? 0m;

        var todaySaleReturns = await conn.ExecuteScalarAsync<decimal?>(@"
            SELECT COALESCE(SUM(final_refund_price), 0)
            FROM sale_returns
            WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
              AND DATE(return_date) = @Today;",
            new { CurrentPharmacyNo, CurrentBranchNo, Today = today }) ?? 0m;

        var todayPurchaseReturns = await conn.ExecuteScalarAsync<decimal?>(@"
            SELECT COALESCE(SUM(gross_return_amount), 0)
            FROM pur_purchase_returns
            WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
              AND DATE(return_date) = @Today;",
            new { CurrentPharmacyNo, CurrentBranchNo, Today = today }) ?? 0m;

        var todayDueCollected = await conn.ExecuteScalarAsync<decimal?>(@"
            SELECT COALESCE(SUM(amount), 0)
            FROM due_collections
            WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
              AND DATE(payment_date) = @Today;",
            new { CurrentPharmacyNo, CurrentBranchNo, Today = today }) ?? 0m;

        var todaySupplierPaid = await conn.ExecuteScalarAsync<decimal?>(@"
            SELECT COALESCE(SUM(amount), 0)
            FROM pur_supplier_payments
            WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
              AND DATE(payment_date) = @Today;",
            new { CurrentPharmacyNo, CurrentBranchNo, Today = today }) ?? 0m;

        var totalCashBalance = await conn.ExecuteScalarAsync<decimal?>(@"
            SELECT COALESCE(SUM(current_balance), 0)
            FROM acc_transaction_accounts
            WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
              AND account_type = 1 AND is_active = TRUE;",
            new { CurrentPharmacyNo, CurrentBranchNo }) ?? salesStats.CashCollected;

        var todayIncomes = await conn.ExecuteScalarAsync<decimal?>(@"
            SELECT COALESCE(SUM(amount), 0)
            FROM acc_incomes
            WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
              AND DATE(income_date) = @Today;",
            new { CurrentPharmacyNo, CurrentBranchNo, Today = today }) ?? 0m;

        var todayDemurrage = await conn.ExecuteScalarAsync<decimal?>(@"
            SELECT COALESCE(SUM(e.amount), 0)
            FROM acc_expenses e
            JOIN acc_expense_categories c ON e.expense_category_no = c.expense_category_no
            WHERE e.pharmacy_no = @CurrentPharmacyNo AND e.branch_no = @CurrentBranchNo
              AND DATE(e.expense_date) = @Today
              AND (c.name ILIKE '%demurrage%' OR c.name ILIKE '%damage%' OR c.name ILIKE '%loss%');",
            new { CurrentPharmacyNo, CurrentBranchNo, Today = today }) ?? 0m;

        var pendingHandoverStats = await conn.QuerySingleOrDefaultAsync<(int Count, decimal Amount)>(@"
            SELECT COUNT(1) AS Count,
                   COALESCE(SUM(customer_will_pay - change_amount), 0) AS Amount
            FROM sale_invoices
            WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
              AND cash_received_status = 1 AND document_status = 2;",
            new { CurrentPharmacyNo, CurrentBranchNo });

        var summary = new ExecutiveDashboardSummary
        {
            TodaySales = salesStats.TotalSales,
            TodaySalesCount = salesStats.SalesCount,
            TodayProfit = salesStats.TotalProfit,
            TodayPurchases = purchasesToday,
            TodayExpenses = expensesToday,
            TodayCashCollected = salesStats.CashCollected,
            TotalCustomerDue = totalCustomerDue,
            TotalSupplierPayable = totalSupplierPayable,
            TotalInventoryValuation = totalInventoryValuation,
            TotalProductsCount = totalProducts,
            LowStockItemsCount = lowStockCount,
            ExpiredItemsCount = expiredCount,
            NearExpiryItemsCount = nearExpiryCount,
            TotalBankBalance = bankBalance,
            TotalMfsBalance = mfsBalance,
            TotalCashBalance = totalCashBalance,
            TodayIncomes = todayIncomes,
            TodayDemurrage = todayDemurrage,
            PendingHandoverCount = pendingHandoverStats.Count,
            PendingHandoverAmount = pendingHandoverStats.Amount,
            TodaySaleReturns = todaySaleReturns,
            TodayPurchaseReturns = todayPurchaseReturns,
            TodayDueCollected = todayDueCollected,
            TodaySupplierPaid = todaySupplierPaid
        };

        return OkResponse(summary);
    }

    [HttpGet("analytics")]
    public async Task<ActionResult<ApiResponse<List<FastMovingProduct>>>> GetFastMovers(
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        [FromQuery] int limit = 20,
        [FromQuery] string sort = "desc",
        [FromQuery] string? sortBy = "qty")
    {
        using var conn = _db.CreateConnection();
        var orderDir = sort.Equals("asc", StringComparison.OrdinalIgnoreCase) ? "ASC" : "DESC";
        var orderCol = (sortBy?.ToLower()) switch
        {
            "revenue" => "total_revenue",
            "profit" => "total_profit",
            "margin" => "profit_margin_pct",
            _ => "total_sold_pcs"
        };

        var pms = new DynamicParameters();
        pms.Add("CurrentPharmacyNo", CurrentPharmacyNo);
        pms.Add("CurrentBranchNo", CurrentBranchNo);
        pms.Add("Limit", limit);

        var dateFilter = string.Empty;
        if (fromDate.HasValue)
        {
            dateFilter += " AND DATE(s.sale_timestamp) >= @FromDate";
            pms.Add("FromDate", fromDate.Value.Date);
        }
        if (toDate.HasValue)
        {
            dateFilter += " AND DATE(s.sale_timestamp) <= @ToDate";
            pms.Add("ToDate", toDate.Value.Date);
        }

        var sql = $@"
            SELECT i.product_no, p.product_name,
                   COALESCE(g.name, '') AS generic_name,
                   SUM(i.sale_qty) AS total_sold_pcs,
                   SUM(i.total_price) AS total_revenue,
                   SUM(i.total_price - (i.sale_qty * i.unit_cost_price)) AS total_profit,
                   CASE 
                     WHEN SUM(i.total_price) > 0 THEN 
                       ROUND((SUM(i.total_price - (i.sale_qty * i.unit_cost_price)) / SUM(i.total_price)) * 100, 2)
                     ELSE 0 
                   END AS profit_margin_pct
            FROM sale_invoice_items i
            JOIN sale_invoices s ON i.sale_invoice_no = s.sale_invoice_no
            JOIN prod_products p ON i.product_no = p.product_no
            LEFT JOIN prod_generics g ON p.generic_no = g.generic_no
            WHERE i.pharmacy_no = @CurrentPharmacyNo AND i.branch_no = @CurrentBranchNo
              AND s.document_status = 2 {dateFilter}
            GROUP BY i.product_no, p.product_name, g.name
            ORDER BY {orderCol} {orderDir}
            LIMIT @Limit;";

        var list = (await conn.QueryAsync<FastMovingProduct>(sql, pms)).ToList();
        return OkResponse(list);
    }

    [HttpGet("cashbook")]
    public async Task<ActionResult<ApiResponse<List<CashbookEntry>>>> GetCashbook(
        [FromQuery] DateTime? date,
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        [FromQuery] long? accountNo,
        [FromQuery] short? accountType)
    {
        using var conn = _db.CreateConnection();
        var pms = new DynamicParameters();
        pms.Add("CurrentPharmacyNo", CurrentPharmacyNo);
        pms.Add("CurrentBranchNo", CurrentBranchNo);

        var dateFilter = string.Empty;
        if (fromDate.HasValue && toDate.HasValue)
        {
            dateFilter = " AND DATE(l.transaction_date) >= @FromDate AND DATE(l.transaction_date) <= @ToDate";
            pms.Add("FromDate", fromDate.Value.Date);
            pms.Add("ToDate", toDate.Value.Date);
        }
        else if (fromDate.HasValue)
        {
            dateFilter = " AND DATE(l.transaction_date) >= @FromDate";
            pms.Add("FromDate", fromDate.Value.Date);
        }
        else if (toDate.HasValue)
        {
            dateFilter = " AND DATE(l.transaction_date) <= @ToDate";
            pms.Add("ToDate", toDate.Value.Date);
        }
        else if (date.HasValue)
        {
            dateFilter = " AND DATE(l.transaction_date) = @TargetDate";
            pms.Add("TargetDate", date.Value.Date);
        }
        else
        {
            // Default to today if nothing passed
            dateFilter = " AND DATE(l.transaction_date) = CURRENT_DATE";
        }

        var accFilter = string.Empty;
        if (accountNo.HasValue && accountNo.Value > 0)
        {
            accFilter = " AND l.account_no = @AccountNo";
            pms.Add("AccountNo", accountNo.Value);
        }
        else if (accountType.HasValue && accountType.Value > 0)
        {
            accFilter = " AND a.account_type = @AccountType";
            pms.Add("AccountType", accountType.Value);
        }
        else
        {
            // Show all cash & bank accounts (Cash, Bank, bKash, Nagad)
            accFilter = " AND (a.account_type IN (1, 2, 4, 5) OR a.account_code = '10101' OR a.account_category = 1)";
        }

        var sql = $@"
            SELECT l.transaction_date AS entry_date,
                   l.source_document_type AS doc_type,
                   l.source_document_no AS doc_no,
                   l.narration AS description,
                   l.debit_amount AS cash_in,
                   l.credit_amount AS cash_out,
                   l.balance_after AS balance,
                   l.account_no,
                   a.account_name,
                   a.account_type
            FROM acc_transaction_ledger l
            JOIN acc_transaction_accounts a ON l.account_no = a.account_no
            WHERE l.pharmacy_no = @CurrentPharmacyNo AND l.branch_no = @CurrentBranchNo
              {dateFilter}
              {accFilter}
            ORDER BY l.transaction_date ASC, l.ledger_no ASC;";

        var entries = (await conn.QueryAsync<CashbookEntry>(sql, pms)).ToList();
        return OkResponse(entries);
    }

    [HttpGet("trends")]
    public async Task<ActionResult<ApiResponse<List<DailySalesTrend>>>> GetTrends(
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate)
    {
        using var conn = _db.CreateConnection();
        var pms = new DynamicParameters();
        pms.Add("CurrentPharmacyNo", CurrentPharmacyNo);
        pms.Add("CurrentBranchNo", CurrentBranchNo);

        string seriesSql;
        if (fromDate.HasValue && toDate.HasValue)
        {
            seriesSql = "SELECT generate_series(@FromDate::date, @ToDate::date, '1 day'::interval)::date AS day";
            pms.Add("FromDate", fromDate.Value.Date);
            pms.Add("ToDate", toDate.Value.Date);
        }
        else
        {
            seriesSql = "SELECT generate_series(CURRENT_DATE - INTERVAL '6 days', CURRENT_DATE, '1 day'::interval)::date AS day";
        }

        var sql = $@"
            WITH days AS (
                {seriesSql}
            ),
            daily_sales AS (
                SELECT 
                    DATE(sale_timestamp) AS sale_date,
                    COALESCE(SUM(final_price), 0) AS total_sale,
                    COALESCE(SUM(approximate_profit), 0) AS gross_profit,
                    COALESCE(SUM(final_price - approximate_profit), 0) AS buy_price
                FROM sale_invoices
                WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
                  AND document_status = 2
                  AND DATE(sale_timestamp) IN (SELECT day FROM days)
                GROUP BY DATE(sale_timestamp)
            ),
            daily_expenses AS (
                SELECT 
                    DATE(expense_date) AS exp_date,
                    COALESCE(SUM(amount), 0) AS op_cost
                FROM acc_expenses
                WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
                  AND DATE(expense_date) IN (SELECT day FROM days)
                GROUP BY DATE(expense_date)
            )
            SELECT 
                TO_CHAR(d.day, 'Mon DD') AS date_label,
                d.day AS date_val,
                COALESCE(s.buy_price, 0) AS buy_price,
                COALESCE(s.gross_profit, 0) AS gross_profit,
                COALESCE(s.total_sale, 0) AS total_sale,
                COALESCE(e.op_cost, 0) AS op_cost,
                GREATEST(0, COALESCE(s.gross_profit, 0) - COALESCE(e.op_cost, 0)) AS net_profit
            FROM days d
            LEFT JOIN daily_sales s ON s.sale_date = d.day
            LEFT JOIN daily_expenses e ON e.exp_date = d.day
            ORDER BY d.day ASC;";

        var list = (await conn.QueryAsync<DailySalesTrend>(sql, pms)).ToList();
        return OkResponse(list);
    }

    [HttpGet("report-summary")]
    public async Task<ActionResult<ApiResponse<FinancialReportSummary>>> GetReportSummary(
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate)
    {
        using var conn = _db.CreateConnection();
        var fDate = (fromDate ?? DateTime.UtcNow).Date;
        var tDate = (toDate ?? DateTime.UtcNow).Date;

        var pms = new { CurrentPharmacyNo, CurrentBranchNo, FromDate = fDate, ToDate = tDate };

        var salesStats = await conn.QuerySingleOrDefaultAsync<(decimal TotalSales, int SalesCount, decimal TotalProfit, decimal CashCollected)>(@"
            SELECT COALESCE(SUM(final_price), 0) AS TotalSales,
                   COUNT(1) AS SalesCount,
                   COALESCE(SUM(approximate_profit), 0) AS TotalProfit,
                   COALESCE(SUM(CASE WHEN payment_method = 1 THEN customer_will_pay - change_amount ELSE 0 END), 0) AS CashCollected
            FROM sale_invoices
            WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
              AND DATE(sale_timestamp) >= @FromDate AND DATE(sale_timestamp) <= @ToDate
              AND document_status = 2;",
            pms);

        var purchasesTotal = await conn.ExecuteScalarAsync<decimal?>(@"
            SELECT COALESCE(SUM(final_price), 0)
            FROM pur_purchase_invoices
            WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
              AND DATE(purchase_date) >= @FromDate AND DATE(purchase_date) <= @ToDate
              AND document_status = 2;",
            pms) ?? 0m;

        var expensesTotal = await conn.ExecuteScalarAsync<decimal?>(@"
            SELECT COALESCE(SUM(amount), 0)
            FROM acc_expenses
            WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
              AND DATE(expense_date) >= @FromDate AND DATE(expense_date) <= @ToDate;",
            pms) ?? 0m;

        var incomesTotal = await conn.ExecuteScalarAsync<decimal?>(@"
            SELECT COALESCE(SUM(amount), 0)
            FROM acc_incomes
            WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
              AND DATE(income_date) >= @FromDate AND DATE(income_date) <= @ToDate;",
            pms) ?? 0m;

        var dueCollected = await conn.ExecuteScalarAsync<decimal?>(@"
            SELECT COALESCE(SUM(amount), 0)
            FROM due_collections
            WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
              AND DATE(payment_date) >= @FromDate AND DATE(payment_date) <= @ToDate;",
            pms) ?? 0m;

        var supplierPaid = await conn.ExecuteScalarAsync<decimal?>(@"
            SELECT COALESCE(SUM(amount), 0)
            FROM pur_supplier_payments
            WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
              AND DATE(payment_date) >= @FromDate AND DATE(payment_date) <= @ToDate;",
            pms) ?? 0m;

        var saleReturns = await conn.ExecuteScalarAsync<decimal?>(@"
            SELECT COALESCE(SUM(final_refund_price), 0)
            FROM sale_returns
            WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
              AND DATE(return_date) >= @FromDate AND DATE(return_date) <= @ToDate;",
            pms) ?? 0m;

        var purchaseReturns = await conn.ExecuteScalarAsync<decimal?>(@"
            SELECT COALESCE(SUM(gross_return_amount), 0)
            FROM pur_purchase_returns
            WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
              AND DATE(return_date) >= @FromDate AND DATE(return_date) <= @ToDate;",
            pms) ?? 0m;

        var netProfit = salesStats.TotalProfit + incomesTotal - expensesTotal;
        var netCashFlow = (salesStats.CashCollected + dueCollected + incomesTotal) - (supplierPaid + expensesTotal + saleReturns);

        var report = new FinancialReportSummary
        {
            FromDate = fDate,
            ToDate = tDate,
            TotalSales = salesStats.TotalSales,
            TotalSalesCount = salesStats.SalesCount,
            TotalProfit = salesStats.TotalProfit,
            TotalPurchases = purchasesTotal,
            TotalExpenses = expensesTotal,
            TotalIncomes = incomesTotal,
            CashCollected = salesStats.CashCollected,
            DueCollected = dueCollected,
            SupplierPaid = supplierPaid,
            SaleReturns = saleReturns,
            PurchaseReturns = purchaseReturns,
            NetProfit = netProfit,
            NetCashFlow = netCashFlow
        };

        return OkResponse(report);
    }
}

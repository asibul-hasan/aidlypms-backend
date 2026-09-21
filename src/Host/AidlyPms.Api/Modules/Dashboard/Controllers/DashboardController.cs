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

        var nearExpiryThreshold = DateTime.UtcNow.AddDays(90).Date;
        var nearExpiryCount = await conn.ExecuteScalarAsync<int>(@"
            SELECT COUNT(1) FROM prod_product_batches
            WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
              AND expiry_date >= CURRENT_DATE AND expiry_date <= @NearExpiryThreshold
              AND total_quantity_pcs > 0;",
            new { CurrentPharmacyNo, CurrentBranchNo, NearExpiryThreshold = nearExpiryThreshold });

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
            NearExpiryItemsCount = nearExpiryCount
        };

        return OkResponse(summary);
    }

    [HttpGet("analytics")]
    public async Task<ActionResult<ApiResponse<List<FastMovingProduct>>>> GetFastMovers([FromQuery] int limit = 10)
    {
        using var conn = _db.CreateConnection();
        var sql = @"
            SELECT i.product_no, p.product_name,
                   SUM(i.sale_qty) AS total_sold_pcs,
                   SUM(i.total_price) AS total_revenue
            FROM sale_invoice_items i
            JOIN prod_products p ON i.product_no = p.product_no
            WHERE i.pharmacy_no = @CurrentPharmacyNo AND i.branch_no = @CurrentBranchNo
            GROUP BY i.product_no, p.product_name
            ORDER BY total_sold_pcs DESC
            LIMIT @Limit;";

        var list = (await conn.QueryAsync<FastMovingProduct>(sql, new { CurrentPharmacyNo, CurrentBranchNo, Limit = limit })).ToList();
        return OkResponse(list);
    }

    [HttpGet("cashbook")]
    public async Task<ActionResult<ApiResponse<List<CashbookEntry>>>> GetCashbook([FromQuery] DateTime? date)
    {
        using var conn = _db.CreateConnection();
        var targetDate = (date ?? DateTime.UtcNow).Date;

        var sql = @"
            SELECT l.transaction_date AS entry_date,
                   l.source_document_type AS doc_type,
                   l.source_document_no AS doc_no,
                   l.narration AS description,
                   l.debit_amount AS cash_in,
                   l.credit_amount AS cash_out,
                   l.balance_after AS balance
            FROM acc_transaction_ledger l
            JOIN acc_transaction_accounts a ON l.account_no = a.account_no
            WHERE l.pharmacy_no = @CurrentPharmacyNo AND l.branch_no = @CurrentBranchNo
              AND a.account_code = '10101'
              AND DATE(l.transaction_date) = @TargetDate
            ORDER BY l.ledger_no ASC;";

        var entries = (await conn.QueryAsync<CashbookEntry>(sql, new { CurrentPharmacyNo, CurrentBranchNo, TargetDate = targetDate })).ToList();
        return OkResponse(entries);
    }
}

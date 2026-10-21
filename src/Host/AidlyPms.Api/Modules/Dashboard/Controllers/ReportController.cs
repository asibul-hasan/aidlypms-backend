using AidlyPms.Api.Common;
using AidlyPms.Api.Common.Database;
using AidlyPms.Api.Modules.Dashboard.Models;
using Dapper;
using Microsoft.AspNetCore.Mvc;

namespace AidlyPms.Api.Modules.Dashboard.Controllers;

[Route("api/v1/reports")]
public class ReportController : BaseController
{
    private readonly IDbConnectionFactory _db;

    public ReportController(IDbConnectionFactory db)
    {
        _db = db;
    }

    /// <summary>
    /// Categorizes stock batches into expiry risk buckets: Expired, Critical (0-30d), Moderate (31-60d), Approaching (61-90d).
    /// </summary>
    [HttpGet("expiry-buckets")]
    public async Task<ActionResult<ApiResponse<ExpiryBucketReport>>> GetExpiryBuckets()
    {
        using var conn = _db.CreateConnection();

        const string sql = @"
            SELECT
                p.product_no,
                p.product_name,
                g.name AS generic_name,
                c.name AS company_name,
                b.batch_no,
                b.batch_number,
                b.expiry_date,
                (b.expiry_date::DATE - CURRENT_DATE) AS days_until_expiry,
                b.total_quantity_pcs AS quantity_pcs,
                COALESCE(p.purchase_price_per_piece, 0) AS unit_cost_price,
                COALESCE(p.sale_price_per_piece, 0) AS unit_sale_price,
                (b.total_quantity_pcs * COALESCE(p.purchase_price_per_piece, 0)) AS total_cost_value,
                (b.total_quantity_pcs * COALESCE(p.sale_price_per_piece, 0)) AS total_mrp_value,
                p.rak_number,
                CASE
                    WHEN b.expiry_date < CURRENT_DATE THEN 'Expired'
                    WHEN b.expiry_date <= CURRENT_DATE + INTERVAL '30 days' THEN '0-30 Days'
                    WHEN b.expiry_date <= CURRENT_DATE + INTERVAL '60 days' THEN '31-60 Days'
                    ELSE '61-90 Days'
                END AS bucket
            FROM prod_product_batches b
            JOIN prod_products p ON b.product_no = p.product_no
            LEFT JOIN prod_generics g ON p.generic_no = g.generic_no
            LEFT JOIN prod_companies c ON p.company_no = c.company_no
            WHERE b.pharmacy_no = @CurrentPharmacyNo
              AND b.branch_no = @CurrentBranchNo
              AND b.total_quantity_pcs > 0
              AND b.expiry_date <= CURRENT_DATE + INTERVAL '90 days'
            ORDER BY b.expiry_date ASC;";

        var rows = (await conn.QueryAsync<ExpiryBatchItem>(sql, new { CurrentPharmacyNo, CurrentBranchNo })).ToList();

        var report = new ExpiryBucketReport
        {
            Items = rows,
            Summary = new ExpiryBucketSummary
            {
                ExpiredCount = rows.Count(r => r.Bucket == "Expired"),
                ExpiredCostValue = rows.Where(r => r.Bucket == "Expired").Sum(r => r.TotalCostValue),

                Critical30Count = rows.Count(r => r.Bucket == "0-30 Days"),
                Critical30CostValue = rows.Where(r => r.Bucket == "0-30 Days").Sum(r => r.TotalCostValue),

                Moderate60Count = rows.Count(r => r.Bucket == "31-60 Days"),
                Moderate60CostValue = rows.Where(r => r.Bucket == "31-60 Days").Sum(r => r.TotalCostValue),

                Approaching90Count = rows.Count(r => r.Bucket == "61-90 Days"),
                Approaching90CostValue = rows.Where(r => r.Bucket == "61-90 Days").Sum(r => r.TotalCostValue),

                TotalAtRiskCount = rows.Count,
                TotalAtRiskCostValue = rows.Sum(r => r.TotalCostValue)
            }
        };

        return OkResponse(report);
    }

    /// <summary>
    /// Computes Pareto ABC revenue classification and FSN velocity movement analysis for catalog inventory.
    /// </summary>
    [HttpGet("abc-fsn")]
    public async Task<ActionResult<ApiResponse<List<AbcFsnItem>>>> GetAbcFsnAnalysis([FromQuery] int days = 90)
    {
        using var conn = _db.CreateConnection();
        var cutoffDays = Math.Clamp(days, 30, 365);

        const string sql = @"
            WITH ProductSales AS (
                SELECT
                    sii.product_no,
                    COALESCE(SUM(sii.sale_qty), 0) AS units_sold,
                    COALESCE(SUM(sii.total_price), 0) AS total_revenue,
                    COUNT(DISTINCT sii.sale_invoice_no) AS movement_frequency
                FROM sale_invoice_items sii
                JOIN sale_invoices si ON sii.sale_invoice_no = si.sale_invoice_no
                WHERE si.pharmacy_no = @CurrentPharmacyNo
                  AND si.branch_no = @CurrentBranchNo
                  AND si.sale_timestamp >= CURRENT_DATE - (@CutoffDays || ' days')::INTERVAL
                  AND si.document_status = 2
                GROUP BY sii.product_no
            ),
            ProductReturns AS (
                SELECT
                    sri.product_no,
                    COALESCE(SUM(sri.total_qty_pcs), 0) AS units_returned,
                    COALESCE(SUM(sri.total_price), 0) AS revenue_returned
                FROM sale_return_items sri
                JOIN sale_returns sr ON sri.sale_return_no = sr.sale_return_no
                WHERE sr.pharmacy_no = @CurrentPharmacyNo
                  AND sr.branch_no = @CurrentBranchNo
                  AND sr.return_date >= CURRENT_DATE - (@CutoffDays || ' days')::INTERVAL
                  AND sr.return_status = 2
                GROUP BY sri.product_no
            ),
            CurrentStock AS (
                SELECT product_no, COALESCE(SUM(total_quantity_pcs), 0) AS current_stock
                FROM prod_product_batches
                WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
                GROUP BY product_no
            )
            SELECT
                p.product_no,
                p.product_name,
                g.name AS generic_name,
                c.name AS company_name,
                COALESCE(cs.current_stock, 0) AS current_stock_pcs,
                GREATEST(0, COALESCE(ps.units_sold, 0) - COALESCE(pr.units_returned, 0)) AS units_sold,
                GREATEST(0, COALESCE(ps.total_revenue, 0) - COALESCE(pr.revenue_returned, 0)) AS total_revenue,
                COALESCE(ps.movement_frequency, 0) AS movement_frequency
            FROM prod_products p
            LEFT JOIN prod_generics g ON p.generic_no = g.generic_no
            LEFT JOIN prod_companies c ON p.company_no = c.company_no
            LEFT JOIN ProductSales ps ON p.product_no = ps.product_no
            LEFT JOIN ProductReturns pr ON p.product_no = pr.product_no
            LEFT JOIN CurrentStock cs ON p.product_no = cs.product_no
            WHERE p.pharmacy_no = @CurrentPharmacyNo
              AND p.branch_no = @CurrentBranchNo
              AND p.is_active = TRUE
            ORDER BY total_revenue DESC, units_sold DESC;";

        var rows = (await conn.QueryAsync<AbcFsnItem>(sql, new { CurrentPharmacyNo, CurrentBranchNo, CutoffDays = cutoffDays })).ToList();

        var grandTotalRevenue = rows.Sum(r => r.TotalRevenue);
        decimal runningTotal = 0;

        foreach (var item in rows)
        {
            runningTotal += item.TotalRevenue;
            var cumPct = grandTotalRevenue > 0 ? (runningTotal / grandTotalRevenue) * 100m : 100m;
            item.CumulativeRevenuePercent = Math.Round(cumPct, 2);

            // ABC Analysis: 80% A, next 15% B, rest C
            if (cumPct <= 80m || (item.TotalRevenue > 0 && cumPct - ((item.TotalRevenue / grandTotalRevenue) * 100m) < 80m))
            {
                item.AbcClassification = "A";
            }
            else if (cumPct <= 95m)
            {
                item.AbcClassification = "B";
            }
            else
            {
                item.AbcClassification = "C";
            }

            // FSN Velocity (Fast, Slow, Non-moving) based on movement frequency
            if (item.MovementFrequency >= 10 || item.UnitsSold >= 50)
            {
                item.FsnVelocity = "Fast-Moving";
            }
            else if (item.MovementFrequency > 0 || item.UnitsSold > 0)
            {
                item.FsnVelocity = "Slow-Moving";
            }
            else
            {
                item.FsnVelocity = "Non-Moving";
            }

            // Turnover Rate (Sold / Average Stock)
            item.TurnoverRate = item.CurrentStockPcs > 0
                ? Math.Round((decimal)item.UnitsSold / item.CurrentStockPcs, 2)
                : (item.UnitsSold > 0 ? item.UnitsSold : 0m);
        }

        return OkResponse(rows);
    }

    /// <summary>
    /// Analyzes Trade Price (TP) cost vs Maximum Retail Price (MRP) selling price and realized gross margins.
    /// </summary>
    [HttpGet("margin-analysis")]
    public async Task<ActionResult<ApiResponse<List<ProductMarginItem>>>> GetMarginAnalysis()
    {
        using var conn = _db.CreateConnection();

        const string sql = @"
            SELECT
                p.product_no,
                p.product_name,
                g.name AS generic_name,
                c.name AS company_name,
                COALESCE(p.purchase_price_per_piece, 0) AS trade_price_cost,
                COALESCE(p.sale_price_per_piece, 0) AS mrp_sale_price,
                (COALESCE(p.sale_price_per_piece, 0) - COALESCE(p.purchase_price_per_piece, 0)) AS profit_per_unit,
                CASE
                    WHEN COALESCE(p.sale_price_per_piece, 0) > 0 THEN
                        ROUND(((COALESCE(p.sale_price_per_piece, 0) - COALESCE(p.purchase_price_per_piece, 0)) / p.sale_price_per_piece) * 100, 2)
                    ELSE 0
                END AS margin_percent,
                GREATEST(0, COALESCE(sales.total_qty, 0) - COALESCE(returns.returned_qty, 0)) AS total_units_sold,
                GREATEST(0, COALESCE(sales.realized_profit, 0) - COALESCE(returns.returned_profit, 0)) AS total_realized_profit
            FROM prod_products p
            LEFT JOIN prod_generics g ON p.generic_no = g.generic_no
            LEFT JOIN prod_companies c ON p.company_no = c.company_no
            LEFT JOIN (
                SELECT
                    sii.product_no,
                    SUM(sii.sale_qty) AS total_qty,
                    SUM(sii.total_price - (sii.unit_cost_price * sii.sale_qty)) AS realized_profit
                FROM sale_invoice_items sii
                JOIN sale_invoices si ON sii.sale_invoice_no = si.sale_invoice_no
                WHERE si.pharmacy_no = @CurrentPharmacyNo
                  AND si.branch_no = @CurrentBranchNo
                  AND si.document_status = 2
                GROUP BY sii.product_no
            ) sales ON p.product_no = sales.product_no
            LEFT JOIN (
                SELECT
                    sri.product_no,
                    SUM(sri.total_qty_pcs) AS returned_qty,
                    SUM(sri.total_price - (COALESCE(sii.unit_cost_price, 0) * sri.total_qty_pcs)) AS returned_profit
                FROM sale_return_items sri
                JOIN sale_returns sr ON sri.sale_return_no = sr.sale_return_no
                LEFT JOIN sale_invoice_items sii ON sri.sale_invoice_item_no = sii.sale_invoice_item_no
                WHERE sr.pharmacy_no = @CurrentPharmacyNo
                  AND sr.branch_no = @CurrentBranchNo
                  AND sr.return_status = 2
                GROUP BY sri.product_no
            ) returns ON p.product_no = returns.product_no
            WHERE p.pharmacy_no = @CurrentPharmacyNo
              AND p.branch_no = @CurrentBranchNo
              AND p.is_active = TRUE
            ORDER BY margin_percent DESC, total_realized_profit DESC;";

        var rows = (await conn.QueryAsync<ProductMarginItem>(sql, new { CurrentPharmacyNo, CurrentBranchNo })).ToList();
        return OkResponse(rows);
    }

    /// <summary>
    /// Gets detailed audit breakdown of all customer returns and exchanges for a given Dhaka date.
    /// </summary>
    [HttpGet("daily-returns")]
    public async Task<ActionResult<ApiResponse<DailyReturnsReport>>> GetDailyReturns([FromQuery] string? date)
    {
        using var conn = _db.CreateConnection();
        var dhakaDate = DateHelper.GetDhakaToday();
        if (!string.IsNullOrWhiteSpace(date) && DateHelper.TryParse(date, out var parsed))
        {
            dhakaDate = DateOnly.FromDateTime(parsed);
        }

        var (startUtc, endUtc) = DateHelper.GetDhakaDayUtcRange(dhakaDate);

        const string sql = @"
            SELECT
                r.sale_return_no,
                r.return_invoice_no AS return_number,
                r.sale_invoice_no,
                si.sales_number,
                COALESCE(c.first_name || ' ' || c.last_name, 'Walking Customer') AS customer_name,
                r.return_date,
                r.total_items,
                r.final_refund_price,
                r.cash_paid_refund,
                r.refund_method,
                r.return_status,
                r.exchange_invoice_no,
                ex.sales_number AS exchange_sales_number,
                COALESCE(u.first_name || ' ' || u.last_name, 'Admin') AS created_by_user_name
            FROM sale_returns r
            INNER JOIN sale_invoices si ON r.sale_invoice_no = si.sale_invoice_no
            LEFT JOIN cust_customers c ON r.customer_no = c.customer_no
            LEFT JOIN sale_invoices ex ON r.exchange_invoice_no = ex.sale_invoice_no
            LEFT JOIN sys_users u ON r.created_by_user_no = u.user_no
            WHERE r.pharmacy_no = @CurrentPharmacyNo
              AND r.branch_no = @CurrentBranchNo
              AND r.return_date >= @startUtc AND r.return_date <= @endUtc
            ORDER BY r.sale_return_no DESC;";

        var items = (await conn.QueryAsync<DailyReturnItem>(sql, new { CurrentPharmacyNo, CurrentBranchNo, startUtc, endUtc })).ToList();

        var completed = items.Where(x => x.ReturnStatus == 2).ToList();

        var report = new DailyReturnsReport
        {
            Date = dhakaDate.ToString("yyyy-MM-dd"),
            TotalReturnsCount = items.Count,
            TotalRefundAmount = completed.Sum(x => x.FinalRefundPrice),
            TotalCashRefund = completed.Sum(x => x.CashPaidRefund),
            Items = items
        };

        return OkResponse(report);
    }
}

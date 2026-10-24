using AidlyPms.Api.Common.Database;
using AidlyPms.Api.Modules.Products.Models;
using Dapper;
using Npgsql;

namespace AidlyPms.Api.Infrastructure;

public interface IStockIntegrityService
{
    Task<StockIntegrityResult> CheckIntegrityAsync(NpgsqlConnection conn, long pharmacyNo, long branchNo);
}

public class StockIntegrityService : IStockIntegrityService
{
    public async Task<StockIntegrityResult> CheckIntegrityAsync(NpgsqlConnection conn, long pharmacyNo, long branchNo)
    {
        const string sql = @"
            SELECT
                b.batch_no,
                b.batch_number,
                b.product_no,
                p.product_name,
                b.total_quantity_pcs AS batch_quantity_pcs,
                COALESCE(m.sum_qty, 0)::int AS ledger_sum_pcs,
                b.is_placeholder,
                CASE
                    WHEN b.is_placeholder = TRUE AND b.total_quantity_pcs > 0 THEN 'POSITIVE_PLACEHOLDER'
                    WHEN b.is_placeholder = FALSE AND b.total_quantity_pcs < 0 THEN 'NEGATIVE_REAL_STOCK'
                    WHEN m.movement_count IS NULL OR m.movement_count = 0 THEN 'MISSING_LEDGER'
                    WHEN b.total_quantity_pcs != COALESCE(m.sum_qty, 0) THEN 'LEDGER_MISMATCH'
                    ELSE 'CLEAN'
                END AS issue_type,
                CASE
                    WHEN b.is_placeholder = TRUE AND b.total_quantity_pcs > 0 THEN 'Placeholder batch has positive stock quantity.'
                    WHEN b.is_placeholder = FALSE AND b.total_quantity_pcs < 0 THEN 'Real batch has negative stock quantity.'
                    WHEN m.movement_count IS NULL OR m.movement_count = 0 THEN 'Batch exists with no ledger movement rows.'
                    WHEN b.total_quantity_pcs != COALESCE(m.sum_qty, 0) THEN 'Batch stock deviates from physical movement ledger sum.'
                    ELSE 'Clean'
                END AS description
            FROM prod_product_batches b
            JOIN prod_products p ON b.product_no = p.product_no
            LEFT JOIN (
                SELECT
                    batch_no,
                    SUM(qty_pcs) AS sum_qty,
                    COUNT(1) AS movement_count
                FROM inv_stock_movement
                WHERE pharmacy_no = @pharmacyNo AND branch_no = @branchNo
                GROUP BY batch_no
            ) m ON b.batch_no = m.batch_no
            WHERE b.pharmacy_no = @pharmacyNo AND b.branch_no = @branchNo
              AND (
                (b.total_quantity_pcs != COALESCE(m.sum_qty, 0)) OR
                (b.is_placeholder = FALSE AND b.total_quantity_pcs < 0) OR
                (b.is_placeholder = TRUE AND b.total_quantity_pcs > 0) OR
                (m.movement_count IS NULL OR m.movement_count = 0)
              )
            ORDER BY b.batch_no DESC;";

        var discrepancies = (await conn.QueryAsync<StockIntegrityDiscrepancyItem>(
            sql, new { pharmacyNo, branchNo })).ToList();

        var result = new StockIntegrityResult
        {
            IsClean = discrepancies.Count == 0,
            TotalDiscrepancies = discrepancies.Count,
            DiscrepantLedgerCount = discrepancies.Count(d => d.IssueType == "LEDGER_MISMATCH"),
            NegativeRealBatchesCount = discrepancies.Count(d => d.IssueType == "NEGATIVE_REAL_STOCK"),
            PositivePlaceholderCount = discrepancies.Count(d => d.IssueType == "POSITIVE_PLACEHOLDER"),
            MissingLedgerBatchesCount = discrepancies.Count(d => d.IssueType == "MISSING_LEDGER"),
            OffendingBatches = discrepancies.Take(50).ToList()
        };

        return result;
    }
}

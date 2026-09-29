using AidlyPms.Api.Modules.Products.Models;
using Dapper;
using Npgsql;

namespace AidlyPms.Api.Infrastructure;

public class StockMovementEntry
{
    public long PharmacyNo { get; set; }
    public long BranchNo { get; set; }
    public long ProductNo { get; set; }
    public long? BatchNo { get; set; }
    public short MovementType { get; set; }
    public string ReferenceDocType { get; set; } = string.Empty;
    public string ReferenceDocNo { get; set; } = string.Empty;
    public long ReferenceDocId { get; set; }
    public int QtyPcs { get; set; } // Positive for IN, Negative for OUT
    public decimal UnitCost { get; set; }
    public string? Remarks { get; set; }
}

public interface IStockPostingService
{
    Task PostMovementAsync(NpgsqlConnection conn, NpgsqlTransaction tran, StockMovementEntry entry);
}

public class StockPostingService : IStockPostingService
{
    public async Task PostMovementAsync(NpgsqlConnection conn, NpgsqlTransaction tran, StockMovementEntry entry)
    {
        if (entry.QtyPcs == 0) return;

        // If batch is not specified, resolve the active or latest batch for this product
        if (!entry.BatchNo.HasValue || entry.BatchNo.Value <= 0)
        {
            var fallbackBatchNo = await conn.ExecuteScalarAsync<long?>(@"
                SELECT batch_no
                FROM prod_product_batches
                WHERE product_no = @ProductNo AND pharmacy_no = @PharmacyNo AND branch_no = @BranchNo
                ORDER BY (total_quantity_pcs > 0) DESC, expiry_date ASC, batch_no ASC
                LIMIT 1
                FOR UPDATE;",
                new { entry.ProductNo, entry.PharmacyNo, entry.BranchNo },
                transaction: tran);

            if (fallbackBatchNo.HasValue && fallbackBatchNo.Value > 0)
            {
                entry.BatchNo = fallbackBatchNo.Value;
            }
        }

        // If batch is specified, update batch stock
        if (entry.BatchNo.HasValue && entry.BatchNo.Value > 0)
        {
            var batch = await conn.QuerySingleOrDefaultAsync<ProdProductBatch>(@"
                SELECT batch_no, total_quantity_pcs, quantity_in_box
                FROM prod_product_batches
                WHERE batch_no = @BatchNo AND pharmacy_no = @PharmacyNo AND branch_no = @BranchNo
                FOR UPDATE;",
                new { entry.BatchNo, entry.PharmacyNo, entry.BranchNo },
                transaction: tran);

            if (batch != null)
            {
                var newBatchQty = batch.TotalQuantityPcs + entry.QtyPcs;
                var qInBox = batch.QuantityInBox > 0 ? batch.QuantityInBox : 1;
                var newBoxQty = newBatchQty / qInBox;

                await conn.ExecuteAsync(@"
                    UPDATE prod_product_batches
                    SET total_quantity_pcs = @newBatchQty,
                        box_quantity = @newBoxQty,
                        updated_at = NOW()
                    WHERE batch_no = @BatchNo;",
                    new { newBatchQty, newBoxQty, entry.BatchNo },
                    transaction: tran);
            }
        }

        // Calculate total stock remaining across all batches for product
        var currentTotalStock = await conn.ExecuteScalarAsync<int?>(@"
            SELECT COALESCE(SUM(total_quantity_pcs), 0)
            FROM prod_product_batches
            WHERE product_no = @ProductNo AND pharmacy_no = @PharmacyNo AND branch_no = @BranchNo;",
            new { entry.ProductNo, entry.PharmacyNo, entry.BranchNo },
            transaction: tran) ?? 0;

        // Generate movement_no
        var movementNo = await conn.ExecuteScalarAsync<long>(
            "SELECT COALESCE(MAX(movement_no), 0) + 1 FROM inv_stock_movement;", transaction: tran);

        const string insertSql = @"
            INSERT INTO inv_stock_movement (
                movement_no, pharmacy_no, branch_no, product_no, batch_no,
                movement_type, reference_doc_type, reference_doc_no, reference_doc_id,
                qty_pcs, unit_cost, balance_after_pcs, remarks, created_at
            ) VALUES (
                @movementNo, @PharmacyNo, @BranchNo, @ProductNo, @BatchNo,
                @MovementType, @ReferenceDocType, @ReferenceDocNo, @ReferenceDocId,
                @QtyPcs, @UnitCost, @currentTotalStock, @Remarks, CURRENT_TIMESTAMP
            );";

        await conn.ExecuteAsync(insertSql, new
        {
            movementNo,
            entry.PharmacyNo,
            entry.BranchNo,
            entry.ProductNo,
            entry.BatchNo,
            entry.MovementType,
            entry.ReferenceDocType,
            entry.ReferenceDocNo,
            entry.ReferenceDocId,
            entry.QtyPcs,
            entry.UnitCost,
            currentTotalStock,
            entry.Remarks
        }, transaction: tran);
    }
}

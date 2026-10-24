using AidlyPms.Api.Common;
using AidlyPms.Api.Common.Database;
using AidlyPms.Api.Infrastructure;
using AidlyPms.Api.Modules.Products.Models;
using Dapper;
using Microsoft.AspNetCore.Mvc;

namespace AidlyPms.Api.Modules.Products.Controllers;

[Route("api/v1/inv/stocktakes")]
[RequirePermission("PMS-Product-Module")]
public class StocktakeController : BaseController
{
    private readonly IDbConnectionFactory _db;
    private readonly IStockPostingService _stockPosting;
    private readonly IDocumentNumberService _docNumService;

    public StocktakeController(
        IDbConnectionFactory db,
        IStockPostingService stockPosting,
        IDocumentNumberService docNumService)
    {
        _db = db;
        _stockPosting = stockPosting;
        _docNumService = docNumService;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<InvStocktake>>>> GetStocktakes(
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        await using var conn = await _db.CreateOpenConnectionAsync();
        var offset = Math.Max(0, (page - 1) * pageSize);

        var sql = @"
            SELECT 
                s.stocktake_no, s.pharmacy_no, s.branch_no, s.status, s.note,
                s.started_at, s.started_by, s.posted_at, s.posted_by,
                s.created_at, s.updated_at,
                u1.user_name AS started_by_name,
                u2.user_name AS posted_by_name,
                COUNT(i.stocktake_item_no) AS total_items,
                COUNT(CASE WHEN i.counted_qty_pcs IS NOT NULL THEN 1 END) AS total_counted,
                COALESCE(SUM(CASE WHEN i.variance_pcs != 0 THEN 1 ELSE 0 END), 0) AS total_variance
            FROM inv_stocktakes s
            LEFT JOIN inv_stocktake_items i ON s.stocktake_no = i.stocktake_no
            LEFT JOIN sys_users u1 ON s.started_by = u1.user_no
            LEFT JOIN sys_users u2 ON s.posted_by = u2.user_no
            WHERE s.pharmacy_no = @CurrentPharmacyNo AND s.branch_no = @CurrentBranchNo";

        var countSql = @"
            SELECT COUNT(1)
            FROM inv_stocktakes s
            WHERE s.pharmacy_no = @CurrentPharmacyNo AND s.branch_no = @CurrentBranchNo";

        var pms = new DynamicParameters();
        pms.Add("CurrentPharmacyNo", CurrentPharmacyNo);
        pms.Add("CurrentBranchNo", CurrentBranchNo);
        pms.Add("Offset", offset);
        pms.Add("PageSize", pageSize);

        if (!string.IsNullOrWhiteSpace(status))
        {
            sql += " AND s.status = @status";
            countSql += " AND s.status = @status";
            pms.Add("status", status.Trim().ToUpperInvariant());
        }

        sql += @"
            GROUP BY s.stocktake_no, s.pharmacy_no, s.branch_no, s.status, s.note,
                     s.started_at, s.started_by, s.posted_at, s.posted_by,
                     s.created_at, s.updated_at, u1.user_name, u2.user_name
            ORDER BY s.stocktake_no DESC
            OFFSET @Offset LIMIT @PageSize;";

        var total = await conn.ExecuteScalarAsync<int>(countSql, pms);
        var items = (await conn.QueryAsync<InvStocktake>(sql, pms)).AsList();

        return OkResponse(new PagedResult<InvStocktake>(items, total, page, pageSize));
    }

    [HttpGet("{stocktakeNo:long}")]
    public async Task<ActionResult<ApiResponse<InvStocktake>>> GetStocktake(long stocktakeNo)
    {
        await using var conn = await _db.CreateOpenConnectionAsync();

        var header = await conn.QueryFirstOrDefaultAsync<InvStocktake>(@"
            SELECT 
                s.stocktake_no, s.pharmacy_no, s.branch_no, s.status, s.note,
                s.started_at, s.started_by, s.posted_at, s.posted_by,
                s.created_at, s.updated_at,
                u1.user_name AS started_by_name,
                u2.user_name AS posted_by_name
            FROM inv_stocktakes s
            LEFT JOIN sys_users u1 ON s.started_by = u1.user_no
            LEFT JOIN sys_users u2 ON s.posted_by = u2.user_no
            WHERE s.stocktake_no = @stocktakeNo AND s.pharmacy_no = @CurrentPharmacyNo AND s.branch_no = @CurrentBranchNo;",
            new { stocktakeNo, CurrentPharmacyNo, CurrentBranchNo });

        if (header == null)
        {
            return FailResponse<InvStocktake>("Not Found", "Stocktake not found.", 404);
        }

        var items = (await conn.QueryAsync<InvStocktakeItem>(@"
            SELECT 
                i.stocktake_item_no, i.pharmacy_no, i.branch_no, i.stocktake_no,
                i.product_no, i.batch_no, i.system_qty_pcs, i.counted_qty_pcs,
                i.variance_pcs, i.reason,
                p.product_name, p.rak_number,
                COALESCE(p.purchase_price_per_piece, 0) AS unit_cost,
                b.batch_number, b.expiry_date,
                c.name AS company_name,
                g.name AS generic_name
            FROM inv_stocktake_items i
            JOIN prod_products p ON i.product_no = p.product_no
            LEFT JOIN prod_product_batches b ON i.batch_no = b.batch_no
            LEFT JOIN prod_companies c ON p.company_no = c.company_no
            LEFT JOIN prod_generics g ON p.generic_no = g.generic_no
            WHERE i.stocktake_no = @stocktakeNo
            ORDER BY p.rak_number ASC NULLS LAST, p.product_name ASC, b.expiry_date ASC NULLS LAST;",
            new { stocktakeNo })).AsList();

        header.Items = items;
        header.TotalItems = items.Count;
        header.TotalCounted = items.Count(x => x.CountedQtyPcs.HasValue);
        header.TotalVariance = items.Count(x => x.VariancePcs.GetValueOrDefault() != 0);

        return OkResponse(header);
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<InvStocktake>>> CreateStocktake([FromBody] CreateStocktakeRequest request)
    {
        await using var conn = await _db.CreateOpenConnectionAsync();
        await using var tran = await conn.BeginTransactionAsync();

        try
        {
            var filterType = request.FilterType?.Trim().ToUpperInvariant() ?? "ALL";
            var query = @"
                SELECT 
                    b.batch_no, b.product_no, b.total_quantity_pcs AS system_qty_pcs,
                    COALESCE(p.purchase_price_per_piece, 0) AS unit_cost
                FROM prod_product_batches b
                JOIN prod_products p ON b.product_no = p.product_no
                WHERE b.pharmacy_no = @CurrentPharmacyNo AND b.branch_no = @CurrentBranchNo
                  AND b.is_placeholder = FALSE
                  AND p.is_active = TRUE";

            var filterPms = new DynamicParameters();
            filterPms.Add("CurrentPharmacyNo", CurrentPharmacyNo);
            filterPms.Add("CurrentBranchNo", CurrentBranchNo);

            if (filterType == "RACK" && !string.IsNullOrWhiteSpace(request.FilterValue))
            {
                query += " AND p.rak_number = @filterValue";
                filterPms.Add("filterValue", request.FilterValue.Trim());
            }
            else if (filterType == "COMPANY" && long.TryParse(request.FilterValue, out var compNo))
            {
                query += " AND p.company_no = @compNo";
                filterPms.Add("compNo", compNo);
            }
            else if (filterType == "PRODUCT" && request.ProductIds != null && request.ProductIds.Count > 0)
            {
                query += " AND p.product_no = ANY(@prodIds)";
                filterPms.Add("prodIds", request.ProductIds);
            }

            query += " ORDER BY b.batch_no ASC;";

            var snapshotBatches = (await conn.QueryAsync<(long BatchNo, long ProductNo, int SystemQtyPcs, decimal UnitCost)>(
                query, filterPms, transaction: tran)).ToList();

            if (snapshotBatches.Count == 0)
            {
                return FailResponse<InvStocktake>("Bad Request", "No active product batches found matching the selected filter criteria.", 400);
            }

            var stocktakeNo = await conn.ExecuteScalarAsync<long>(
                "SELECT nextval('seq_stocktake_no');", transaction: tran);

            var now = DateTime.UtcNow;

            await conn.ExecuteAsync(@"
                INSERT INTO inv_stocktakes (
                    stocktake_no, pharmacy_no, branch_no, status, note,
                    started_at, started_by, posted_at, posted_by,
                    created_at, updated_at
                ) VALUES (
                    @stocktakeNo, @CurrentPharmacyNo, @CurrentBranchNo, 'OPEN', @Note,
                    @now, @CurrentUserNo, NULL, NULL,
                    @now, @now
                );",
                new
                {
                    stocktakeNo,
                    CurrentPharmacyNo,
                    CurrentBranchNo,
                    request.Note,
                    now,
                    CurrentUserNo
                },
                transaction: tran);

            const string insertItemSql = @"
                INSERT INTO inv_stocktake_items (
                    stocktake_item_no, pharmacy_no, branch_no, stocktake_no,
                    product_no, batch_no, system_qty_pcs, counted_qty_pcs,
                    variance_pcs, reason
                ) VALUES (
                    @itemNo, @CurrentPharmacyNo, @CurrentBranchNo, @stocktakeNo,
                    @ProductNo, @BatchNo, @SystemQtyPcs, NULL,
                    0, NULL
                );";

            foreach (var b in snapshotBatches)
            {
                var itemNo = await conn.ExecuteScalarAsync<long>(
                    "SELECT nextval('seq_stocktake_item_no');", transaction: tran);

                await conn.ExecuteAsync(insertItemSql, new
                {
                    itemNo,
                    CurrentPharmacyNo,
                    CurrentBranchNo,
                    stocktakeNo,
                    b.ProductNo,
                    b.BatchNo,
                    b.SystemQtyPcs
                }, transaction: tran);
            }

            await tran.CommitAsync();

            var result = new InvStocktake
            {
                StocktakeNo = stocktakeNo,
                PharmacyNo = CurrentPharmacyNo,
                BranchNo = CurrentBranchNo,
                Status = "OPEN",
                Note = request.Note,
                StartedAt = now,
                StartedBy = CurrentUserNo,
                CreatedAt = now,
                UpdatedAt = now,
                TotalItems = snapshotBatches.Count
            };

            return OkResponse(result, "Stocktake initialized successfully with stock snapshot.");
        }
        catch (Exception)
        {
            await tran.RollbackAsync();
            return FailResponse<InvStocktake>("Internal Error", "Failed to create stocktake.", 500);
        }
    }

    [HttpPut("{stocktakeNo:long}/count")]
    public async Task<ActionResult<ApiResponse<object>>> SaveCounts(
        long stocktakeNo,
        [FromBody] SaveStocktakeCountsRequest request)
    {
        if (request == null || request.Items == null || request.Items.Count == 0)
        {
            return FailResponse<object>("Bad Request", "No count entries provided.", 400);
        }

        await using var conn = await _db.CreateOpenConnectionAsync();
        await using var tran = await conn.BeginTransactionAsync();

        var header = await conn.QueryFirstOrDefaultAsync<InvStocktake>(@"
            SELECT stocktake_no, status FROM inv_stocktakes
            WHERE stocktake_no = @stocktakeNo AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
            FOR UPDATE;",
            new { stocktakeNo, CurrentPharmacyNo, CurrentBranchNo },
            transaction: tran);

        if (header == null)
        {
            return FailResponse<object>("Not Found", "Stocktake not found.", 404);
        }

        if (header.Status == "POSTED" || header.Status == "CANCELLED")
        {
            return FailResponse<object>("Bad Request", $"Cannot edit counts for a stocktake with status '{header.Status}'.", 400);
        }

        foreach (var entry in request.Items)
        {
            await conn.ExecuteAsync(@"
                UPDATE inv_stocktake_items
                SET counted_qty_pcs = @CountedQtyPcs,
                    variance_pcs = CASE 
                        WHEN @CountedQtyPcs IS NOT NULL THEN (@CountedQtyPcs - system_qty_pcs)
                        ELSE 0 
                    END,
                    reason = @Reason
                WHERE stocktake_item_no = @StocktakeItemNo AND stocktake_no = @stocktakeNo;",
                new { entry.CountedQtyPcs, entry.Reason, entry.StocktakeItemNo, stocktakeNo },
                transaction: tran);
        }

        // Transition status from OPEN to COUNTING if still OPEN
        var newStatus = header.Status == "OPEN" ? "COUNTING" : header.Status;
        await conn.ExecuteAsync(@"
            UPDATE inv_stocktakes
            SET status = @newStatus, updated_at = NOW()
            WHERE stocktake_no = @stocktakeNo;",
            new { newStatus, stocktakeNo },
            transaction: tran);

        await tran.CommitAsync();

        return OkResponse<object>(new { stocktake_no = stocktakeNo, updated_items_count = request.Items.Count, status = newStatus }, "Counts saved successfully.");
    }

    [HttpPost("{stocktakeNo:long}/refresh")]
    public async Task<ActionResult<ApiResponse<List<StocktakeChangedBatch>>>> RefreshSnapshot(long stocktakeNo)
    {
        await using var conn = await _db.CreateOpenConnectionAsync();
        await using var tran = await conn.BeginTransactionAsync();

        var header = await conn.QueryFirstOrDefaultAsync<InvStocktake>(@"
            SELECT stocktake_no, status FROM inv_stocktakes
            WHERE stocktake_no = @stocktakeNo AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
            FOR UPDATE;",
            new { stocktakeNo, CurrentPharmacyNo, CurrentBranchNo },
            transaction: tran);

        if (header == null)
        {
            return FailResponse<List<StocktakeChangedBatch>>("Not Found", "Stocktake not found.", 404);
        }

        if (header.Status == "POSTED" || header.Status == "CANCELLED")
        {
            return FailResponse<List<StocktakeChangedBatch>>("Bad Request", $"Cannot refresh snapshot for a stocktake with status '{header.Status}'.", 400);
        }

        var changed = (await conn.QueryAsync<StocktakeChangedBatch>(@"
            SELECT 
                i.batch_no,
                b.batch_number,
                p.product_name,
                i.system_qty_pcs AS system_qty_snapshot,
                b.total_quantity_pcs AS current_system_qty
            FROM inv_stocktake_items i
            JOIN prod_product_batches b ON i.batch_no = b.batch_no
            JOIN prod_products p ON i.product_no = p.product_no
            WHERE i.stocktake_no = @stocktakeNo
              AND i.system_qty_pcs != b.total_quantity_pcs;",
            new { stocktakeNo },
            transaction: tran)).ToList();

        if (changed.Count > 0)
        {
            await conn.ExecuteAsync(@"
                UPDATE inv_stocktake_items i
                SET system_qty_pcs = b.total_quantity_pcs,
                    variance_pcs = CASE 
                        WHEN i.counted_qty_pcs IS NOT NULL THEN (i.counted_qty_pcs - b.total_quantity_pcs)
                        ELSE 0 
                    END
                FROM prod_product_batches b
                WHERE i.batch_no = b.batch_no AND i.stocktake_no = @stocktakeNo;",
                new { stocktakeNo },
                transaction: tran);

            await conn.ExecuteAsync(@"
                UPDATE inv_stocktakes
                SET updated_at = NOW()
                WHERE stocktake_no = @stocktakeNo;",
                new { stocktakeNo },
                transaction: tran);
        }

        await tran.CommitAsync();

        return OkResponse(changed, $"{changed.Count} batch(es) refreshed to current system quantities.");
    }

    [HttpPost("{stocktakeNo:long}/post")]
    [RequirePermission("stock.adjust")]
    public async Task<ActionResult<ApiResponse<object>>> PostStocktake(long stocktakeNo)
    {
        await using var conn = await _db.CreateOpenConnectionAsync();
        await using var tran = await conn.BeginTransactionAsync();

        try
        {
            var header = await conn.QueryFirstOrDefaultAsync<InvStocktake>(@"
                SELECT stocktake_no, status FROM inv_stocktakes
                WHERE stocktake_no = @stocktakeNo AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
                FOR UPDATE;",
                new { stocktakeNo, CurrentPharmacyNo, CurrentBranchNo },
                transaction: tran);

            if (header == null)
            {
                return FailResponse<object>("Not Found", "Stocktake not found.", 404);
            }

            if (header.Status == "POSTED")
            {
                return FailResponse<object>("Bad Request", "A stocktake can only be posted once.", 400);
            }

            if (header.Status == "CANCELLED")
            {
                return FailResponse<object>("Bad Request", "Cannot post a cancelled stocktake.", 400);
            }

            // Check if any batch changed since snapshot
            var changedBatches = (await conn.QueryAsync<StocktakeChangedBatch>(@"
                SELECT 
                    i.batch_no,
                    b.batch_number,
                    p.product_name,
                    i.system_qty_pcs AS system_qty_snapshot,
                    b.total_quantity_pcs AS current_system_qty
                FROM inv_stocktake_items i
                JOIN prod_product_batches b ON i.batch_no = b.batch_no
                JOIN prod_products p ON i.product_no = p.product_no
                WHERE i.stocktake_no = @stocktakeNo
                  AND i.system_qty_pcs != b.total_quantity_pcs;",
                new { stocktakeNo },
                transaction: tran)).ToList();

            if (changedBatches.Count > 0)
            {
                return StatusCode(400, new ApiResponse<List<StocktakeChangedBatch>>
                {
                    Success = false,
                    Message = $"{changedBatches.Count} batch(es) have changed in the system since snapshot was taken. Refresh count before posting.",
                    Errors = new List<string> { "SnapshotOutOfDate" },
                    Data = changedBatches
                });
            }

            // Retrieve items with variance
            var items = (await conn.QueryAsync<InvStocktakeItem>(@"
                SELECT 
                    i.stocktake_item_no, i.product_no, i.batch_no,
                    i.system_qty_pcs, i.counted_qty_pcs, i.variance_pcs, i.reason,
                    p.product_name,
                    COALESCE(p.purchase_price_per_piece, 0) AS unit_cost,
                    b.batch_number
                FROM inv_stocktake_items i
                JOIN prod_products p ON i.product_no = p.product_no
                LEFT JOIN prod_product_batches b ON i.batch_no = b.batch_no
                WHERE i.stocktake_no = @stocktakeNo
                ORDER BY i.stocktake_item_no ASC;",
                new { stocktakeNo },
                transaction: tran)).ToList();

            var varianceItems = items.Where(i => i.VariancePcs.GetValueOrDefault() != 0).ToList();

            // Validate that every item with variance has an explanatory reason
            var missingReason = varianceItems.FirstOrDefault(i => string.IsNullOrWhiteSpace(i.Reason));
            if (missingReason != null)
            {
                return FailResponse<object>("Bad Request",
                    $"Item '{missingReason.ProductName}' (Batch '{missingReason.BatchNumber}') has a variance of {missingReason.VariancePcs} pcs but is missing a required reason.",
                    400);
            }

            // Post adjustment movements for variance items
            if (varianceItems.Count > 0)
            {
                var adjNo = await conn.ExecuteScalarAsync<long>(
                    "SELECT nextval('seq_adjustment_no');", transaction: tran);

                var adjDocNumber = await _docNumService.GetNextDocumentNumberAsync(
                    conn, tran, CurrentPharmacyNo, CurrentBranchNo, "ADJUSTMENT");

                var netCost = varianceItems.Sum(i => i.VariancePcs!.Value * i.UnitCost);
                var now = DateTime.UtcNow;

                await conn.ExecuteAsync(@"
                    INSERT INTO inv_adjustments (
                        adjustment_no, pharmacy_no, branch_no, adjustment_number, adjustment_type,
                        total_items, net_cost_adjustment, adjustment_status, approved_by_user_no,
                        note, adjustment_date, created_at
                    ) VALUES (
                        @adjNo, @CurrentPharmacyNo, @CurrentBranchNo, @adjDocNumber, 1,
                        @totalItems, @netCost, 2, @CurrentUserNo,
                        @note, @now, @now
                    );",
                    new
                    {
                        adjNo,
                        CurrentPharmacyNo,
                        CurrentBranchNo,
                        adjDocNumber,
                        totalItems = varianceItems.Count,
                        netCost,
                        CurrentUserNo,
                        note = $"Cycle count stocktake #{stocktakeNo} adjustment",
                        now
                    },
                    transaction: tran);

                foreach (var vItem in varianceItems)
                {
                    var adjItemNo = await conn.ExecuteScalarAsync<long>(
                        "SELECT nextval('seq_adjustment_item_no');", transaction: tran);

                    var qtyDiff = vItem.VariancePcs!.Value;
                    var costImpact = qtyDiff * vItem.UnitCost;

                    await conn.ExecuteAsync(@"
                        INSERT INTO inv_adjustment_items (
                            adjustment_item_no, pharmacy_no, branch_no, adjustment_no,
                            product_no, batch_no, batch_number, counted_qty_pcs,
                            system_qty_pcs, difference_qty_pcs, unit_cost, total_cost_impact,
                            reason_code
                        ) VALUES (
                            @adjItemNo, @CurrentPharmacyNo, @CurrentBranchNo, @adjNo,
                            @ProductNo, @BatchNo, @BatchNumber, @CountedQtyPcs,
                            @SystemQtyPcs, @qtyDiff, @UnitCost, @costImpact,
                            1
                        );",
                        new
                        {
                            adjItemNo,
                            CurrentPharmacyNo,
                            CurrentBranchNo,
                            adjNo,
                            vItem.ProductNo,
                            vItem.BatchNo,
                            vItem.BatchNumber,
                            CountedQtyPcs = vItem.CountedQtyPcs.GetValueOrDefault(),
                            vItem.SystemQtyPcs,
                            qtyDiff,
                            vItem.UnitCost,
                            costImpact
                        },
                        transaction: tran);

                    // Post movement through StockPostingService
                    await _stockPosting.PostMovementAsync(conn, tran, new StockMovementEntry
                    {
                        PharmacyNo = CurrentPharmacyNo,
                        BranchNo = CurrentBranchNo,
                        ProductNo = vItem.ProductNo,
                        BatchNo = vItem.BatchNo,
                        MovementType = 7, // Stock Adjustment
                        ReferenceDocType = "STOCKTAKE",
                        ReferenceDocNo = adjDocNumber,
                        ReferenceDocId = stocktakeNo,
                        QtyPcs = qtyDiff,
                        UnitCost = vItem.UnitCost,
                        Remarks = $"Stocktake #{stocktakeNo} variance: {vItem.Reason}"
                    });
                }
            }

            var postTime = DateTime.UtcNow;
            await conn.ExecuteAsync(@"
                UPDATE inv_stocktakes
                SET status = 'POSTED',
                    posted_at = @postTime,
                    posted_by = @CurrentUserNo,
                    updated_at = @postTime
                WHERE stocktake_no = @stocktakeNo;",
                new { postTime, CurrentUserNo, stocktakeNo },
                transaction: tran);

            await tran.CommitAsync();

            return OkResponse<object>(new
            {
                stocktake_no = stocktakeNo,
                status = "POSTED",
                adjusted_items_count = varianceItems.Count
            }, "Stocktake successfully posted and stock movements recorded.");
        }
        catch (Exception)
        {
            await tran.RollbackAsync();
            return FailResponse<object>("Internal Error", "Failed to post stocktake adjustments.", 500);
        }
    }

    [HttpPost("{stocktakeNo:long}/cancel")]
    public async Task<ActionResult<ApiResponse<object>>> CancelStocktake(long stocktakeNo)
    {
        await using var conn = await _db.CreateOpenConnectionAsync();

        var header = await conn.QueryFirstOrDefaultAsync<InvStocktake>(@"
            SELECT stocktake_no, status FROM inv_stocktakes
            WHERE stocktake_no = @stocktakeNo AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
            new { stocktakeNo, CurrentPharmacyNo, CurrentBranchNo });

        if (header == null)
        {
            return FailResponse<object>("Not Found", "Stocktake not found.", 404);
        }

        if (header.Status == "POSTED")
        {
            return FailResponse<object>("Bad Request", "Cannot cancel a posted stocktake.", 400);
        }

        if (header.Status == "CANCELLED")
        {
            return FailResponse<object>("Bad Request", "Stocktake is already cancelled.", 400);
        }

        await conn.ExecuteAsync(@"
            UPDATE inv_stocktakes
            SET status = 'CANCELLED', updated_at = NOW()
            WHERE stocktake_no = @stocktakeNo;",
            new { stocktakeNo });

        return OkResponse<object>(new { stocktake_no = stocktakeNo, status = "CANCELLED" }, "Stocktake cancelled.");
    }
}

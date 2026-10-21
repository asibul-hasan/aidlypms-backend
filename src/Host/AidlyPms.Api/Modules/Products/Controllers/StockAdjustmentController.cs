using AidlyPms.Api.Common;
using AidlyPms.Api.Common.Database;
using AidlyPms.Api.Infrastructure;
using AidlyPms.Api.Modules.Products.Models;
using Dapper;
using Microsoft.AspNetCore.Mvc;

namespace AidlyPms.Api.Modules.Products.Controllers;

[Route("api/v1/inv/adjustments")]
public class StockAdjustmentController : BaseController
{
    private readonly IDbConnectionFactory _db;
    private readonly IDocumentNumberService _docNumService;
    private readonly IStockPostingService _stockPosting;

    public StockAdjustmentController(
        IDbConnectionFactory db,
        IDocumentNumberService docNumService,
        IStockPostingService stockPosting)
    {
        _db = db;
        _docNumService = docNumService;
        _stockPosting = stockPosting;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<InvAdjustment>>>> GetAdjustments(
        [FromQuery] QueryFilter filter,
        [FromQuery] string? search,
        [FromQuery] short? adjustmentType,
        [FromQuery] short? status,
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate)
    {
        (fromDate, toDate) = DateHelper.NormalizeDateRange(fromDate, toDate);
        using var conn = _db.CreateConnection();

        var sql = @"
            SELECT a.*
            FROM inv_adjustments a
            WHERE a.pharmacy_no = @CurrentPharmacyNo AND a.branch_no = @CurrentBranchNo";

        var countSql = @"
            SELECT COUNT(1)
            FROM inv_adjustments a
            WHERE a.pharmacy_no = @CurrentPharmacyNo AND a.branch_no = @CurrentBranchNo";

        var pms = new DynamicParameters();
        pms.Add("CurrentPharmacyNo", CurrentPharmacyNo);
        pms.Add("CurrentBranchNo", CurrentBranchNo);

        if (!string.IsNullOrWhiteSpace(search))
        {
            sql += " AND (a.adjustment_number ILIKE '%' || @search || '%' OR a.note ILIKE '%' || @search || '%')";
            countSql += " AND (a.adjustment_number ILIKE '%' || @search || '%' OR a.note ILIKE '%' || @search || '%')";
            pms.Add("search", search);
        }

        if (adjustmentType.HasValue)
        {
            sql += " AND a.adjustment_type = @adjustmentType";
            countSql += " AND a.adjustment_type = @adjustmentType";
            pms.Add("adjustmentType", adjustmentType.Value);
        }

        if (status.HasValue)
        {
            sql += " AND a.adjustment_status = @status";
            countSql += " AND a.adjustment_status = @status";
            pms.Add("status", status.Value);
        }

        if (fromDate.HasValue)
        {
            sql += " AND a.adjustment_date >= @fromDate";
            countSql += " AND a.adjustment_date >= @fromDate";
            pms.Add("fromDate", fromDate.Value);
        }

        if (toDate.HasValue)
        {
            sql += " AND a.adjustment_date <= @toDate";
            countSql += " AND a.adjustment_date <= @toDate";
            pms.Add("toDate", toDate.Value);
        }

        var total = await conn.ExecuteScalarAsync<int>(countSql, pms);

        var offset = Math.Max(0, (filter.PageIndex - 1) * filter.PageSize);
        sql += " ORDER BY a.adjustment_date DESC, a.adjustment_no DESC OFFSET @offset LIMIT @pageSize;";
        pms.Add("offset", offset);
        pms.Add("pageSize", filter.PageSize);

        var list = (await conn.QueryAsync<InvAdjustment>(sql, pms)).AsList();

        return PagedResponse(new PagedResult<InvAdjustment>(list, total, filter.PageIndex, filter.PageSize));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<InvAdjustment>>> GetAdjustment(long id)
    {
        using var conn = _db.CreateConnection();

        var sql = @"
            SELECT a.*
            FROM inv_adjustments a
            WHERE a.adjustment_no = @id AND a.pharmacy_no = @CurrentPharmacyNo AND a.branch_no = @CurrentBranchNo;";

        var adj = await conn.QuerySingleOrDefaultAsync<InvAdjustment>(sql, new { id, CurrentPharmacyNo, CurrentBranchNo });
        if (adj == null) return NotFoundResponse<InvAdjustment>("Adjustment record not found.");

        var itemsSql = @"
            SELECT i.*, p.product_name
            FROM inv_adjustment_items i
            JOIN prod_products p ON i.product_no = p.product_no
            WHERE i.adjustment_no = @id AND i.pharmacy_no = @CurrentPharmacyNo AND i.branch_no = @CurrentBranchNo
            ORDER BY i.adjustment_item_no ASC;";

        adj.Items = (await conn.QueryAsync<InvAdjustmentItem>(itemsSql, new { id, CurrentPharmacyNo, CurrentBranchNo })).AsList();

        return OkResponse(adj);
    }

    [HttpPost]
    [RequirePermission("stock.adjust")]
    public async Task<ActionResult<ApiResponse<InvAdjustment>>> CreateAdjustment([FromBody] CreateAdjustmentPayload payload)
    {
        if (payload == null || payload.Items == null || payload.Items.Count == 0)
        {
            return FailResponse<InvAdjustment>("Bad Request", "At least one adjustment item is required.", 400);
        }

        if (string.IsNullOrWhiteSpace(payload.Note) && payload.Items.All(i => !i.ReasonCode.HasValue || i.ReasonCode.Value <= 0))
        {
            return FailResponse<InvAdjustment>("Bad Request", "A mandatory reason must be provided for stock adjustment (either note or item reason code).", 400);
        }

        using var conn = _db.CreateConnection();
        conn.Open();
        using var tran = conn.BeginTransaction();

        try
        {
            var adjustmentNo = await conn.ExecuteScalarAsync<long>(
                "SELECT COALESCE(MAX(adjustment_no), 0) + 1 FROM inv_adjustments;", transaction: tran);

            var adjustmentNumber = await _docNumService.GetNextDocumentNumberAsync(
                conn, tran, CurrentPharmacyNo, CurrentBranchNo, "ADJUSTMENT");

            var totalItems = payload.Items.Count;
            var netCost = payload.Items.Sum(i => i.TotalCostImpact);
            var adjDate = payload.AdjustmentDate ?? DateTime.UtcNow;

            const string insertAdjSql = @"
                INSERT INTO inv_adjustments (
                    adjustment_no, pharmacy_no, branch_no, adjustment_number, adjustment_type,
                    total_items, net_cost_adjustment, adjustment_status, approved_by_user_no,
                    note, adjustment_date, created_at
                ) VALUES (
                    @adjustmentNo, @CurrentPharmacyNo, @CurrentBranchNo, @adjustmentNumber, @AdjustmentType,
                    @totalItems, @netCost, 2, @CurrentUserNo,
                    @Note, @adjDate, CURRENT_TIMESTAMP
                );";

            await conn.ExecuteAsync(insertAdjSql, new
            {
                adjustmentNo,
                CurrentPharmacyNo,
                CurrentBranchNo,
                adjustmentNumber,
                payload.AdjustmentType,
                totalItems,
                netCost,
                CurrentUserNo,
                payload.Note,
                adjDate
            }, transaction: tran);

            var maxItemNo = await conn.ExecuteScalarAsync<long>(
                "SELECT COALESCE(MAX(adjustment_item_no), 0) FROM inv_adjustment_items;", transaction: tran);

            foreach (var item in payload.Items)
            {
                maxItemNo++;
                item.AdjustmentItemNo = maxItemNo;
                item.AdjustmentNo = adjustmentNo;
                item.PharmacyNo = CurrentPharmacyNo;
                item.BranchNo = CurrentBranchNo;

                const string insertItemSql = @"
                    INSERT INTO inv_adjustment_items (
                        adjustment_item_no, pharmacy_no, branch_no, adjustment_no, product_no,
                        batch_no, batch_number, counted_qty_pcs, system_qty_pcs, difference_qty_pcs,
                        unit_cost, total_cost_impact, reason_code
                    ) VALUES (
                        @AdjustmentItemNo, @PharmacyNo, @BranchNo, @AdjustmentNo, @ProductNo,
                        @BatchNo, @BatchNumber, @CountedQtyPcs, @SystemQtyPcs, @DifferenceQtyPcs,
                        @UnitCost, @TotalCostImpact, @ReasonCode
                    );";

                await conn.ExecuteAsync(insertItemSql, item, transaction: tran);

                // Post stock movement for discrepancy
                if (item.DifferenceQtyPcs != 0)
                {
                    await _stockPosting.PostMovementAsync(conn, tran, new StockMovementEntry
                    {
                        PharmacyNo = CurrentPharmacyNo,
                        BranchNo = CurrentBranchNo,
                        ProductNo = item.ProductNo,
                        BatchNo = item.BatchNo,
                        MovementType = 7, // ADJUSTMENT
                        ReferenceDocType = "INV_ADJUSTMENT",
                        ReferenceDocNo = adjustmentNumber,
                        ReferenceDocId = adjustmentNo,
                        QtyPcs = item.DifferenceQtyPcs, // Positive increases stock, Negative decreases stock
                        UnitCost = item.UnitCost,
                        Remarks = $"Stock Adjustment {adjustmentNumber} (Reason: {item.ReasonCode})"
                    });
                }
            }

            tran.Commit();

            var result = new InvAdjustment
            {
                AdjustmentNo = adjustmentNo,
                PharmacyNo = CurrentPharmacyNo,
                BranchNo = CurrentBranchNo,
                AdjustmentNumber = adjustmentNumber,
                AdjustmentType = payload.AdjustmentType,
                TotalItems = totalItems,
                NetCostAdjustment = netCost,
                AdjustmentStatus = 2,
                Note = payload.Note,
                AdjustmentDate = adjDate,
                CreatedAt = DateTime.UtcNow,
                Items = payload.Items
            };

            return OkResponse(result, $"Stock Adjustment '{adjustmentNumber}' recorded and posted successfully.");
        }
        catch
        {
            tran.Rollback();
            throw;
        }
    }
}

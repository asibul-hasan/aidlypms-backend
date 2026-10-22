using AidlyPms.Api.Common;
using AidlyPms.Api.Common.Database;
using AidlyPms.Api.Infrastructure;
using AidlyPms.Api.Modules.Products.Models;
using Dapper;
using Microsoft.AspNetCore.Mvc;

namespace AidlyPms.Api.Modules.Products.Controllers;

[Route("api/v1/inv/transfers")]
[RequirePermission("PMS-Product-Module")]
public class StockTransferController : BaseController
{
    private readonly IDbConnectionFactory _db;
    private readonly IDocumentNumberService _docNumService;
    private readonly IStockPostingService _stockPosting;

    public StockTransferController(
        IDbConnectionFactory db,
        IDocumentNumberService docNumService,
        IStockPostingService stockPosting)
    {
        _db = db;
        _docNumService = docNumService;
        _stockPosting = stockPosting;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<InvTransfer>>>> GetTransfers(
        [FromQuery] QueryFilter filter,
        [FromQuery] string? search,
        [FromQuery] short? status,
        [FromQuery] long? toBranchNo,
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate)
    {
        (fromDate, toDate) = DateHelper.NormalizeDateRange(fromDate, toDate);
        using var conn = _db.CreateConnection();

        var sql = @"
            SELECT t.*,
                   fb.name AS from_branch_name,
                   tb.name AS to_branch_name
            FROM inv_transfers t
            LEFT JOIN sys_branches fb ON t.from_branch_no = fb.branch_no
            LEFT JOIN sys_branches tb ON t.to_branch_no = tb.branch_no
            WHERE t.pharmacy_no = @CurrentPharmacyNo
              AND (t.from_branch_no = @CurrentBranchNo OR t.to_branch_no = @CurrentBranchNo)";

        var countSql = @"
            SELECT COUNT(1)
            FROM inv_transfers t
            WHERE t.pharmacy_no = @CurrentPharmacyNo
              AND (t.from_branch_no = @CurrentBranchNo OR t.to_branch_no = @CurrentBranchNo)";

        var pms = new DynamicParameters();
        pms.Add("CurrentPharmacyNo", CurrentPharmacyNo);
        pms.Add("CurrentBranchNo", CurrentBranchNo);

        if (!string.IsNullOrWhiteSpace(search))
        {
            sql += " AND (t.transfer_number ILIKE '%' || @search || '%' OR t.note ILIKE '%' || @search || '%')";
            countSql += " AND (t.transfer_number ILIKE '%' || @search || '%' OR t.note ILIKE '%' || @search || '%')";
            pms.Add("search", search);
        }

        if (status.HasValue)
        {
            sql += " AND t.transfer_status = @status";
            countSql += " AND t.transfer_status = @status";
            pms.Add("status", status.Value);
        }

        if (toBranchNo.HasValue)
        {
            sql += " AND t.to_branch_no = @toBranchNo";
            countSql += " AND t.to_branch_no = @toBranchNo";
            pms.Add("toBranchNo", toBranchNo.Value);
        }

        if (fromDate.HasValue)
        {
            sql += " AND t.sent_at >= @fromDate";
            countSql += " AND t.sent_at >= @fromDate";
            pms.Add("fromDate", fromDate.Value);
        }

        if (toDate.HasValue)
        {
            sql += " AND t.sent_at <= @toDate";
            countSql += " AND t.sent_at <= @toDate";
            pms.Add("toDate", toDate.Value);
        }

        var total = await conn.ExecuteScalarAsync<int>(countSql, pms);

        var offset = Math.Max(0, (filter.PageIndex - 1) * filter.PageSize);
        sql += " ORDER BY t.sent_at DESC, t.transfer_no DESC OFFSET @offset LIMIT @pageSize;";
        pms.Add("offset", offset);
        pms.Add("pageSize", filter.PageSize);

        var list = (await conn.QueryAsync<InvTransfer>(sql, pms)).AsList();

        return PagedResponse(new PagedResult<InvTransfer>(list, total, filter.PageIndex, filter.PageSize));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<InvTransfer>>> GetTransfer(long id)
    {
        using var conn = _db.CreateConnection();

        var sql = @"
            SELECT t.*,
                   fb.name AS from_branch_name,
                   tb.name AS to_branch_name
            FROM inv_transfers t
            LEFT JOIN sys_branches fb ON t.from_branch_no = fb.branch_no
            LEFT JOIN sys_branches tb ON t.to_branch_no = tb.branch_no
            WHERE t.transfer_no = @id AND t.pharmacy_no = @CurrentPharmacyNo;";

        var transfer = await conn.QuerySingleOrDefaultAsync<InvTransfer>(sql, new { id, CurrentPharmacyNo });
        if (transfer == null) return NotFoundResponse<InvTransfer>("Transfer record not found.");

        var itemsSql = @"
            SELECT i.*, p.product_name
            FROM inv_transfer_items i
            JOIN prod_products p ON i.product_no = p.product_no
            WHERE i.transfer_no = @id AND i.pharmacy_no = @CurrentPharmacyNo
            ORDER BY i.transfer_item_no ASC;";

        transfer.Items = (await conn.QueryAsync<InvTransferItem>(itemsSql, new { id, CurrentPharmacyNo })).AsList();

        return OkResponse(transfer);
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<InvTransfer>>> InitiateTransfer([FromBody] CreateTransferPayload payload)
    {
        if (payload == null || payload.Items == null || payload.Items.Count == 0)
        {
            return FailResponse<InvTransfer>("Bad Request", "At least one transfer item is required.", 400);
        }

        if (payload.ToBranchNo <= 0 || payload.ToBranchNo == CurrentBranchNo)
        {
            return FailResponse<InvTransfer>("Bad Request", "Destination branch must be a valid, different branch.", 400);
        }

        using var conn = _db.CreateConnection();
        conn.Open();
        using var tran = conn.BeginTransaction();

        try
        {
            var transferNo = await conn.ExecuteScalarAsync<long>(
                "SELECT nextval('seq_transfer_no');", transaction: tran);

            var transferNumber = await _docNumService.GetNextDocumentNumberAsync(
                conn, tran, CurrentPharmacyNo, CurrentBranchNo, "TRANSFER");

            var totalItems = payload.Items.Count;
            var totalCost = payload.Items.Sum(i => i.TotalCost);

            const string insertTransferSql = @"
                INSERT INTO inv_transfers (
                    transfer_no, pharmacy_no, branch_no, transfer_number, from_branch_no,
                    to_branch_no, total_items, total_cost_amount, transfer_status, note,
                    sent_by_user_no, sent_at, created_at
                ) VALUES (
                    @transferNo, @CurrentPharmacyNo, @CurrentBranchNo, @transferNumber, @CurrentBranchNo,
                    @ToBranchNo, @totalItems, @totalCost, 1, @Note,
                    @CurrentUserNo, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
                );";

            await conn.ExecuteAsync(insertTransferSql, new
            {
                transferNo,
                CurrentPharmacyNo,
                CurrentBranchNo,
                transferNumber,
                payload.ToBranchNo,
                totalItems,
                totalCost,
                payload.Note,
                CurrentUserNo
            }, transaction: tran);

            foreach (var item in payload.Items)
            {
                item.TransferItemNo = await conn.ExecuteScalarAsync<long>(
                    "SELECT nextval('seq_transfer_item_no');", transaction: tran);
                item.TransferNo = transferNo;
                item.PharmacyNo = CurrentPharmacyNo;
                item.BranchNo = CurrentBranchNo;

                const string insertItemSql = @"
                    INSERT INTO inv_transfer_items (
                        transfer_item_no, pharmacy_no, branch_no, transfer_no, product_no,
                        batch_no, batch_number, qty_pcs, unit_cost, total_cost, expiry_date
                    ) VALUES (
                        @TransferItemNo, @PharmacyNo, @BranchNo, @TransferNo, @ProductNo,
                        @BatchNo, @BatchNumber, @QtyPcs, @UnitCost, @TotalCost, @ExpiryDate
                    );";

                await conn.ExecuteAsync(insertItemSql, item, transaction: tran);

                // Deduct stock from sending branch (TRANSFER OUT)
                await _stockPosting.PostMovementAsync(conn, tran, new StockMovementEntry
                {
                    PharmacyNo = CurrentPharmacyNo,
                    BranchNo = CurrentBranchNo,
                    ProductNo = item.ProductNo,
                    BatchNo = item.BatchNo,
                    MovementType = 6, // TRANSFER OUT (-)
                    ReferenceDocType = "INV_TRANSFER",
                    ReferenceDocNo = transferNumber,
                    ReferenceDocId = transferNo,
                    QtyPcs = -item.QtyPcs,
                    UnitCost = item.UnitCost,
                    Remarks = $"Transfer Out {transferNumber} to Branch {payload.ToBranchNo}"
                });
            }

            tran.Commit();

            var result = new InvTransfer
            {
                TransferNo = transferNo,
                PharmacyNo = CurrentPharmacyNo,
                BranchNo = CurrentBranchNo,
                TransferNumber = transferNumber,
                FromBranchNo = CurrentBranchNo,
                ToBranchNo = payload.ToBranchNo,
                TotalItems = totalItems,
                TotalCostAmount = totalCost,
                TransferStatus = 1,
                Note = payload.Note,
                SentAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                Items = payload.Items
            };

            return OkResponse(result, $"Stock Transfer '{transferNumber}' initiated successfully.");
        }
        catch
        {
            tran.Rollback();
            throw;
        }
    }

    [HttpPost("{id}/receive")]
    public async Task<ActionResult<ApiResponse<object>>> ReceiveTransfer(long id)
    {
        using var conn = _db.CreateConnection();
        conn.Open();
        using var tran = conn.BeginTransaction();

        try
        {
            var transfer = await conn.QuerySingleOrDefaultAsync<InvTransfer>(@"
                SELECT * FROM inv_transfers
                WHERE transfer_no = @id AND pharmacy_no = @CurrentPharmacyNo
                FOR UPDATE;",
                new { id, CurrentPharmacyNo }, transaction: tran);

            if (transfer == null) return NotFoundResponse<object>("Transfer record not found.");
            if (transfer.ToBranchNo != CurrentBranchNo)
            {
                return FailResponse<object>("Access Denied", "Only the destination branch can receive this transfer.", 403);
            }
            if (transfer.TransferStatus == 2) return FailResponse<object>("Bad Request", "Transfer already received.", 400);

            var items = (await conn.QueryAsync<InvTransferItem>(@"
                SELECT * FROM inv_transfer_items
                WHERE transfer_no = @id AND pharmacy_no = @CurrentPharmacyNo;",
                new { id, CurrentPharmacyNo }, transaction: tran)).AsList();

            // Inward items to receiving branch
            foreach (var item in items)
            {
                // Inward stock movement for destination branch (TRANSFER IN)
                await _stockPosting.PostMovementAsync(conn, tran, new StockMovementEntry
                {
                    PharmacyNo = CurrentPharmacyNo,
                    BranchNo = transfer.ToBranchNo,
                    ProductNo = item.ProductNo,
                    BatchNo = item.BatchNo,
                    MovementType = 5, // TRANSFER IN (+)
                    ReferenceDocType = "INV_TRANSFER_RCV",
                    ReferenceDocNo = transfer.TransferNumber,
                    ReferenceDocId = transfer.TransferNo,
                    QtyPcs = item.QtyPcs,
                    UnitCost = item.UnitCost,
                    Remarks = $"Transfer In {transfer.TransferNumber} from Branch {transfer.FromBranchNo}"
                });
            }

            await conn.ExecuteAsync(@"
                UPDATE inv_transfers
                SET transfer_status = 2,
                    received_by_user_no = @CurrentUserNo,
                    received_at = CURRENT_TIMESTAMP
                WHERE transfer_no = @id;",
                new { id, CurrentUserNo }, transaction: tran);

            tran.Commit();
            return OkResponse<object>(new { transfer.TransferNo, transfer.TransferNumber, Status = "Received" },
                $"Transfer '{transfer.TransferNumber}' received and stocked successfully.");
        }
        catch
        {
            tran.Rollback();
            throw;
        }
    }
}

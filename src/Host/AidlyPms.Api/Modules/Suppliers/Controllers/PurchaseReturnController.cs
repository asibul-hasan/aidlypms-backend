using AidlyPms.Api.Common;
using AidlyPms.Api.Common.Database;
using AidlyPms.Api.Infrastructure;
using AidlyPms.Api.Modules.Products.Models;
using AidlyPms.Api.Modules.Suppliers.Models;
using Dapper;
using Microsoft.AspNetCore.Mvc;

namespace AidlyPms.Api.Modules.Suppliers.Controllers;

[Route("api/v1/pur/returns")]
public class PurchaseReturnController : BaseController
{
    private readonly IDbConnectionFactory _db;
    private readonly IDocumentNumberService _docNumService;
    private readonly IStockPostingService _stockPosting;
    private readonly ILedgerPostingService _ledgerPosting;

    public PurchaseReturnController(
        IDbConnectionFactory db,
        IDocumentNumberService docNumService,
        IStockPostingService stockPosting,
        ILedgerPostingService ledgerPosting)
    {
        _db = db;
        _docNumService = docNumService;
        _stockPosting = stockPosting;
        _ledgerPosting = ledgerPosting;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<PurPurchaseReturn>>>> GetReturns(
        [FromQuery] QueryFilter filter,
        [FromQuery] long? supplierNo,
        [FromQuery] string? search)
    {
        using var conn = _db.CreateConnection();
        var sql = @"
            SELECT r.*, s.name AS supplier_name
            FROM pur_purchase_returns r
            JOIN supp_suppliers s ON r.supplier_no = s.supplier_no
            WHERE r.pharmacy_no = @CurrentPharmacyNo AND r.branch_no = @CurrentBranchNo";

        var countSql = @"
            SELECT COUNT(1)
            FROM pur_purchase_returns r
            JOIN supp_suppliers s ON r.supplier_no = s.supplier_no
            WHERE r.pharmacy_no = @CurrentPharmacyNo AND r.branch_no = @CurrentBranchNo";

        var pms = new DynamicParameters();
        pms.Add("CurrentPharmacyNo", CurrentPharmacyNo);
        pms.Add("CurrentBranchNo", CurrentBranchNo);

        if (!string.IsNullOrWhiteSpace(search))
        {
            sql += " AND (r.return_number ILIKE @Search OR s.name ILIKE @Search)";
            countSql += " AND (r.return_number ILIKE @Search OR s.name ILIKE @Search)";
            pms.Add("Search", $"%{search.Trim()}%");
        }

        if (supplierNo.HasValue)
        {
            sql += " AND r.supplier_no = @SupplierNo";
            countSql += " AND r.supplier_no = @SupplierNo";
            pms.Add("SupplierNo", supplierNo.Value);
        }

        sql += " ORDER BY r.purchase_return_no DESC LIMIT @PageSize OFFSET @Offset;";
        pms.Add("PageSize", filter.PageSize);
        pms.Add("Offset", (filter.PageIndex - 1) * filter.PageSize);

        var total = await conn.ExecuteScalarAsync<int>(countSql, pms);
        var items = (await conn.QueryAsync<PurPurchaseReturn>(sql, pms)).ToList();

        var result = new PagedResult<PurPurchaseReturn>(items, total, filter.PageIndex, filter.PageSize);
        return PagedResponse(result);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<PurPurchaseReturn>>> GetReturnById(long id)
    {
        using var conn = _db.CreateConnection();
        var ret = await conn.QuerySingleOrDefaultAsync<PurPurchaseReturn>(@"
            SELECT r.*, s.name AS supplier_name
            FROM pur_purchase_returns r
            JOIN supp_suppliers s ON r.supplier_no = s.supplier_no
            WHERE r.purchase_return_no = @Id AND r.pharmacy_no = @CurrentPharmacyNo AND r.branch_no = @CurrentBranchNo;",
            new { Id = id, CurrentPharmacyNo, CurrentBranchNo });

        if (ret == null)
            return FailResponse<PurPurchaseReturn>("Purchase return not found.", statusCode: 404);

        var items = (await conn.QueryAsync<PurPurchaseReturnItem>(@"
            SELECT ri.*, p.product_name
            FROM pur_purchase_return_items ri
            JOIN prod_products p ON ri.product_no = p.product_no
            WHERE ri.purchase_return_no = @Id;",
            new { Id = id })).ToList();

        ret.Items = items;
        return OkResponse(ret);
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<PurPurchaseReturn>>> CreateReturn([FromBody] PurPurchaseReturn payload)
    {
        if (payload.Items == null || payload.Items.Count == 0)
            return FailResponse<PurPurchaseReturn>("At least one return line item must be provided.");

        if (payload.SupplierNo <= 0)
            return FailResponse<PurPurchaseReturn>("A valid supplier must be selected.");

        using var conn = _db.CreateConnection();
        conn.Open();
        using var tran = conn.BeginTransaction();

        try
        {
            var supplier = await conn.QuerySingleOrDefaultAsync<SuppSupplier>(@"
                SELECT * FROM supp_suppliers
                WHERE supplier_no = @SupplierNo AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
                FOR UPDATE;",
                new { payload.SupplierNo, CurrentPharmacyNo, CurrentBranchNo },
                transaction: tran);

            if (supplier == null)
                throw new InvalidOperationException($"Supplier #{payload.SupplierNo} not found.");

            var returnNumber = await _docNumService.GetNextDocumentNumberAsync(conn, tran, CurrentPharmacyNo, CurrentBranchNo, "PUR_RETURN");

            var nextReturnNo = await conn.ExecuteScalarAsync<long>(
                "SELECT COALESCE(MAX(purchase_return_no), 0) + 1 FROM pur_purchase_returns;", transaction: tran);

            decimal grossTotal = 0;
            var processedItems = new List<PurPurchaseReturnItem>();

            foreach (var line in payload.Items)
            {
                var product = await conn.QuerySingleOrDefaultAsync<ProdProduct>(@"
                    SELECT product_no, product_name, cost_per_box, purchase_price_per_piece, quantity_per_box
                    FROM prod_products
                    WHERE product_no = @ProductNo AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
                    new { line.ProductNo, CurrentPharmacyNo, CurrentBranchNo },
                    transaction: tran);

                if (product == null)
                    throw new InvalidOperationException($"Product #{line.ProductNo} not found.");

                var qty = line.ReturnQtyPcs > 0 ? line.ReturnQtyPcs : 1;
                var unitPrice = line.UnitPurchasePrice > 0 ? line.UnitPurchasePrice : product.PurchasePricePerPiece;
                var lineTotal = Math.Round(qty * unitPrice, 4);

                grossTotal += lineTotal;

                // Deduct stock from batch
                long? batchNo = line.BatchNo;
                if (!string.IsNullOrWhiteSpace(line.BatchNumber))
                {
                    var batch = await conn.QuerySingleOrDefaultAsync<ProdProductBatch>(@"
                        SELECT * FROM prod_product_batches
                        WHERE product_no = @ProductNo AND batch_number = @BatchNumber
                          AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
                        FOR UPDATE;",
                        new { line.ProductNo, line.BatchNumber, CurrentPharmacyNo, CurrentBranchNo },
                        transaction: tran);

                    if (batch != null)
                    {
                        batchNo = batch.BatchNo;
                        await conn.ExecuteAsync(@"
                            UPDATE prod_product_batches
                            SET total_quantity_pcs = GREATEST(0, total_quantity_pcs - @Qty),
                                box_quantity = GREATEST(0, (total_quantity_pcs - @Qty) / GREATEST(quantity_in_box, 1))
                            WHERE batch_no = @BatchNo;",
                            new { Qty = qty, batch.BatchNo },
                            transaction: tran);
                    }
                }

                // Append inv_stock_movement (type 5: Supplier Return, negative qty)
                await _stockPosting.PostMovementAsync(conn, tran, new StockMovementEntry
                {
                    PharmacyNo = CurrentPharmacyNo,
                    BranchNo = CurrentBranchNo,
                    ProductNo = line.ProductNo,
                    BatchNo = batchNo,
                    MovementType = 5, // Supplier Return
                    ReferenceDocType = "PUR_RETURN",
                    ReferenceDocNo = returnNumber,
                    ReferenceDocId = nextReturnNo,
                    QtyPcs = -qty,
                    UnitCost = unitPrice,
                    Remarks = $"Supplier return {returnNumber}"
                });

                line.ReturnQtyPcs = qty;
                line.UnitPurchasePrice = unitPrice;
                line.TotalReturnAmount = lineTotal;
                line.ProductName = product.ProductName;
                processedItems.Add(line);
            }

            var refundAmount = payload.RefundReceivedAmount;
            var dueAdjustment = Math.Max(0, grossTotal - refundAmount);

            // Insert pur_purchase_returns
            await conn.ExecuteAsync(@"
                INSERT INTO pur_purchase_returns (
                    purchase_return_no, pharmacy_no, branch_no, supplier_no, purchase_invoice_no,
                    return_number, total_items, gross_return_amount, refund_received_amount,
                    due_adjustment_amount, return_status, note, created_by_user_no,
                    return_date, created_at
                ) VALUES (
                    @PurchaseReturnNo, @PharmacyNo, @BranchNo, @SupplierNo, @PurchaseInvoiceNo,
                    @ReturnNumber, @TotalItems, @GrossReturnAmount, @RefundReceivedAmount,
                    @DueAdjustmentAmount, @ReturnStatus, @Note, @CreatedByUserNo,
                    @ReturnDate, @CreatedAt
                );",
                new
                {
                    PurchaseReturnNo = nextReturnNo,
                    PharmacyNo = CurrentPharmacyNo,
                    BranchNo = CurrentBranchNo,
                    payload.SupplierNo,
                    payload.PurchaseInvoiceNo,
                    ReturnNumber = returnNumber,
                    TotalItems = processedItems.Count,
                    GrossReturnAmount = grossTotal,
                    RefundReceivedAmount = refundAmount,
                    DueAdjustmentAmount = dueAdjustment,
                    ReturnStatus = (short)2, // Completed
                    payload.Note,
                    CreatedByUserNo = CurrentUserNo,
                    ReturnDate = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow
                },
                transaction: tran);

            // Insert items
            foreach (var item in processedItems)
            {
                var nextItemNo = await conn.ExecuteScalarAsync<long>(
                    "SELECT COALESCE(MAX(purchase_return_item_no), 0) + 1 FROM pur_purchase_return_items;", transaction: tran);

                await conn.ExecuteAsync(@"
                    INSERT INTO pur_purchase_return_items (
                        purchase_return_item_no, pharmacy_no, branch_no, purchase_return_no,
                        purchase_item_no, product_no, batch_no, batch_number,
                        return_qty_pcs, unit_purchase_price, total_return_amount, reason_code
                    ) VALUES (
                        @PurchaseReturnItemNo, @PharmacyNo, @BranchNo, @PurchaseReturnNo,
                        @PurchaseItemNo, @ProductNo, @BatchNo, @BatchNumber,
                        @ReturnQtyPcs, @UnitPurchasePrice, @TotalReturnAmount, @ReasonCode
                    );",
                    new
                    {
                        PurchaseReturnItemNo = nextItemNo,
                        PharmacyNo = CurrentPharmacyNo,
                        BranchNo = CurrentBranchNo,
                        PurchaseReturnNo = nextReturnNo,
                        item.PurchaseItemNo,
                        item.ProductNo,
                        item.BatchNo,
                        item.BatchNumber,
                        item.ReturnQtyPcs,
                        item.UnitPurchasePrice,
                        item.TotalReturnAmount,
                        item.ReasonCode
                    },
                    transaction: tran);
            }

            // Deduct supplier AP balance by due adjustment
            if (dueAdjustment > 0)
            {
                await conn.ExecuteAsync(@"
                    UPDATE supp_suppliers
                    SET current_due_balance = GREATEST(0, current_due_balance - @Adjustment),
                        updated_at = NOW()
                    WHERE supplier_no = @SupplierNo;",
                    new { Adjustment = dueAdjustment, payload.SupplierNo },
                    transaction: tran);
            }

            // Universal GL Posting:
            // Dr 20101 AP (for due adjustment)
            // Dr 10101 Cash / Bank (for refund received)
            // Cr 10401 Merchandise Inventory Asset (for gross return total)
            var invAssetAcc = await conn.QuerySingleOrDefaultAsync<long?>(
                "SELECT account_no FROM acc_transaction_accounts WHERE account_code = '10401' AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
                new { CurrentPharmacyNo, CurrentBranchNo }, transaction: tran) ?? 1;

            var apAcc = await conn.QuerySingleOrDefaultAsync<long?>(
                "SELECT account_no FROM acc_transaction_accounts WHERE account_code = '20101' AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
                new { CurrentPharmacyNo, CurrentBranchNo }, transaction: tran) ?? 1;

            var journalLegs = new List<LedgerLeg>();

            if (dueAdjustment > 0)
            {
                journalLegs.Add(new LedgerLeg
                {
                    AccountNo = apAcc,
                    DebitAmount = dueAdjustment,
                    CreditAmount = 0,
                    PartyType = 2,
                    PartyNo = payload.SupplierNo,
                    Narration = $"Debit note reduction on supplier return {returnNumber}"
                });
            }

            if (refundAmount > 0)
            {
                journalLegs.Add(new LedgerLeg
                {
                    AccountNo = 1, // Cash
                    DebitAmount = refundAmount,
                    CreditAmount = 0,
                    Narration = $"Cash refund received on supplier return {returnNumber}"
                });
            }

            journalLegs.Add(new LedgerLeg
            {
                AccountNo = invAssetAcc,
                DebitAmount = 0,
                CreditAmount = grossTotal,
                Narration = $"Inventory asset reduction on supplier return {returnNumber}"
            });

            await _ledgerPosting.PostJournalAsync(conn, tran, new CompoundJournalEntry
            {
                PharmacyNo = CurrentPharmacyNo,
                BranchNo = CurrentBranchNo,
                TransactionType = 4, // Supplier Return
                SourceDocumentType = "PUR_RETURN",
                SourceDocumentNo = returnNumber,
                SourceDocumentId = nextReturnNo,
                CreatedByUserNo = CurrentUserNo,
                Legs = journalLegs
            });

            tran.Commit();

            payload.PurchaseReturnNo = nextReturnNo;
            payload.ReturnNumber = returnNumber;
            payload.GrossReturnAmount = grossTotal;
            payload.DueAdjustmentAmount = dueAdjustment;
            payload.SupplierName = supplier.Name;
            payload.Items = processedItems;

            return OkResponse(payload, $"Supplier return {returnNumber} processed successfully.");
        }
        catch (Exception ex)
        {
            tran.Rollback();
            return FailResponse<PurPurchaseReturn>($"Return failed: {ex.Message}");
        }
    }
}

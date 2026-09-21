using AidlyPms.Api.Common;
using AidlyPms.Api.Common.Database;
using AidlyPms.Api.Infrastructure;
using AidlyPms.Api.Modules.Products.Models;
using AidlyPms.Api.Modules.Suppliers.Models;
using Dapper;
using Microsoft.AspNetCore.Mvc;

namespace AidlyPms.Api.Modules.Suppliers.Controllers;

[Route("api/v1/pur")]
public class PurchaseInvoiceController : BaseController
{
    private readonly IDbConnectionFactory _db;
    private readonly IDocumentNumberService _docNumService;
    private readonly IStockPostingService _stockPosting;
    private readonly ILedgerPostingService _ledgerPosting;

    public PurchaseInvoiceController(
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

    [HttpGet("invoices")]
    [HttpGet("purchases")]
    public async Task<ActionResult<ApiResponse<PagedResult<PurPurchaseInvoice>>>> GetPurchases(
        [FromQuery] QueryFilter filter,
        [FromQuery] string? search,
        [FromQuery] long? supplierNo,
        [FromQuery] short? inventoryType,
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate)
    {
        using var conn = _db.CreateConnection();
        var sql = @"
            SELECT p.*, s.name AS supplier_name, s.phone AS supplier_phone
            FROM pur_purchase_invoices p
            JOIN supp_suppliers s ON p.supplier_no = s.supplier_no
            WHERE p.pharmacy_no = @CurrentPharmacyNo AND p.branch_no = @CurrentBranchNo";

        var countSql = @"
            SELECT COUNT(1)
            FROM pur_purchase_invoices p
            JOIN supp_suppliers s ON p.supplier_no = s.supplier_no
            WHERE p.pharmacy_no = @CurrentPharmacyNo AND p.branch_no = @CurrentBranchNo";

        var pms = new DynamicParameters();
        pms.Add("CurrentPharmacyNo", CurrentPharmacyNo);
        pms.Add("CurrentBranchNo", CurrentBranchNo);

        if (!string.IsNullOrWhiteSpace(search))
        {
            sql += " AND (p.inventory_number ILIKE @Search OR p.supplier_invoice_no ILIKE @Search OR s.name ILIKE @Search)";
            countSql += " AND (p.inventory_number ILIKE @Search OR p.supplier_invoice_no ILIKE @Search OR s.name ILIKE @Search)";
            pms.Add("Search", $"%{search.Trim()}%");
        }

        if (supplierNo.HasValue)
        {
            sql += " AND p.supplier_no = @SupplierNo";
            countSql += " AND p.supplier_no = @SupplierNo";
            pms.Add("SupplierNo", supplierNo.Value);
        }

        if (inventoryType.HasValue)
        {
            sql += " AND p.inventory_type = @InventoryType";
            countSql += " AND p.inventory_type = @InventoryType";
            pms.Add("InventoryType", inventoryType.Value);
        }

        if (fromDate.HasValue)
        {
            sql += " AND p.purchase_date >= @FromDate";
            countSql += " AND p.purchase_date >= @FromDate";
            pms.Add("FromDate", fromDate.Value.ToUniversalTime());
        }

        if (toDate.HasValue)
        {
            sql += " AND p.purchase_date <= @ToDate";
            countSql += " AND p.purchase_date <= @ToDate";
            pms.Add("ToDate", toDate.Value.ToUniversalTime());
        }

        sql += " ORDER BY p.purchase_invoice_no DESC LIMIT @PageSize OFFSET @Offset;";
        pms.Add("PageSize", filter.PageSize);
        pms.Add("Offset", (filter.PageIndex - 1) * filter.PageSize);

        var total = await conn.ExecuteScalarAsync<int>(countSql, pms);
        var items = (await conn.QueryAsync<PurPurchaseInvoice>(sql, pms)).ToList();

        var result = new PagedResult<PurPurchaseInvoice>(items, total, filter.PageIndex, filter.PageSize);
        return PagedResponse(result);
    }

    [HttpGet("invoices/{id}")]
    [HttpGet("purchases/{id}")]
    public async Task<ActionResult<ApiResponse<PurPurchaseInvoice>>> GetPurchaseById(long id)
    {
        using var conn = _db.CreateConnection();
        var invoice = await conn.QuerySingleOrDefaultAsync<PurPurchaseInvoice>(@"
            SELECT p.*, s.name AS supplier_name, s.phone AS supplier_phone
            FROM pur_purchase_invoices p
            JOIN supp_suppliers s ON p.supplier_no = s.supplier_no
            WHERE p.purchase_invoice_no = @Id AND p.pharmacy_no = @CurrentPharmacyNo AND p.branch_no = @CurrentBranchNo;",
            new { Id = id, CurrentPharmacyNo, CurrentBranchNo });

        if (invoice == null)
            return FailResponse<PurPurchaseInvoice>("Purchase invoice not found.", statusCode: 404);

        var items = (await conn.QueryAsync<PurPurchaseInvoiceItem>(@"
            SELECT pi.*, pr.product_name
            FROM pur_purchase_invoice_items pi
            JOIN prod_products pr ON pi.product_no = pr.product_no
            WHERE pi.purchase_invoice_no = @Id;",
            new { Id = id })).ToList();

        invoice.Items = items;
        return OkResponse(invoice);
    }

    [HttpPost("invoices")]
    [HttpPost("purchases")]
    public async Task<ActionResult<ApiResponse<PurPurchaseInvoice>>> CreatePurchase([FromBody] CreatePurchasePayload payload)
    {
        if (payload.Items == null || payload.Items.Count == 0)
            return FailResponse<PurPurchaseInvoice>("At least one purchase item is required.");

        var inv = payload.Invoice ?? new PurPurchaseInvoice();
        var supplierNo = payload.SupplierNo ?? inv.SupplierNo;

        if (supplierNo <= 0)
            return FailResponse<PurPurchaseInvoice>("A valid supplier must be selected.");

        using var conn = _db.CreateConnection();
        conn.Open();
        using var tran = conn.BeginTransaction();

        try
        {
            var supplier = await conn.QuerySingleOrDefaultAsync<SuppSupplier>(@"
                SELECT * FROM supp_suppliers
                WHERE supplier_no = @SupplierNo AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
                FOR UPDATE;",
                new { SupplierNo = supplierNo, CurrentPharmacyNo, CurrentBranchNo },
                transaction: tran);

            if (supplier == null)
                throw new InvalidOperationException($"Supplier #{supplierNo} does not exist in this pharmacy branch.");

            var inventoryNumber = await _docNumService.GetNextDocumentNumberAsync(conn, tran, CurrentPharmacyNo, CurrentBranchNo, "PUR_INVOICE");

            var nextInvoiceNo = await conn.ExecuteScalarAsync<long>(
                "SELECT COALESCE(MAX(purchase_invoice_no), 0) + 1 FROM pur_purchase_invoices;", transaction: tran);

            decimal grossTotal = 0;
            decimal totalVat = 0;
            var processedItems = new List<PurPurchaseInvoiceItem>();

            foreach (var line in payload.Items)
            {
                var product = await conn.QuerySingleOrDefaultAsync<ProdProduct>(@"
                    SELECT * FROM prod_products
                    WHERE product_no = @ProductNo AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
                    FOR UPDATE;",
                    new { line.ProductNo, CurrentPharmacyNo, CurrentBranchNo },
                    transaction: tran);

                if (product == null)
                    throw new InvalidOperationException($"Product #{line.ProductNo} not found in this pharmacy branch.");

                var boxQty = line.BoxQty > 0 ? line.BoxQty : 1;
                var qtyInBox = line.QtyInBox > 0 ? line.QtyInBox : (product.QuantityPerBox > 0 ? product.QuantityPerBox : 1);
                var totalQtyPcs = line.TotalQtyPcs > 0 ? line.TotalQtyPcs : (boxQty * qtyInBox);

                var btp = line.Btp > 0 ? line.Btp : (product.CostPerBox > 0 ? product.CostPerBox : 0);
                var vatPercent = line.VatPercent >= 0 ? line.VatPercent : 0m;
                var lineGross = Math.Round(boxQty * btp, 4);
                var lineVat = Math.Round(lineGross * (vatPercent / 100m), 4);
                var lineTotal = lineGross + lineVat;
                var unitCost = totalQtyPcs > 0 ? Math.Round(lineTotal / totalQtyPcs, 4) : 0;
                var salePrice = line.SalePrice > 0 ? line.SalePrice : product.SalePricePerPiece;

                grossTotal += lineGross;
                totalVat += lineVat;

                var batchNum = !string.IsNullOrWhiteSpace(line.BatchNumber)
                    ? line.BatchNumber.Trim()
                    : $"B-{DateTime.UtcNow:yyyyMMdd}-{line.ProductNo}";

                var expiryDate = line.ExpiryDate ?? DateTime.UtcNow.AddYears(2).Date;

                // Create or increment batch in prod_product_batches
                var batch = await conn.QuerySingleOrDefaultAsync<ProdProductBatch>(@"
                    SELECT * FROM prod_product_batches
                    WHERE product_no = @ProductNo AND batch_number = @BatchNumber
                      AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
                    FOR UPDATE;",
                    new { line.ProductNo, BatchNumber = batchNum, CurrentPharmacyNo, CurrentBranchNo },
                    transaction: tran);

                long batchNo;
                if (batch == null)
                {
                    batchNo = await conn.ExecuteScalarAsync<long>(
                        "SELECT COALESCE(MAX(batch_no), 0) + 1 FROM prod_product_batches;", transaction: tran);

                    await conn.ExecuteAsync(@"
                        INSERT INTO prod_product_batches (
                            batch_no, pharmacy_no, branch_no, product_no, batch_number,
                            expiry_date, box_quantity, quantity_in_box, total_quantity_pcs,
                            cost_per_box, sale_price_per_box, vat_percent, is_expired,
                            created_at, updated_at
                        ) VALUES (
                            @BatchNo, @PharmacyNo, @BranchNo, @ProductNo, @BatchNumber,
                            @ExpiryDate, @BoxQuantity, @QuantityInBox, @TotalQuantityPcs,
                            @CostPerBox, @SalePricePerBox, @VatPercent, FALSE,
                            NOW(), NOW()
                        );",
                        new
                        {
                            BatchNo = batchNo,
                            PharmacyNo = CurrentPharmacyNo,
                            BranchNo = CurrentBranchNo,
                            line.ProductNo,
                            BatchNumber = batchNum,
                            ExpiryDate = expiryDate,
                            BoxQuantity = boxQty,
                            QuantityInBox = qtyInBox,
                            TotalQuantityPcs = totalQtyPcs,
                            CostPerBox = btp,
                            SalePricePerBox = salePrice * qtyInBox,
                            VatPercent = vatPercent
                        },
                        transaction: tran);
                }
                else
                {
                    batchNo = batch.BatchNo;
                    await conn.ExecuteAsync(@"
                        UPDATE prod_product_batches
                        SET total_quantity_pcs = total_quantity_pcs + @QtyPcs,
                            box_quantity = (total_quantity_pcs + @QtyPcs) / GREATEST(quantity_in_box, 1),
                            cost_per_box = @CostPerBox,
                            sale_price_per_box = @SalePricePerBox,
                            expiry_date = @ExpiryDate,
                            updated_at = NOW()
                        WHERE batch_no = @BatchNo;",
                        new
                        {
                            QtyPcs = totalQtyPcs,
                            CostPerBox = btp,
                            SalePricePerBox = salePrice * qtyInBox,
                            ExpiryDate = expiryDate,
                            BatchNo = batchNo
                        },
                        transaction: tran);
                }

                // Update Moving Average Cost (MAC) on master product
                var existingStock = await conn.ExecuteScalarAsync<int>(@"
                    SELECT COALESCE(SUM(total_quantity_pcs), 0) FROM prod_product_batches
                    WHERE product_no = @ProductNo AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
                    new { line.ProductNo, CurrentPharmacyNo, CurrentBranchNo },
                    transaction: tran);

                // stock before this batch was added
                var prevStock = Math.Max(0, existingStock - totalQtyPcs);
                var oldMac = product.AvgPurchasePricePerPiece > 0 ? product.AvgPurchasePricePerPiece : product.PurchasePricePerPiece;
                var totalPiecesAfter = prevStock + totalQtyPcs;
                var newMac = totalPiecesAfter > 0
                    ? Math.Round(((prevStock * oldMac) + (totalQtyPcs * unitCost)) / totalPiecesAfter, 4)
                    : unitCost;

                await conn.ExecuteAsync(@"
                    UPDATE prod_products
                    SET avg_purchase_price_per_piece = @NewMac,
                        purchase_price_per_piece = @UnitCost,
                        cost_per_box = @CostPerBox,
                        sale_price_per_box = @SalePricePerBox,
                        sale_price_per_piece = @SalePricePerPiece
                    WHERE product_no = @ProductNo;",
                    new
                    {
                        NewMac = newMac,
                        UnitCost = unitCost,
                        CostPerBox = btp,
                        SalePricePerBox = salePrice * qtyInBox,
                        SalePricePerPiece = salePrice,
                        line.ProductNo
                    },
                    transaction: tran);

                // Append stock ledger (inv_stock_movement - type 1: Purchase or 2: Opening)
                short mvtType = (payload.InventoryType ?? inv.InventoryType) == 2 ? (short)2 : (short)1;
                await _stockPosting.PostMovementAsync(conn, tran, new StockMovementEntry
                {
                    PharmacyNo = CurrentPharmacyNo,
                    BranchNo = CurrentBranchNo,
                    ProductNo = line.ProductNo,
                    BatchNo = batchNo,
                    MovementType = mvtType,
                    ReferenceDocType = "PUR_INVOICE",
                    ReferenceDocNo = inventoryNumber,
                    ReferenceDocId = nextInvoiceNo,
                    QtyPcs = totalQtyPcs,
                    UnitCost = unitCost,
                    Remarks = $"Purchase GRN {inventoryNumber}"
                });

                line.BoxQty = boxQty;
                line.QtyInBox = qtyInBox;
                line.TotalQtyPcs = totalQtyPcs;
                line.Btp = btp;
                line.VatPercent = vatPercent;
                line.VatAmount = lineVat;
                line.BtpVat = lineGross + lineVat;
                line.UnitCost = unitCost;
                line.SalePrice = salePrice;
                line.TotalPrice = lineTotal;
                line.BatchNumber = batchNum;
                line.ExpiryDate = expiryDate;
                line.ProductName = product.ProductName;
                processedItems.Add(line);
            }

            // Calculation mode and discounts
            var discountType = payload.DiscountType ?? inv.DiscountType;
            var discountVal = payload.DiscountValue ?? inv.DiscountValue;
            var discountAmount = discountType == 2
                ? discountVal
                : Math.Round(grossTotal * (discountVal / 100m), 4);

            var netAmount = Math.Max(0, grossTotal - discountAmount);
            var finalPrice = Math.Round(netAmount + totalVat, 4);
            var paidAmount = payload.PaidAmount ?? inv.PaidAmount;
            if (paidAmount < 0) paidAmount = 0;
            if (paidAmount > finalPrice) paidAmount = finalPrice;
            var dueAmount = Math.Round(finalPrice - paidAmount, 4);

            var calculationMode = payload.CalculationMode ?? inv.CalculationMode;
            var inventoryTypeVal = payload.InventoryType ?? inv.InventoryType;
            var supplierInvoiceNo = payload.SupplierInvoiceNo ?? inv.SupplierInvoiceNo;
            var paymentAccountNo = payload.PaymentAccountNo ?? 1;

            // Insert purchase header
            await conn.ExecuteAsync(@"
                INSERT INTO pur_purchase_invoices (
                    purchase_invoice_no, uuid, pharmacy_no, branch_no, supplier_no,
                    inventory_number, calculation_mode, inventory_type, supplier_invoice_no,
                    total_items, gross_total_price, discount_type, discount_value,
                    net_amount, vat_amount, round_off_amount, final_price,
                    paid_amount, due_amount, document_status, created_by_user_no,
                    purchase_date, created_at, updated_at
                ) VALUES (
                    @PurchaseInvoiceNo, @Uuid, @PharmacyNo, @BranchNo, @SupplierNo,
                    @InventoryNumber, @CalculationMode, @InventoryType, @SupplierInvoiceNo,
                    @TotalItems, @GrossTotalPrice, @DiscountType, @DiscountValue,
                    @NetAmount, @VatAmount, @RoundOffAmount, @FinalPrice,
                    @PaidAmount, @DueAmount, @DocumentStatus, @CreatedByUserNo,
                    @PurchaseDate, @CreatedAt, @UpdatedAt
                );",
                new
                {
                    PurchaseInvoiceNo = nextInvoiceNo,
                    Uuid = Guid.NewGuid(),
                    PharmacyNo = CurrentPharmacyNo,
                    BranchNo = CurrentBranchNo,
                    SupplierNo = supplierNo,
                    InventoryNumber = inventoryNumber,
                    CalculationMode = calculationMode,
                    InventoryType = inventoryTypeVal,
                    SupplierInvoiceNo = supplierInvoiceNo,
                    TotalItems = processedItems.Count,
                    GrossTotalPrice = grossTotal,
                    DiscountType = discountType,
                    DiscountValue = discountVal,
                    NetAmount = netAmount,
                    VatAmount = totalVat,
                    RoundOffAmount = 0m,
                    FinalPrice = finalPrice,
                    PaidAmount = paidAmount,
                    DueAmount = dueAmount,
                    DocumentStatus = (short)2, // Confirmed
                    CreatedByUserNo = CurrentUserNo,
                    PurchaseDate = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                transaction: tran);

            // Insert purchase items
            foreach (var item in processedItems)
            {
                var nextItemNo = await conn.ExecuteScalarAsync<long>(
                    "SELECT COALESCE(MAX(purchase_item_no), 0) + 1 FROM pur_purchase_invoice_items;", transaction: tran);

                await conn.ExecuteAsync(@"
                    INSERT INTO pur_purchase_invoice_items (
                        purchase_item_no, pharmacy_no, branch_no, purchase_invoice_no,
                        product_no, batch_number, box_qty, qty_in_box, total_qty_pcs,
                        free_qty_pcs, btp, vat_percent, vat_amount, btp_vat,
                        unit_cost, sale_price, total_price, expiry_date, min_qty_pcs, barcode_value
                    ) VALUES (
                        @PurchaseItemNo, @PharmacyNo, @BranchNo, @PurchaseInvoiceNo,
                        @ProductNo, @BatchNumber, @BoxQty, @QtyInBox, @TotalQtyPcs,
                        @FreeQtyPcs, @Btp, @VatPercent, @VatAmount, @BtpVat,
                        @UnitCost, @SalePrice, @TotalPrice, @ExpiryDate, @MinQtyPcs, @BarcodeValue
                    );",
                    new
                    {
                        PurchaseItemNo = nextItemNo,
                        PharmacyNo = CurrentPharmacyNo,
                        BranchNo = CurrentBranchNo,
                        PurchaseInvoiceNo = nextInvoiceNo,
                        item.ProductNo,
                        item.BatchNumber,
                        item.BoxQty,
                        item.QtyInBox,
                        item.TotalQtyPcs,
                        FreeQtyPcs = item.FreeQtyPcs,
                        item.Btp,
                        item.VatPercent,
                        item.VatAmount,
                        item.BtpVat,
                        item.UnitCost,
                        item.SalePrice,
                        item.TotalPrice,
                        item.ExpiryDate,
                        item.MinQtyPcs,
                        item.BarcodeValue
                    },
                    transaction: tran);
            }

            // Update supplier due balance
            if (dueAmount > 0)
            {
                await conn.ExecuteAsync(@"
                    UPDATE supp_suppliers
                    SET current_due_balance = current_due_balance + @Due,
                        updated_at = NOW()
                    WHERE supplier_no = @SupplierNo;",
                    new { Due = dueAmount, SupplierNo = supplierNo },
                    transaction: tran);
            }

            // Synchronous General Ledger Posting (acc_transaction_ledger)
            var invAssetAcc = await conn.QuerySingleOrDefaultAsync<long?>(
                "SELECT account_no FROM acc_transaction_accounts WHERE account_code = '10401' AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
                new { CurrentPharmacyNo, CurrentBranchNo }, transaction: tran) ?? 1;

            var apAcc = await conn.QuerySingleOrDefaultAsync<long?>(
                "SELECT account_no FROM acc_transaction_accounts WHERE account_code = '20101' AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
                new { CurrentPharmacyNo, CurrentBranchNo }, transaction: tran) ?? 1;

            var inputVatAcc = await conn.QuerySingleOrDefaultAsync<long?>(
                "SELECT account_no FROM acc_transaction_accounts WHERE account_code = '10501' AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
                new { CurrentPharmacyNo, CurrentBranchNo }, transaction: tran) ?? 1;

            var journalLegs = new List<LedgerLeg>();

            // Dr 10401 Merchandise Inventory Asset (Goods value net of discount)
            if (netAmount > 0)
            {
                journalLegs.Add(new LedgerLeg
                {
                    AccountNo = invAssetAcc,
                    DebitAmount = netAmount,
                    CreditAmount = 0,
                    Narration = $"Inventory receipt for purchase {inventoryNumber}"
                });
            }

            // Dr 10501 Input VAT Asset (if any)
            if (totalVat > 0)
            {
                journalLegs.Add(new LedgerLeg
                {
                    AccountNo = inputVatAcc,
                    DebitAmount = totalVat,
                    CreditAmount = 0,
                    Narration = $"Input VAT asset for purchase {inventoryNumber}"
                });
            }

            // Cr 20101 Accounts Payable (AP) for due amount
            if (dueAmount > 0)
            {
                journalLegs.Add(new LedgerLeg
                {
                    AccountNo = apAcc,
                    DebitAmount = 0,
                    CreditAmount = dueAmount,
                    PartyType = 2, // Supplier
                    PartyNo = supplierNo,
                    Narration = $"Payable to {supplier.Name} on purchase {inventoryNumber}"
                });
            }

            // Cr Cash / Bank account for paid amount
            if (paidAmount > 0)
            {
                journalLegs.Add(new LedgerLeg
                {
                    AccountNo = paymentAccountNo,
                    DebitAmount = 0,
                    CreditAmount = paidAmount,
                    PartyType = 2, // Supplier
                    PartyNo = supplierNo,
                    Narration = $"Cash/Bank disbursement on purchase {inventoryNumber}"
                });
            }

            await _ledgerPosting.PostJournalAsync(conn, tran, new CompoundJournalEntry
            {
                PharmacyNo = CurrentPharmacyNo,
                BranchNo = CurrentBranchNo,
                TransactionType = 3, // Purchase Bill
                SourceDocumentType = "PUR_INVOICE",
                SourceDocumentNo = inventoryNumber,
                SourceDocumentId = nextInvoiceNo,
                CreatedByUserNo = CurrentUserNo,
                Legs = journalLegs
            });

            tran.Commit();

            var savedInvoice = new PurPurchaseInvoice
            {
                PurchaseInvoiceNo = nextInvoiceNo,
                PharmacyNo = CurrentPharmacyNo,
                BranchNo = CurrentBranchNo,
                SupplierNo = supplierNo,
                SupplierName = supplier.Name,
                InventoryNumber = inventoryNumber,
                CalculationMode = calculationMode,
                InventoryType = inventoryTypeVal,
                SupplierInvoiceNo = supplierInvoiceNo,
                TotalItems = processedItems.Count,
                GrossTotalPrice = grossTotal,
                DiscountType = discountType,
                DiscountValue = discountVal,
                NetAmount = netAmount,
                VatAmount = totalVat,
                FinalPrice = finalPrice,
                PaidAmount = paidAmount,
                DueAmount = dueAmount,
                DocumentStatus = 2,
                PurchaseDate = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                Items = processedItems
            };

            return OkResponse(savedInvoice, $"Purchase invoice {inventoryNumber} registered successfully.");
        }
        catch (Exception ex)
        {
            tran.Rollback();
            return FailResponse<PurPurchaseInvoice>($"Purchase failed: {ex.Message}");
        }
    }
}

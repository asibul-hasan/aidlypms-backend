using AidlyPms.Api.Common;
using AidlyPms.Api.Infrastructure;
using AidlyPms.Api.Modules.Products.Models;
using AidlyPms.Api.Modules.Sales.Models;
using AidlyPms.Api.Modules.Settings.Models;
using Dapper;
using Npgsql;

namespace AidlyPms.Api.Modules.Sales.Services;

public class SalePostingEngine : ISalePostingEngine
{
    private readonly IDocumentNumberService _docNumService;
    private readonly IStockPostingService _stockPosting;
    private readonly ILedgerPostingService _ledgerPosting;
    private readonly IPaymentAccountResolver _accountResolver;

    public SalePostingEngine(
        IDocumentNumberService docNumService,
        IStockPostingService stockPosting,
        ILedgerPostingService ledgerPosting,
        IPaymentAccountResolver accountResolver)
    {
        _docNumService = docNumService;
        _stockPosting = stockPosting;
        _ledgerPosting = ledgerPosting;
        _accountResolver = accountResolver;
    }

    public async Task<SalePostingResult> ExecuteSaleAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tran,
        SalePostingRequest request)
    {
        var pharmacyNo = request.PharmacyNo;
        var branchNo = request.BranchNo;
        var userNo = request.UserNo;
        var invoice = request.Invoice;
        var items = request.Items;
        var rawPayments = request.Payments ?? new List<SaleInvoicePayment>();

        if (items.Count == 0)
        {
            throw new InvalidOperationException("A sale invoice must contain at least one item.");
        }

        var saleConfig = await conn.QueryFirstOrDefaultAsync<CfgSaleConfig>(@"
            SELECT * FROM cfg_sale_configs
            WHERE pharmacy_no = @pharmacyNo AND branch_no = @branchNo;",
            new { pharmacyNo, branchNo },
            transaction: tran);

        var allowOutOfStock = saleConfig?.OutOfStockSale ?? true;

        // Monotonic document number
        var salesNumber = await _docNumService.GetNextDocumentNumberAsync(
            conn, tran, pharmacyNo, branchNo, "SALE_INVOICE");

        var saleInvoiceNo = await conn.ExecuteScalarAsync<long>(
            "SELECT nextval('seq_sale_invoice_no');", transaction: tran);

        decimal grossTotal = 0;
        decimal totalVat = 0;
        decimal totalCogs = 0;

        var isOffline = request.IsOffline || invoice.IsOffline;
        bool lateSync = false;
        DateTime saleTimestamp;
        DateTime? clientCreatedAtUtc = null;

        if (isOffline)
        {
            if (saleConfig?.OfflinePosEnabled != true)
            {
                throw new PosSaleException("offline_pos_disabled",
                    "Offline POS sales are disabled for this branch in settings.",
                    StatusCodes.Status400BadRequest);
            }

            var clientCreatedAt = request.ClientCreatedAt ?? (invoice.ClientCreatedAt.HasValue
                ? DateHelper.ToUtcOffset(invoice.ClientCreatedAt.Value)
                : (DateTimeOffset?)null);

            if (!clientCreatedAt.HasValue)
            {
                throw new PosSaleException("client_created_at_required",
                    "Client timestamp is required for offline sales.",
                    StatusCodes.Status400BadRequest);
            }

            if (clientCreatedAt.Value > DateTimeOffset.UtcNow.AddMinutes(5))
            {
                throw new PosSaleException("future_timestamp",
                    "Client timestamp cannot be in the future (exceeds 5 minutes threshold).",
                    StatusCodes.Status400BadRequest);
            }

            var maxAgeHours = saleConfig.OfflineMaxAgeHours > 0 ? saleConfig.OfflineMaxAgeHours : 72;
            if (clientCreatedAt.Value < DateTimeOffset.UtcNow.AddHours(-maxAgeHours))
            {
                throw new PosSaleException("offline_sale_too_old",
                    $"Offline sale exceeds maximum age limit of {maxAgeHours} hours.",
                    StatusCodes.Status400BadRequest);
            }

            clientCreatedAtUtc = clientCreatedAt.Value.UtcDateTime;

            // Check if cash shift for that Dhaka date has been closed
            var dhakaDateTime = TimeZoneInfo.ConvertTime(clientCreatedAt.Value, DateHelper.DhakaTimeZone);
            var dhakaDate = DateOnly.FromDateTime(dhakaDateTime.DateTime);

            var isShiftClosed = await conn.ExecuteScalarAsync<bool>(@"
                SELECT EXISTS (
                    SELECT 1 FROM cash_closers
                    WHERE pharmacy_no = @pharmacyNo
                      AND branch_no = @branchNo
                      AND closing_date >= @closingDate
                );",
                new { pharmacyNo, branchNo, closingDate = dhakaDate.ToDateTime(TimeOnly.MinValue) },
                transaction: tran);

            if (isShiftClosed)
            {
                lateSync = true;
                saleTimestamp = DateTime.UtcNow;
            }
            else
            {
                lateSync = false;
                saleTimestamp = clientCreatedAt.Value.UtcDateTime;
            }
        }
        else
        {
            isOffline = false;
            lateSync = false;
            saleTimestamp = DateTime.UtcNow;
        }

        var processedItems = new List<SaleInvoiceItem>();

        foreach (var line in items)
        {
            var product = await conn.QuerySingleOrDefaultAsync<ProdProduct>(@"
                SELECT product_no, product_name, cost_per_box, sale_price_per_box,
                       purchase_price_per_piece, sale_price_per_piece, wholesale_price_per_piece,
                       quantity_per_box, rak_number, is_prescription_required, is_narcotic
                FROM prod_products
                WHERE product_no = @ProductNo AND pharmacy_no = @pharmacyNo AND branch_no = @branchNo;",
                new { line.ProductNo, pharmacyNo, branchNo },
                transaction: tran);

            // Item 5: Product resolution strictly by product_no or batch_no (zero client price lookup)
            if (product == null && line.BatchNo.HasValue && line.BatchNo.Value > 0)
            {
                product = await conn.QuerySingleOrDefaultAsync<ProdProduct>(@"
                    SELECT p.product_no, p.product_name, p.cost_per_box, p.sale_price_per_box,
                           p.purchase_price_per_piece, p.sale_price_per_piece, p.wholesale_price_per_piece,
                           p.quantity_per_box, p.rak_number, p.is_prescription_required, p.is_narcotic
                    FROM prod_product_batches b
                    JOIN prod_products p ON b.product_no = p.product_no AND b.pharmacy_no = p.pharmacy_no
                    WHERE b.batch_no = @batchNo AND b.pharmacy_no = @pharmacyNo AND b.branch_no = @branchNo;",
                    new { batchNo = line.BatchNo.Value, pharmacyNo, branchNo },
                    transaction: tran);

                if (product != null)
                {
                    line.ProductNo = product.ProductNo;
                }
            }

            if (product == null)
            {
                throw new InvalidOperationException($"Product #{line.ProductNo} not found in this pharmacy branch.");
            }

            // Offline check: Narcotic/Rx items are strictly prohibited offline
            if (isOffline && (product.IsNarcotic || product.IsPrescriptionRequired))
            {
                throw new PosSaleException("offline_not_allowed",
                    $"Offline sale of narcotic or prescription-required medicine '{product.ProductName}' is prohibited. Needs internet.",
                    StatusCodes.Status400BadRequest);
            }

            // Item 3: Prescription check on POS sale - PrescriptionNo must exist in doc_prescriptions for the same pharmacy.
            // Reject missing, foreign or fake numbers. No bypass for any role.
            if (product.IsNarcotic || product.IsPrescriptionRequired)
            {
                var drugType = product.IsNarcotic ? "Narcotic" : "Prescription-only";
                if (!invoice.PrescriptionNo.HasValue || invoice.PrescriptionNo.Value <= 0)
                {
                    throw new InvalidOperationException(
                        $"{drugType} medicine '{product.ProductName}' requires an authentic prescription number.");
                }

                var prescriptionExists = await conn.ExecuteScalarAsync<bool>(@"
                    SELECT EXISTS (
                        SELECT 1 FROM doc_prescriptions
                        WHERE prescription_no = @PrescriptionNo AND pharmacy_no = @pharmacyNo
                    );",
                    new { invoice.PrescriptionNo, pharmacyNo },
                    transaction: tran);

                if (!prescriptionExists)
                {
                    throw new InvalidOperationException(
                        $"{drugType} medicine '{product.ProductName}' requires an authentic prescription registered with this pharmacy. Prescription #{invoice.PrescriptionNo} does not exist or belongs to another pharmacy.");
                }
            }

            var isWholesaleLine = invoice.SaleMode == 2 || line.SaleMode == 2;
            var pcsPerBoxVal = line.PcsPerBox > 0 ? line.PcsPerBox : (product.QuantityPerBox > 0 ? product.QuantityPerBox : 1);
            var qtyPcs = line.SaleQty > 0 ? line.SaleQty : (line.TotalQuantityPcs > 0 ? line.TotalQuantityPcs : 1);
            if (isWholesaleLine && line.BoxQty > 0 && line.SaleQty == line.BoxQty && pcsPerBoxVal > 1)
            {
                qtyPcs = line.BoxQty * pcsPerBoxVal;
            }

            // Item 3: DB Authoritative price - client SalePrice, UnitSalePrice, TotalPrice are ignored
            var unitSalePrice = isWholesaleLine && product.WholesalePricePerPiece > 0
                ? product.WholesalePricePerPiece
                : product.SalePricePerPiece;
            if (unitSalePrice <= 0 && product.SalePricePerBox > 0 && pcsPerBoxVal > 0)
            {
                unitSalePrice = Math.Round(product.SalePricePerBox / pcsPerBoxVal, 4);
            }

            // Retrieve available real unexpired batches with stock (FEFO order, excluding placeholders)
            var availableRealBatches = (await conn.QueryAsync<ProdProductBatch>(@"
                SELECT batch_no, batch_number, expiry_date, cost_per_box, quantity_in_box, total_quantity_pcs, vat_percent
                FROM prod_product_batches
                WHERE product_no = @ProductNo AND pharmacy_no = @pharmacyNo AND branch_no = @branchNo
                  AND is_placeholder = FALSE AND is_expired = FALSE AND expiry_date >= CURRENT_DATE AND total_quantity_pcs > 0
                ORDER BY expiry_date ASC, batch_no ASC
                FOR UPDATE;",
                new { line.ProductNo, pharmacyNo, branchNo },
                transaction: tran)).ToList();

            if (line.BatchNo.HasValue && line.BatchNo.Value > 0)
            {
                var matching = availableRealBatches.FirstOrDefault(b => b.BatchNo == line.BatchNo.Value);
                if (matching != null)
                {
                    availableRealBatches = new List<ProdProductBatch> { matching };
                }
                else
                {
                    var specified = await conn.QueryFirstOrDefaultAsync<ProdProductBatch>(@"
                        SELECT batch_no, batch_number, expiry_date, total_quantity_pcs, is_placeholder, is_expired
                        FROM prod_product_batches
                        WHERE batch_no = @bNo AND pharmacy_no = @pharmacyNo AND branch_no = @branchNo;",
                        new { bNo = line.BatchNo.Value, pharmacyNo, branchNo }, transaction: tran);
                    if (specified != null && !specified.IsPlaceholder && specified.ExpiryDate.Date < DateTime.UtcNow.Date)
                    {
                        throw new InvalidOperationException($"Clinical Safety Block: Batch '{specified.BatchNumber}' expired on {specified.ExpiryDate:yyyy-MM-dd} and cannot be dispensed.");
                    }
                }
            }

            var totalRealAvailable = availableRealBatches.Sum(b => b.TotalQuantityPcs);

            if (qtyPcs > totalRealAvailable && !allowOutOfStock)
            {
                throw new PosSaleException("insufficient_stock",
                    $"Medicine '{product.ProductName}' has insufficient stock (available: {totalRealAvailable} pcs, requested: {qtyPcs} pcs), and out-of-stock sales are disabled in settings.",
                    StatusCodes.Status409Conflict);
            }

            // Split needed quantity across real batches (FEFO) and placeholder (remainder)
            var allocations = new List<(long batchNo, string batchNumber, decimal batchCost, decimal batchVatPercent, int allocatedQty, bool isPlaceholder)>();
            var remainingToAllocate = qtyPcs;

            foreach (var rb in availableRealBatches)
            {
                if (remainingToAllocate <= 0) break;
                var take = Math.Min(rb.TotalQuantityPcs, remainingToAllocate);
                if (take > 0)
                {
                    var bCost = (rb.QuantityInBox > 0 && rb.CostPerBox > 0)
                        ? Math.Round(rb.CostPerBox / rb.QuantityInBox, 4)
                        : product.PurchasePricePerPiece;
                    allocations.Add((rb.BatchNo, rb.BatchNumber, bCost, rb.VatPercent, take, false));
                    remainingToAllocate -= take;
                }
            }

            if (remainingToAllocate > 0 && allowOutOfStock)
            {
                // Remainder goes to ONE placeholder batch per product (batch_number 'UNRECEIVED', expiry_date = sale date, is_placeholder = true)
                var placeholder = await conn.QueryFirstOrDefaultAsync<ProdProductBatch>(@"
                    SELECT batch_no, batch_number, expiry_date, cost_per_box, quantity_in_box, total_quantity_pcs, vat_percent
                    FROM prod_product_batches
                    WHERE product_no = @ProductNo AND pharmacy_no = @pharmacyNo AND branch_no = @branchNo
                      AND is_placeholder = TRUE
                    LIMIT 1
                    FOR UPDATE;",
                    new { line.ProductNo, pharmacyNo, branchNo },
                    transaction: tran);

                long pBatchNo;
                string pBatchNum = "UNRECEIVED";
                decimal pCost = product.PurchasePricePerPiece;
                decimal pVat = 0m;

                if (placeholder != null)
                {
                    pBatchNo = placeholder.BatchNo;
                    pBatchNum = placeholder.BatchNumber;
                    if (placeholder.QuantityInBox > 0 && placeholder.CostPerBox > 0)
                    {
                        pCost = Math.Round(placeholder.CostPerBox / placeholder.QuantityInBox, 4);
                    }
                    await conn.ExecuteAsync(@"
                        UPDATE prod_product_batches
                        SET expiry_date = @saleDate, updated_at = NOW()
                        WHERE batch_no = @pBatchNo;",
                        new { saleDate = saleTimestamp.Date, pBatchNo }, transaction: tran);
                }
                else
                {
                    pBatchNo = await conn.ExecuteScalarAsync<long>(
                        "SELECT nextval('seq_prod_batch_no');", transaction: tran);
                    var qInBox = product.QuantityPerBox > 0 ? product.QuantityPerBox : 10;
                    await conn.ExecuteAsync(@"
                        INSERT INTO prod_product_batches (
                            batch_no, pharmacy_no, branch_no, product_no, batch_number,
                            expiry_date, box_quantity, quantity_in_box, total_quantity_pcs,
                            cost_per_box, sale_price_per_box, vat_percent, is_expired, is_placeholder,
                            created_at, updated_at
                        ) VALUES (
                            @pBatchNo, @pharmacyNo, @branchNo, @ProductNo, 'UNRECEIVED',
                            @saleDate, 0, @qInBox, 0,
                            @CostPerBox, @SalePricePerBox, 0, FALSE, TRUE,
                            CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
                        );",
                        new
                        {
                            pBatchNo,
                            pharmacyNo,
                            branchNo,
                            line.ProductNo,
                            saleDate = saleTimestamp.Date,
                            qInBox,
                            CostPerBox = product.CostPerBox > 0 ? product.CostPerBox : 0m,
                            SalePricePerBox = product.SalePricePerBox > 0 ? product.SalePricePerBox : 0m
                        },
                        transaction: tran);
                }

                allocations.Add((pBatchNo, pBatchNum, pCost, pVat, remainingToAllocate, true));
            }

            // Cap line discount using cfg_sale_configs and ignore client VAT
            var maxDiscountPct = (saleConfig?.IsMaxDiscountEnabled == true)
                ? (saleConfig.MaxSaleDiscountPercent > 0 ? saleConfig.MaxSaleDiscountPercent : 20m)
                : 100m;

            foreach (var alloc in allocations)
            {
                var allocQty = alloc.allocatedQty;
                var lineGross = Math.Round(allocQty * unitSalePrice, 4);

                var saleItemNo = await conn.ExecuteScalarAsync<long>(
                    "SELECT nextval('seq_sale_invoice_item_no');", transaction: tran);

                var requestedDiscount = (qtyPcs > 0 && line.DiscountAmount > 0)
                    ? Math.Round(line.DiscountAmount * (allocQty / (decimal)qtyPcs), 4)
                    : 0m;
                var maxAllowedLineDiscount = Math.Round(lineGross * (maxDiscountPct / 100m), 4);
                var lineDiscount = Math.Min(requestedDiscount, maxAllowedLineDiscount);
                var lineNet = Math.Max(0, lineGross - lineDiscount);

                var effectiveUnitPrice = allocQty > 0 ? (lineNet / allocQty) : 0m;
                if (saleConfig?.AllowLossSale != true && effectiveUnitPrice < alloc.batchCost)
                {
                    throw new InvalidOperationException(
                        $"Sale price ৳{effectiveUnitPrice:F2} for '{product.ProductName}' is below batch cost ৳{alloc.batchCost:F2}. Loss sales are prohibited.");
                }

                var lineVat = Math.Round(lineNet * (alloc.batchVatPercent / 100m), 4);
                var lineTotal = lineNet + lineVat;
                var lineCogs = Math.Round(allocQty * alloc.batchCost, 4);

                grossTotal += lineGross;
                totalVat += lineVat;
                totalCogs += lineCogs;

                processedItems.Add(new SaleInvoiceItem
                {
                    SaleInvoiceItemNo = saleItemNo,
                    PharmacyNo = pharmacyNo,
                    BranchNo = branchNo,
                    SaleInvoiceNo = saleInvoiceNo,
                    ProductNo = line.ProductNo,
                    BatchNo = alloc.batchNo,
                    SaleMode = isWholesaleLine ? (short)2 : (short)1,
                    SaleQty = allocQty,
                    BoxQty = line.BoxQty,
                    PcsPerBox = pcsPerBoxVal,
                    UnitCostPrice = alloc.batchCost,
                    UnitSalePrice = unitSalePrice,
                    DiscountAmount = lineDiscount,
                    VatPercent = alloc.batchVatPercent,
                    VatAmount = lineVat,
                    NetAmount = lineNet,
                    TotalPrice = lineTotal,
                    ProductName = product.ProductName
                });
            }
        }

        // Apply invoice-level discounts (capped by cfg_sale_configs)
        var maxInvDiscountPct = (saleConfig?.IsMaxDiscountEnabled == true)
            ? (saleConfig.MaxSaleDiscountPercent > 0 ? saleConfig.MaxSaleDiscountPercent : 20m)
            : 100m;

        decimal discountVal = 0;
        if (invoice.DiscountType == 1 && invoice.DiscountValue > 0)
        {
            var cappedPct = Math.Min(invoice.DiscountValue, maxInvDiscountPct);
            discountVal = Math.Round(grossTotal * (cappedPct / 100m), 4);
        }
        else if (invoice.DiscountType == 2 && invoice.DiscountValue > 0)
        {
            var maxAllowedFlat = Math.Round(grossTotal * (maxInvDiscountPct / 100m), 4);
            discountVal = Math.Min(invoice.DiscountValue, maxAllowedFlat);
        }

        var netAmount = Math.Max(0, grossTotal - discountVal);
        if (saleConfig?.AllowLossSale != true && netAmount < totalCogs)
        {
            throw new InvalidOperationException(
                $"Invoice discount brings total ৳{netAmount:F2} below total cost of goods ৳{totalCogs:F2}. Loss sales are prohibited.");
        }

        var finalPrice = Math.Round(netAmount + totalVat - invoice.RoundOffAmount + invoice.CardCommissionAmount, 2);

        var totalPaid = rawPayments.Sum(p => p.Amount > 0 ? p.Amount : p.PaidAmount);
        var customerWillPay = invoice.CustomerWillPay > 0
            ? invoice.CustomerWillPay
            : (totalPaid > 0 ? totalPaid : finalPrice);

        var changeAmount = invoice.ChangeAmount;
        if (customerWillPay > finalPrice && changeAmount <= 0)
        {
            changeAmount = customerWillPay - finalPrice;
        }
        var dueAmount = invoice.DueAmount;
        if (dueAmount <= 0 && finalPrice > customerWillPay)
        {
            dueAmount = finalPrice - customerWillPay;
        }

        var totalItemsCount = processedItems.Count;
        var approxProfit = finalPrice - totalCogs;

        var offlineClientTotal = request.OfflineClientTotal ?? invoice.OfflineClientTotal;
        var priceVariance = (isOffline && offlineClientTotal.HasValue)
            ? Math.Round(finalPrice - offlineClientTotal.Value, 4)
            : (isOffline ? 0m : (decimal?)null);
        var offlineReceiptNo = request.OfflineReceiptNo ?? invoice.OfflineReceiptNo;
        var idempotencyKey = request.IdempotencyKey ?? invoice.IdempotencyKey;
        var idempotencyPayloadHash = request.IdempotencyPayloadHash ?? invoice.IdempotencyPayloadHash;

        const string insertInvoiceSql = @"
            INSERT INTO sale_invoices (
                sale_invoice_no, uuid, pharmacy_no, branch_no, customer_no,
                prescription_no, prescription_image_url, sales_number, sale_mode,
                total_items, gross_total_price, discount_type, discount_value,
                card_commission_percent, card_commission_amount, net_amount,
                vat_amount, round_off_amount, final_price, approximate_profit,
                customer_will_pay, change_amount, due_amount, payment_method,
                document_status, cash_received_status, received_by_user_no,
                salesman_user_no, is_quick_printed, exchange_of_return_no,
                sale_timestamp, is_offline, client_created_at, offline_receipt_no,
                offline_client_total, price_variance, late_sync, idempotency_key,
                idempotency_payload_hash, created_at, updated_at
            ) VALUES (
                @saleInvoiceNo, gen_random_uuid(), @pharmacyNo, @branchNo, @CustomerNo,
                @PrescriptionNo, @PrescriptionImageUrl, @salesNumber, @SaleMode,
                @totalItems, @grossTotal, @DiscountType, @discountVal,
                @CardCommissionPercent, @CardCommissionAmount, @netAmount,
                @totalVat, @RoundOffAmount, @finalPrice, @approxProfit,
                @customerWillPay, @changeAmount, @dueAmount, @PaymentMethod,
                2, @CashReceivedStatus, @userNo,
                @userNo, @IsQuickPrinted, @ExchangeOfReturnNo,
                @saleTimestamp, @isOffline, @clientCreatedAtUtc, @offlineReceiptNo,
                @offlineClientTotal, @priceVariance, @lateSync, @idempotencyKey,
                @idempotencyPayloadHash, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            );";

        await conn.ExecuteAsync(insertInvoiceSql, new
        {
            saleInvoiceNo,
            pharmacyNo,
            branchNo,
            CustomerNo = (invoice.CustomerNo.HasValue && invoice.CustomerNo.Value > 0) ? invoice.CustomerNo.Value : (long?)null,
            invoice.PrescriptionNo,
            invoice.PrescriptionImageUrl,
            salesNumber,
            SaleMode = invoice.SaleMode > 0 ? invoice.SaleMode : 1,
            totalItems = totalItemsCount,
            grossTotal,
            invoice.DiscountType,
            discountVal,
            invoice.CardCommissionPercent,
            invoice.CardCommissionAmount,
            netAmount,
            totalVat,
            invoice.RoundOffAmount,
            finalPrice,
            approxProfit,
            customerWillPay,
            changeAmount,
            dueAmount,
            PaymentMethod = invoice.PaymentMethod > 0 ? invoice.PaymentMethod : 1,
            CashReceivedStatus = invoice.CashReceivedStatus > 0 ? invoice.CashReceivedStatus : (short)1,
            userNo,
            invoice.IsQuickPrinted,
            ExchangeOfReturnNo = request.ExchangeOfReturnNo,
            saleTimestamp,
            isOffline,
            clientCreatedAtUtc,
            offlineReceiptNo,
            offlineClientTotal,
            priceVariance,
            lateSync,
            idempotencyKey,
            idempotencyPayloadHash
        }, transaction: tran);

        // Insert items and stock deduction
        const string insertItemSql = @"
            INSERT INTO sale_invoice_items (
                sale_invoice_item_no, pharmacy_no, branch_no, sale_invoice_no,
                product_no, batch_no, sale_mode, sale_qty, box_qty, pcs_per_box,
                unit_cost_price, unit_sale_price, discount_amount, vat_percent,
                vat_amount, net_amount, total_price
            ) VALUES (
                @SaleInvoiceItemNo, @PharmacyNo, @BranchNo, @SaleInvoiceNo,
                @ProductNo, @BatchNo, @SaleMode, @SaleQty, @BoxQty, @PcsPerBox,
                @UnitCostPrice, @UnitSalePrice, @DiscountAmount, @VatPercent,
                @VatAmount, @NetAmount, @TotalPrice
            );";

        foreach (var item in processedItems)
        {
            await conn.ExecuteAsync(insertItemSql, item, transaction: tran);

            await _stockPosting.PostMovementAsync(conn, tran, new StockMovementEntry
            {
                PharmacyNo = pharmacyNo,
                BranchNo = branchNo,
                ProductNo = item.ProductNo,
                BatchNo = item.BatchNo,
                MovementType = StockMovementTypes.PosSale, // POS Sale (-)
                ReferenceDocType = "SALE_INVOICE",
                ReferenceDocNo = salesNumber,
                ReferenceDocId = saleInvoiceNo,
                QtyPcs = -item.SaleQty,
                UnitCost = item.UnitCostPrice,
                Remarks = $"POS Sale {salesNumber}"
            });
        }

        // Process Split Payments - Cash payment row strictly stored NET OF CHANGE
        var paymentsToRecord = new List<SaleInvoicePayment>();
        foreach (var p in rawPayments)
        {
            var amt = p.Amount > 0 ? p.Amount : p.PaidAmount;
            if (amt > 0)
            {
                paymentsToRecord.Add(new SaleInvoicePayment
                {
                    AccountNo = p.AccountNo,
                    PaymentMethod = p.PaymentMethod,
                    Amount = amt,
                    PaidAmount = amt,
                    GatewayReference = p.GatewayReference
                });
            }
        }

        var expectedPaymentTotal = Math.Max(0, finalPrice - dueAmount);

        if (paymentsToRecord.Count == 0 && expectedPaymentTotal > 0)
        {
            paymentsToRecord.Add(new SaleInvoicePayment
            {
                AccountNo = 0, // Resolve dynamically below
                PaymentMethod = invoice.PaymentMethod > 0 ? invoice.PaymentMethod : (short)1,
                Amount = expectedPaymentTotal
            });
        }
        else
        {
            // Cap applied payments so cash drawer reflects net cash received (tendered minus change)
            var totalTendered = paymentsToRecord.Sum(p => p.Amount);
            if (totalTendered > expectedPaymentTotal && expectedPaymentTotal >= 0)
            {
                var remainingToAllocate = expectedPaymentTotal;
                // Non-cash first, cash last so change is subtracted from cash drawer
                var sortedPayments = paymentsToRecord
                    .OrderBy(p => p.PaymentMethod == 1 ? 1 : 0)
                    .ToList();

                foreach (var p in sortedPayments)
                {
                    var allocated = Math.Min(p.Amount, remainingToAllocate);
                    remainingToAllocate -= allocated;
                    p.Amount = allocated;
                }
            }
        }

        var recordedPayments = new List<SaleInvoicePayment>();

        const string insertPaymentSql = @"
            INSERT INTO sale_invoice_payments (
                sale_payment_no, pharmacy_no, branch_no, sale_invoice_no,
                account_no, payment_method, amount, gateway_reference, created_at
            ) VALUES (
                @paymentNo, @pharmacyNo, @branchNo, @saleInvoiceNo,
                @AccountNo, @PaymentMethod, @Amount, @GatewayReference, CURRENT_TIMESTAMP
            );";

        foreach (var pay in paymentsToRecord)
        {
            if (pay.Amount <= 0) continue;

            var paymentNo = await conn.ExecuteScalarAsync<long>(
                "SELECT nextval('seq_sale_invoice_payment_no');", transaction: tran);

            // Dynamic payment account resolution from payment_method and tenant using shared PaymentAccountResolver
            var accountNo = await _accountResolver.ResolveAccountNoAsync(
                conn, tran, pharmacyNo, branchNo, pay.PaymentMethod);

            pay.AccountNo = accountNo;

            await conn.ExecuteAsync(insertPaymentSql, new
            {
                paymentNo,
                pharmacyNo,
                branchNo,
                saleInvoiceNo,
                AccountNo = accountNo,
                pay.PaymentMethod,
                pay.Amount,
                pay.GatewayReference
            }, transaction: tran);

            pay.SalePaymentNo = paymentNo;
            recordedPayments.Add(pay);
        }

        // Customer Due update
        if (dueAmount > 0 && invoice.CustomerNo.HasValue && invoice.CustomerNo.Value > 0)
        {
            await conn.ExecuteAsync(@"
                UPDATE cust_customers
                SET current_due = current_due + @dueAmount,
                    has_due = true,
                    updated_at = CURRENT_TIMESTAMP
                WHERE customer_no = @CustomerNo AND pharmacy_no = @pharmacyNo AND branch_no = @branchNo;",
                new { dueAmount, invoice.CustomerNo, pharmacyNo, branchNo },
                transaction: tran);
        }

        // Customer Loyalty Points award
        if (invoice.CustomerNo.HasValue && invoice.CustomerNo.Value > 0)
        {
            var loyaltyCheck = await conn.QuerySingleOrDefaultAsync<(bool IsLoyaltyMember, bool IsEnabled, decimal SpendPerPoint)>(@"
                SELECT c.is_loyalty_member AS IsLoyaltyMember,
                       COALESCE(cfg.is_enabled, false) AS IsEnabled,
                       COALESCE(cfg.earning_spend_amount_per_point, 100) AS SpendPerPoint
                FROM cust_customers c
                LEFT JOIN cfg_loyalty_configs cfg
                       ON cfg.pharmacy_no = @pharmacyNo AND cfg.branch_no = @branchNo
                WHERE c.customer_no = @CustomerNo AND c.pharmacy_no = @pharmacyNo AND c.branch_no = @branchNo;",
                new { invoice.CustomerNo, pharmacyNo, branchNo },
                transaction: tran);

            if (loyaltyCheck.IsLoyaltyMember && loyaltyCheck.IsEnabled && loyaltyCheck.SpendPerPoint > 0)
            {
                var pointsEarned = (int)Math.Floor(finalPrice / loyaltyCheck.SpendPerPoint);
                if (pointsEarned > 0)
                {
                    await conn.ExecuteAsync(@"
                        UPDATE cust_customers
                        SET loyalty_points = loyalty_points + @pointsEarned,
                            updated_at = CURRENT_TIMESTAMP
                        WHERE customer_no = @CustomerNo AND pharmacy_no = @pharmacyNo AND branch_no = @branchNo;",
                        new { pointsEarned, invoice.CustomerNo, pharmacyNo, branchNo },
                        transaction: tran);
                }
            }
        }

        // General Ledger Journal Entry
        var journalEntry = new CompoundJournalEntry
        {
            PharmacyNo = pharmacyNo,
            BranchNo = branchNo,
            TransactionType = 1, // Sale
            SourceDocumentType = "SALE_INVOICE",
            SourceDocumentNo = salesNumber,
            SourceDocumentId = saleInvoiceNo,
            CreatedByUserNo = userNo,
            Legs = new List<LedgerLeg>()
        };

        // 1. Debit Payments
        foreach (var pay in recordedPayments)
        {
            var methodLabel = pay.PaymentMethod switch
            {
                1 => "Cash Drawer",
                2 => "Bank",
                3 => "bKash",
                4 => "Nagad",
                5 => "Rocket",
                6 => "Card Terminal",
                9 => "Return Credit Clearing",
                _ => "Payment"
            };

            journalEntry.Legs.Add(new LedgerLeg
            {
                AccountNo = pay.AccountNo,
                DebitAmount = pay.Amount,
                CreditAmount = 0,
                Narration = $"{methodLabel} collection for sale {salesNumber}"
            });
        }

        // 2. Debit Customer Accounts Receivable (Account 10301) for Due Amount
        if (dueAmount > 0)
        {
            var arAccount = await conn.ExecuteScalarAsync<long?>(@"
                SELECT account_no FROM acc_transaction_accounts
                WHERE account_code = '10301' AND pharmacy_no = @pharmacyNo AND branch_no = @branchNo;",
                new { pharmacyNo, branchNo }, transaction: tran);

            if (!arAccount.HasValue)
            {
                throw new InvalidOperationException($"Customer AR account '10301' is not configured for pharmacy {pharmacyNo} branch {branchNo}.");
            }

            journalEntry.Legs.Add(new LedgerLeg
            {
                AccountNo = arAccount.Value,
                DebitAmount = dueAmount,
                CreditAmount = 0,
                Narration = $"Customer AR due on sale {salesNumber}"
            });
        }

        // 3. Debit Card Commission Expense (Account 50301)
        if (invoice.CardCommissionAmount > 0)
        {
            var commAcc = await conn.ExecuteScalarAsync<long?>(@"
                SELECT account_no FROM acc_transaction_accounts
                WHERE account_code = '50301' AND pharmacy_no = @pharmacyNo AND branch_no = @branchNo;",
                new { pharmacyNo, branchNo }, transaction: tran);

            if (!commAcc.HasValue)
            {
                throw new InvalidOperationException($"Card commission account '50301' is not configured for pharmacy {pharmacyNo} branch {branchNo}.");
            }

            journalEntry.Legs.Add(new LedgerLeg
            {
                AccountNo = commAcc.Value,
                DebitAmount = invoice.CardCommissionAmount,
                CreditAmount = 0,
                Narration = $"Card processing surcharge fee for {salesNumber}"
            });
        }

        // 4. Credit Sales Revenue (Account 40101) for Net Sales Amount
        var revAcc = await conn.ExecuteScalarAsync<long?>(@"
            SELECT account_no FROM acc_transaction_accounts
            WHERE account_code = '40101' AND pharmacy_no = @pharmacyNo AND branch_no = @branchNo;",
            new { pharmacyNo, branchNo }, transaction: tran);

        if (!revAcc.HasValue)
        {
            throw new InvalidOperationException($"Sales revenue account '40101' is not configured for pharmacy {pharmacyNo} branch {branchNo}.");
        }

        journalEntry.Legs.Add(new LedgerLeg
        {
            AccountNo = revAcc.Value,
            DebitAmount = 0,
            CreditAmount = netAmount,
            Narration = $"Sales revenue from invoice {salesNumber}"
        });

        // 5. Credit Output VAT Payable (Account 20201)
        if (totalVat > 0)
        {
            var vatAcc = await conn.ExecuteScalarAsync<long?>(@"
                SELECT account_no FROM acc_transaction_accounts
                WHERE account_code = '20201' AND pharmacy_no = @pharmacyNo AND branch_no = @branchNo;",
                new { pharmacyNo, branchNo }, transaction: tran);

            if (!vatAcc.HasValue)
            {
                throw new InvalidOperationException($"Output VAT account '20201' is not configured for pharmacy {pharmacyNo} branch {branchNo}.");
            }

            journalEntry.Legs.Add(new LedgerLeg
            {
                AccountNo = vatAcc.Value,
                DebitAmount = 0,
                CreditAmount = totalVat,
                Narration = $"Output VAT on sale {salesNumber}"
            });
        }

        // 6. Round-off credit (if round off gain > 0)
        if (invoice.RoundOffAmount > 0)
        {
            var roundGainAcc = await conn.ExecuteScalarAsync<long?>(@"
                SELECT account_no FROM acc_transaction_accounts
                WHERE account_code = '40202' AND pharmacy_no = @pharmacyNo AND branch_no = @branchNo;",
                new { pharmacyNo, branchNo }, transaction: tran);

            if (!roundGainAcc.HasValue)
            {
                throw new InvalidOperationException($"Round-off gain account '40202' is not configured for pharmacy {pharmacyNo} branch {branchNo}.");
            }

            journalEntry.Legs.Add(new LedgerLeg
            {
                AccountNo = roundGainAcc.Value,
                DebitAmount = 0,
                CreditAmount = invoice.RoundOffAmount,
                Narration = "Rounding gain adjustment"
            });
        }

        // 7. FEFO COGS & Inventory Relief (Dr 50101 COGS, Cr 10401/10201 Inventory Asset)
        if (totalCogs > 0)
        {
            var cogsAcc = await conn.ExecuteScalarAsync<long?>(@"
                SELECT account_no FROM acc_transaction_accounts
                WHERE account_code = '50101' AND pharmacy_no = @pharmacyNo AND branch_no = @branchNo
                LIMIT 1;",
                new { pharmacyNo, branchNo }, transaction: tran);

            if (!cogsAcc.HasValue)
            {
                cogsAcc = await conn.ExecuteScalarAsync<long?>(@"
                    SELECT account_no FROM acc_transaction_accounts
                    WHERE pharmacy_no = @pharmacyNo AND branch_no = @branchNo
                      AND (account_name ILIKE '%Cost of Goods%' OR account_name ILIKE '%COGS%')
                    LIMIT 1;",
                    new { pharmacyNo, branchNo }, transaction: tran);
            }

            if (!cogsAcc.HasValue)
            {
                throw new InvalidOperationException($"Cost of Goods Sold (COGS) account '50101' is not configured for pharmacy {pharmacyNo} branch {branchNo}.");
            }

            var invAssetAcc = await conn.ExecuteScalarAsync<long?>(@"
                SELECT account_no FROM acc_transaction_accounts
                WHERE account_code IN ('10401', '10201') AND pharmacy_no = @pharmacyNo AND branch_no = @branchNo
                ORDER BY (account_code = '10401') DESC
                LIMIT 1;",
                new { pharmacyNo, branchNo }, transaction: tran);

            if (!invAssetAcc.HasValue)
            {
                invAssetAcc = await conn.ExecuteScalarAsync<long?>(@"
                    SELECT account_no FROM acc_transaction_accounts
                    WHERE pharmacy_no = @pharmacyNo AND branch_no = @branchNo
                      AND (account_name ILIKE '%Inventory%' OR account_code LIKE '10%')
                    LIMIT 1;",
                    new { pharmacyNo, branchNo }, transaction: tran);
            }

            if (!invAssetAcc.HasValue)
            {
                throw new InvalidOperationException($"Inventory Asset account '10401'/'10201' is not configured for pharmacy {pharmacyNo} branch {branchNo}.");
            }

            journalEntry.Legs.Add(new LedgerLeg
            {
                AccountNo = cogsAcc.Value,
                DebitAmount = totalCogs,
                CreditAmount = 0,
                Narration = $"Cost of goods sold for {salesNumber}"
            });

            journalEntry.Legs.Add(new LedgerLeg
            {
                AccountNo = invAssetAcc.Value,
                DebitAmount = 0,
                CreditAmount = totalCogs,
                Narration = $"Inventory relief for {salesNumber}"
            });
        }

        await _ledgerPosting.PostJournalAsync(conn, tran, journalEntry);

        invoice.SaleInvoiceNo = saleInvoiceNo;
        invoice.SalesNumber = salesNumber;
        invoice.GrossTotalPrice = grossTotal;
        invoice.NetAmount = netAmount;
        invoice.VatAmount = totalVat;
        invoice.FinalPrice = finalPrice;
        invoice.CustomerWillPay = customerWillPay;
        invoice.ChangeAmount = changeAmount;
        invoice.DueAmount = dueAmount;
        invoice.ApproximateProfit = approxProfit;
        invoice.TotalItems = totalItemsCount;
        invoice.DocumentStatus = 2;
        invoice.SaleTimestamp = saleTimestamp;
        invoice.IsOffline = isOffline;
        invoice.ClientCreatedAt = clientCreatedAtUtc;
        invoice.OfflineReceiptNo = offlineReceiptNo;
        invoice.OfflineClientTotal = offlineClientTotal;
        invoice.PriceVariance = priceVariance;
        invoice.LateSync = lateSync;
        invoice.IdempotencyKey = idempotencyKey;
        invoice.IdempotencyPayloadHash = idempotencyPayloadHash;
        invoice.Items = processedItems;
        invoice.Payments = recordedPayments;

        return new SalePostingResult
        {
            Invoice = invoice,
            Items = processedItems,
            Payments = recordedPayments,
            SaleInvoiceNo = saleInvoiceNo,
            SalesNumber = salesNumber
        };
    }
}

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

    public SalePostingEngine(
        IDocumentNumberService docNumService,
        IStockPostingService stockPosting,
        ILedgerPostingService ledgerPosting)
    {
        _docNumService = docNumService;
        _stockPosting = stockPosting;
        _ledgerPosting = ledgerPosting;
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
            "SELECT COALESCE(MAX(sale_invoice_no), 0) + 1 FROM sale_invoices;", transaction: tran);

        var maxSaleItemNo = await conn.ExecuteScalarAsync<long>(
            "SELECT COALESCE(MAX(sale_invoice_item_no), 0) FROM sale_invoice_items;", transaction: tran);

        decimal grossTotal = 0;
        decimal totalVat = 0;
        decimal totalCogs = 0;

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

            if (product == null && line.ProductNo > 1_000_000_000_000)
            {
                product = await conn.QueryFirstOrDefaultAsync<ProdProduct>(@"
                    SELECT product_no, product_name, cost_per_box, sale_price_per_box,
                           purchase_price_per_piece, sale_price_per_piece, wholesale_price_per_piece,
                           quantity_per_box, rak_number, is_prescription_required, is_narcotic
                    FROM prod_products
                    WHERE pharmacy_no = @pharmacyNo AND branch_no = @branchNo
                      AND (sale_price_per_piece = @UnitSalePrice OR purchase_price_per_piece = @UnitCostPrice)
                    ORDER BY product_no ASC
                    LIMIT 1;",
                    new { pharmacyNo, branchNo, line.UnitSalePrice, line.UnitCostPrice },
                    transaction: tran);

                if (product == null)
                {
                    product = await conn.QueryFirstOrDefaultAsync<ProdProduct>(@"
                        SELECT product_no, product_name, cost_per_box, sale_price_per_box,
                               purchase_price_per_piece, sale_price_per_piece, wholesale_price_per_piece,
                               quantity_per_box, rak_number, is_prescription_required, is_narcotic
                        FROM prod_products
                        WHERE pharmacy_no = @pharmacyNo AND branch_no = @branchNo AND is_active = true
                        ORDER BY product_no ASC
                        LIMIT 1;",
                        new { pharmacyNo, branchNo },
                        transaction: tran);
                }

                if (product != null)
                {
                    line.ProductNo = product.ProductNo;
                }
            }

            if (product == null)
            {
                throw new InvalidOperationException($"Product #{line.ProductNo} not found in this pharmacy branch.");
            }

            // Item 4: Prescription & Narcotic check
            if ((product.IsNarcotic || product.IsPrescriptionRequired) &&
                (invoice.PrescriptionNo == null || invoice.PrescriptionNo <= 0) &&
                string.IsNullOrWhiteSpace(invoice.PrescriptionImageUrl))
            {
                var drugType = product.IsNarcotic ? "Narcotic" : "Prescription-only";
                throw new InvalidOperationException(
                    $"{drugType} medicine '{product.ProductName}' requires a valid prescription to dispense.");
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
            var lineGross = Math.Round(qtyPcs * unitSalePrice, 4);

            // Batch Selection (FEFO)
            long? batchNo = line.BatchNo;
            string batchNumber = line.BatchNumber ?? string.Empty;
            decimal batchCost = product.PurchasePricePerPiece;
            decimal batchVatPercent = 0m;

            if (!batchNo.HasValue || batchNo.Value <= 0)
            {
                var activeBatch = await conn.QueryFirstOrDefaultAsync<ProdProductBatch>(@"
                    SELECT batch_no, batch_number, cost_per_box, quantity_in_box, total_quantity_pcs, vat_percent
                    FROM prod_product_batches
                    WHERE product_no = @ProductNo AND pharmacy_no = @pharmacyNo AND branch_no = @branchNo
                      AND total_quantity_pcs > 0 AND expiry_date >= CURRENT_DATE
                    ORDER BY expiry_date ASC
                    FOR UPDATE;",
                    new { line.ProductNo, pharmacyNo, branchNo },
                    transaction: tran);

                if (activeBatch != null && activeBatch.BatchNo > 0)
                {
                    batchNo = activeBatch.BatchNo;
                    batchNumber = activeBatch.BatchNumber;
                    batchVatPercent = activeBatch.VatPercent;
                    if (activeBatch.QuantityInBox > 0 && activeBatch.CostPerBox > 0)
                    {
                        batchCost = Math.Round(activeBatch.CostPerBox / activeBatch.QuantityInBox, 4);
                    }
                }
                else
                {
                    if (!allowOutOfStock)
                    {
                        throw new InvalidOperationException(
                            $"Medicine '{product.ProductName}' is out of stock (available: 0 pcs), and out-of-stock sales are disabled in settings.");
                    }

                    var anyBatch = await conn.QueryFirstOrDefaultAsync<ProdProductBatch>(@"
                        SELECT batch_no, batch_number, cost_per_box, quantity_in_box, total_quantity_pcs, vat_percent
                        FROM prod_product_batches
                        WHERE product_no = @ProductNo AND pharmacy_no = @pharmacyNo AND branch_no = @branchNo
                        ORDER BY (total_quantity_pcs > 0) DESC, expiry_date DESC, batch_no DESC
                        LIMIT 1
                        FOR UPDATE;",
                        new { line.ProductNo, pharmacyNo, branchNo },
                        transaction: tran);

                    if (anyBatch != null && anyBatch.BatchNo > 0)
                    {
                        batchNo = anyBatch.BatchNo;
                        batchNumber = anyBatch.BatchNumber;
                        batchVatPercent = anyBatch.VatPercent;
                        if (anyBatch.QuantityInBox > 0 && anyBatch.CostPerBox > 0)
                        {
                            batchCost = Math.Round(anyBatch.CostPerBox / anyBatch.QuantityInBox, 4);
                        }
                    }
                    else
                    {
                        var newBatchNo = await conn.ExecuteScalarAsync<long>(
                            "SELECT COALESCE(MAX(batch_no), 0) + 1 FROM prod_product_batches;", transaction: tran);
                        var defaultBatchNum = !string.IsNullOrWhiteSpace(line.BatchNumber) ? line.BatchNumber.Trim() : "PRE-PURCHASE";
                        var expiry = new DateTime(2099, 12, 31);
                        var qInBox = product.QuantityPerBox > 0 ? product.QuantityPerBox : 10;

                        await conn.ExecuteAsync(@"
                            INSERT INTO prod_product_batches (
                                batch_no, pharmacy_no, branch_no, product_no, batch_number,
                                expiry_date, box_quantity, quantity_in_box, total_quantity_pcs,
                                cost_per_box, sale_price_per_box, vat_percent, is_expired,
                                created_at, updated_at
                            ) VALUES (
                                @newBatchNo, @pharmacyNo, @branchNo, @ProductNo, @defaultBatchNum,
                                @expiry, 0, @qInBox, 0,
                                @CostPerBox, @SalePricePerBox, 0, false,
                                CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
                            );",
                            new
                            {
                                newBatchNo,
                                pharmacyNo,
                                branchNo,
                                line.ProductNo,
                                defaultBatchNum,
                                expiry,
                                CostPerBox = product.CostPerBox > 0 ? product.CostPerBox : 0m,
                                SalePricePerBox = product.SalePricePerBox > 0 ? product.SalePricePerBox : 0m
                            },
                            transaction: tran);

                        batchNo = newBatchNo;
                        batchNumber = defaultBatchNum;
                    }
                }
            }
            else
            {
                var specifiedBatch = await conn.QueryFirstOrDefaultAsync<ProdProductBatch>(@"
                    SELECT batch_no, batch_number, cost_per_box, quantity_in_box, total_quantity_pcs, vat_percent
                    FROM prod_product_batches
                    WHERE batch_no = @batchNo AND pharmacy_no = @pharmacyNo AND branch_no = @branchNo;",
                    new { batchNo, pharmacyNo, branchNo },
                    transaction: tran);
                if (specifiedBatch != null)
                {
                    batchVatPercent = specifiedBatch.VatPercent;
                    if (specifiedBatch.QuantityInBox > 0 && specifiedBatch.CostPerBox > 0)
                    {
                        batchCost = Math.Round(specifiedBatch.CostPerBox / specifiedBatch.QuantityInBox, 4);
                    }
                }
            }

            maxSaleItemNo++;
            var saleItemNo = maxSaleItemNo;

            // Item 3: Cap line discount using cfg_sale_configs and ignore client VAT
            var maxDiscountPct = (saleConfig?.IsMaxDiscountEnabled == true)
                ? (saleConfig.MaxSaleDiscountPercent > 0 ? saleConfig.MaxSaleDiscountPercent : 20m)
                : 100m;

            var requestedDiscount = line.DiscountAmount;
            var maxAllowedLineDiscount = Math.Round(lineGross * (maxDiscountPct / 100m), 4);
            var lineDiscount = Math.Min(requestedDiscount, maxAllowedLineDiscount);
            var lineNet = Math.Max(0, lineGross - lineDiscount);

            // Item 3: Reject a sale below the batch cost
            var effectiveUnitPrice = qtyPcs > 0 ? (lineNet / qtyPcs) : 0m;
            if (saleConfig?.AllowLossSale != true && effectiveUnitPrice < batchCost)
            {
                throw new InvalidOperationException(
                    $"Sale price ৳{effectiveUnitPrice:F2} for '{product.ProductName}' is below batch cost ৳{batchCost:F2}. Loss sales are prohibited.");
            }

            var lineVat = Math.Round(lineNet * (batchVatPercent / 100m), 4);
            var lineTotal = lineNet + lineVat;
            var lineCogs = Math.Round(qtyPcs * batchCost, 4);

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
                BatchNo = batchNo.Value,
                SaleMode = isWholesaleLine ? (short)2 : (short)1,
                SaleQty = qtyPcs,
                BoxQty = line.BoxQty,
                PcsPerBox = pcsPerBoxVal,
                UnitCostPrice = batchCost,
                UnitSalePrice = unitSalePrice,
                DiscountAmount = lineDiscount,
                VatPercent = batchVatPercent,
                VatAmount = lineVat,
                NetAmount = lineNet,
                TotalPrice = lineTotal,
                ProductName = product.ProductName
            });
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

        var customerWillPay = invoice.CustomerWillPay > 0 ? invoice.CustomerWillPay : finalPrice;
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
                sale_timestamp, created_at, updated_at
            ) VALUES (
                @saleInvoiceNo, gen_random_uuid(), @pharmacyNo, @branchNo, @CustomerNo,
                @PrescriptionNo, @PrescriptionImageUrl, @salesNumber, @SaleMode,
                @totalItems, @grossTotal, @DiscountType, @discountVal,
                @CardCommissionPercent, @CardCommissionAmount, @netAmount,
                @totalVat, @RoundOffAmount, @finalPrice, @approxProfit,
                @customerWillPay, @changeAmount, @dueAmount, @PaymentMethod,
                2, @CashReceivedStatus, @userNo,
                @userNo, @IsQuickPrinted, @ExchangeOfReturnNo,
                CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            );";

        await conn.ExecuteAsync(insertInvoiceSql, new
        {
            saleInvoiceNo,
            pharmacyNo,
            branchNo,
            CustomerNo = invoice.CustomerNo > 0 ? invoice.CustomerNo : 1,
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
            ExchangeOfReturnNo = request.ExchangeOfReturnNo
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
                MovementType = 4, // POS Sale (-)
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
                AccountNo = 1, // Main Cash Drawer
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
        var maxPaymentNo = await conn.ExecuteScalarAsync<long>(
            "SELECT COALESCE(MAX(sale_payment_no), 0) FROM sale_invoice_payments;", transaction: tran);

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

            maxPaymentNo++;
            var paymentNo = maxPaymentNo;

            // If account_no is not provided or <= 0, resolve account dynamically
            var accountNo = pay.AccountNo;
            if (accountNo <= 0)
            {
                accountNo = pay.PaymentMethod switch
                {
                    1 => await conn.ExecuteScalarAsync<long?>(@"
                        SELECT account_no FROM acc_transaction_accounts
                        WHERE account_code = '10101' AND pharmacy_no = @pharmacyNo AND branch_no = @branchNo;",
                        new { pharmacyNo, branchNo }, transaction: tran) ?? 1,
                    2 => await conn.ExecuteScalarAsync<long?>(@"
                        SELECT account_no FROM acc_transaction_accounts
                        WHERE account_code = '10201' AND pharmacy_no = @pharmacyNo AND branch_no = @branchNo;",
                        new { pharmacyNo, branchNo }, transaction: tran) ?? 2,
                    3 => await conn.ExecuteScalarAsync<long?>(@"
                        SELECT account_no FROM acc_transaction_accounts
                        WHERE account_code = '10202' AND pharmacy_no = @pharmacyNo AND branch_no = @branchNo;",
                        new { pharmacyNo, branchNo }, transaction: tran) ?? 13,
                    4 => await conn.ExecuteScalarAsync<long?>(@"
                        SELECT account_no FROM acc_transaction_accounts
                        WHERE account_code = '10203' AND pharmacy_no = @pharmacyNo AND branch_no = @branchNo;",
                        new { pharmacyNo, branchNo }, transaction: tran) ?? 14,
                    6 => await conn.ExecuteScalarAsync<long?>(@"
                        SELECT account_no FROM acc_transaction_accounts
                        WHERE account_code = '10205' AND pharmacy_no = @pharmacyNo AND branch_no = @branchNo;",
                        new { pharmacyNo, branchNo }, transaction: tran) ?? 15,
                    9 => await conn.ExecuteScalarAsync<long?>(@"
                        SELECT account_no FROM acc_transaction_accounts
                        WHERE (account_code = '10901' OR account_name = 'Return Credit Clearing')
                          AND pharmacy_no = @pharmacyNo AND branch_no = @branchNo;",
                        new { pharmacyNo, branchNo }, transaction: tran) ?? 20,
                    _ => 1
                };
            }

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
                new { pharmacyNo, branchNo }, transaction: tran) ?? 6;

            journalEntry.Legs.Add(new LedgerLeg
            {
                AccountNo = arAccount,
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
                new { pharmacyNo, branchNo }, transaction: tran) ?? 17;

            journalEntry.Legs.Add(new LedgerLeg
            {
                AccountNo = commAcc,
                DebitAmount = invoice.CardCommissionAmount,
                CreditAmount = 0,
                Narration = $"Card processing surcharge fee for {salesNumber}"
            });
        }

        // 4. Credit Sales Revenue (Account 40101) for Net Sales Amount
        var revAcc = await conn.ExecuteScalarAsync<long?>(@"
            SELECT account_no FROM acc_transaction_accounts
            WHERE account_code = '40101' AND pharmacy_no = @pharmacyNo AND branch_no = @branchNo;",
            new { pharmacyNo, branchNo }, transaction: tran) ?? 5;

        journalEntry.Legs.Add(new LedgerLeg
        {
            AccountNo = revAcc,
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
                new { pharmacyNo, branchNo }, transaction: tran) ?? 7;

            journalEntry.Legs.Add(new LedgerLeg
            {
                AccountNo = vatAcc,
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
                new { pharmacyNo, branchNo }, transaction: tran) ?? 16;

            journalEntry.Legs.Add(new LedgerLeg
            {
                AccountNo = roundGainAcc,
                DebitAmount = 0,
                CreditAmount = invoice.RoundOffAmount,
                Narration = "Rounding gain adjustment"
            });
        }

        // 7. FEFO COGS & Inventory Relief (Dr 50101 COGS, Cr 10401 Inventory Asset)
        if (totalCogs > 0)
        {
            var cogsAcc = await conn.ExecuteScalarAsync<long?>(@"
                SELECT account_no FROM acc_transaction_accounts
                WHERE account_code = '50101' AND pharmacy_no = @pharmacyNo AND branch_no = @branchNo;",
                new { pharmacyNo, branchNo }, transaction: tran) ?? 11;

            var invAssetAcc = await conn.ExecuteScalarAsync<long?>(@"
                SELECT account_no FROM acc_transaction_accounts
                WHERE account_code = '10401' AND pharmacy_no = @pharmacyNo AND branch_no = @branchNo;",
                new { pharmacyNo, branchNo }, transaction: tran) ?? 4;

            journalEntry.Legs.Add(new LedgerLeg
            {
                AccountNo = cogsAcc,
                DebitAmount = totalCogs,
                CreditAmount = 0,
                Narration = $"Cost of goods sold for {salesNumber}"
            });

            journalEntry.Legs.Add(new LedgerLeg
            {
                AccountNo = invAssetAcc,
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

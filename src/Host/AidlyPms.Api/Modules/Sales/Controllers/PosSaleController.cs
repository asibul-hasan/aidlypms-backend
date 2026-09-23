using AidlyPms.Api.Common;
using AidlyPms.Api.Common.Database;
using AidlyPms.Api.Infrastructure;
using AidlyPms.Api.Modules.Products.Models;
using AidlyPms.Api.Modules.Sales.Models;
using Dapper;
using Microsoft.AspNetCore.Mvc;

namespace AidlyPms.Api.Modules.Sales.Controllers;

[Route("api/v1/pos/sales")]
public class PosSaleController : BaseController
{
    private readonly IDbConnectionFactory _db;
    private readonly IDocumentNumberService _docNumService;
    private readonly IStockPostingService _stockPosting;
    private readonly ILedgerPostingService _ledgerPosting;

    public PosSaleController(
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
    public async Task<ActionResult<ApiResponse<PagedResult<SaleInvoice>>>> GetSales(
        [FromQuery] string? search = null,
        [FromQuery] long? customerNo = null,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 20)
    {
        await using var conn = await _db.CreateOpenConnectionAsync();
        var offset = (pageIndex - 1) * pageSize;

        const string countSql = @"
            SELECT COUNT(*)
            FROM sale_invoices s
            LEFT JOIN cust_customers c ON s.customer_no = c.customer_no
            WHERE s.pharmacy_no = @CurrentPharmacyNo AND s.branch_no = @CurrentBranchNo
              AND (@search::text IS NULL OR s.sales_number ILIKE '%' || @search || '%' OR c.name ILIKE '%' || @search || '%' OR c.phone ILIKE '%' || @search || '%')
              AND (@customerNo::bigint IS NULL OR s.customer_no = @customerNo::bigint)
              AND (@startDate::timestamptz IS NULL OR s.sale_timestamp >= @startDate::timestamptz)
              AND (@endDate::timestamptz IS NULL OR s.sale_timestamp <= @endDate::timestamptz);";

        var total = await conn.ExecuteScalarAsync<int>(countSql, new
        {
            CurrentPharmacyNo,
            CurrentBranchNo,
            search,
            customerNo,
            startDate,
            endDate
        });

        const string dataSql = @"
            SELECT 
                s.sale_invoice_no, s.uuid, s.pharmacy_no, s.branch_no, s.customer_no,
                s.prescription_no, s.prescription_image_url, s.sales_number, s.sale_mode,
                s.total_items, s.gross_total_price, s.discount_type, s.discount_value,
                s.card_commission_percent, s.card_commission_amount, s.net_amount,
                s.vat_amount, s.round_off_amount, s.final_price, s.approximate_profit,
                s.customer_will_pay, s.change_amount, s.due_amount, s.payment_method,
                s.document_status, s.cash_received_status, s.received_by_user_no,
                s.salesman_user_no, s.is_quick_printed, s.sale_timestamp,
                s.created_at, s.updated_at,
                c.name AS customer_name,
                c.phone AS customer_phone
            FROM sale_invoices s
            LEFT JOIN cust_customers c ON s.customer_no = c.customer_no
            WHERE s.pharmacy_no = @CurrentPharmacyNo AND s.branch_no = @CurrentBranchNo
              AND (@search::text IS NULL OR s.sales_number ILIKE '%' || @search || '%' OR c.name ILIKE '%' || @search || '%' OR c.phone ILIKE '%' || @search || '%')
              AND (@customerNo::bigint IS NULL OR s.customer_no = @customerNo::bigint)
              AND (@startDate::timestamptz IS NULL OR s.sale_timestamp >= @startDate::timestamptz)
              AND (@endDate::timestamptz IS NULL OR s.sale_timestamp <= @endDate::timestamptz)
            ORDER BY s.sale_timestamp DESC
            OFFSET @offset LIMIT @pageSize;";

        var items = (await conn.QueryAsync<SaleInvoice>(dataSql, new
        {
            CurrentPharmacyNo,
            CurrentBranchNo,
            search,
            customerNo,
            startDate,
            endDate,
            offset,
            pageSize
        })).AsList();

        return PagedResponse(new PagedResult<SaleInvoice>(items, total, pageIndex, pageSize));
    }

    [HttpGet("{id:long}")]
    public async Task<ActionResult<ApiResponse<SaleInvoice>>> GetSaleById(long id)
    {
        await using var conn = await _db.CreateOpenConnectionAsync();

        const string sql = @"
            SELECT 
                s.sale_invoice_no, s.uuid, s.pharmacy_no, s.branch_no, s.customer_no,
                s.prescription_no, s.prescription_image_url, s.sales_number, s.sale_mode,
                s.total_items, s.gross_total_price, s.discount_type, s.discount_value,
                s.card_commission_percent, s.card_commission_amount, s.net_amount,
                s.vat_amount, s.round_off_amount, s.final_price, s.approximate_profit,
                s.customer_will_pay, s.change_amount, s.due_amount, s.payment_method,
                s.document_status, s.cash_received_status, s.received_by_user_no,
                s.salesman_user_no, s.is_quick_printed, s.sale_timestamp,
                s.created_at, s.updated_at,
                c.name AS customer_name,
                c.phone AS customer_phone
            FROM sale_invoices s
            LEFT JOIN cust_customers c ON s.customer_no = c.customer_no
            WHERE s.sale_invoice_no = @id AND s.pharmacy_no = @CurrentPharmacyNo AND s.branch_no = @CurrentBranchNo;";

        var invoice = await conn.QuerySingleOrDefaultAsync<SaleInvoice>(sql, new
        {
            id,
            CurrentPharmacyNo,
            CurrentBranchNo
        });

        if (invoice == null)
        {
            return FailResponse<SaleInvoice>("Sale invoice not found.", statusCode: 404);
        }

        const string itemsSql = @"
            SELECT 
                i.sale_invoice_item_no, i.pharmacy_no, i.branch_no, i.sale_invoice_no,
                i.product_no, i.batch_no, i.sale_mode, i.sale_qty, i.box_qty, i.pcs_per_box,
                i.unit_cost_price, i.unit_sale_price, i.discount_amount,
                i.vat_percent, i.vat_amount, i.net_amount, i.total_price,
                p.product_name, b.batch_number
            FROM sale_invoice_items i
            JOIN prod_products p ON i.product_no = p.product_no
            LEFT JOIN prod_product_batches b ON i.batch_no = b.batch_no
            WHERE i.sale_invoice_no = @id;";


        invoice.Items = (await conn.QueryAsync<SaleInvoiceItem>(itemsSql, new { id })).AsList();

        const string paymentsSql = @"
            SELECT 
                sale_payment_no, pharmacy_no, branch_no, sale_invoice_no,
                account_no, payment_method, amount, gateway_reference, created_at
            FROM sale_invoice_payments
            WHERE sale_invoice_no = @id;";

        invoice.Payments = (await conn.QueryAsync<SaleInvoicePayment>(paymentsSql, new { id })).AsList();

        return OkResponse(invoice);
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<SaleInvoice>>> CreateSale([FromBody] CreateSalePayload payload)
    {
        if (payload.Items == null || payload.Items.Count == 0)
        {
            return FailResponse<SaleInvoice>("A sale invoice must contain at least one item.");
        }

        await using var conn = await _db.CreateOpenConnectionAsync();
        await using var tran = await conn.BeginTransactionAsync();

        try
        {
            var invoice = payload.Invoice;

            // Generate monotonic document number
            var salesNumber = await _docNumService.GetNextDocumentNumberAsync(
                conn, tran, CurrentPharmacyNo, CurrentBranchNo, "SALE_INVOICE");

            var saleInvoiceNo = await conn.ExecuteScalarAsync<long>(
                "SELECT COALESCE(MAX(sale_invoice_no), 0) + 1 FROM sale_invoices;", transaction: tran);

            decimal grossTotal = 0;
            decimal totalVat = 0;
            decimal totalCogs = 0;

            // Line items evaluation & batch FEFO deduction
            var processedItems = new List<SaleInvoiceItem>();

            foreach (var line in payload.Items)
            {
                var product = await conn.QuerySingleOrDefaultAsync<ProdProduct>(@"
                    SELECT product_no, product_name, cost_per_box, sale_price_per_box,
                           purchase_price_per_piece, sale_price_per_piece, quantity_per_box, rak_number
                    FROM prod_products
                    WHERE product_no = @ProductNo AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
                    new { line.ProductNo, CurrentPharmacyNo, CurrentBranchNo },
                    transaction: tran);

                if (product == null)
                {
                    throw new InvalidOperationException($"Product #{line.ProductNo} not found in this pharmacy branch.");
                }

                var qtyPcs = line.TotalQuantityPcs > 0 ? line.TotalQuantityPcs : 1;
                var unitSalePrice = line.SalePrice > 0 ? line.SalePrice : product.SalePricePerPiece;
                var lineGross = Math.Round(qtyPcs * unitSalePrice, 4);

                // Batch Selection (FEFO)
                long? batchNo = line.BatchNo;
                string batchNumber = line.BatchNumber ?? string.Empty;
                decimal batchCost = product.PurchasePricePerPiece;

                if (!batchNo.HasValue || batchNo.Value <= 0)
                {
                    // Select earliest expiring active batch
                    var activeBatch = await conn.QueryFirstOrDefaultAsync<(long BatchNo, string BatchNumber, decimal CostPerBox, int QtyInBox)>(@"
                        SELECT batch_no, batch_number, cost_per_box, quantity_in_box
                        FROM prod_product_batches
                        WHERE product_no = @ProductNo AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
                          AND total_quantity_pcs > 0 AND expiry_date >= CURRENT_DATE
                        ORDER BY expiry_date ASC
                        FOR UPDATE;",
                        new { line.ProductNo, CurrentPharmacyNo, CurrentBranchNo },
                        transaction: tran);

                    if (activeBatch.BatchNo > 0)
                    {
                        batchNo = activeBatch.BatchNo;
                        batchNumber = activeBatch.BatchNumber;
                        if (activeBatch.QtyInBox > 0)
                        {
                            batchCost = Math.Round(activeBatch.CostPerBox / activeBatch.QtyInBox, 4);
                        }
                    }
                }
                else
                {
                    var selectedBatch = await conn.QuerySingleOrDefaultAsync<(string BatchNumber, decimal CostPerBox, int QtyInBox)>(@"
                        SELECT batch_number, cost_per_box, quantity_in_box
                        FROM prod_product_batches
                        WHERE batch_no = @BatchNo FOR UPDATE;",
                        new { BatchNo = batchNo.Value },
                        transaction: tran);

                    batchNumber = selectedBatch.BatchNumber ?? batchNumber;
                    if (selectedBatch.QtyInBox > 0)
                    {
                        batchCost = Math.Round(selectedBatch.CostPerBox / selectedBatch.QtyInBox, 4);
                    }
                }

                var itemDiscount = line.DiscountAmount;
                var itemNet = lineGross - itemDiscount;
                var itemVat = line.VatAmount;
                var itemTotal = itemNet + itemVat;

                grossTotal += lineGross;
                totalVat += itemVat;
                totalCogs += (batchCost * qtyPcs);

                var saleItemNo = await conn.ExecuteScalarAsync<long>(
                    "SELECT COALESCE(MAX(sale_invoice_item_no), 0) + 1 FROM sale_invoice_items;", transaction: tran);

                var processedLine = new SaleInvoiceItem
                {
                    SaleInvoiceItemNo = saleItemNo,
                    PharmacyNo = CurrentPharmacyNo,
                    BranchNo = CurrentBranchNo,
                    SaleInvoiceNo = saleInvoiceNo,
                    ProductNo = line.ProductNo,
                    BatchNo = batchNo,
                    SaleMode = (short)(invoice.SaleMode > 0 ? invoice.SaleMode : 1),
                    SaleQty = qtyPcs,
                    BoxQty = product.QuantityPerBox > 0 ? qtyPcs / product.QuantityPerBox : 0,
                    PcsPerBox = product.QuantityPerBox > 0 ? product.QuantityPerBox : 1,
                    UnitCostPrice = batchCost,
                    UnitSalePrice = unitSalePrice,
                    DiscountAmount = itemDiscount,
                    VatPercent = line.VatPercent,
                    VatAmount = itemVat,
                    NetAmount = itemNet,
                    TotalPrice = itemTotal,
                    ProductName = product.ProductName,
                    BatchNumber = batchNumber
                };

                processedItems.Add(processedLine);
            }

            // Calculate invoice totals
            var discountVal = invoice.DiscountValue;
            var netAmount = grossTotal - discountVal;
            var finalPrice = Math.Round(netAmount + totalVat + invoice.RoundOffAmount, 4);
            var customerWillPay = invoice.CustomerWillPay > 0 ? invoice.CustomerWillPay : finalPrice;
            var changeAmount = customerWillPay > finalPrice ? customerWillPay - finalPrice : 0;
            var dueAmount = finalPrice > customerWillPay ? finalPrice - customerWillPay : invoice.DueAmount;

            // Insert invoice header first so foreign keys are satisfied
            const string insertInvoiceSql = @"
                INSERT INTO sale_invoices (
                    sale_invoice_no, uuid, pharmacy_no, branch_no, customer_no,
                    prescription_no, prescription_image_url, sales_number, sale_mode,
                    total_items, gross_total_price, discount_type, discount_value,
                    card_commission_percent, card_commission_amount, net_amount,
                    vat_amount, round_off_amount, final_price, approximate_profit,
                    customer_will_pay, change_amount, due_amount, payment_method,
                    document_status, cash_received_status, received_by_user_no,
                    salesman_user_no, is_quick_printed, sale_timestamp, created_at, updated_at
                ) VALUES (
                    @saleInvoiceNo, gen_random_uuid(), @CurrentPharmacyNo, @CurrentBranchNo, @CustomerNo,
                    @PrescriptionNo, @PrescriptionImageUrl, @salesNumber, @SaleMode,
                    @totalItems, @grossTotal, @DiscountType, @discountVal,
                    @CardCommissionPercent, @CardCommissionAmount, @netAmount,
                    @totalVat, @RoundOffAmount, @finalPrice, @approxProfit,
                    @customerWillPay, @changeAmount, @dueAmount, @PaymentMethod,
                    2, @CashReceivedStatus, @CurrentUserNo,
                    @CurrentUserNo, @IsQuickPrinted, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
                );";

            var totalItemsCount = processedItems.Count;
            var approxProfit = finalPrice - totalCogs;

            await conn.ExecuteAsync(insertInvoiceSql, new
            {
                saleInvoiceNo,
                CurrentPharmacyNo,
                CurrentBranchNo,
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
                CurrentUserNo,
                invoice.IsQuickPrinted
            }, transaction: tran);


            // Now insert line items and deduct stock
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

                // Stock deduction movement (type 4: POS Sale)
                await _stockPosting.PostMovementAsync(conn, tran, new StockMovementEntry
                {
                    PharmacyNo = CurrentPharmacyNo,
                    BranchNo = CurrentBranchNo,
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


            // Record split tender payments
            var paymentsToRecord = payload.Payments ?? new List<SaleInvoicePayment>();
            if (paymentsToRecord.Count == 0 && finalPrice > 0 && dueAmount < finalPrice)
            {
                // Default cash payment
                paymentsToRecord.Add(new SaleInvoicePayment
                {
                    AccountNo = 1, // Main Cash Drawer
                    PaymentMethod = invoice.PaymentMethod > 0 ? invoice.PaymentMethod : (short)1,
                    Amount = finalPrice - dueAmount
                });
            }

            var recordedPayments = new List<SaleInvoicePayment>();
            decimal totalPaidReceived = 0;

            foreach (var pay in paymentsToRecord)
            {
                if (pay.Amount <= 0) continue;

                var paymentNo = await conn.ExecuteScalarAsync<long>(
                    "SELECT COALESCE(MAX(sale_payment_no), 0) + 1 FROM sale_invoice_payments;", transaction: tran);

                const string insertPaymentSql = @"
                    INSERT INTO sale_invoice_payments (
                        sale_payment_no, pharmacy_no, branch_no, sale_invoice_no,
                        account_no, payment_method, amount, gateway_reference, created_at
                    ) VALUES (
                        @paymentNo, @CurrentPharmacyNo, @CurrentBranchNo, @saleInvoiceNo,
                        @AccountNo, @PaymentMethod, @Amount, @GatewayReference, CURRENT_TIMESTAMP
                    );";

                await conn.ExecuteAsync(insertPaymentSql, new
                {
                    paymentNo,
                    CurrentPharmacyNo,
                    CurrentBranchNo,
                    saleInvoiceNo,
                    pay.AccountNo,
                    pay.PaymentMethod,
                    pay.Amount,
                    pay.GatewayReference
                }, transaction: tran);

                totalPaidReceived += pay.Amount;
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
                    WHERE customer_no = @CustomerNo AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
                    new { dueAmount, invoice.CustomerNo, CurrentPharmacyNo, CurrentBranchNo },
                    transaction: tran);
            }

            // Synchronous Double-Entry Accounting Ledger Posting
            var journalEntry = new CompoundJournalEntry
            {
                PharmacyNo = CurrentPharmacyNo,
                BranchNo = CurrentBranchNo,
                TransactionType = 1, // POS Sale
                SourceDocumentType = "SALE_INVOICE",
                SourceDocumentNo = salesNumber,
                SourceDocumentId = saleInvoiceNo,
                CreatedByUserNo = CurrentUserNo
            };

            // 1. Debit Tender Accounts for payments received
            foreach (var pay in recordedPayments)
            {
                journalEntry.Legs.Add(new LedgerLeg
                {
                    AccountNo = pay.AccountNo > 0 ? pay.AccountNo : 1, // 1: Main Cash Drawer
                    DebitAmount = pay.Amount,
                    CreditAmount = 0,
                    Narration = $"POS Sale {salesNumber} payment"
                });
            }

            // 2. Debit AR for Customer Due (Account 3 / 10301)
            if (dueAmount > 0)
            {
                // Find account_no for 10301 AR
                var arAccountNo = await conn.ExecuteScalarAsync<long?>(@"
                    SELECT account_no FROM acc_transaction_accounts
                    WHERE account_code = '10301' AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
                    new { CurrentPharmacyNo, CurrentBranchNo }, transaction: tran) ?? 3;

                journalEntry.Legs.Add(new LedgerLeg
                {
                    AccountNo = arAccountNo,
                    DebitAmount = dueAmount,
                    CreditAmount = 0,
                    PartyType = 1, // Customer
                    PartyNo = invoice.CustomerNo,
                    Narration = $"Customer due on POS sale {salesNumber}"
                });
            }

            // 3. Round-off debit (if round off loss < 0)
            if (invoice.RoundOffAmount < 0)
            {
                var roundLossAcc = await conn.ExecuteScalarAsync<long?>(@"
                    SELECT account_no FROM acc_transaction_accounts
                    WHERE account_code = '50203' AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
                    new { CurrentPharmacyNo, CurrentBranchNo }, transaction: tran) ?? 17;

                journalEntry.Legs.Add(new LedgerLeg
                {
                    AccountNo = roundLossAcc,
                    DebitAmount = Math.Abs(invoice.RoundOffAmount),
                    CreditAmount = 0,
                    Narration = "Rounding loss adjustment"
                });
            }

            // 4. Credit Sales Revenue (Account 9 / 40101)
            var salesRevAcc = await conn.ExecuteScalarAsync<long?>(@"
                SELECT account_no FROM acc_transaction_accounts
                WHERE account_code = '40101' AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
                new { CurrentPharmacyNo, CurrentBranchNo }, transaction: tran) ?? 9;

            journalEntry.Legs.Add(new LedgerLeg
            {
                AccountNo = salesRevAcc,
                DebitAmount = 0,
                CreditAmount = netAmount,
                Narration = $"Revenue from POS sale {salesNumber}"
            });

            // 5. Credit Output VAT Payable (Account 7 / 20201)
            if (totalVat > 0)
            {
                var vatAcc = await conn.ExecuteScalarAsync<long?>(@"
                    SELECT account_no FROM acc_transaction_accounts
                    WHERE account_code = '20201' AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
                    new { CurrentPharmacyNo, CurrentBranchNo }, transaction: tran) ?? 7;

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
                    WHERE account_code = '40202' AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
                    new { CurrentPharmacyNo, CurrentBranchNo }, transaction: tran) ?? 16;

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
                    WHERE account_code = '50101' AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
                    new { CurrentPharmacyNo, CurrentBranchNo }, transaction: tran) ?? 11;

                var invAssetAcc = await conn.ExecuteScalarAsync<long?>(@"
                    SELECT account_no FROM acc_transaction_accounts
                    WHERE account_code = '10401' AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
                    new { CurrentPharmacyNo, CurrentBranchNo }, transaction: tran) ?? 4;

                journalEntry.Legs.Add(new LedgerLeg
                {
                    AccountNo = cogsAcc,
                    DebitAmount = totalCogs,
                    CreditAmount = 0,
                    Narration = $"COGS relief for sale {salesNumber}"
                });

                journalEntry.Legs.Add(new LedgerLeg
                {
                    AccountNo = invAssetAcc,
                    DebitAmount = 0,
                    CreditAmount = totalCogs,
                    Narration = $"Inventory relief for sale {salesNumber}"
                });
            }

            await _ledgerPosting.PostJournalAsync(conn, tran, journalEntry);

            await tran.CommitAsync();

            invoice.SaleInvoiceNo = saleInvoiceNo;
            invoice.SalesNumber = salesNumber;
            invoice.PharmacyNo = CurrentPharmacyNo;
            invoice.BranchNo = CurrentBranchNo;
            invoice.TotalItems = totalItemsCount;
            invoice.GrossTotalPrice = grossTotal;
            invoice.NetAmount = netAmount;
            invoice.VatAmount = totalVat;
            invoice.FinalPrice = finalPrice;
            invoice.DueAmount = dueAmount;
            invoice.ChangeAmount = changeAmount;
            invoice.Items = processedItems;
            invoice.Payments = recordedPayments;

            return OkResponse(invoice, $"Sale {salesNumber} posted successfully.");
        }
        catch (Exception ex)
        {
            await tran.RollbackAsync();
            return FailResponse<SaleInvoice>($"Failed to post sale: {ex.Message}");
        }
    }
}

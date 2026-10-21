using AidlyPms.Api.Common;
using AidlyPms.Api.Common.Database;
using AidlyPms.Api.Infrastructure;
using AidlyPms.Api.Modules.Products.Models;
using AidlyPms.Api.Modules.Sales.Models;
using AidlyPms.Api.Modules.Sales.Services;
using AidlyPms.Api.Modules.Settings.Models;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace AidlyPms.Api.Modules.Sales.Controllers;

[Route("api/v1")]
public class SaleReturnController : BaseController
{
    private readonly IDbConnectionFactory _db;
    private readonly IDocumentNumberService _docNumService;
    private readonly IStockPostingService _stockPosting;
    private readonly ILedgerPostingService _ledgerPosting;
    private readonly ISalePostingEngine _salePostingEngine;

    public SaleReturnController(
        IDbConnectionFactory db,
        IDocumentNumberService docNumService,
        IStockPostingService stockPosting,
        ILedgerPostingService ledgerPosting,
        ISalePostingEngine salePostingEngine)
    {
        _db = db;
        _docNumService = docNumService;
        _stockPosting = stockPosting;
        _ledgerPosting = ledgerPosting;
        _salePostingEngine = salePostingEngine;
    }

    [HttpGet("customer/returns")]
    [HttpGet("pos/sales/returns")]
    public async Task<ActionResult<ApiResponse<PagedResult<SaleReturn>>>> GetReturns(
        [FromQuery] QueryFilter filter,
        [FromQuery] string? search,
        [FromQuery] long? customerNo)
    {
        using var conn = _db.CreateConnection();
        var sql = @"
            SELECT r.*, c.name AS customer_name
            FROM sale_returns r
            LEFT JOIN cust_customers c ON r.customer_no = c.customer_no
            WHERE r.pharmacy_no = @CurrentPharmacyNo AND r.branch_no = @CurrentBranchNo";

        var countSql = @"
            SELECT COUNT(1) FROM sale_returns r
            WHERE r.pharmacy_no = @CurrentPharmacyNo AND r.branch_no = @CurrentBranchNo";

        var p = new DynamicParameters();
        p.Add("CurrentPharmacyNo", CurrentPharmacyNo);
        p.Add("CurrentBranchNo", CurrentBranchNo);

        if (!string.IsNullOrWhiteSpace(search))
        {
            sql += " AND (r.return_invoice_no ILIKE @Search OR r.original_invoice_no ILIKE @Search)";
            countSql += " AND (r.return_invoice_no ILIKE @Search OR r.original_invoice_no ILIKE @Search)";
            p.Add("Search", $"%{search.Trim()}%");
        }

        if (customerNo.HasValue)
        {
            sql += " AND r.customer_no = @CustomerNo";
            countSql += " AND r.customer_no = @CustomerNo";
            p.Add("CustomerNo", customerNo.Value);
        }

        sql += " ORDER BY r.sale_return_no DESC LIMIT @PageSize OFFSET @Offset;";
        p.Add("PageSize", filter.PageSize);
        p.Add("Offset", (filter.PageIndex - 1) * filter.PageSize);

        var total = await conn.ExecuteScalarAsync<int>(countSql, p);
        var items = (await conn.QueryAsync<SaleReturn>(sql, p)).ToList();

        var result = new PagedResult<SaleReturn>(items, total, filter.PageIndex, filter.PageSize);
        return PagedResponse(result);
    }

    [HttpGet("customer/returns/{id}")]
    [HttpGet("pos/sales/returns/{id}")]
    public async Task<ActionResult<ApiResponse<SaleReturn>>> GetReturnById(long id)
    {
        using var conn = _db.CreateConnection();
        var ret = await conn.QuerySingleOrDefaultAsync<SaleReturn>(@"
            SELECT r.*, c.name AS customer_name
            FROM sale_returns r
            LEFT JOIN cust_customers c ON r.customer_no = c.customer_no
            WHERE r.sale_return_no = @Id AND r.pharmacy_no = @CurrentPharmacyNo AND r.branch_no = @CurrentBranchNo;",
            new { Id = id, CurrentPharmacyNo, CurrentBranchNo });

        if (ret == null)
            return FailResponse<SaleReturn>("Sale return not found.", statusCode: 404);

        var items = (await conn.QueryAsync<SaleReturnItem>(@"
            SELECT ri.*, p.product_name
            FROM sale_return_items ri
            JOIN prod_products p ON ri.product_no = p.product_no
            WHERE ri.sale_return_no = @Id;",
            new { Id = id })).ToList();

        ret.Items = items;
        return OkResponse(ret);
    }

    [HttpPost("customer/returns")]
    [HttpPost("pos/sales/return")]
    [HttpPost("pos/sales/returns")]
    [RequirePermission("sale.return")]
    public async Task<ActionResult<ApiResponse<SaleReturn>>> ProcessReturn([FromBody] ReturnReq req)
    {
        if (req.Lines == null || req.Lines.Count == 0)
        {
            return FailResponse<SaleReturn>("At least one return item line must be provided.");
        }

        await using var conn = await _db.CreateOpenConnectionAsync();
        await using var tran = await conn.BeginTransactionAsync();

        try
        {
            // 1. Idempotency Check
            if (req.IdempotencyKey.HasValue)
            {
                var existingReturn = await conn.QuerySingleOrDefaultAsync<SaleReturn>(@"
                    SELECT r.*, c.name AS customer_name
                    FROM sale_returns r
                    LEFT JOIN cust_customers c ON r.customer_no = c.customer_no
                    WHERE r.idempotency_key = @IdempotencyKey
                      AND r.pharmacy_no = @CurrentPharmacyNo
                      AND r.branch_no = @CurrentBranchNo;",
                    new { req.IdempotencyKey, CurrentPharmacyNo, CurrentBranchNo },
                    transaction: tran);

                if (existingReturn != null)
                {
                    existingReturn.Items = (await conn.QueryAsync<SaleReturnItem>(@"
                        SELECT ri.*, p.product_name
                        FROM sale_return_items ri
                        JOIN prod_products p ON ri.product_no = p.product_no
                        WHERE ri.sale_return_no = @SaleReturnNo;",
                        new { existingReturn.SaleReturnNo },
                        transaction: tran)).ToList();

                    await tran.RollbackAsync();
                    return OkResponse(existingReturn, "Return request already processed (idempotent replay).");
                }
            }

            // 2. Open Cash Shift Guard: Check if shift is closed for Dhaka today
            var dhakaToday = DateHelper.GetDhakaToday();
            var shiftClosed = await conn.ExecuteScalarAsync<bool>(@"
                SELECT EXISTS(
                    SELECT 1 FROM cash_closers
                    WHERE pharmacy_no = @CurrentPharmacyNo
                      AND branch_no = @CurrentBranchNo
                      AND (closing_date AT TIME ZONE 'Asia/Dhaka')::date = @DhakaToday
                );",
                new { CurrentPharmacyNo, CurrentBranchNo, DhakaToday = dhakaToday.ToDateTime(TimeOnly.MinValue) },
                transaction: tran);

            if (shiftClosed)
            {
                await tran.RollbackAsync();
                return FailResponse<SaleReturn>(
                    $"Cannot process return: Cash shift for today ({dhakaToday:yyyy-MM-dd}) is closed.",
                    statusCode: 422);
            }

            // 3. Resolve Original Sale Invoice
            SaleInvoice? origInvoice = null;
            if (req.SaleInvoiceNo.HasValue && req.SaleInvoiceNo.Value > 0)
            {
                origInvoice = await conn.QuerySingleOrDefaultAsync<SaleInvoice>(@"
                    SELECT * FROM sale_invoices
                    WHERE sale_invoice_no = @SaleInvoiceNo
                      AND pharmacy_no = @CurrentPharmacyNo
                      AND branch_no = @CurrentBranchNo;",
                    new { req.SaleInvoiceNo, CurrentPharmacyNo, CurrentBranchNo },
                    transaction: tran);
            }
            else if (!string.IsNullOrWhiteSpace(req.OriginalInvoiceNo))
            {
                origInvoice = await conn.QuerySingleOrDefaultAsync<SaleInvoice>(@"
                    SELECT * FROM sale_invoices
                    WHERE sales_number = @OriginalInvoiceNo
                      AND pharmacy_no = @CurrentPharmacyNo
                      AND branch_no = @CurrentBranchNo;",
                    new { req.OriginalInvoiceNo, CurrentPharmacyNo, CurrentBranchNo },
                    transaction: tran);
            }

            if (origInvoice == null)
            {
                await tran.RollbackAsync();
                return FailResponse<SaleReturn>("Original sale invoice was not found for this branch.", statusCode: 404);
            }

            if (origInvoice.DocumentStatus == 3)
            {
                await tran.RollbackAsync();
                return FailResponse<SaleReturn>(
                    $"Cannot process return: Original sale invoice {origInvoice.SalesNumber} has been voided.",
                    statusCode: 422);
            }

            // 4. Return Window Check
            var saleConfig = await conn.QueryFirstOrDefaultAsync<CfgSaleConfig>(@"
                SELECT * FROM cfg_sale_configs
                WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
                new { CurrentPharmacyNo, CurrentBranchNo },
                transaction: tran);

            var returnWindowDays = saleConfig?.ReturnWindowDays > 0 ? saleConfig.ReturnWindowDays : 7;
            var saleDhakaTime = TimeZoneInfo.ConvertTimeFromUtc(origInvoice.SaleTimestamp, DateHelper.DhakaTimeZone);
            var nowDhakaTime = DateHelper.GetDhakaNow();
            if ((nowDhakaTime - saleDhakaTime).TotalDays > returnWindowDays)
            {
                await tran.RollbackAsync();
                return FailResponse<SaleReturn>(
                    $"Return window of {returnWindowDays} day(s) has expired for invoice {origInvoice.SalesNumber}.",
                    statusCode: 422);
            }

            // 5. Line Items Evaluation with Returnable Quantity Cap and DB Pricing
            var returnNumber = await _docNumService.GetNextDocumentNumberAsync(
                conn, tran, CurrentPharmacyNo, CurrentBranchNo, "SALE_RETURN");

            var nextReturnNo = await conn.ExecuteScalarAsync<long>(
                "SELECT COALESCE(MAX(sale_return_no), 0) + 1 FROM sale_returns;", transaction: tran);

            var maxReturnItemNo = await conn.ExecuteScalarAsync<long>(
                "SELECT COALESCE(MAX(sale_return_item_no), 0) FROM sale_return_items;", transaction: tran);

            decimal totalGross = 0;
            decimal totalNet = 0;
            decimal totalVat = 0;
            decimal totalDiscountDeduction = 0;
            decimal totalCogsReversal = 0;

            var processedItems = new List<SaleReturnItem>();
            var invDiscountRatio = origInvoice.GrossTotalPrice > 0
                ? (origInvoice.DiscountValue / origInvoice.GrossTotalPrice)
                : 0m;

            foreach (var reqLine in req.Lines)
            {
                if (reqLine.QtyPcs <= 0) continue;

                var origItem = await conn.QuerySingleOrDefaultAsync<SaleInvoiceItem>(@"
                    SELECT * FROM sale_invoice_items
                    WHERE sale_invoice_item_no = @SaleInvoiceItemNo
                      AND sale_invoice_no = @SaleInvoiceNo
                      AND pharmacy_no = @CurrentPharmacyNo
                      AND branch_no = @CurrentBranchNo;",
                    new { reqLine.SaleInvoiceItemNo, origInvoice.SaleInvoiceNo, CurrentPharmacyNo, CurrentBranchNo },
                    transaction: tran);

                if (origItem == null)
                {
                    await tran.RollbackAsync();
                    return FailResponse<SaleReturn>(
                        $"Invoice line #{reqLine.SaleInvoiceItemNo} not found on invoice {origInvoice.SalesNumber}.",
                        statusCode: 422);
                }

                // Check previously completed returns for this line
                var alreadyReturned = await conn.QuerySingleOrDefaultAsync<(int Qty, decimal Net, decimal Vat)>(@"
                    SELECT COALESCE(SUM(ri.total_qty_pcs), 0) AS Qty,
                           COALESCE(SUM(ri.net_amount), 0) AS Net,
                           COALESCE(SUM(ri.vat_amount), 0) AS Vat
                    FROM sale_return_items ri
                    JOIN sale_returns r ON ri.sale_return_no = r.sale_return_no
                    WHERE ri.sale_invoice_item_no = @SaleInvoiceItemNo
                      AND r.return_status = 2;",
                    new { reqLine.SaleInvoiceItemNo },
                    transaction: tran);

                var maxReturnableQty = origItem.SaleQty - alreadyReturned.Qty;
                if (reqLine.QtyPcs > maxReturnableQty)
                {
                    await tran.RollbackAsync();
                    return FailResponse<SaleReturn>(
                        $"Requested return quantity ({reqLine.QtyPcs}) exceeds returnable quantity ({maxReturnableQty}) for line item #{origItem.SaleInvoiceItemNo}.",
                        statusCode: 422);
                }

                var product = await conn.QuerySingleOrDefaultAsync<ProdProduct>(@"
                    SELECT product_no, product_name, is_prescription_required, is_narcotic, purchase_price_per_piece
                    FROM prod_products
                    WHERE product_no = @ProductNo AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
                    new { origItem.ProductNo, CurrentPharmacyNo, CurrentBranchNo },
                    transaction: tran);

                // Narcotic / prescription-only validation: strictly non-returnable for all roles
                if (product != null && (product.IsPrescriptionRequired || product.IsNarcotic))
                {
                    await tran.RollbackAsync();
                    return FailResponse<SaleReturn>(
                        $"Product '{product.ProductName}' is prescription-controlled / narcotic and cannot be returned.",
                        statusCode: 400);
                }

                // Server-computed pricing: Exact money from DB original line
                decimal lineGross = Math.Round(reqLine.QtyPcs * origItem.UnitSalePrice, 2, MidpointRounding.AwayFromZero);
                decimal lineNet;
                decimal lineVat;

                // Last-unit remainder rule: If returning all remaining units, use subtraction to prevent penny drift
                if (reqLine.QtyPcs == maxReturnableQty)
                {
                    var fullOriginalNet = origItem.NetAmount * (1 - invDiscountRatio);
                    lineNet = Math.Max(0, Math.Round(fullOriginalNet - alreadyReturned.Net, 2, MidpointRounding.AwayFromZero));
                    lineVat = Math.Max(0, Math.Round(origItem.VatAmount - alreadyReturned.Vat, 2, MidpointRounding.AwayFromZero));
                }
                else
                {
                    lineNet = Math.Round((origItem.NetAmount / origItem.SaleQty) * reqLine.QtyPcs * (1 - invDiscountRatio), 2, MidpointRounding.AwayFromZero);
                    lineVat = Math.Round((origItem.VatAmount / origItem.SaleQty) * reqLine.QtyPcs, 2, MidpointRounding.AwayFromZero);
                }

                decimal lineDiscount = Math.Max(0, lineGross - lineNet);
                decimal lineTotal = lineNet + lineVat;

                totalGross += lineGross;
                totalNet += lineNet;
                totalVat += lineVat;
                totalDiscountDeduction += lineDiscount;

                var disposition = reqLine.Disposition > 0 ? reqLine.Disposition : (short)1;
                var reasonCode = reqLine.ReasonCode > 0 ? reqLine.ReasonCode : (short)4;

                maxReturnItemNo++;
                var returnItemNo = maxReturnItemNo;

                // Stock Movement: Restock to original batch (Disposition 1)
                if (disposition == 1 && origItem.BatchNo.HasValue && origItem.BatchNo.Value > 0)
                {
                    var lineCogs = Math.Round(reqLine.QtyPcs * origItem.UnitCostPrice, 4);
                    totalCogsReversal += lineCogs;

                    await _stockPosting.PostMovementAsync(conn, tran, new StockMovementEntry
                    {
                        PharmacyNo = CurrentPharmacyNo,
                        BranchNo = CurrentBranchNo,
                        ProductNo = origItem.ProductNo,
                        BatchNo = origItem.BatchNo.Value,
                        MovementType = 3, // Customer Return (+)
                        ReferenceDocType = "SALE_RETURN",
                        ReferenceDocNo = returnNumber,
                        ReferenceDocId = nextReturnNo,
                        QtyPcs = reqLine.QtyPcs,
                        UnitCost = origItem.UnitCostPrice,
                        Remarks = $"Customer return restocked to batch {origItem.BatchNo}"
                    });
                }

                // Batch Number lookup for item row
                var batchNumber = await conn.ExecuteScalarAsync<string?>(@"
                    SELECT batch_number FROM prod_product_batches
                    WHERE batch_no = @BatchNo AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
                    new { BatchNo = origItem.BatchNo, CurrentPharmacyNo, CurrentBranchNo },
                    transaction: tran) ?? string.Empty;

                var retItem = new SaleReturnItem
                {
                    SaleReturnItemNo = returnItemNo,
                    PharmacyNo = CurrentPharmacyNo,
                    BranchNo = CurrentBranchNo,
                    SaleReturnNo = nextReturnNo,
                    SaleInvoiceItemNo = origItem.SaleInvoiceItemNo,
                    ProductNo = origItem.ProductNo,
                    BatchNumber = batchNumber,
                    BoxQty = 0,
                    QtyInBox = origItem.PcsPerBox > 0 ? origItem.PcsPerBox : 1,
                    TotalQtyPcs = reqLine.QtyPcs,
                    BtpVat = 0,
                    SalePrice = origItem.UnitSalePrice,
                    VatPercent = origItem.VatPercent,
                    VatAmount = lineVat,
                    NetAmount = lineNet,
                    TotalPrice = lineTotal,
                    FreeQtyPcs = 0,
                    ReasonCode = reasonCode,
                    Disposition = disposition,
                    ProductName = product?.ProductName
                };

                processedItems.Add(retItem);
            }

            if (processedItems.Count == 0)
            {
                await tran.RollbackAsync();
                return FailResponse<SaleReturn>("No valid return items were processed.");
            }

            var finalRefundPrice = Math.Round(totalNet + totalVat, 2);

            // 6. Tender Refund Hierarchy
            // Step A: Offset customer due if original invoice had unpaid balance and customer currently has due
            decimal dueAdjustment = 0;
            var customerNo = req.CustomerNo ?? origInvoice.CustomerNo;
            if (customerNo.HasValue && customerNo.Value > 0 && origInvoice.DueAmount > 0)
            {
                var currentCustomerDue = await conn.ExecuteScalarAsync<decimal>(@"
                    SELECT current_due FROM cust_customers
                    WHERE customer_no = @CustomerNo AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
                    new { CustomerNo = customerNo.Value, CurrentPharmacyNo, CurrentBranchNo },
                    transaction: tran);

                if (currentCustomerDue > 0)
                {
                    dueAdjustment = Math.Min(finalRefundPrice, Math.Min(origInvoice.DueAmount, currentCustomerDue));
                    await conn.ExecuteAsync(@"
                        UPDATE cust_customers
                        SET current_due = GREATEST(0, current_due - @DueAdjustment),
                            has_due = CASE WHEN current_due - @DueAdjustment > 0 THEN true ELSE false END,
                            updated_at = CURRENT_TIMESTAMP
                        WHERE customer_no = @CustomerNo AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
                        new { DueAdjustment = dueAdjustment, CustomerNo = customerNo.Value, CurrentPharmacyNo, CurrentBranchNo },
                        transaction: tran);
                }
            }

            var netRefundRemaining = Math.Max(0, finalRefundPrice - dueAdjustment);

            // Step B: Capped cash/channel refund
            var origTotalPaid = Math.Max(0, origInvoice.FinalPrice - origInvoice.DueAmount);
            var cashPaidRefund = Math.Min(netRefundRemaining, origTotalPaid);
            var storeCreditExcess = netRefundRemaining - cashPaidRefund;

            if (storeCreditExcess > 0 && customerNo.HasValue && customerNo.Value > 0)
            {
                await conn.ExecuteAsync(@"
                    UPDATE cust_customers
                    SET advance_balance = COALESCE(advance_balance, 0) + @StoreCredit,
                        updated_at = CURRENT_TIMESTAMP
                    WHERE customer_no = @CustomerNo AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
                    new { StoreCredit = storeCreditExcess, CustomerNo = customerNo.Value, CurrentPharmacyNo, CurrentBranchNo },
                    transaction: tran);
            }

            // Step C: Customer Loyalty points rollback
            if (customerNo.HasValue && customerNo.Value > 0 && origInvoice.FinalPrice > 0)
            {
                var loyaltyCheck = await conn.QuerySingleOrDefaultAsync<(bool IsLoyaltyMember, bool IsEnabled, decimal SpendPerPoint)>(@"
                    SELECT c.is_loyalty_member AS IsLoyaltyMember,
                           COALESCE(cfg.is_enabled, false) AS IsEnabled,
                           COALESCE(cfg.earning_spend_amount_per_point, 100) AS SpendPerPoint
                    FROM cust_customers c
                    LEFT JOIN cfg_loyalty_configs cfg
                           ON cfg.pharmacy_no = @CurrentPharmacyNo AND cfg.branch_no = @CurrentBranchNo
                    WHERE c.customer_no = @CustomerNo AND c.pharmacy_no = @CurrentPharmacyNo AND c.branch_no = @CurrentBranchNo;",
                    new { CustomerNo = customerNo.Value, CurrentPharmacyNo, CurrentBranchNo },
                    transaction: tran);

                if (loyaltyCheck.IsLoyaltyMember && loyaltyCheck.IsEnabled && loyaltyCheck.SpendPerPoint > 0)
                {
                    var totalOrigPoints = Math.Floor(origInvoice.FinalPrice / loyaltyCheck.SpendPerPoint);
                    var pointsToDeduct = (int)Math.Floor((finalRefundPrice / origInvoice.FinalPrice) * totalOrigPoints);
                    if (pointsToDeduct > 0)
                    {
                        await conn.ExecuteAsync(@"
                            UPDATE cust_customers
                            SET loyalty_points = GREATEST(0, loyalty_points - @PointsToDeduct),
                                updated_at = CURRENT_TIMESTAMP
                            WHERE customer_no = @CustomerNo AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
                            new { PointsToDeduct = pointsToDeduct, CustomerNo = customerNo.Value, CurrentPharmacyNo, CurrentBranchNo },
                            transaction: tran);
                    }
                }
            }

            // 7. Insert sale_returns header
            var refundMethod = req.RefundMethod ?? origInvoice.PaymentMethod;

            await conn.ExecuteAsync(@"
                INSERT INTO sale_returns (
                    sale_return_no, pharmacy_no, branch_no, sale_invoice_no, customer_no,
                    return_invoice_no, original_invoice_no, total_items,
                    gross_refund_price, discount_deduction, net_amount, vat_amount,
                    round_off_amount, final_refund_price, cash_paid_refund, inventory_type,
                    return_status, refund_method, exchange_invoice_no, idempotency_key,
                    created_by_user_no, return_date, created_at
                ) VALUES (
                    @SaleReturnNo, @CurrentPharmacyNo, @CurrentBranchNo, @SaleInvoiceNo, @CustomerNo,
                    @ReturnInvoiceNo, @OriginalInvoiceNo, @TotalItems,
                    @GrossRefundPrice, @DiscountDeduction, @NetAmount, @VatAmount,
                    0, @FinalRefundPrice, @CashPaidRefund, 1,
                    2, @RefundMethod, NULL, @IdempotencyKey,
                    @CurrentUserNo, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
                );",
                new
                {
                    SaleReturnNo = nextReturnNo,
                    CurrentPharmacyNo,
                    CurrentBranchNo,
                    origInvoice.SaleInvoiceNo,
                    CustomerNo = customerNo,
                    ReturnInvoiceNo = returnNumber,
                    OriginalInvoiceNo = origInvoice.SalesNumber,
                    TotalItems = processedItems.Count,
                    GrossRefundPrice = totalGross,
                    DiscountDeduction = totalDiscountDeduction,
                    NetAmount = totalNet,
                    VatAmount = totalVat,
                    FinalRefundPrice = finalRefundPrice,
                    CashPaidRefund = cashPaidRefund,
                    RefundMethod = refundMethod,
                    req.IdempotencyKey,
                    CurrentUserNo
                },
                transaction: tran);

            // 8. Insert sale_return_items
            const string insertItemSql = @"
                INSERT INTO sale_return_items (
                    sale_return_item_no, pharmacy_no, branch_no, sale_return_no,
                    sale_invoice_item_no, product_no, batch_number, box_qty,
                    qty_in_box, total_qty_pcs, btp_vat, sale_price,
                    vat_percent, vat_amount, net_amount, total_price,
                    free_qty_pcs, expiry_date, reason_code, disposition
                ) VALUES (
                    @SaleReturnItemNo, @PharmacyNo, @BranchNo, @SaleReturnNo,
                    @SaleInvoiceItemNo, @ProductNo, @BatchNumber, @BoxQty,
                    @QtyInBox, @TotalQtyPcs, @BtpVat, @SalePrice,
                    @VatPercent, @VatAmount, @NetAmount, @TotalPrice,
                    @FreeQtyPcs, @ExpiryDate, @ReasonCode, @Disposition
                );";

            foreach (var item in processedItems)
            {
                await conn.ExecuteAsync(insertItemSql, item, transaction: tran);
            }

            // 9. Real General Ledger Journal Posting
            var journalEntry = new CompoundJournalEntry
            {
                PharmacyNo = CurrentPharmacyNo,
                BranchNo = CurrentBranchNo,
                TransactionType = 2, // Sale Return
                SourceDocumentType = "SALE_RETURN",
                SourceDocumentNo = returnNumber,
                SourceDocumentId = nextReturnNo,
                CreatedByUserNo = CurrentUserNo,
                Legs = new List<LedgerLeg>()
            };

            // Dr 40102 Sales Returns & Allowances
            var retAcc = await conn.ExecuteScalarAsync<long?>(@"
                SELECT account_no FROM acc_transaction_accounts
                WHERE account_code = '40102' AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
                new { CurrentPharmacyNo, CurrentBranchNo }, transaction: tran) ?? 1;

            if (totalNet > 0)
            {
                journalEntry.Legs.Add(new LedgerLeg
                {
                    AccountNo = retAcc,
                    DebitAmount = totalNet,
                    CreditAmount = 0,
                    Narration = $"Sales return allowance for {returnNumber}"
                });
            }

            // Dr 20201 Output VAT Reversal
            if (totalVat > 0)
            {
                var vatAcc = await conn.ExecuteScalarAsync<long?>(@"
                    SELECT account_no FROM acc_transaction_accounts
                    WHERE account_code = '20201' AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
                    new { CurrentPharmacyNo, CurrentBranchNo }, transaction: tran) ?? 7;

                journalEntry.Legs.Add(new LedgerLeg
                {
                    AccountNo = vatAcc,
                    DebitAmount = totalVat,
                    CreditAmount = 0,
                    Narration = $"Output VAT reversal on return {returnNumber}"
                });
            }

            // Cr 10301 AR for Customer Due Offset
            if (dueAdjustment > 0)
            {
                var arAcc = await conn.ExecuteScalarAsync<long?>(@"
                    SELECT account_no FROM acc_transaction_accounts
                    WHERE account_code = '10301' AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
                    new { CurrentPharmacyNo, CurrentBranchNo }, transaction: tran) ?? 6;

                journalEntry.Legs.Add(new LedgerLeg
                {
                    AccountNo = arAcc,
                    DebitAmount = 0,
                    CreditAmount = dueAdjustment,
                    Narration = $"Customer AR credit relief on return {returnNumber}"
                });
            }

            // Cr Cash / Bank / MFS account for disbursed refund
            if (cashPaidRefund > 0)
            {
                var refundAccountCode = refundMethod switch
                {
                    2 => "10201",
                    3 => "10202",
                    4 => "10203",
                    6 => "10205",
                    _ => "10101"
                };

                var refundAcc = await conn.ExecuteScalarAsync<long?>(@"
                    SELECT account_no FROM acc_transaction_accounts
                    WHERE account_code = @refundAccountCode AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
                    new { refundAccountCode, CurrentPharmacyNo, CurrentBranchNo }, transaction: tran) ?? 1;

                journalEntry.Legs.Add(new LedgerLeg
                {
                    AccountNo = refundAcc,
                    DebitAmount = 0,
                    CreditAmount = cashPaidRefund,
                    Narration = $"Refund disbursed via channel {refundMethod} for {returnNumber}"
                });
            }

            // Cr 20301 Customer Store Credit Liability for excess
            if (storeCreditExcess > 0)
            {
                var creditAcc = await conn.ExecuteScalarAsync<long?>(@"
                    SELECT account_no FROM acc_transaction_accounts
                    WHERE account_code = '20301' AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
                    new { CurrentPharmacyNo, CurrentBranchNo }, transaction: tran) ?? 1;

                journalEntry.Legs.Add(new LedgerLeg
                {
                    AccountNo = creditAcc,
                    DebitAmount = 0,
                    CreditAmount = storeCreditExcess,
                    Narration = $"Store credit issued to customer on return {returnNumber}"
                });
            }

            // Restock COGS Relief: Dr 10401 Inventory Asset, Cr 50101 COGS
            if (totalCogsReversal > 0)
            {
                var invAssetAcc = await conn.ExecuteScalarAsync<long?>(@"
                    SELECT account_no FROM acc_transaction_accounts
                    WHERE account_code = '10401' AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
                    new { CurrentPharmacyNo, CurrentBranchNo }, transaction: tran) ?? 4;

                var cogsAcc = await conn.ExecuteScalarAsync<long?>(@"
                    SELECT account_no FROM acc_transaction_accounts
                    WHERE account_code = '50101' AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
                    new { CurrentPharmacyNo, CurrentBranchNo }, transaction: tran) ?? 11;

                journalEntry.Legs.Add(new LedgerLeg
                {
                    AccountNo = invAssetAcc,
                    DebitAmount = totalCogsReversal,
                    CreditAmount = 0,
                    Narration = $"Inventory asset restocked on {returnNumber}"
                });

                journalEntry.Legs.Add(new LedgerLeg
                {
                    AccountNo = cogsAcc,
                    DebitAmount = 0,
                    CreditAmount = totalCogsReversal,
                    Narration = $"COGS reversal on return {returnNumber}"
                });
            }

            await _ledgerPosting.PostJournalAsync(conn, tran, journalEntry);

            await tran.CommitAsync();

            var savedReturn = new SaleReturn
            {
                SaleReturnNo = nextReturnNo,
                PharmacyNo = CurrentPharmacyNo,
                BranchNo = CurrentBranchNo,
                SaleInvoiceNo = origInvoice.SaleInvoiceNo,
                CustomerNo = customerNo,
                ReturnInvoiceNo = returnNumber,
                OriginalInvoiceNo = origInvoice.SalesNumber,
                TotalItems = processedItems.Count,
                GrossRefundPrice = totalGross,
                DiscountDeduction = totalDiscountDeduction,
                NetAmount = totalNet,
                VatAmount = totalVat,
                FinalRefundPrice = finalRefundPrice,
                CashPaidRefund = cashPaidRefund,
                RefundMethod = refundMethod,
                IdempotencyKey = req.IdempotencyKey,
                ReturnStatus = 2,
                ReturnDate = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                Items = processedItems
            };

            return OkResponse(savedReturn, $"Return {returnNumber} processed successfully.");
        }
        catch
        {
            await tran.RollbackAsync();
            throw;
        }
    }

    [HttpPost("pos/exchange")]
    [RequirePermission("sale.return")]
    public async Task<ActionResult<ApiResponse<ExchangeResult>>> ProcessExchange([FromBody] ExchangeReq req)
    {
        if (req.Return == null || req.Return.Lines == null || req.Return.Lines.Count == 0)
        {
            return FailResponse<ExchangeResult>("Exchange requires return items.");
        }

        if (req.NewItems == null || req.NewItems.Count == 0)
        {
            return FailResponse<ExchangeResult>("Exchange requires at least one new replacement item.");
        }

        await using var conn = await _db.CreateOpenConnectionAsync();
        await using var tran = await conn.BeginTransactionAsync();

        try
        {
            // 1. Shift Open Check
            var dhakaToday = DateHelper.GetDhakaToday();
            var shiftClosed = await conn.ExecuteScalarAsync<bool>(@"
                SELECT EXISTS(
                    SELECT 1 FROM cash_closers
                    WHERE pharmacy_no = @CurrentPharmacyNo
                      AND branch_no = @CurrentBranchNo
                      AND (closing_date AT TIME ZONE 'Asia/Dhaka')::date = @DhakaToday
                );",
                new { CurrentPharmacyNo, CurrentBranchNo, DhakaToday = dhakaToday.ToDateTime(TimeOnly.MinValue) },
                transaction: tran);

            if (shiftClosed)
            {
                await tran.RollbackAsync();
                return FailResponse<ExchangeResult>(
                    $"Cannot process exchange: Cash shift for today ({dhakaToday:yyyy-MM-dd}) is closed.",
                    statusCode: 422);
            }

            // 2. Resolve Dynamic Clearing Account 10901 per pharmacy & branch
            var clearingAccountNo = await conn.ExecuteScalarAsync<long?>(@"
                SELECT account_no FROM acc_transaction_accounts
                WHERE (account_code = '10901' OR account_name = 'Return Credit Clearing')
                  AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
                new { CurrentPharmacyNo, CurrentBranchNo },
                transaction: tran);

            if (!clearingAccountNo.HasValue)
            {
                clearingAccountNo = await conn.ExecuteScalarAsync<long>(@"
                    INSERT INTO acc_transaction_accounts (
                        account_no, pharmacy_no, branch_no, account_category, account_code, account_name,
                        account_type, current_balance, is_active, created_at, updated_at
                    ) VALUES (
                        (SELECT COALESCE(MAX(account_no), 0) + 1 FROM acc_transaction_accounts),
                        @CurrentPharmacyNo, @CurrentBranchNo, 1, '10901', 'Return Credit Clearing',
                        1, 0, true, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
                    ) RETURNING account_no;",
                    new { CurrentPharmacyNo, CurrentBranchNo },
                    transaction: tran);
            }

            // 3. Process the Return portion
            SaleInvoice? origInvoice = null;
            if (req.Return.SaleInvoiceNo.HasValue && req.Return.SaleInvoiceNo.Value > 0)
            {
                origInvoice = await conn.QuerySingleOrDefaultAsync<SaleInvoice>(@"
                    SELECT * FROM sale_invoices
                    WHERE sale_invoice_no = @SaleInvoiceNo
                      AND pharmacy_no = @CurrentPharmacyNo
                      AND branch_no = @CurrentBranchNo;",
                    new { req.Return.SaleInvoiceNo, CurrentPharmacyNo, CurrentBranchNo },
                    transaction: tran);
            }
            else if (!string.IsNullOrWhiteSpace(req.Return.OriginalInvoiceNo))
            {
                origInvoice = await conn.QuerySingleOrDefaultAsync<SaleInvoice>(@"
                    SELECT * FROM sale_invoices
                    WHERE sales_number = @OriginalInvoiceNo
                      AND pharmacy_no = @CurrentPharmacyNo
                      AND branch_no = @CurrentBranchNo;",
                    new { req.Return.OriginalInvoiceNo, CurrentPharmacyNo, CurrentBranchNo },
                    transaction: tran);
            }

            if (origInvoice == null)
            {
                await tran.RollbackAsync();
                return FailResponse<ExchangeResult>("Original sale invoice not found.", statusCode: 404);
            }

            if (origInvoice.DocumentStatus == 3)
            {
                await tran.RollbackAsync();
                return FailResponse<ExchangeResult>("Cannot exchange against voided sale invoice.", statusCode: 422);
            }

            var returnNumber = await _docNumService.GetNextDocumentNumberAsync(
                conn, tran, CurrentPharmacyNo, CurrentBranchNo, "SALE_RETURN");

            var nextReturnNo = await conn.ExecuteScalarAsync<long>(
                "SELECT COALESCE(MAX(sale_return_no), 0) + 1 FROM sale_returns;", transaction: tran);

            var maxReturnItemNo = await conn.ExecuteScalarAsync<long>(
                "SELECT COALESCE(MAX(sale_return_item_no), 0) FROM sale_return_items;", transaction: tran);

            decimal returnGross = 0;
            decimal returnNet = 0;
            decimal returnVat = 0;
            decimal returnDiscountDeduction = 0;
            decimal returnCogsReversal = 0;

            var processedReturnItems = new List<SaleReturnItem>();
            var invDiscountRatio = origInvoice.GrossTotalPrice > 0
                ? (origInvoice.DiscountValue / origInvoice.GrossTotalPrice)
                : 0m;

            foreach (var rLine in req.Return.Lines)
            {
                if (rLine.QtyPcs <= 0) continue;

                var origItem = await conn.QuerySingleOrDefaultAsync<SaleInvoiceItem>(@"
                    SELECT * FROM sale_invoice_items
                    WHERE sale_invoice_item_no = @SaleInvoiceItemNo
                      AND sale_invoice_no = @SaleInvoiceNo
                      AND pharmacy_no = @CurrentPharmacyNo
                      AND branch_no = @CurrentBranchNo;",
                    new { rLine.SaleInvoiceItemNo, origInvoice.SaleInvoiceNo, CurrentPharmacyNo, CurrentBranchNo },
                    transaction: tran);

                if (origItem == null)
                {
                    await tran.RollbackAsync();
                    return FailResponse<ExchangeResult>($"Original item #{rLine.SaleInvoiceItemNo} not found.", statusCode: 422);
                }

                var alreadyReturned = await conn.QuerySingleOrDefaultAsync<(int Qty, decimal Net, decimal Vat)>(@"
                    SELECT COALESCE(SUM(ri.total_qty_pcs), 0) AS Qty,
                           COALESCE(SUM(ri.net_amount), 0) AS Net,
                           COALESCE(SUM(ri.vat_amount), 0) AS Vat
                    FROM sale_return_items ri
                    JOIN sale_returns r ON ri.sale_return_no = r.sale_return_no
                    WHERE ri.sale_invoice_item_no = @SaleInvoiceItemNo
                      AND r.return_status = 2;",
                    new { rLine.SaleInvoiceItemNo },
                    transaction: tran);

                var maxReturnableQty = origItem.SaleQty - alreadyReturned.Qty;
                if (rLine.QtyPcs > maxReturnableQty)
                {
                    await tran.RollbackAsync();
                    return FailResponse<ExchangeResult>(
                        $"Return quantity {rLine.QtyPcs} exceeds returnable quantity ({maxReturnableQty}) for line item #{origItem.SaleInvoiceItemNo}.",
                        statusCode: 422);
                }

                decimal lineGross = Math.Round(rLine.QtyPcs * origItem.UnitSalePrice, 2, MidpointRounding.AwayFromZero);
                decimal lineNet;
                decimal lineVat;

                if (rLine.QtyPcs == maxReturnableQty)
                {
                    var fullOriginalNet = origItem.NetAmount * (1 - invDiscountRatio);
                    lineNet = Math.Max(0, Math.Round(fullOriginalNet - alreadyReturned.Net, 2, MidpointRounding.AwayFromZero));
                    lineVat = Math.Max(0, Math.Round(origItem.VatAmount - alreadyReturned.Vat, 2, MidpointRounding.AwayFromZero));
                }
                else
                {
                    lineNet = Math.Round((origItem.NetAmount / origItem.SaleQty) * rLine.QtyPcs * (1 - invDiscountRatio), 2, MidpointRounding.AwayFromZero);
                    lineVat = Math.Round((origItem.VatAmount / origItem.SaleQty) * rLine.QtyPcs, 2, MidpointRounding.AwayFromZero);
                }

                decimal lineDiscount = Math.Max(0, lineGross - lineNet);
                decimal lineTotal = lineNet + lineVat;

                returnGross += lineGross;
                returnNet += lineNet;
                returnVat += lineVat;
                returnDiscountDeduction += lineDiscount;

                var disposition = rLine.Disposition > 0 ? rLine.Disposition : (short)1;
                var reasonCode = rLine.ReasonCode > 0 ? rLine.ReasonCode : (short)4;

                maxReturnItemNo++;
                var returnItemNo = maxReturnItemNo;

                if (disposition == 1 && origItem.BatchNo.HasValue && origItem.BatchNo.Value > 0)
                {
                    var lineCogs = Math.Round(rLine.QtyPcs * origItem.UnitCostPrice, 4);
                    returnCogsReversal += lineCogs;

                    await _stockPosting.PostMovementAsync(conn, tran, new StockMovementEntry
                    {
                        PharmacyNo = CurrentPharmacyNo,
                        BranchNo = CurrentBranchNo,
                        ProductNo = origItem.ProductNo,
                        BatchNo = origItem.BatchNo.Value,
                        MovementType = 3,
                        ReferenceDocType = "SALE_RETURN",
                        ReferenceDocNo = returnNumber,
                        ReferenceDocId = nextReturnNo,
                        QtyPcs = rLine.QtyPcs,
                        UnitCost = origItem.UnitCostPrice,
                        Remarks = $"Customer return (exchange) restocked to batch {origItem.BatchNo}"
                    });
                }

                var batchNumber = await conn.ExecuteScalarAsync<string?>(@"
                    SELECT batch_number FROM prod_product_batches
                    WHERE batch_no = @BatchNo AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
                    new { BatchNo = origItem.BatchNo, CurrentPharmacyNo, CurrentBranchNo },
                    transaction: tran) ?? string.Empty;

                processedReturnItems.Add(new SaleReturnItem
                {
                    SaleReturnItemNo = returnItemNo,
                    PharmacyNo = CurrentPharmacyNo,
                    BranchNo = CurrentBranchNo,
                    SaleReturnNo = nextReturnNo,
                    SaleInvoiceItemNo = origItem.SaleInvoiceItemNo,
                    ProductNo = origItem.ProductNo,
                    BatchNumber = batchNumber,
                    BoxQty = 0,
                    QtyInBox = origItem.PcsPerBox > 0 ? origItem.PcsPerBox : 1,
                    TotalQtyPcs = rLine.QtyPcs,
                    BtpVat = 0,
                    SalePrice = origItem.UnitSalePrice,
                    VatPercent = origItem.VatPercent,
                    VatAmount = lineVat,
                    NetAmount = lineNet,
                    TotalPrice = lineTotal,
                    FreeQtyPcs = 0,
                    ReasonCode = reasonCode,
                    Disposition = disposition
                });
            }

            var totalReturnCredit = Math.Round(returnNet + returnVat, 2);

            // Insert sale_returns
            await conn.ExecuteAsync(@"
                INSERT INTO sale_returns (
                    sale_return_no, pharmacy_no, branch_no, sale_invoice_no, customer_no,
                    return_invoice_no, original_invoice_no, total_items,
                    gross_refund_price, discount_deduction, net_amount, vat_amount,
                    round_off_amount, final_refund_price, cash_paid_refund, inventory_type,
                    return_status, refund_method, exchange_invoice_no, idempotency_key,
                    created_by_user_no, return_date, created_at
                ) VALUES (
                    @SaleReturnNo, @CurrentPharmacyNo, @CurrentBranchNo, @SaleInvoiceNo, @CustomerNo,
                    @ReturnInvoiceNo, @OriginalInvoiceNo, @TotalItems,
                    @GrossRefundPrice, @DiscountDeduction, @NetAmount, @VatAmount,
                    0, @FinalRefundPrice, 0, 1,
                    2, 9, NULL, @IdempotencyKey,
                    @CurrentUserNo, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
                );",
                new
                {
                    SaleReturnNo = nextReturnNo,
                    CurrentPharmacyNo,
                    CurrentBranchNo,
                    origInvoice.SaleInvoiceNo,
                    CustomerNo = req.Return.CustomerNo ?? origInvoice.CustomerNo,
                    ReturnInvoiceNo = returnNumber,
                    OriginalInvoiceNo = origInvoice.SalesNumber,
                    TotalItems = processedReturnItems.Count,
                    GrossRefundPrice = returnGross,
                    DiscountDeduction = returnDiscountDeduction,
                    NetAmount = returnNet,
                    VatAmount = returnVat,
                    FinalRefundPrice = totalReturnCredit,
                    IdempotencyKey = req.IdempotencyKey ?? req.Return.IdempotencyKey,
                    CurrentUserNo
                },
                transaction: tran);

            // Insert sale_return_items
            const string insertRetItemSql = @"
                INSERT INTO sale_return_items (
                    sale_return_item_no, pharmacy_no, branch_no, sale_return_no,
                    sale_invoice_item_no, product_no, batch_number, box_qty,
                    qty_in_box, total_qty_pcs, btp_vat, sale_price,
                    vat_percent, vat_amount, net_amount, total_price,
                    free_qty_pcs, expiry_date, reason_code, disposition
                ) VALUES (
                    @SaleReturnItemNo, @PharmacyNo, @BranchNo, @SaleReturnNo,
                    @SaleInvoiceItemNo, @ProductNo, @BatchNumber, @BoxQty,
                    @QtyInBox, @TotalQtyPcs, @BtpVat, @SalePrice,
                    @VatPercent, @VatAmount, @NetAmount, @TotalPrice,
                    @FreeQtyPcs, @ExpiryDate, @ReasonCode, @Disposition
                );";

            foreach (var item in processedReturnItems)
            {
                await conn.ExecuteAsync(insertRetItemSql, item, transaction: tran);
            }

            // Return Journal Posting: Cr 10901 Return Credit Clearing ($totalReturnCredit)
            var returnJournal = new CompoundJournalEntry
            {
                PharmacyNo = CurrentPharmacyNo,
                BranchNo = CurrentBranchNo,
                TransactionType = 2, // Sale Return
                SourceDocumentType = "SALE_RETURN",
                SourceDocumentNo = returnNumber,
                SourceDocumentId = nextReturnNo,
                CreatedByUserNo = CurrentUserNo,
                Legs = new List<LedgerLeg>()
            };

            var retAllowanceAcc = await conn.ExecuteScalarAsync<long?>(@"
                SELECT account_no FROM acc_transaction_accounts
                WHERE account_code = '40102' AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
                new { CurrentPharmacyNo, CurrentBranchNo }, transaction: tran) ?? 1;

            if (returnNet > 0)
            {
                returnJournal.Legs.Add(new LedgerLeg
                {
                    AccountNo = retAllowanceAcc,
                    DebitAmount = returnNet,
                    CreditAmount = 0,
                    Narration = $"Sales return allowance for exchange {returnNumber}"
                });
            }

            if (returnVat > 0)
            {
                var vatAcc = await conn.ExecuteScalarAsync<long?>(@"
                    SELECT account_no FROM acc_transaction_accounts
                    WHERE account_code = '20201' AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
                    new { CurrentPharmacyNo, CurrentBranchNo }, transaction: tran) ?? 7;

                returnJournal.Legs.Add(new LedgerLeg
                {
                    AccountNo = vatAcc,
                    DebitAmount = returnVat,
                    CreditAmount = 0,
                    Narration = $"Output VAT reversal for exchange {returnNumber}"
                });
            }

            // Credit the clearing account with the full return value
            returnJournal.Legs.Add(new LedgerLeg
            {
                AccountNo = clearingAccountNo.Value,
                DebitAmount = 0,
                CreditAmount = totalReturnCredit,
                Narration = $"Return credit voucher issued to clearing account for {returnNumber}"
            });

            if (returnCogsReversal > 0)
            {
                var invAssetAcc = await conn.ExecuteScalarAsync<long?>(@"
                    SELECT account_no FROM acc_transaction_accounts
                    WHERE account_code = '10401' AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
                    new { CurrentPharmacyNo, CurrentBranchNo }, transaction: tran) ?? 4;

                var cogsAcc = await conn.ExecuteScalarAsync<long?>(@"
                    SELECT account_no FROM acc_transaction_accounts
                    WHERE account_code = '50101' AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
                    new { CurrentPharmacyNo, CurrentBranchNo }, transaction: tran) ?? 11;

                returnJournal.Legs.Add(new LedgerLeg
                {
                    AccountNo = invAssetAcc,
                    DebitAmount = returnCogsReversal,
                    CreditAmount = 0,
                    Narration = $"Inventory restocked for exchange {returnNumber}"
                });

                returnJournal.Legs.Add(new LedgerLeg
                {
                    AccountNo = cogsAcc,
                    DebitAmount = 0,
                    CreditAmount = returnCogsReversal,
                    Narration = $"COGS reversal for exchange {returnNumber}"
                });
            }

            await _ledgerPosting.PostJournalAsync(conn, tran, returnJournal);

            // 4. Build and Execute the New Sale Invoice via ISalePostingEngine
            var newSaleInvoice = new SaleInvoice
            {
                PharmacyNo = CurrentPharmacyNo,
                BranchNo = CurrentBranchNo,
                CustomerNo = req.Return.CustomerNo ?? origInvoice.CustomerNo,
                SaleMode = 1,
                PaymentMethod = 9, // Return Credit settlement
                ExchangeOfReturnNo = nextReturnNo
            };

            var newSaleItems = new List<SaleInvoiceItem>();
            foreach (var ni in req.NewItems)
            {
                newSaleItems.Add(new SaleInvoiceItem
                {
                    ProductNo = ni.ProductNo,
                    SaleQty = ni.QtyPcs,
                    SaleMode = ni.SaleMode > 0 ? ni.SaleMode : (short)1
                });
            }

            // Calculate estimated sale total to prepare payments
            decimal estimatedSaleGross = 0;
            foreach (var si in newSaleItems)
            {
                var price = await conn.ExecuteScalarAsync<decimal>(@"
                    SELECT sale_price_per_piece FROM prod_products
                    WHERE product_no = @ProductNo AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
                    new { si.ProductNo, CurrentPharmacyNo, CurrentBranchNo }, transaction: tran);
                estimatedSaleGross += price * si.SaleQty;
            }

            var returnCreditToApply = Math.Min(totalReturnCredit, estimatedSaleGross);
            var remainingPayable = Math.Max(0, estimatedSaleGross - returnCreditToApply);

            var salePayments = new List<SaleInvoicePayment>
            {
                new SaleInvoicePayment
                {
                    PaymentMethod = 9, // Return Credit
                    AccountNo = clearingAccountNo.Value,
                    Amount = returnCreditToApply
                }
            };

            if (remainingPayable > 0)
            {
                if (req.AdditionalPayments != null && req.AdditionalPayments.Count > 0)
                {
                    salePayments.AddRange(req.AdditionalPayments);
                }
                else
                {
                    salePayments.Add(new SaleInvoicePayment
                    {
                        PaymentMethod = req.PaymentMethod ?? 1,
                        Amount = remainingPayable
                    });
                }
            }

            var salePostRequest = new SalePostingRequest
            {
                PharmacyNo = CurrentPharmacyNo,
                BranchNo = CurrentBranchNo,
                UserNo = CurrentUserNo,
                Invoice = newSaleInvoice,
                Items = newSaleItems,
                Payments = salePayments,
                ExchangeOfReturnNo = nextReturnNo
            };

            var saleResult = await _salePostingEngine.ExecuteSaleAsync(conn, tran, salePostRequest);
            var postedSale = saleResult.Invoice;

            // Invariant Check & Settlement for Clearing Account:
            // totalReturnCredit was credited to 10901.
            // postedSale debited 10901 by returnCreditToApply.
            // If totalReturnCredit > postedSale.FinalPrice:
            // Customer is owed cash back or store credit for (totalReturnCredit - postedSale.FinalPrice).
            decimal excessRefundToCustomer = Math.Max(0, totalReturnCredit - postedSale.FinalPrice);
            decimal additionalCollectedFromCustomer = Math.Max(0, postedSale.FinalPrice - totalReturnCredit);

            if (excessRefundToCustomer > 0)
            {
                var settlementAccountCode = (req.SettlementPaymentMethod ?? req.PaymentMethod) switch
                {
                    2 => "10201",
                    3 => "10202",
                    4 => "10203",
                    5 => "10204",
                    6 => "10205",
                    _ => "10101"
                };

                var settlementAccNo = await conn.ExecuteScalarAsync<long?>(@"
                    SELECT account_no FROM acc_transaction_accounts
                    WHERE account_code = @settlementAccountCode AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
                    new { settlementAccountCode, CurrentPharmacyNo, CurrentBranchNo }, transaction: tran)
                    ?? await conn.ExecuteScalarAsync<long?>(@"
                    SELECT account_no FROM acc_transaction_accounts
                    WHERE account_code = '10101' AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
                    new { CurrentPharmacyNo, CurrentBranchNo }, transaction: tran) ?? 1;

                // Disburse excess cash back or store credit to clear 10901 to exactly zero
                var settlementJournal = new CompoundJournalEntry
                {
                    PharmacyNo = CurrentPharmacyNo,
                    BranchNo = CurrentBranchNo,
                    TransactionType = 2,
                    SourceDocumentType = "EXCHANGE_SETTLEMENT",
                    SourceDocumentNo = $"{returnNumber}-{postedSale.SalesNumber}",
                    SourceDocumentId = nextReturnNo,
                    CreatedByUserNo = CurrentUserNo,
                    Legs = new List<LedgerLeg>
                    {
                        // Dr 10901 Return Credit Clearing (relieves the excess credit)
                        new LedgerLeg
                        {
                            AccountNo = clearingAccountNo.Value,
                            DebitAmount = excessRefundToCustomer,
                            CreditAmount = 0,
                            Narration = $"Relief of remaining return credit on exchange {returnNumber}"
                        },
                        // Cr Settlement Account (Cash Drawer / Bank / MFS refund to customer)
                        new LedgerLeg
                        {
                            AccountNo = settlementAccNo,
                            DebitAmount = 0,
                            CreditAmount = excessRefundToCustomer,
                            Narration = $"Payment difference refund to customer on exchange {returnNumber}"
                        }
                    }
                };

                await _ledgerPosting.PostJournalAsync(conn, tran, settlementJournal);

                // Update cash_paid_refund on the return record
                await conn.ExecuteAsync(@"
                    UPDATE sale_returns
                    SET cash_paid_refund = @ExcessRefund
                    WHERE sale_return_no = @SaleReturnNo;",
                    new { ExcessRefund = excessRefundToCustomer, SaleReturnNo = nextReturnNo },
                    transaction: tran);
            }

            // 5. Cross-link return and sale vouchers
            await conn.ExecuteAsync(@"
                UPDATE sale_returns
                SET exchange_invoice_no = @SaleInvoiceNo
                WHERE sale_return_no = @SaleReturnNo;",
                new { postedSale.SaleInvoiceNo, SaleReturnNo = nextReturnNo },
                transaction: tran);

            await tran.CommitAsync();

            var finalReturn = new SaleReturn
            {
                SaleReturnNo = nextReturnNo,
                PharmacyNo = CurrentPharmacyNo,
                BranchNo = CurrentBranchNo,
                SaleInvoiceNo = origInvoice.SaleInvoiceNo,
                CustomerNo = req.Return.CustomerNo ?? origInvoice.CustomerNo,
                ReturnInvoiceNo = returnNumber,
                OriginalInvoiceNo = origInvoice.SalesNumber,
                TotalItems = processedReturnItems.Count,
                GrossRefundPrice = returnGross,
                DiscountDeduction = returnDiscountDeduction,
                NetAmount = returnNet,
                VatAmount = returnVat,
                FinalRefundPrice = totalReturnCredit,
                CashPaidRefund = excessRefundToCustomer,
                RefundMethod = 9,
                ExchangeInvoiceNo = postedSale.SaleInvoiceNo,
                ReturnStatus = 2,
                ReturnDate = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                Items = processedReturnItems
            };

            var exchangeResult = new ExchangeResult
            {
                Return = finalReturn,
                Sale = postedSale,
                ReturnCreditAmount = totalReturnCredit,
                AdditionalCollected = additionalCollectedFromCustomer,
                CashRefunded = excessRefundToCustomer,
                DueAdjusted = 0
            };

            return OkResponse(exchangeResult, $"Exchange processed successfully: Return {returnNumber} and Sale {postedSale.SalesNumber}.");
        }
        catch
        {
            await tran.RollbackAsync();
            throw;
        }
    }
}

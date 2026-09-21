using AidlyPms.Api.Common;
using AidlyPms.Api.Common.Database;
using AidlyPms.Api.Infrastructure;
using AidlyPms.Api.Modules.Products.Models;
using AidlyPms.Api.Modules.Sales.Models;
using Dapper;
using Microsoft.AspNetCore.Mvc;

namespace AidlyPms.Api.Modules.Sales.Controllers;

[Route("api/v1")]
public class SaleReturnController : BaseController
{
    private readonly IDbConnectionFactory _db;
    private readonly IDocumentNumberService _docNumService;
    private readonly IStockPostingService _stockPosting;
    private readonly ILedgerPostingService _ledgerPosting;

    public SaleReturnController(
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

    [HttpGet("customer/returns")]
    [HttpGet("pos/sales/returns")]
    public async Task<ActionResult<ApiResponse<PagedResult<SaleReturn>>>> GetReturns([FromQuery] QueryFilter filter, [FromQuery] string? search, [FromQuery] long? customerNo)
    {
        using var conn = _db.CreateConnection();
        var sql = @"
            SELECT r.*, c.name AS customer_name
            FROM sale_returns r
            LEFT JOIN cust_customers c ON r.customer_no = c.customer_no
            WHERE r.pharmacy_no = @CurrentPharmacyNo AND r.branch_no = @CurrentBranchNo";

        var countSql = "SELECT COUNT(1) FROM sale_returns r WHERE r.pharmacy_no = @CurrentPharmacyNo AND r.branch_no = @CurrentBranchNo";

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

    [HttpPost("pos/sales/return")]
    public async Task<ActionResult<ApiResponse<SaleReturn>>> ProcessReturn([FromBody] CreateSaleReturnPayload payload)
    {
        if (payload.Items == null || payload.Items.Count == 0)
            return FailResponse<SaleReturn>("At least one return item must be provided.");

        using var conn = _db.CreateConnection();
        conn.Open();
        using var tran = conn.BeginTransaction();

        try
        {
            var returnData = payload.ReturnData ?? new SaleReturn();
            var customerNo = payload.CustomerNo ?? returnData.CustomerNo;
            var saleInvoiceNo = payload.SaleInvoiceNo ?? returnData.SaleInvoiceNo;
            var originalInvoiceNo = payload.OriginalInvoiceNo ?? returnData.OriginalInvoiceNo;
            var accountNo = payload.AccountNo ?? 1; // Default Cash Drawer 10101

            var returnNumber = await _docNumService.GetNextDocumentNumberAsync(conn, tran, CurrentPharmacyNo, CurrentBranchNo, "SALE_RETURN");

            var nextReturnNo = await conn.ExecuteScalarAsync<long>(
                "SELECT COALESCE(MAX(sale_return_no), 0) + 1 FROM sale_returns;", transaction: tran);

            decimal grossRefund = 0;
            decimal totalVat = 0;
            decimal totalCogsReversal = 0;
            var processedItems = new List<SaleReturnItem>();

            foreach (var line in payload.Items)
            {
                var product = await conn.QuerySingleOrDefaultAsync<ProdProduct>(@"
                    SELECT product_no, product_name, cost_per_box, sale_price_per_box,
                           purchase_price_per_piece, sale_price_per_piece, quantity_per_box
                    FROM prod_products
                    WHERE product_no = @ProductNo AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
                    new { line.ProductNo, CurrentPharmacyNo, CurrentBranchNo },
                    transaction: tran);

                if (product == null)
                    throw new InvalidOperationException($"Product #{line.ProductNo} not found in this pharmacy branch.");

                var qtyPcs = line.TotalQtyPcs > 0 ? line.TotalQtyPcs : 1;
                var unitSalePrice = line.SalePrice > 0 ? line.SalePrice : product.SalePricePerPiece;
                var lineGross = Math.Round(qtyPcs * unitSalePrice, 4);

                var vatPercent = line.VatPercent >= 0 ? line.VatPercent : 0m;
                var lineVat = Math.Round(lineGross * (vatPercent / 100m), 4);
                var lineTotal = lineGross + lineVat;

                grossRefund += lineGross;
                totalVat += lineVat;

                var unitCost = product.PurchasePricePerPiece > 0
                    ? product.PurchasePricePerPiece
                    : (product.QuantityPerBox > 0 ? product.CostPerBox / product.QuantityPerBox : 0);

                var lineCogs = Math.Round(qtyPcs * unitCost, 4);

                // Check disposition: 1: Restock to Batch
                if (line.Disposition == 1)
                {
                    totalCogsReversal += lineCogs;

                    // If batch exists, increment it
                    if (!string.IsNullOrWhiteSpace(line.BatchNumber))
                    {
                        var batch = await conn.QuerySingleOrDefaultAsync<ProdProductBatch>(@"
                            SELECT * FROM prod_product_batches
                            WHERE product_no = @ProductNo AND batch_number = @BatchNumber
                              AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
                            new { line.ProductNo, line.BatchNumber, CurrentPharmacyNo, CurrentBranchNo },
                            transaction: tran);

                        if (batch != null)
                        {
                            await _stockPosting.PostMovementAsync(conn, tran, new StockMovementEntry
                            {
                                PharmacyNo = CurrentPharmacyNo,
                                BranchNo = CurrentBranchNo,
                                ProductNo = line.ProductNo,
                                BatchNo = batch.BatchNo,
                                MovementType = 3, // Customer Return
                                ReferenceDocType = "SALE_RETURN",
                                ReferenceDocNo = returnNumber,
                                ReferenceDocId = nextReturnNo,
                                QtyPcs = qtyPcs,
                                UnitCost = unitCost,
                                Remarks = "Customer return restocked to batch"
                            });
                        }
                    }
                }

                line.TotalQtyPcs = qtyPcs;
                line.SalePrice = unitSalePrice;
                line.VatPercent = vatPercent;
                line.VatAmount = lineVat;
                line.NetAmount = lineGross;
                line.TotalPrice = lineTotal;
                line.ProductName = product.ProductName;
                processedItems.Add(line);
            }

            var discountDeduction = returnData.DiscountDeduction;
            var netAmount = Math.Max(0, grossRefund - discountDeduction);
            var finalRefund = Math.Round(netAmount + totalVat, 4);
            var cashPaidRefund = payload.CashPaidRefund ?? returnData.CashPaidRefund;
            if (cashPaidRefund <= 0) cashPaidRefund = finalRefund;

            // Insert sale_returns
            await conn.ExecuteAsync(@"
                INSERT INTO sale_returns (
                    sale_return_no, pharmacy_no, branch_no, sale_invoice_no, customer_no,
                    return_invoice_no, original_invoice_no, total_items,
                    gross_refund_price, discount_deduction, net_amount, vat_amount,
                    round_off_amount, final_refund_price, cash_paid_refund, inventory_type,
                    return_status, created_by_user_no, return_date, created_at
                ) VALUES (
                    @SaleReturnNo, @PharmacyNo, @BranchNo, @SaleInvoiceNo, @CustomerNo,
                    @ReturnInvoiceNo, @OriginalInvoiceNo, @TotalItems,
                    @GrossRefundPrice, @DiscountDeduction, @NetAmount, @VatAmount,
                    @RoundOffAmount, @FinalRefundPrice, @CashPaidRefund, @InventoryType,
                    @ReturnStatus, @CreatedByUserNo, @ReturnDate, @CreatedAt
                );",
                new
                {
                    SaleReturnNo = nextReturnNo,
                    PharmacyNo = CurrentPharmacyNo,
                    BranchNo = CurrentBranchNo,
                    SaleInvoiceNo = saleInvoiceNo,
                    CustomerNo = customerNo,
                    ReturnInvoiceNo = returnNumber,
                    OriginalInvoiceNo = originalInvoiceNo,
                    TotalItems = processedItems.Count,
                    GrossRefundPrice = grossRefund,
                    DiscountDeduction = discountDeduction,
                    NetAmount = netAmount,
                    VatAmount = totalVat,
                    RoundOffAmount = 0m,
                    FinalRefundPrice = finalRefund,
                    CashPaidRefund = cashPaidRefund,
                    InventoryType = (short)1,
                    ReturnStatus = (short)2,
                    CreatedByUserNo = CurrentUserNo,
                    ReturnDate = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow
                },
                transaction: tran);

            // Insert sale_return_items
            foreach (var item in processedItems)
            {
                var nextItemNo = await conn.ExecuteScalarAsync<long>(
                    "SELECT COALESCE(MAX(sale_return_item_no), 0) + 1 FROM sale_return_items;", transaction: tran);

                await conn.ExecuteAsync(@"
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
                    );",
                    new
                    {
                        SaleReturnItemNo = nextItemNo,
                        PharmacyNo = CurrentPharmacyNo,
                        BranchNo = CurrentBranchNo,
                        SaleReturnNo = nextReturnNo,
                        item.SaleInvoiceItemNo,
                        item.ProductNo,
                        item.BatchNumber,
                        BoxQty = item.BoxQty,
                        QtyInBox = item.QtyInBox > 0 ? item.QtyInBox : 1,
                        TotalQtyPcs = item.TotalQtyPcs,
                        BtpVat = item.BtpVat,
                        item.SalePrice,
                        item.VatPercent,
                        item.VatAmount,
                        item.NetAmount,
                        item.TotalPrice,
                        FreeQtyPcs = item.FreeQtyPcs,
                        item.ExpiryDate,
                        item.ReasonCode,
                        item.Disposition
                    },
                    transaction: tran);
            }

            // Universal Financial Ledger Posting (Two-Tier Synchronous)
            var returnAccount = await conn.QuerySingleOrDefaultAsync<long?>(
                "SELECT account_no FROM acc_transaction_accounts WHERE account_code = '40102' AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
                new { CurrentPharmacyNo, CurrentBranchNo }, transaction: tran) ?? 1;

            var vatAccount = await conn.QuerySingleOrDefaultAsync<long?>(
                "SELECT account_no FROM acc_transaction_accounts WHERE account_code = '20201' AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
                new { CurrentPharmacyNo, CurrentBranchNo }, transaction: tran) ?? 1;

            var journalLegs = new List<LedgerLeg>();

            // Dr 40102 Sales Returns and Allowances
            if (netAmount > 0)
            {
                journalLegs.Add(new LedgerLeg
                {
                    AccountNo = returnAccount,
                    DebitAmount = netAmount,
                    CreditAmount = 0,
                    Narration = $"Sales return {returnNumber}"
                });
            }

            // Dr 20201 Output VAT Reversal
            if (totalVat > 0)
            {
                journalLegs.Add(new LedgerLeg
                {
                    AccountNo = vatAccount,
                    DebitAmount = totalVat,
                    CreditAmount = 0,
                    Narration = $"Output VAT refund on {returnNumber}"
                });
            }

            // Cr Cash / Bank Account for refund
            if (cashPaidRefund > 0)
            {
                journalLegs.Add(new LedgerLeg
                {
                    AccountNo = accountNo,
                    DebitAmount = 0,
                    CreditAmount = cashPaidRefund,
                    Narration = $"Cash refund disbursed for {returnNumber}"
                });
            }

            // COGS Reversal for restocked inventory: Dr 10401 Inventory Asset, Cr 50101 COGS
            if (totalCogsReversal > 0)
            {
                var invAssetAcc = await conn.QuerySingleOrDefaultAsync<long?>(
                    "SELECT account_no FROM acc_transaction_accounts WHERE account_code = '10401' AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
                    new { CurrentPharmacyNo, CurrentBranchNo }, transaction: tran) ?? 1;

                var cogsAcc = await conn.QuerySingleOrDefaultAsync<long?>(
                    "SELECT account_no FROM acc_transaction_accounts WHERE account_code = '50101' AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
                    new { CurrentPharmacyNo, CurrentBranchNo }, transaction: tran) ?? 1;

                journalLegs.Add(new LedgerLeg
                {
                    AccountNo = invAssetAcc,
                    DebitAmount = totalCogsReversal,
                    CreditAmount = 0,
                    Narration = $"Restocked inventory asset valuation {returnNumber}"
                });

                journalLegs.Add(new LedgerLeg
                {
                    AccountNo = cogsAcc,
                    DebitAmount = 0,
                    CreditAmount = totalCogsReversal,
                    Narration = $"COGS reversal for restocked goods {returnNumber}"
                });
            }

            // If Customer Due needs reduction because refund was credited to customer AR
            if (finalRefund > cashPaidRefund && customerNo.HasValue)
            {
                var diffDue = finalRefund - cashPaidRefund;
                var arAcc = await conn.QuerySingleOrDefaultAsync<long?>(
                    "SELECT account_no FROM acc_transaction_accounts WHERE account_code = '10301' AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
                    new { CurrentPharmacyNo, CurrentBranchNo }, transaction: tran) ?? 1;

                journalLegs.Add(new LedgerLeg
                {
                    AccountNo = arAcc,
                    DebitAmount = 0,
                    CreditAmount = diffDue,
                    Narration = $"Customer AR credit adjustment on return {returnNumber}"
                });

                // Deduct from customer due
                await conn.ExecuteAsync(@"
                    UPDATE cust_customers
                    SET current_due = GREATEST(0, current_due - @Diff),
                        has_due = CASE WHEN current_due - @Diff > 0 THEN TRUE ELSE FALSE END
                    WHERE customer_no = @CustomerNo;",
                    new { Diff = diffDue, CustomerNo = customerNo.Value },
                    transaction: tran);
            }

            // Post compound balanced journal entry
            await _ledgerPosting.PostJournalAsync(conn, tran, new CompoundJournalEntry
            {
                PharmacyNo = CurrentPharmacyNo,
                BranchNo = CurrentBranchNo,
                TransactionType = 2, // Sale Return
                SourceDocumentType = "SALE_RETURN",
                SourceDocumentNo = returnNumber,
                SourceDocumentId = nextReturnNo,
                CreatedByUserNo = CurrentUserNo,
                Legs = journalLegs
            });

            tran.Commit();

            var savedReturn = new SaleReturn
            {
                SaleReturnNo = nextReturnNo,
                PharmacyNo = CurrentPharmacyNo,
                BranchNo = CurrentBranchNo,
                SaleInvoiceNo = saleInvoiceNo,
                CustomerNo = customerNo,
                ReturnInvoiceNo = returnNumber,
                OriginalInvoiceNo = originalInvoiceNo,
                TotalItems = processedItems.Count,
                GrossRefundPrice = grossRefund,
                DiscountDeduction = discountDeduction,
                NetAmount = netAmount,
                VatAmount = totalVat,
                FinalRefundPrice = finalRefund,
                CashPaidRefund = cashPaidRefund,
                ReturnStatus = 2,
                ReturnDate = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                Items = processedItems
            };

            return OkResponse(savedReturn, $"Return {returnNumber} processed successfully.");
        }
        catch (Exception ex)
        {
            tran.Rollback();
            return FailResponse<SaleReturn>($"Return processing failed: {ex.Message}");
        }
    }
}

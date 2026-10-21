using AidlyPms.Api.Common;
using AidlyPms.Api.Common.Database;
using AidlyPms.Api.Infrastructure;
using AidlyPms.Api.Modules.Products.Models;
using AidlyPms.Api.Modules.Sales.Models;
using AidlyPms.Api.Modules.Sales.Services;
using AidlyPms.Api.Modules.Settings.Models;
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
    private readonly ISalePostingEngine _salePostingEngine;

    public PosSaleController(
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

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<SaleInvoice>>>> GetSales(
        [FromQuery] string? search = null,
        [FromQuery] long? customerNo = null,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 20)
    {
        (startDate, endDate) = DateHelper.NormalizeDateRange(startDate, endDate);
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

    [HttpGet("{idOrNumber}")]
    public async Task<ActionResult<ApiResponse<SaleInvoice>>> GetSale(string idOrNumber)
    {
        await using var conn = await _db.CreateOpenConnectionAsync();
        long.TryParse(idOrNumber, out var id);

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
            WHERE ((@id > 0 AND s.sale_invoice_no = @id) OR s.sales_number = @idOrNumber)
              AND s.pharmacy_no = @CurrentPharmacyNo AND s.branch_no = @CurrentBranchNo
            LIMIT 1;";

        var invoice = await conn.QuerySingleOrDefaultAsync<SaleInvoice>(sql, new
        {
            id,
            idOrNumber,
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
                p.product_name, b.batch_number,
                COALESCE((
                    SELECT SUM(sri.total_qty_pcs)
                    FROM sale_return_items sri
                    JOIN sale_returns sr ON sri.sale_return_no = sr.sale_return_no
                    WHERE sri.sale_invoice_item_no = i.sale_invoice_item_no
                      AND sr.return_status = 2
                ), 0) AS already_returned_qty
            FROM sale_invoice_items i
            JOIN prod_products p ON i.product_no = p.product_no
            LEFT JOIN prod_product_batches b ON i.batch_no = b.batch_no
            WHERE i.sale_invoice_no = @invoiceNo;";

        invoice.Items = (await conn.QueryAsync<SaleInvoiceItem>(itemsSql, new { invoiceNo = invoice.SaleInvoiceNo })).AsList();

        const string paymentsSql = @"
            SELECT 
                sale_payment_no, pharmacy_no, branch_no, sale_invoice_no,
                account_no, payment_method, amount, gateway_reference, created_at
            FROM sale_invoice_payments
            WHERE sale_invoice_no = @invoiceNo;";

        invoice.Payments = (await conn.QueryAsync<SaleInvoicePayment>(paymentsSql, new { invoiceNo = invoice.SaleInvoiceNo })).AsList();

        return OkResponse(invoice);
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<SaleInvoice>>> CreateSale([FromBody] CreateSalePayload payload)
    {
        if ((payload.Items == null || payload.Items.Count == 0) && payload.Invoice?.Items?.Count > 0)
        {
            payload.Items = payload.Invoice.Items;
        }

        if (payload.Items == null || payload.Items.Count == 0)
        {
            return FailResponse<SaleInvoice>("A sale invoice must contain at least one item.");
        }

        if (payload.Payments == null || payload.Payments.Count == 0)
        {
            if (payload.Invoice?.Payment != null)
            {
                payload.Payments = new List<SaleInvoicePayment> { payload.Invoice.Payment };
            }
            else if (payload.Invoice?.Payments != null && payload.Invoice.Payments.Count > 0)
            {
                payload.Payments = payload.Invoice.Payments;
            }
            else if (payload.Payment != null)
            {
                payload.Payments = new List<SaleInvoicePayment> { payload.Payment };
            }
        }

        await using var conn = await _db.CreateOpenConnectionAsync();
        await using var tran = await conn.BeginTransactionAsync();

        try
        {
            var postRequest = new SalePostingRequest
            {
                PharmacyNo = CurrentPharmacyNo,
                BranchNo = CurrentBranchNo,
                UserNo = CurrentUserNo,
                Invoice = payload.Invoice ?? new SaleInvoice(),
                Items = payload.Items,
                Payments = payload.Payments
            };

            var result = await _salePostingEngine.ExecuteSaleAsync(conn, tran, postRequest);
            await tran.CommitAsync();

            return OkResponse(result.Invoice, $"Sale {result.SalesNumber} posted successfully.");
        }
        catch (Exception ex)
        {
            await tran.RollbackAsync();
            return FailResponse<SaleInvoice>($"Failed to post sale: {ex.Message}");
        }
    }

    [HttpPost("{id:long}/void")]
    [HttpDelete("{id:long}")]
    public async Task<ActionResult<ApiResponse<bool>>> VoidSale(
        long id,
        [FromBody] VoidSaleRequest? request = null,
        [FromQuery] string? reason = null,
        [FromQuery] long? approvedByUserNo = null)
    {
        await using var conn = await _db.CreateOpenConnectionAsync();
        await using var tran = await conn.BeginTransactionAsync();

        try
        {
            var voidReason = !string.IsNullOrWhiteSpace(request?.Reason)
                ? request.Reason.Trim()
                : (!string.IsNullOrWhiteSpace(reason) ? reason.Trim() : null);

            if (string.IsNullOrWhiteSpace(voidReason))
            {
                return FailResponse<bool>("A mandatory void reason must be provided to void a sale invoice.", statusCode: 422);
            }

            var approverNo = request?.ApprovedByUserNo ?? approvedByUserNo ?? CurrentUserNo;
            var approvalNote = request?.ApprovalNote?.Trim();

            var invoice = await conn.QuerySingleOrDefaultAsync<SaleInvoice>(@"
                SELECT sale_invoice_no, sales_number, customer_no, final_price, net_amount,
                       vat_amount, round_off_amount, due_amount, payment_method,
                       document_status, cash_received_status, sale_timestamp, total_items, gross_total_price
                FROM sale_invoices
                WHERE sale_invoice_no = @id AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
                FOR UPDATE;",
                new { id, CurrentPharmacyNo, CurrentBranchNo },
                transaction: tran);

            if (invoice == null)
            {
                return FailResponse<bool>("Sale invoice not found.", statusCode: 404);
            }

            if (invoice.DocumentStatus == 3)
            {
                return FailResponse<bool>($"Sale invoice {invoice.SalesNumber} is already voided.");
            }

            if (invoice.DocumentStatus == 4 || invoice.DocumentStatus == 5)
            {
                return FailResponse<bool>($"Sale invoice {invoice.SalesNumber} has already been returned and cannot be voided.");
            }

            // Guard 1: Verify cash shift for this sale's date is not closed
            var closedShift = await conn.QueryFirstOrDefaultAsync<(DateTime ClosingDate, int ShiftNo)>(@"
                SELECT closing_date AS ClosingDate, shift_no AS ShiftNo
                FROM cash_closers
                WHERE pharmacy_no = @CurrentPharmacyNo
                  AND branch_no = @CurrentBranchNo
                  AND closing_date >= @saleDate::DATE
                ORDER BY closing_date DESC, shift_no DESC
                LIMIT 1;",
                new { CurrentPharmacyNo, CurrentBranchNo, saleDate = invoice.SaleTimestamp.Date },
                transaction: tran);

            if (closedShift.ClosingDate != default)
            {
                return FailResponse<bool>(
                    $"Cannot void sale: The cash register shift for sale date {invoice.SaleTimestamp:yyyy-MM-dd} (or a closed shift on {closedShift.ClosingDate:yyyy-MM-dd}, Shift #{closedShift.ShiftNo}) has already been closed and locked. Modifying sales in closed registers is prohibited.");
            }

            // Guard 2: Verify no returns exist on this invoice to avoid duplicate stock restoration
            var existingReturns = (await conn.QueryAsync<string>(@"
                SELECT return_invoice_no
                FROM sale_returns
                WHERE sale_invoice_no = @id AND return_status != 3;",
                new { id },
                transaction: tran)).AsList();

            if (existingReturns.Count > 0)
            {
                return FailResponse<bool>(
                    $"Cannot void invoice {invoice.SalesNumber}: Returns have already been recorded against this invoice ({string.Join(", ", existingReturns)}). Voiding would cause duplicate stock restoration. Please process a return or stock adjustment instead.");
            }

            // Guard 3: Verify no due collections have been recorded against this invoice
            var existingDueCollections = (await conn.QueryAsync<string>(@"
                SELECT payment_number
                FROM due_collections
                WHERE sale_invoice_no = @id;",
                new { id },
                transaction: tran)).AsList();

            if (existingDueCollections.Count > 0)
            {
                return FailResponse<bool>(
                    $"Cannot void invoice {invoice.SalesNumber}: Due collections ({string.Join(", ", existingDueCollections)}) have already been processed for this sale. Please reverse the due collection vouchers before voiding the invoice.");
            }

            var items = (await conn.QueryAsync<SaleInvoiceItem>(@"
                SELECT product_no, batch_no, sale_qty, unit_cost_price, net_amount
                FROM sale_invoice_items
                WHERE sale_invoice_no = @id;",
                new { id },
                transaction: tran)).AsList();

            decimal totalCogs = 0;
            foreach (var item in items)
            {
                var itemCost = item.UnitCostPrice > 0 ? item.UnitCostPrice : 0;
                totalCogs += (item.SaleQty * itemCost);
                await _stockPosting.PostMovementAsync(conn, tran, new StockMovementEntry
                {
                    PharmacyNo = CurrentPharmacyNo,
                    BranchNo = CurrentBranchNo,
                    ProductNo = item.ProductNo,
                    BatchNo = item.BatchNo,
                    MovementType = 3, // Restock / Void
                    ReferenceDocType = "SALE_VOID",
                    ReferenceDocNo = invoice.SalesNumber ?? $"INV-{id}",
                    ReferenceDocId = id,
                    QtyPcs = item.SaleQty,
                    UnitCost = itemCost,
                    Remarks = $"Restored stock on void of invoice {invoice.SalesNumber}. Reason: {voidReason}"
                });
            }

            // 6. Reverse Customer Due (if invoice had due amount)
            if (invoice.DueAmount > 0 && invoice.CustomerNo.HasValue && invoice.CustomerNo.Value > 0)
            {
                await conn.ExecuteAsync(@"
                    UPDATE cust_customers
                    SET current_due = GREATEST(0, current_due - @dueAmount),
                        has_due = CASE WHEN GREATEST(0, current_due - @dueAmount) > 0 THEN true ELSE false END,
                        updated_at = CURRENT_TIMESTAMP
                    WHERE customer_no = @CustomerNo AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
                    new { dueAmount = invoice.DueAmount, invoice.CustomerNo, CurrentPharmacyNo, CurrentBranchNo },
                    transaction: tran);
            }

            // 7. Reverse Customer Loyalty Points (if customer is a loyalty member and points were earned)
            if (invoice.CustomerNo.HasValue && invoice.CustomerNo.Value > 0)
            {
                var loyaltyCheck = await conn.QuerySingleOrDefaultAsync<(bool IsLoyaltyMember, bool IsEnabled, decimal SpendPerPoint)>(@"
                    SELECT c.is_loyalty_member AS IsLoyaltyMember,
                           COALESCE(cfg.is_enabled, false) AS IsEnabled,
                           COALESCE(cfg.earning_spend_amount_per_point, 100) AS SpendPerPoint
                    FROM cust_customers c
                    LEFT JOIN cfg_loyalty_configs cfg
                           ON cfg.pharmacy_no = @CurrentPharmacyNo AND cfg.branch_no = @CurrentBranchNo
                    WHERE c.customer_no = @CustomerNo AND c.pharmacy_no = @CurrentPharmacyNo AND c.branch_no = @CurrentBranchNo;",
                    new { invoice.CustomerNo, CurrentPharmacyNo, CurrentBranchNo },
                    transaction: tran);

                if (loyaltyCheck.IsLoyaltyMember && loyaltyCheck.IsEnabled && loyaltyCheck.SpendPerPoint > 0)
                {
                    var pointsToDeduct = Math.Floor(invoice.FinalPrice / loyaltyCheck.SpendPerPoint);
                    if (pointsToDeduct > 0)
                    {
                        await conn.ExecuteAsync(@"
                            UPDATE cust_customers
                            SET loyalty_points = GREATEST(0, loyalty_points - @pointsToDeduct),
                                updated_at = CURRENT_TIMESTAMP
                            WHERE customer_no = @CustomerNo AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
                            new { pointsToDeduct, invoice.CustomerNo, CurrentPharmacyNo, CurrentBranchNo },
                            transaction: tran);
                    }
                }
            }

            // 8. Reversing Double-Entry Accounting Ledger Journal
            var origLedgerEntries = (await conn.QueryAsync<(long LedgerNo, long AccountNo, decimal DebitAmount, decimal CreditAmount)>(@"
                SELECT ledger_no AS LedgerNo, account_no AS AccountNo, debit_amount AS DebitAmount, credit_amount AS CreditAmount
                FROM acc_transaction_ledger
                WHERE source_document_id = @id
                  AND source_document_type = 'SALE_INVOICE'
                  AND pharmacy_no = @CurrentPharmacyNo
                  AND branch_no = @CurrentBranchNo;",
                new { id, CurrentPharmacyNo, CurrentBranchNo },
                transaction: tran)).AsList();

            var reversingJournal = new CompoundJournalEntry
            {
                PharmacyNo = CurrentPharmacyNo,
                BranchNo = CurrentBranchNo,
                TransactionDate = DateTime.UtcNow,
                TransactionType = 1, // POS Sale Reversal
                SourceDocumentType = "SALE_VOID",
                SourceDocumentNo = invoice.SalesNumber ?? $"INV-{id}",
                SourceDocumentId = id,
                CreatedByUserNo = CurrentUserNo,
                Legs = new List<LedgerLeg>()
            };

            // 8a. Debit Sales Revenue (Account 40101 / fallback 9) to reverse revenue credit
            var salesRevAcc = await conn.ExecuteScalarAsync<long?>(@"
                SELECT account_no FROM acc_transaction_accounts
                WHERE account_code = '40101' AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
                new { CurrentPharmacyNo, CurrentBranchNo }, transaction: tran) ?? 9;

            var origRevLeg = origLedgerEntries.FirstOrDefault(ol => ol.AccountNo == salesRevAcc);
            reversingJournal.Legs.Add(new LedgerLeg
            {
                AccountNo = salesRevAcc,
                DebitAmount = invoice.NetAmount,
                CreditAmount = 0,
                ReversalOfLedgerNo = origRevLeg.LedgerNo > 0 ? origRevLeg.LedgerNo : null,
                Narration = $"Reversal of sales revenue on void of sale {invoice.SalesNumber}"
            });

            // 8b. Debit Output VAT Payable (Account 20201 / fallback 7) to reverse VAT liability credit
            if (invoice.VatAmount > 0)
            {
                var vatAcc = await conn.ExecuteScalarAsync<long?>(@"
                    SELECT account_no FROM acc_transaction_accounts
                    WHERE account_code = '20201' AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
                    new { CurrentPharmacyNo, CurrentBranchNo }, transaction: tran) ?? 7;

                var origVatLeg = origLedgerEntries.FirstOrDefault(ol => ol.AccountNo == vatAcc);
                reversingJournal.Legs.Add(new LedgerLeg
                {
                    AccountNo = vatAcc,
                    DebitAmount = invoice.VatAmount,
                    CreditAmount = 0,
                    ReversalOfLedgerNo = origVatLeg.LedgerNo > 0 ? origVatLeg.LedgerNo : null,
                    Narration = $"Reversal of output VAT on void of sale {invoice.SalesNumber}"
                });
            }

            // 8c. Round-off adjustment reversal
            if (invoice.RoundOffAmount > 0)
            {
                var roundGainAcc = await conn.ExecuteScalarAsync<long?>(@"
                    SELECT account_no FROM acc_transaction_accounts
                    WHERE account_code = '40202' AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
                    new { CurrentPharmacyNo, CurrentBranchNo }, transaction: tran) ?? 16;

                var origGainLeg = origLedgerEntries.FirstOrDefault(ol => ol.AccountNo == roundGainAcc);
                reversingJournal.Legs.Add(new LedgerLeg
                {
                    AccountNo = roundGainAcc,
                    DebitAmount = invoice.RoundOffAmount,
                    CreditAmount = 0,
                    ReversalOfLedgerNo = origGainLeg.LedgerNo > 0 ? origGainLeg.LedgerNo : null,
                    Narration = $"Reversal of rounding gain on void of sale {invoice.SalesNumber}"
                });
            }
            else if (invoice.RoundOffAmount < 0)
            {
                var roundLossAcc = await conn.ExecuteScalarAsync<long?>(@"
                    SELECT account_no FROM acc_transaction_accounts
                    WHERE account_code = '50203' AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
                    new { CurrentPharmacyNo, CurrentBranchNo }, transaction: tran) ?? 17;

                var origLossLeg = origLedgerEntries.FirstOrDefault(ol => ol.AccountNo == roundLossAcc);
                reversingJournal.Legs.Add(new LedgerLeg
                {
                    AccountNo = roundLossAcc,
                    DebitAmount = 0,
                    CreditAmount = Math.Abs(invoice.RoundOffAmount),
                    ReversalOfLedgerNo = origLossLeg.LedgerNo > 0 ? origLossLeg.LedgerNo : null,
                    Narration = $"Reversal of rounding loss on void of sale {invoice.SalesNumber}"
                });
            }

            // 8d. Credit Payment Tender Accounts
            var payments = (await conn.QueryAsync<SaleInvoicePayment>(@"
                SELECT sale_payment_no, account_no, payment_method, amount
                FROM sale_invoice_payments
                WHERE sale_invoice_no = @id;",
                new { id },
                transaction: tran)).AsList();

            if (payments.Count > 0)
            {
                foreach (var pay in payments)
                {
                    if (pay.Amount <= 0) continue;
                    var payAcc = pay.AccountNo > 0 ? pay.AccountNo : 1;
                    var origPayLeg = origLedgerEntries.FirstOrDefault(ol => ol.AccountNo == payAcc && ol.DebitAmount == pay.Amount);

                    reversingJournal.Legs.Add(new LedgerLeg
                    {
                        AccountNo = payAcc,
                        DebitAmount = 0,
                        CreditAmount = pay.Amount,
                        ReversalOfLedgerNo = origPayLeg.LedgerNo > 0 ? origPayLeg.LedgerNo : null,
                        Narration = $"Payment refund on void of sale {invoice.SalesNumber}"
                    });
                }
            }
            else
            {
                var cashPaid = Math.Max(0, invoice.FinalPrice - invoice.DueAmount);
                if (cashPaid > 0)
                {
                    var origCashLeg = origLedgerEntries.FirstOrDefault(ol => ol.AccountNo == 1);
                    reversingJournal.Legs.Add(new LedgerLeg
                    {
                        AccountNo = 1, // Main Cash Drawer
                        DebitAmount = 0,
                        CreditAmount = cashPaid,
                        ReversalOfLedgerNo = origCashLeg.LedgerNo > 0 ? origCashLeg.LedgerNo : null,
                        Narration = $"Cash refund on void of sale {invoice.SalesNumber}"
                    });
                }
            }

            // 8e. Credit AR for Customer Due (Account 10301 / fallback 3)
            if (invoice.DueAmount > 0)
            {
                var arAccountNo = await conn.ExecuteScalarAsync<long?>(@"
                    SELECT account_no FROM acc_transaction_accounts
                    WHERE account_code = '10301' AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
                    new { CurrentPharmacyNo, CurrentBranchNo }, transaction: tran) ?? 3;

                var origArLeg = origLedgerEntries.FirstOrDefault(ol => ol.AccountNo == arAccountNo);
                reversingJournal.Legs.Add(new LedgerLeg
                {
                    AccountNo = arAccountNo,
                    DebitAmount = 0,
                    CreditAmount = invoice.DueAmount,
                    PartyType = 1, // Customer
                    PartyNo = invoice.CustomerNo,
                    ReversalOfLedgerNo = origArLeg.LedgerNo > 0 ? origArLeg.LedgerNo : null,
                    Narration = $"Customer AR due reversal on void of sale {invoice.SalesNumber}"
                });
            }

            // 8f. FEFO COGS & Inventory Relief Reversal: Dr 10401 Inventory Asset, Cr 50101 COGS
            if (totalCogs > 0)
            {
                var invAssetAcc = await conn.ExecuteScalarAsync<long?>(@"
                    SELECT account_no FROM acc_transaction_accounts
                    WHERE account_code = '10401' AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
                    new { CurrentPharmacyNo, CurrentBranchNo }, transaction: tran) ?? 4;

                var cogsAcc = await conn.ExecuteScalarAsync<long?>(@"
                    SELECT account_no FROM acc_transaction_accounts
                    WHERE account_code = '50101' AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
                    new { CurrentPharmacyNo, CurrentBranchNo }, transaction: tran) ?? 11;

                var origInvLeg = origLedgerEntries.FirstOrDefault(ol => ol.AccountNo == invAssetAcc);
                var origCogsLeg = origLedgerEntries.FirstOrDefault(ol => ol.AccountNo == cogsAcc);

                reversingJournal.Legs.Add(new LedgerLeg
                {
                    AccountNo = invAssetAcc,
                    DebitAmount = totalCogs,
                    CreditAmount = 0,
                    ReversalOfLedgerNo = origInvLeg.LedgerNo > 0 ? origInvLeg.LedgerNo : null,
                    Narration = $"Inventory restoration on void of sale {invoice.SalesNumber}"
                });

                reversingJournal.Legs.Add(new LedgerLeg
                {
                    AccountNo = cogsAcc,
                    DebitAmount = 0,
                    CreditAmount = totalCogs,
                    ReversalOfLedgerNo = origCogsLeg.LedgerNo > 0 ? origCogsLeg.LedgerNo : null,
                    Narration = $"COGS reversal on void of sale {invoice.SalesNumber}"
                });
            }

            // Post balanced journal entry to general ledger
            await _ledgerPosting.PostJournalAsync(conn, tran, reversingJournal);

            // 9. Update Invoice Status to Voided (3)
            await conn.ExecuteAsync(@"
                UPDATE sale_invoices
                SET document_status = 3,
                    cash_received_status = 3,
                    updated_at = CURRENT_TIMESTAMP
                WHERE sale_invoice_no = @id;",
                new { id },
                transaction: tran);

            // 10. Audit Trail in sys_audit_logs
            long? validUserNo = CurrentUserNo > 0 ? CurrentUserNo : null;
            if (approverNo > 0 && approverNo != CurrentUserNo)
            {
                var userExists = await conn.ExecuteScalarAsync<bool>(
                    "SELECT EXISTS(SELECT 1 FROM sys_users WHERE user_no = @approverNo AND pharmacy_no = @CurrentPharmacyNo);",
                    new { approverNo, CurrentPharmacyNo }, transaction: tran);
                if (userExists) validUserNo = approverNo;
            }

            var oldDataJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                invoice.SaleInvoiceNo,
                invoice.SalesNumber,
                invoice.DocumentStatus,
                invoice.CashReceivedStatus,
                invoice.FinalPrice,
                invoice.NetAmount,
                invoice.VatAmount,
                invoice.DueAmount,
                invoice.TotalItems,
                invoice.SaleTimestamp
            });

            var newDataJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                document_status = 3,
                cash_received_status = 3,
                reason = voidReason,
                approval_note = approvalNote,
                approved_by_user_no = validUserNo,
                voided_by_user_no = CurrentUserNo,
                voided_at = DateTime.UtcNow
            });

            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

            const string auditSql = @"
                INSERT INTO sys_audit_logs (
                    audit_no, pharmacy_no, branch_no, user_no, table_name,
                    record_id, action, old_data, new_data, ip_address, created_at
                ) VALUES (
                    (SELECT COALESCE(MAX(audit_no), 0) + 1 FROM sys_audit_logs),
                    @CurrentPharmacyNo, @CurrentBranchNo, @validUserNo, 'sale_invoices',
                    @id, 'VOID', @oldDataJson, @newDataJson, @ipAddress, CURRENT_TIMESTAMP
                );";

            await conn.ExecuteAsync(auditSql, new
            {
                CurrentPharmacyNo,
                CurrentBranchNo,
                validUserNo,
                id,
                oldDataJson,
                newDataJson,
                ipAddress
            }, transaction: tran);

            await tran.CommitAsync();
            return OkResponse(true, $"Invoice {invoice.SalesNumber} voided, stock restored, customer dues/points reversed, and ledger reversed successfully.");
        }
        catch (Exception ex)
        {
            await tran.RollbackAsync();
            return FailResponse<bool>($"Failed to void invoice: {ex.Message}");
        }
    }
}


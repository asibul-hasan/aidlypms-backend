using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace AidlyPms.Api.Modules.Sales.Models;

public class SaleInvoice
{
    [JsonPropertyName("sale_invoice_no")]
    public long SaleInvoiceNo { get; set; }

    [JsonPropertyName("uuid")]
    public Guid Uuid { get; set; } = Guid.NewGuid();

    [JsonPropertyName("pharmacy_no")]
    public long PharmacyNo { get; set; }

    [JsonPropertyName("branch_no")]
    public long BranchNo { get; set; }

    [JsonPropertyName("customer_no")]
    public long? CustomerNo { get; set; }

    [JsonPropertyName("prescription_no")]
    public long? PrescriptionNo { get; set; }

    [JsonPropertyName("prescription_image_url")]
    public string? PrescriptionImageUrl { get; set; }

    [JsonPropertyName("sales_number")]
    public string SalesNumber { get; set; } = string.Empty;

    [JsonPropertyName("sale_mode")]
    public short SaleMode { get; set; } = 1;

    [JsonPropertyName("total_items")]
    public int TotalItems { get; set; }

    [JsonPropertyName("gross_total_price")]
    public decimal GrossTotalPrice { get; set; }

    [JsonPropertyName("discount_type")]
    public short DiscountType { get; set; } = 1;

    [JsonPropertyName("discount_value")]
    public decimal DiscountValue { get; set; }

    [JsonPropertyName("card_commission_percent")]
    public decimal CardCommissionPercent { get; set; }

    [JsonPropertyName("card_commission_amount")]
    public decimal CardCommissionAmount { get; set; }

    [JsonPropertyName("net_amount")]
    public decimal NetAmount { get; set; }

    [JsonPropertyName("vat_amount")]
    public decimal VatAmount { get; set; }

    [JsonPropertyName("round_off_amount")]
    public decimal RoundOffAmount { get; set; }

    [JsonPropertyName("final_price")]
    public decimal FinalPrice { get; set; }

    [JsonPropertyName("approximate_profit")]
    public decimal ApproximateProfit { get; set; }

    [JsonPropertyName("customer_will_pay")]
    public decimal CustomerWillPay { get; set; }

    [JsonPropertyName("change_amount")]
    public decimal ChangeAmount { get; set; }

    [JsonPropertyName("due_amount")]
    public decimal DueAmount { get; set; }

    [JsonPropertyName("payment_method")]
    public short PaymentMethod { get; set; } = 1;

    [JsonPropertyName("document_status")]
    public short DocumentStatus { get; set; } = 2; // 2: Confirmed

    [JsonPropertyName("cash_received_status")]
    public short CashReceivedStatus { get; set; } = 2; // 2: Received

    [JsonPropertyName("received_by_user_no")]
    public long? ReceivedByUserNo { get; set; }

    [JsonPropertyName("salesman_user_no")]
    public long? SalesmanUserNo { get; set; }

    [JsonPropertyName("is_quick_printed")]
    public bool IsQuickPrinted { get; set; }

    [JsonPropertyName("exchange_of_return_no")]
    public long? ExchangeOfReturnNo { get; set; }

    [JsonPropertyName("sale_timestamp")]
    public DateTime SaleTimestamp { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Joined fields
    [JsonPropertyName("customer_name")]
    public string? CustomerName { get; set; }

    [JsonPropertyName("customer_phone")]
    public string? CustomerPhone { get; set; }

    [JsonPropertyName("items")]
    public List<SaleInvoiceItem>? Items { get; set; }

    [JsonPropertyName("payments")]
    public List<SaleInvoicePayment>? Payments { get; set; }

    [JsonPropertyName("payment")]
    public SaleInvoicePayment? Payment { get; set; }

    [JsonPropertyName("payable_amount")]
    public decimal PayableAmount
    {
        get => FinalPrice;
        set { if (FinalPrice == 0) FinalPrice = value; }
    }

    [JsonPropertyName("paid_amount")]
    public decimal PaidAmount
    {
        get => CustomerWillPay;
        set { if (CustomerWillPay == 0) CustomerWillPay = value; }
    }
}

public class SaleInvoiceItem
{
    [JsonPropertyName("sale_invoice_item_no")]
    public long SaleInvoiceItemNo { get; set; }

    [JsonPropertyName("pharmacy_no")]
    public long PharmacyNo { get; set; }

    [JsonPropertyName("branch_no")]
    public long BranchNo { get; set; }

    [JsonPropertyName("sale_invoice_no")]
    public long SaleInvoiceNo { get; set; }

    [JsonPropertyName("product_no")]
    public long ProductNo { get; set; }

    [JsonPropertyName("batch_no")]
    public long? BatchNo { get; set; }

    [JsonPropertyName("sale_mode")]
    public short SaleMode { get; set; } = 1;

    [JsonPropertyName("sale_qty")]
    public int SaleQty { get; set; }

    [JsonPropertyName("box_qty")]
    public int BoxQty { get; set; }

    [JsonPropertyName("pcs_per_box")]
    public int PcsPerBox { get; set; } = 1;

    [JsonPropertyName("unit_cost_price")]
    public decimal UnitCostPrice { get; set; }

    [JsonPropertyName("unit_sale_price")]
    public decimal UnitSalePrice { get; set; }

    [JsonPropertyName("discount_amount")]
    public decimal DiscountAmount { get; set; }

    [JsonPropertyName("vat_percent")]
    public decimal VatPercent { get; set; }

    [JsonPropertyName("vat_amount")]
    public decimal VatAmount { get; set; }

    [JsonPropertyName("net_amount")]
    public decimal NetAmount { get; set; }

    [JsonPropertyName("total_price")]
    public decimal TotalPrice { get; set; }

    // Navigation and frontend aliases
    [JsonPropertyName("product_name")]
    public string? ProductName { get; set; }

    [JsonPropertyName("batch_number")]
    public string? BatchNumber { get; set; }

    [JsonPropertyName("total_quantity_pcs")]
    public int TotalQuantityPcs => SaleQty;

    [JsonPropertyName("quantity")]
    public int Quantity
    {
        get => SaleQty;
        set { if (SaleQty == 0) SaleQty = value; }
    }

    [JsonPropertyName("sale_price")]
    public decimal SalePrice => UnitSalePrice;

    [JsonPropertyName("unit_price")]
    public decimal UnitPrice
    {
        get => UnitSalePrice;
        set { if (UnitSalePrice == 0) UnitSalePrice = value; }
    }

    [JsonPropertyName("unit_cost")]
    public decimal UnitCost
    {
        get => UnitCostPrice;
        set { if (UnitCostPrice == 0) UnitCostPrice = value; }
    }

    [JsonPropertyName("box_pattern")]
    public int BoxPattern
    {
        get => PcsPerBox;
        set { if (PcsPerBox == 1) PcsPerBox = value; }
    }

    [JsonPropertyName("already_returned_qty")]
    public int AlreadyReturnedQty { get; set; }

    [JsonPropertyName("returnable_qty")]
    public int ReturnableQty => Math.Max(0, SaleQty - AlreadyReturnedQty);
}


public class SaleInvoicePayment
{
    [JsonPropertyName("sale_payment_no")]
    public long SalePaymentNo { get; set; }

    [JsonPropertyName("pharmacy_no")]
    public long PharmacyNo { get; set; }

    [JsonPropertyName("branch_no")]
    public long BranchNo { get; set; }

    [JsonPropertyName("sale_invoice_no")]
    public long SaleInvoiceNo { get; set; }

    [JsonPropertyName("account_no")]
    public long AccountNo { get; set; }

    [JsonPropertyName("payment_method")]
    public short PaymentMethod { get; set; } = 1;

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("paid_amount")]
    public decimal PaidAmount
    {
        get => Amount;
        set { if (Amount == 0) Amount = value; }
    }

    [JsonPropertyName("tendered_amount")]
    public decimal? TenderedAmount { get; set; }

    [JsonPropertyName("change_amount")]
    public decimal? ChangeAmount { get; set; }

    [JsonPropertyName("gateway_reference")]
    public string? GatewayReference { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class CreateSalePayload
{
    [JsonPropertyName("invoice")]
    public SaleInvoice Invoice { get; set; } = new();

    [JsonPropertyName("customer_no")]
    public long? CustomerNo { get; set; }

    [JsonPropertyName("sale_mode")]
    public short? SaleMode { get; set; }

    [JsonPropertyName("prescription_no")]
    public long? PrescriptionNo { get; set; }

    [JsonPropertyName("prescription_image_url")]
    public string? PrescriptionImageUrl { get; set; }

    [JsonPropertyName("discount_type")]
    public short? DiscountType { get; set; }

    [JsonPropertyName("discount_value")]
    public decimal? DiscountValue { get; set; }

    [JsonPropertyName("customer_will_pay")]
    public decimal? CustomerWillPay { get; set; }

    [JsonPropertyName("change_amount")]
    public decimal? ChangeAmount { get; set; }

    [JsonPropertyName("due_amount")]
    public decimal? DueAmount { get; set; }

    [JsonPropertyName("items")]
    public List<SaleInvoiceItem>? Items { get; set; }

    [JsonPropertyName("payments")]
    public List<SaleInvoicePayment>? Payments { get; set; }

    [JsonPropertyName("payment")]
    public SaleInvoicePayment? Payment { get; set; }
}

public class SaleReturn
{
    [JsonPropertyName("sale_return_no")]
    public long SaleReturnNo { get; set; }

    [JsonPropertyName("pharmacy_no")]
    public long PharmacyNo { get; set; }

    [JsonPropertyName("branch_no")]
    public long BranchNo { get; set; }

    [JsonPropertyName("sale_invoice_no")]
    public long? SaleInvoiceNo { get; set; }

    [JsonPropertyName("customer_no")]
    public long? CustomerNo { get; set; }

    [JsonPropertyName("return_invoice_no")]
    public string ReturnInvoiceNo { get; set; } = string.Empty;

    [JsonPropertyName("original_invoice_no")]
    public string? OriginalInvoiceNo { get; set; }

    [JsonPropertyName("total_items")]
    public int TotalItems { get; set; }

    [JsonPropertyName("gross_refund_price")]
    public decimal GrossRefundPrice { get; set; }

    [JsonPropertyName("discount_deduction")]
    public decimal DiscountDeduction { get; set; }

    [JsonPropertyName("net_amount")]
    public decimal NetAmount { get; set; }

    [JsonPropertyName("vat_amount")]
    public decimal VatAmount { get; set; }

    [JsonPropertyName("round_off_amount")]
    public decimal RoundOffAmount { get; set; }

    [JsonPropertyName("final_refund_price")]
    public decimal FinalRefundPrice { get; set; }

    [JsonPropertyName("cash_paid_refund")]
    public decimal CashPaidRefund { get; set; }

    [JsonPropertyName("inventory_type")]
    public short InventoryType { get; set; } = 1;

    [JsonPropertyName("return_status")]
    public short ReturnStatus { get; set; } = 2; // 2: Completed

    [JsonPropertyName("refund_method")]
    public short? RefundMethod { get; set; }

    [JsonPropertyName("exchange_invoice_no")]
    public long? ExchangeInvoiceNo { get; set; }

    [JsonPropertyName("idempotency_key")]
    public Guid? IdempotencyKey { get; set; }

    [JsonPropertyName("created_by_user_no")]
    public long? CreatedByUserNo { get; set; }

    [JsonPropertyName("return_date")]
    public DateTime ReturnDate { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Joined
    [JsonPropertyName("customer_name")]
    public string? CustomerName { get; set; }

    [JsonPropertyName("items")]
    public List<SaleReturnItem>? Items { get; set; }

    [JsonPropertyName("return_no")]
    public long ReturnNo => SaleReturnNo;

    [JsonPropertyName("return_amount")]
    public decimal ReturnAmount => CashPaidRefund > 0 ? CashPaidRefund : FinalRefundPrice;
}

public class SaleReturnItem
{
    [JsonPropertyName("sale_return_item_no")]
    public long SaleReturnItemNo { get; set; }

    [JsonPropertyName("pharmacy_no")]
    public long PharmacyNo { get; set; }

    [JsonPropertyName("branch_no")]
    public long BranchNo { get; set; }

    [JsonPropertyName("sale_return_no")]
    public long SaleReturnNo { get; set; }

    [JsonPropertyName("sale_invoice_item_no")]
    public long? SaleInvoiceItemNo { get; set; }

    [JsonPropertyName("product_no")]
    public long ProductNo { get; set; }

    [JsonPropertyName("batch_number")]
    public string? BatchNumber { get; set; }

    [JsonPropertyName("box_qty")]
    public int BoxQty { get; set; }

    [JsonPropertyName("qty_in_box")]
    public int QtyInBox { get; set; } = 1;

    [JsonPropertyName("total_qty_pcs")]
    public int TotalQtyPcs { get; set; }

    [JsonPropertyName("return_qty")]
    public int ReturnQty
    {
        get => TotalQtyPcs;
        set { if (TotalQtyPcs == 0) TotalQtyPcs = value; }
    }

    [JsonPropertyName("btp_vat")]
    public decimal BtpVat { get; set; }

    [JsonPropertyName("sale_price")]
    public decimal SalePrice { get; set; }

    [JsonPropertyName("unit_price")]
    public decimal UnitPrice
    {
        get => SalePrice;
        set { if (SalePrice == 0) SalePrice = value; }
    }

    [JsonPropertyName("vat_percent")]
    public decimal VatPercent { get; set; }

    [JsonPropertyName("vat_amount")]
    public decimal VatAmount { get; set; }

    [JsonPropertyName("net_amount")]
    public decimal NetAmount { get; set; }

    [JsonPropertyName("total_price")]
    public decimal TotalPrice { get; set; }

    [JsonPropertyName("free_qty_pcs")]
    public int FreeQtyPcs { get; set; }

    [JsonPropertyName("expiry_date")]
    public DateTime? ExpiryDate { get; set; }

    [JsonPropertyName("reason_code")]
    public short ReasonCode { get; set; } = 1;

    [JsonPropertyName("disposition")]
    public short Disposition { get; set; } = 1; // 1: Restock, 2: Quarantine, 3: Destroy/Waste

    [JsonPropertyName("product_name")]
    public string? ProductName { get; set; }
}

public class ReturnLineReq
{
    [JsonPropertyName("sale_invoice_item_no")]
    public long SaleInvoiceItemNo { get; set; }

    [JsonPropertyName("qty_pcs")]
    public int QtyPcs { get; set; }

    [JsonPropertyName("return_qty")]
    public int ReturnQty
    {
        get => QtyPcs;
        set { if (QtyPcs == 0) QtyPcs = value; }
    }

    [JsonPropertyName("reason_code")]
    [Required(ErrorMessage = "reason_code is required.")]
    public short? ReasonCode { get; set; } // 1: Defective, 2: Wrong Item, 3: Expired/Damaged, 4: Customer Choice

    [JsonPropertyName("disposition")]
    [Required(ErrorMessage = "disposition is required.")]
    public short? Disposition { get; set; } // 1: Restock, 2: Quarantine/Damaged, 3: Waste/Destroy
}

public class ReturnReq
{
    [JsonPropertyName("sale_invoice_no")]
    public long? SaleInvoiceNo { get; set; }

    [JsonPropertyName("original_invoice_no")]
    public string? OriginalInvoiceNo { get; set; }

    [JsonPropertyName("customer_no")]
    public long? CustomerNo { get; set; }

    [JsonPropertyName("refund_method")]
    public short? RefundMethod { get; set; } = 1;

    [JsonPropertyName("idempotency_key")]
    public Guid? IdempotencyKey { get; set; }

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    [JsonPropertyName("note")]
    public string? Note { get; set; }

    [JsonPropertyName("lines")]
    public List<ReturnLineReq> Lines { get; set; } = new();

    [JsonPropertyName("items")]
    public List<ReturnLineReq> Items
    {
        get => Lines;
        set { if (Lines.Count == 0 && value != null) Lines = value; }
    }
}

// Backward-compatible payload without client price fields
public class CreateSaleReturnPayload : ReturnReq
{
}

public class ExchangeCartLineReq
{
    [JsonPropertyName("product_no")]
    public long ProductNo { get; set; }

    [JsonPropertyName("qty_pcs")]
    public int QtyPcs { get; set; } = 1;

    [JsonPropertyName("quantity")]
    public int? Quantity
    {
        get => QtyPcs;
        set { if (value.HasValue) QtyPcs = value.Value; }
    }

    [JsonPropertyName("sale_qty")]
    public int? SaleQty
    {
        get => QtyPcs;
        set { if (value.HasValue) QtyPcs = value.Value; }
    }

    [JsonPropertyName("sale_mode")]
    public short SaleMode { get; set; } = 1;

    [JsonPropertyName("unit_type")]
    public short? UnitType
    {
        get => SaleMode;
        set { if (value.HasValue) SaleMode = value.Value; }
    }
}

public class ExchangeReq
{
    private ReturnReq _return = new();

    [JsonPropertyName("return")]
    public ReturnReq Return
    {
        get => _return;
        set => _return = value ?? new();
    }

    [JsonPropertyName("return_req")]
    public ReturnReq? ReturnReqAlias
    {
        get => _return;
        set { if (value != null) _return = value; }
    }

    [JsonPropertyName("new_items")]
    public List<ExchangeCartLineReq> NewItems { get; set; } = new();

    [JsonPropertyName("payment_method")]
    public short? PaymentMethod { get; set; } = 1;

    [JsonPropertyName("settlement_payment_method")]
    public short? SettlementPaymentMethod { get; set; } = 1;

    [JsonPropertyName("additional_payments")]
    public List<SaleInvoicePayment>? AdditionalPayments { get; set; }

    [JsonPropertyName("tender_payments")]
    public List<SaleInvoicePayment>? TenderPayments
    {
        get => AdditionalPayments;
        set { if (AdditionalPayments == null || AdditionalPayments.Count == 0) AdditionalPayments = value; }
    }

    [JsonPropertyName("idempotency_key")]
    public Guid? IdempotencyKey { get; set; }

    [JsonPropertyName("note")]
    public string? Note { get; set; }
}

public class ExchangeResult
{
    [JsonPropertyName("return")]
    public SaleReturn Return { get; set; } = null!;

    [JsonPropertyName("sale")]
    public SaleInvoice Sale { get; set; } = null!;

    [JsonPropertyName("return_credit_amount")]
    public decimal ReturnCreditAmount { get; set; }

    [JsonPropertyName("additional_collected")]
    public decimal AdditionalCollected { get; set; }

    [JsonPropertyName("cash_refunded")]
    public decimal CashRefunded { get; set; }

    [JsonPropertyName("due_adjusted")]
    public decimal DueAdjusted { get; set; }

    [JsonPropertyName("net_difference")]
    public decimal NetDifference => AdditionalCollected - CashRefunded;

    [JsonPropertyName("return_no")]
    public long ReturnNo => Return?.SaleReturnNo ?? 0;

    [JsonPropertyName("return_number")]
    public string ReturnNumber => Return?.ReturnInvoiceNo ?? string.Empty;

    [JsonPropertyName("refund_total")]
    public decimal RefundTotal => ReturnCreditAmount;

    [JsonPropertyName("new_invoice_no")]
    public long? NewInvoiceNo => Sale?.SaleInvoiceNo;

    [JsonPropertyName("new_sales_number")]
    public string? NewSalesNumber => Sale?.SalesNumber;

    [JsonPropertyName("new_invoice_total")]
    public decimal NewInvoiceTotal => Sale?.PayableAmount ?? 0;

    [JsonPropertyName("settlement_difference")]
    public decimal SettlementDifference => NetDifference;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}

public class VoidSaleRequest
{
    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;

    [JsonPropertyName("approval_note")]
    public string? ApprovalNote { get; set; }

    [JsonPropertyName("approved_by_user_no")]
    public long? ApprovedByUserNo { get; set; }
}

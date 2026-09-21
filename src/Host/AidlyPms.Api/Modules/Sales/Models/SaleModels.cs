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

    [JsonPropertyName("sale_price")]
    public decimal SalePrice => UnitSalePrice;
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

    [JsonPropertyName("gateway_reference")]
    public string? GatewayReference { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class CreateSalePayload
{
    [JsonPropertyName("invoice")]
    public SaleInvoice Invoice { get; set; } = new();

    [JsonPropertyName("items")]
    public List<SaleInvoiceItem> Items { get; set; } = new();

    [JsonPropertyName("payments")]
    public List<SaleInvoicePayment>? Payments { get; set; }
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

    [JsonPropertyName("btp_vat")]
    public decimal BtpVat { get; set; }

    [JsonPropertyName("sale_price")]
    public decimal SalePrice { get; set; }

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

public class CreateSaleReturnPayload
{
    [JsonPropertyName("return_data")]
    public SaleReturn? ReturnData { get; set; }

    [JsonPropertyName("items")]
    public List<SaleReturnItem> Items { get; set; } = new();

    // Convenience properties if sent directly
    [JsonPropertyName("sale_invoice_no")]
    public long? SaleInvoiceNo { get; set; }

    [JsonPropertyName("original_invoice_no")]
    public string? OriginalInvoiceNo { get; set; }

    [JsonPropertyName("customer_no")]
    public long? CustomerNo { get; set; }

    [JsonPropertyName("account_no")]
    public long? AccountNo { get; set; } = 1; // Default Cash Drawer 10101

    [JsonPropertyName("cash_paid_refund")]
    public decimal? CashPaidRefund { get; set; }
}


using System.Text.Json.Serialization;

namespace AidlyPms.Api.Modules.Suppliers.Models;

public class SuppSupplier
{
    [JsonPropertyName("supplier_no")]
    public long SupplierNo { get; set; }

    [JsonPropertyName("pharmacy_no")]
    public long PharmacyNo { get; set; }

    [JsonPropertyName("branch_no")]
    public long BranchNo { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("phone")]
    public string Phone { get; set; } = string.Empty;

    [JsonPropertyName("note")]
    public string? Note { get; set; }

    [JsonPropertyName("current_due_balance")]
    public decimal CurrentDueBalance { get; set; }

    [JsonPropertyName("is_active")]
    public bool IsActive { get; set; } = true;

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class PurPurchaseInvoice
{
    [JsonPropertyName("purchase_invoice_no")]
    public long PurchaseInvoiceNo { get; set; }

    [JsonPropertyName("uuid")]
    public Guid Uuid { get; set; } = Guid.NewGuid();

    [JsonPropertyName("pharmacy_no")]
    public long PharmacyNo { get; set; }

    [JsonPropertyName("branch_no")]
    public long BranchNo { get; set; }

    [JsonPropertyName("supplier_no")]
    public long SupplierNo { get; set; }

    [JsonPropertyName("inventory_number")]
    public string InventoryNumber { get; set; } = string.Empty;

    [JsonPropertyName("calculation_mode")]
    public short CalculationMode { get; set; } = 1;

    [JsonPropertyName("inventory_type")]
    public short InventoryType { get; set; } = 1;

    [JsonPropertyName("supplier_invoice_no")]
    public string? SupplierInvoiceNo { get; set; }

    [JsonPropertyName("total_items")]
    public int TotalItems { get; set; }

    [JsonPropertyName("gross_total_price")]
    public decimal GrossTotalPrice { get; set; }

    [JsonPropertyName("discount_type")]
    public short DiscountType { get; set; } = 1;

    [JsonPropertyName("discount_value")]
    public decimal DiscountValue { get; set; }

    [JsonPropertyName("net_amount")]
    public decimal NetAmount { get; set; }

    [JsonPropertyName("vat_amount")]
    public decimal VatAmount { get; set; }

    [JsonPropertyName("round_off_amount")]
    public decimal RoundOffAmount { get; set; }

    [JsonPropertyName("final_price")]
    public decimal FinalPrice { get; set; }

    [JsonPropertyName("paid_amount")]
    public decimal PaidAmount { get; set; }

    [JsonPropertyName("due_amount")]
    public decimal DueAmount { get; set; }

    [JsonPropertyName("document_status")]
    public short DocumentStatus { get; set; } = 2; // 2: Confirmed

    [JsonPropertyName("created_by_user_no")]
    public long? CreatedByUserNo { get; set; }

    [JsonPropertyName("purchase_date")]
    public DateTime PurchaseDate { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Joined
    [JsonPropertyName("supplier_name")]
    public string? SupplierName { get; set; }

    [JsonPropertyName("supplier_phone")]
    public string? SupplierPhone { get; set; }

    [JsonPropertyName("items")]
    public List<PurPurchaseInvoiceItem>? Items { get; set; }
}

public class PurPurchaseInvoiceItem
{
    [JsonPropertyName("purchase_item_no")]
    public long PurchaseItemNo { get; set; }

    [JsonPropertyName("pharmacy_no")]
    public long PharmacyNo { get; set; }

    [JsonPropertyName("branch_no")]
    public long BranchNo { get; set; }

    [JsonPropertyName("purchase_invoice_no")]
    public long PurchaseInvoiceNo { get; set; }

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

    [JsonPropertyName("free_qty_pcs")]
    public int FreeQtyPcs { get; set; }

    [JsonPropertyName("btp")]
    public decimal Btp { get; set; }

    [JsonPropertyName("vat_percent")]
    public decimal VatPercent { get; set; }

    [JsonPropertyName("vat_amount")]
    public decimal VatAmount { get; set; }

    [JsonPropertyName("btp_vat")]
    public decimal BtpVat { get; set; }

    [JsonPropertyName("unit_cost")]
    public decimal UnitCost { get; set; }

    [JsonPropertyName("sale_price")]
    public decimal SalePrice { get; set; }

    [JsonPropertyName("total_price")]
    public decimal TotalPrice { get; set; }

    [JsonPropertyName("expiry_date")]
    public DateTime? ExpiryDate { get; set; }

    [JsonPropertyName("min_qty_pcs")]
    public int? MinQtyPcs { get; set; }

    [JsonPropertyName("barcode_value")]
    public string? BarcodeValue { get; set; }

    // Joined
    [JsonPropertyName("product_name")]
    public string? ProductName { get; set; }
}

public class PurSupplierPayment
{
    [JsonPropertyName("supplier_payment_no")]
    public long SupplierPaymentNo { get; set; }

    [JsonPropertyName("pharmacy_no")]
    public long PharmacyNo { get; set; }

    [JsonPropertyName("branch_no")]
    public long BranchNo { get; set; }

    [JsonPropertyName("supplier_no")]
    public long SupplierNo { get; set; }

    [JsonPropertyName("account_no")]
    public long AccountNo { get; set; }

    [JsonPropertyName("payment_number")]
    public string PaymentNumber { get; set; } = string.Empty;

    [JsonPropertyName("payment_method")]
    public short PaymentMethod { get; set; } = 1;

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("reference_no")]
    public string? ReferenceNo { get; set; }

    [JsonPropertyName("note")]
    public string? Note { get; set; }

    [JsonPropertyName("paid_by_user_no")]
    public long? PaidByUserNo { get; set; }

    [JsonPropertyName("payment_date")]
    public DateTime PaymentDate { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Joined
    [JsonPropertyName("supplier_name")]
    public string? SupplierName { get; set; }

    [JsonPropertyName("account_name")]
    public string? AccountName { get; set; }
}

public class PurPurchaseReturn
{
    [JsonPropertyName("purchase_return_no")]
    public long PurchaseReturnNo { get; set; }

    [JsonPropertyName("pharmacy_no")]
    public long PharmacyNo { get; set; }

    [JsonPropertyName("branch_no")]
    public long BranchNo { get; set; }

    [JsonPropertyName("supplier_no")]
    public long SupplierNo { get; set; }

    [JsonPropertyName("purchase_invoice_no")]
    public long? PurchaseInvoiceNo { get; set; }

    [JsonPropertyName("return_number")]
    public string ReturnNumber { get; set; } = string.Empty;

    [JsonPropertyName("total_items")]
    public int TotalItems { get; set; }

    [JsonPropertyName("gross_return_amount")]
    public decimal GrossReturnAmount { get; set; }

    [JsonPropertyName("refund_received_amount")]
    public decimal RefundReceivedAmount { get; set; }

    [JsonPropertyName("due_adjustment_amount")]
    public decimal DueAdjustmentAmount { get; set; }

    [JsonPropertyName("return_status")]
    public short ReturnStatus { get; set; } = 2; // 2: Completed

    [JsonPropertyName("note")]
    public string? Note { get; set; }

    [JsonPropertyName("created_by_user_no")]
    public long? CreatedByUserNo { get; set; }

    [JsonPropertyName("return_date")]
    public DateTime ReturnDate { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("supplier_name")]
    public string? SupplierName { get; set; }

    [JsonPropertyName("items")]
    public List<PurPurchaseReturnItem>? Items { get; set; }
}

public class PurPurchaseReturnItem
{
    [JsonPropertyName("purchase_return_item_no")]
    public long PurchaseReturnItemNo { get; set; }

    [JsonPropertyName("pharmacy_no")]
    public long PharmacyNo { get; set; }

    [JsonPropertyName("branch_no")]
    public long BranchNo { get; set; }

    [JsonPropertyName("purchase_return_no")]
    public long PurchaseReturnNo { get; set; }

    [JsonPropertyName("purchase_item_no")]
    public long? PurchaseItemNo { get; set; }

    [JsonPropertyName("product_no")]
    public long ProductNo { get; set; }

    [JsonPropertyName("batch_no")]
    public long? BatchNo { get; set; }

    [JsonPropertyName("batch_number")]
    public string? BatchNumber { get; set; }

    [JsonPropertyName("return_qty_pcs")]
    public int ReturnQtyPcs { get; set; }

    [JsonPropertyName("unit_purchase_price")]
    public decimal UnitPurchasePrice { get; set; }

    [JsonPropertyName("total_return_amount")]
    public decimal TotalReturnAmount { get; set; }

    [JsonPropertyName("reason_code")]
    public short ReasonCode { get; set; } = 1;

    [JsonPropertyName("product_name")]
    public string? ProductName { get; set; }
}

public class CreatePurchasePayload
{
    [JsonPropertyName("invoice")]
    public PurPurchaseInvoice? Invoice { get; set; }

    [JsonPropertyName("items")]
    public List<PurPurchaseInvoiceItem> Items { get; set; } = new();

    // Top-level conveniences if sent flat
    [JsonPropertyName("supplier_no")]
    public long? SupplierNo { get; set; }

    [JsonPropertyName("supplier_invoice_no")]
    public string? SupplierInvoiceNo { get; set; }

    [JsonPropertyName("calculation_mode")]
    public short? CalculationMode { get; set; }

    [JsonPropertyName("inventory_type")]
    public short? InventoryType { get; set; }

    [JsonPropertyName("gross_total_price")]
    public decimal? GrossTotalPrice { get; set; }

    [JsonPropertyName("discount_type")]
    public short? DiscountType { get; set; }

    [JsonPropertyName("discount_value")]
    public decimal? DiscountValue { get; set; }

    [JsonPropertyName("paid_amount")]
    public decimal? PaidAmount { get; set; }

    [JsonPropertyName("payment_account_no")]
    public long? PaymentAccountNo { get; set; } = 1; // Default Cash Drawer 10101
}

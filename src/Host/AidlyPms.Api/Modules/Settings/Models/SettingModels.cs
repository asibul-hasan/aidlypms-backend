using System.Text.Json.Serialization;

namespace AidlyPms.Api.Modules.Settings.Models;

public class CfgSaleConfig
{
    [JsonPropertyName("sale_config_no")]
    public long SaleConfigNo { get; set; }

    [JsonPropertyName("pharmacy_no")]
    public long PharmacyNo { get; set; }

    [JsonPropertyName("branch_no")]
    public long BranchNo { get; set; }

    [JsonPropertyName("cash_received")]
    public bool CashReceived { get; set; } = true;

    [JsonPropertyName("out_of_stock_sale")]
    public bool OutOfStockSale { get; set; }

    [JsonPropertyName("allow_loss_sale")]
    public bool AllowLossSale { get; set; }

    [JsonPropertyName("product_purchase_cost")]
    public bool ProductPurchaseCost { get; set; } = true;

    [JsonPropertyName("stock_qty")]
    public bool StockQty { get; set; } = true;

    [JsonPropertyName("company_name")]
    public bool CompanyName { get; set; } = true;

    [JsonPropertyName("profit_margin")]
    public bool ProfitMargin { get; set; } = true;

    [JsonPropertyName("rak_number")]
    public bool RakNumber { get; set; } = true;

    [JsonPropertyName("opening_stock_percent")]
    public decimal OpeningStockPercent { get; set; } = 15m;

    [JsonPropertyName("max_sale_discount_percent")]
    public decimal MaxSaleDiscountPercent { get; set; } = 20m;

    [JsonPropertyName("is_max_discount_enabled")]
    public bool IsMaxDiscountEnabled { get; set; } = true;

    [JsonPropertyName("business_type")]
    public short BusinessType { get; set; } = 1;

    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class CfgInvoiceConfig
{
    [JsonPropertyName("invoice_config_no")]
    public long InvoiceConfigNo { get; set; }

    [JsonPropertyName("pharmacy_no")]
    public long PharmacyNo { get; set; }

    [JsonPropertyName("branch_no")]
    public long BranchNo { get; set; }

    [JsonPropertyName("paper_type")]
    public short PaperType { get; set; } = 1; // 1: 80mm, 2: 58mm

    [JsonPropertyName("top_row_text")]
    public string TopRowText { get; set; } = "Aidly Pharmacy";

    [JsonPropertyName("show_served_by_row")]
    public bool ShowServedByRow { get; set; } = true;

    [JsonPropertyName("show_discount_less_row")]
    public bool ShowDiscountLessRow { get; set; } = true;

    [JsonPropertyName("show_card_fee_row")]
    public bool ShowCardFeeRow { get; set; }

    [JsonPropertyName("show_customer_name")]
    public bool ShowCustomerName { get; set; } = true;

    [JsonPropertyName("show_customer_phone")]
    public bool ShowCustomerPhone { get; set; } = true;

    [JsonPropertyName("simple_print")]
    public bool SimplePrint { get; set; }

    [JsonPropertyName("show_customer_vendor_balance")]
    public bool ShowCustomerVendorBalance { get; set; } = true;

    [JsonPropertyName("show_shop_logo")]
    public bool ShowShopLogo { get; set; } = true;

    [JsonPropertyName("shop_logo_url")]
    public string? ShopLogoUrl { get; set; }

    [JsonPropertyName("bottom_row_text")]
    public string? BottomRowText { get; set; } = "Thank you for your visit! Goods sold are refundable within 7 days.";

    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class CfgProductConfig
{
    [JsonPropertyName("product_config_no")]
    public long ProductConfigNo { get; set; }

    [JsonPropertyName("pharmacy_no")]
    public long PharmacyNo { get; set; }

    [JsonPropertyName("branch_no")]
    public long BranchNo { get; set; }

    [JsonPropertyName("expired_notification_days")]
    public int ExpiredNotificationDays { get; set; } = 90;

    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class CfgLoyaltyConfig
{
    [JsonPropertyName("loyalty_config_no")]
    public long LoyaltyConfigNo { get; set; }

    [JsonPropertyName("pharmacy_no")]
    public long PharmacyNo { get; set; }

    [JsonPropertyName("branch_no")]
    public long BranchNo { get; set; }

    [JsonPropertyName("is_enabled")]
    public bool IsEnabled { get; set; } = true;

    [JsonPropertyName("earning_spend_amount_per_point")]
    public decimal EarningSpendAmountPerPoint { get; set; } = 100m;

    [JsonPropertyName("redemption_points_per_taka")]
    public decimal RedemptionPointsPerTaka { get; set; } = 1m;

    [JsonPropertyName("min_redeem_points")]
    public decimal MinRedeemPoints { get; set; } = 50m;

    [JsonPropertyName("max_redeem_limit_per_tx")]
    public decimal MaxRedeemLimitPerTx { get; set; } = 500m;

    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class CfgBulkSmsConfig
{
    [JsonPropertyName("bulk_sms_config_no")]
    public long BulkSmsConfigNo { get; set; }

    [JsonPropertyName("pharmacy_no")]
    public long PharmacyNo { get; set; }

    [JsonPropertyName("branch_no")]
    public long BranchNo { get; set; }

    [JsonPropertyName("current_sms_count")]
    public int CurrentSmsCount { get; set; } = 1000;

    [JsonPropertyName("sms_pack_unit_size")]
    public int SmsPackUnitSize { get; set; } = 500;

    [JsonPropertyName("sms_pack_unit_price")]
    public decimal SmsPackUnitPrice { get; set; } = 250m;

    [JsonPropertyName("sender_id")]
    public string? SenderId { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

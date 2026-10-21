using System.Text.Json.Serialization;

namespace AidlyPms.Api.Modules.Dashboard.Models;

public class ExpiryBatchItem
{
    [JsonPropertyName("product_no")]
    public long ProductNo { get; set; }

    [JsonPropertyName("product_name")]
    public string ProductName { get; set; } = string.Empty;

    [JsonPropertyName("generic_name")]
    public string? GenericName { get; set; }

    [JsonPropertyName("company_name")]
    public string? CompanyName { get; set; }

    [JsonPropertyName("batch_no")]
    public long BatchNo { get; set; }

    [JsonPropertyName("batch_number")]
    public string BatchNumber { get; set; } = string.Empty;

    [JsonPropertyName("expiry_date")]
    public DateTime ExpiryDate { get; set; }

    [JsonPropertyName("days_until_expiry")]
    public int DaysUntilExpiry { get; set; }

    [JsonPropertyName("quantity_pcs")]
    public int QuantityPcs { get; set; }

    [JsonPropertyName("unit_cost_price")]
    public decimal UnitCostPrice { get; set; }

    [JsonPropertyName("unit_sale_price")]
    public decimal UnitSalePrice { get; set; }

    [JsonPropertyName("total_cost_value")]
    public decimal TotalCostValue { get; set; }

    [JsonPropertyName("total_mrp_value")]
    public decimal TotalMrpValue { get; set; }

    [JsonPropertyName("rak_number")]
    public string? RakNumber { get; set; }

    [JsonPropertyName("bucket")]
    public string Bucket { get; set; } = string.Empty; // "Expired", "0-30 Days", "31-60 Days", "61-90 Days"
}

public class ExpiryBucketSummary
{
    [JsonPropertyName("expired_count")]
    public int ExpiredCount { get; set; }

    [JsonPropertyName("expired_cost_value")]
    public decimal ExpiredCostValue { get; set; }

    [JsonPropertyName("critical_30_count")]
    public int Critical30Count { get; set; }

    [JsonPropertyName("critical_30_cost_value")]
    public decimal Critical30CostValue { get; set; }

    [JsonPropertyName("moderate_60_count")]
    public int Moderate60Count { get; set; }

    [JsonPropertyName("moderate_60_cost_value")]
    public decimal Moderate60CostValue { get; set; }

    [JsonPropertyName("approaching_90_count")]
    public int Approaching90Count { get; set; }

    [JsonPropertyName("approaching_90_cost_value")]
    public decimal Approaching90CostValue { get; set; }

    [JsonPropertyName("total_at_risk_count")]
    public int TotalAtRiskCount { get; set; }

    [JsonPropertyName("total_at_risk_cost_value")]
    public decimal TotalAtRiskCostValue { get; set; }
}

public class ExpiryBucketReport
{
    [JsonPropertyName("summary")]
    public ExpiryBucketSummary Summary { get; set; } = new();

    [JsonPropertyName("items")]
    public List<ExpiryBatchItem> Items { get; set; } = [];
}

public class AbcFsnItem
{
    [JsonPropertyName("product_no")]
    public long ProductNo { get; set; }

    [JsonPropertyName("product_name")]
    public string ProductName { get; set; } = string.Empty;

    [JsonPropertyName("generic_name")]
    public string? GenericName { get; set; }

    [JsonPropertyName("company_name")]
    public string? CompanyName { get; set; }

    [JsonPropertyName("current_stock_pcs")]
    public int CurrentStockPcs { get; set; }

    [JsonPropertyName("units_sold")]
    public int UnitsSold { get; set; }

    [JsonPropertyName("total_revenue")]
    public decimal TotalRevenue { get; set; }

    [JsonPropertyName("cumulative_revenue_percent")]
    public decimal CumulativeRevenuePercent { get; set; }

    [JsonPropertyName("abc_classification")]
    public string AbcClassification { get; set; } = "C"; // A, B, C

    [JsonPropertyName("movement_frequency")]
    public int MovementFrequency { get; set; }

    [JsonPropertyName("fsn_velocity")]
    public string FsnVelocity { get; set; } = "Non-Moving"; // Fast-Moving, Slow-Moving, Non-Moving

    [JsonPropertyName("turnover_rate")]
    public decimal TurnoverRate { get; set; }
}

public class ProductMarginItem
{
    [JsonPropertyName("product_no")]
    public long ProductNo { get; set; }

    [JsonPropertyName("product_name")]
    public string ProductName { get; set; } = string.Empty;

    [JsonPropertyName("generic_name")]
    public string? GenericName { get; set; }

    [JsonPropertyName("company_name")]
    public string? CompanyName { get; set; }

    [JsonPropertyName("trade_price_cost")]
    public decimal TradePriceCost { get; set; }

    [JsonPropertyName("mrp_sale_price")]
    public decimal MrpSalePrice { get; set; }

    [JsonPropertyName("profit_per_unit")]
    public decimal ProfitPerUnit { get; set; }

    [JsonPropertyName("margin_percent")]
    public decimal MarginPercent { get; set; }

    [JsonPropertyName("total_units_sold")]
    public int TotalUnitsSold { get; set; }

    [JsonPropertyName("total_realized_profit")]
    public decimal TotalRealizedProfit { get; set; }
}

public class DailyReturnItem
{
    [JsonPropertyName("sale_return_no")]
    public long SaleReturnNo { get; set; }

    [JsonPropertyName("return_number")]
    public string ReturnNumber { get; set; } = string.Empty;

    [JsonPropertyName("sale_invoice_no")]
    public long SaleInvoiceNo { get; set; }

    [JsonPropertyName("sales_number")]
    public string SalesNumber { get; set; } = string.Empty;

    [JsonPropertyName("customer_name")]
    public string CustomerName { get; set; } = string.Empty;

    [JsonPropertyName("return_date")]
    public DateTime ReturnDate { get; set; }

    [JsonPropertyName("total_items")]
    public int TotalItems { get; set; }

    [JsonPropertyName("final_refund_price")]
    public decimal FinalRefundPrice { get; set; }

    [JsonPropertyName("cash_paid_refund")]
    public decimal CashPaidRefund { get; set; }

    [JsonPropertyName("refund_method")]
    public short RefundMethod { get; set; }

    [JsonPropertyName("return_status")]
    public short ReturnStatus { get; set; }

    [JsonPropertyName("exchange_invoice_no")]
    public long? ExchangeInvoiceNo { get; set; }

    [JsonPropertyName("exchange_sales_number")]
    public string? ExchangeSalesNumber { get; set; }

    [JsonPropertyName("created_by_user_name")]
    public string? CreatedByUserName { get; set; }
}

public class DailyReturnsReport
{
    [JsonPropertyName("date")]
    public string Date { get; set; } = string.Empty;

    [JsonPropertyName("total_returns_count")]
    public int TotalReturnsCount { get; set; }

    [JsonPropertyName("total_refund_amount")]
    public decimal TotalRefundAmount { get; set; }

    [JsonPropertyName("total_cash_refund")]
    public decimal TotalCashRefund { get; set; }

    [JsonPropertyName("items")]
    public List<DailyReturnItem> Items { get; set; } = [];
}

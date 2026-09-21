using System.Text.Json.Serialization;

namespace AidlyPms.Api.Modules.Dashboard.Models;

public class ExecutiveDashboardSummary
{
    [JsonPropertyName("today_sales")]
    public decimal TodaySales { get; set; }

    [JsonPropertyName("today_sales_count")]
    public int TodaySalesCount { get; set; }

    [JsonPropertyName("today_profit")]
    public decimal TodayProfit { get; set; }

    [JsonPropertyName("today_purchases")]
    public decimal TodayPurchases { get; set; }

    [JsonPropertyName("today_expenses")]
    public decimal TodayExpenses { get; set; }

    [JsonPropertyName("today_cash_collected")]
    public decimal TodayCashCollected { get; set; }

    [JsonPropertyName("total_customer_due")]
    public decimal TotalCustomerDue { get; set; }

    [JsonPropertyName("total_supplier_payable")]
    public decimal TotalSupplierPayable { get; set; }

    [JsonPropertyName("total_inventory_valuation")]
    public decimal TotalInventoryValuation { get; set; }

    [JsonPropertyName("total_products_count")]
    public int TotalProductsCount { get; set; }

    [JsonPropertyName("low_stock_items_count")]
    public int LowStockItemsCount { get; set; }

    [JsonPropertyName("expired_items_count")]
    public int ExpiredItemsCount { get; set; }

    [JsonPropertyName("near_expiry_items_count")]
    public int NearExpiryItemsCount { get; set; }
}

public class FastMovingProduct
{
    [JsonPropertyName("product_no")]
    public long ProductNo { get; set; }

    [JsonPropertyName("product_name")]
    public string ProductName { get; set; } = string.Empty;

    [JsonPropertyName("total_sold_pcs")]
    public int TotalSoldPcs { get; set; }

    [JsonPropertyName("total_revenue")]
    public decimal TotalRevenue { get; set; }
}

public class CashbookEntry
{
    [JsonPropertyName("entry_date")]
    public DateTime EntryDate { get; set; }

    [JsonPropertyName("doc_type")]
    public string DocType { get; set; } = string.Empty;

    [JsonPropertyName("doc_no")]
    public string DocNo { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("cash_in")]
    public decimal CashIn { get; set; }

    [JsonPropertyName("cash_out")]
    public decimal CashOut { get; set; }

    [JsonPropertyName("balance")]
    public decimal Balance { get; set; }
}

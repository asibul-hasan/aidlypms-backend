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

    [JsonPropertyName("total_bank_balance")]
    public decimal TotalBankBalance { get; set; }

    [JsonPropertyName("total_mfs_balance")]
    public decimal TotalMfsBalance { get; set; }

    [JsonPropertyName("total_cash_balance")]
    public decimal TotalCashBalance { get; set; }

    [JsonPropertyName("today_incomes")]
    public decimal TodayIncomes { get; set; }

    [JsonPropertyName("today_demurrage")]
    public decimal TodayDemurrage { get; set; }

    [JsonPropertyName("pending_handover_count")]
    public int PendingHandoverCount { get; set; }

    [JsonPropertyName("pending_handover_amount")]
    public decimal PendingHandoverAmount { get; set; }

    [JsonPropertyName("today_sale_returns")]
    public decimal TodaySaleReturns { get; set; }

    [JsonPropertyName("today_purchase_returns")]
    public decimal TodayPurchaseReturns { get; set; }

    [JsonPropertyName("today_due_collected")]
    public decimal TodayDueCollected { get; set; }

    [JsonPropertyName("today_supplier_paid")]
    public decimal TodaySupplierPaid { get; set; }
}

public class FastMovingProduct
{
    [JsonPropertyName("product_no")]
    public long ProductNo { get; set; }

    [JsonPropertyName("product_name")]
    public string ProductName { get; set; } = string.Empty;

    [JsonPropertyName("generic_name")]
    public string GenericName { get; set; } = string.Empty;

    [JsonPropertyName("total_sold_pcs")]
    public int TotalSoldPcs { get; set; }

    [JsonPropertyName("total_revenue")]
    public decimal TotalRevenue { get; set; }

    [JsonPropertyName("total_profit")]
    public decimal TotalProfit { get; set; }

    [JsonPropertyName("profit_margin_pct")]
    public decimal ProfitMarginPct { get; set; }
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

    [JsonPropertyName("account_no")]
    public long? AccountNo { get; set; }

    [JsonPropertyName("account_name")]
    public string? AccountName { get; set; }

    [JsonPropertyName("account_type")]
    public short? AccountType { get; set; }
}

public class DailySalesTrend
{
    [JsonPropertyName("date_label")]
    public string DateLabel { get; set; } = string.Empty;

    [JsonPropertyName("date_val")]
    public DateTime DateVal { get; set; }

    [JsonPropertyName("buy_price")]
    public decimal BuyPrice { get; set; }

    [JsonPropertyName("gross_profit")]
    public decimal GrossProfit { get; set; }

    [JsonPropertyName("total_sale")]
    public decimal TotalSale { get; set; }

    [JsonPropertyName("op_cost")]
    public decimal OpCost { get; set; }

    [JsonPropertyName("net_profit")]
    public decimal NetProfit { get; set; }
}

public class FinancialReportSummary
{
    [JsonPropertyName("from_date")]
    public DateTime FromDate { get; set; }

    [JsonPropertyName("to_date")]
    public DateTime ToDate { get; set; }

    [JsonPropertyName("total_sales")]
    public decimal TotalSales { get; set; }

    [JsonPropertyName("total_sales_count")]
    public int TotalSalesCount { get; set; }

    [JsonPropertyName("total_profit")]
    public decimal TotalProfit { get; set; }

    [JsonPropertyName("total_purchases")]
    public decimal TotalPurchases { get; set; }

    [JsonPropertyName("total_expenses")]
    public decimal TotalExpenses { get; set; }

    [JsonPropertyName("total_incomes")]
    public decimal TotalIncomes { get; set; }

    [JsonPropertyName("cash_collected")]
    public decimal CashCollected { get; set; }

    [JsonPropertyName("due_collected")]
    public decimal DueCollected { get; set; }

    [JsonPropertyName("supplier_paid")]
    public decimal SupplierPaid { get; set; }

    [JsonPropertyName("sale_returns")]
    public decimal SaleReturns { get; set; }

    [JsonPropertyName("purchase_returns")]
    public decimal PurchaseReturns { get; set; }

    [JsonPropertyName("net_profit")]
    public decimal NetProfit { get; set; }

    [JsonPropertyName("net_cash_flow")]
    public decimal NetCashFlow { get; set; }
}

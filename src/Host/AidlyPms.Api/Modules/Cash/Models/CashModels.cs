using System.Text.Json.Serialization;

namespace AidlyPms.Api.Modules.Cash.Models;

public class CashCloser
{
    [JsonPropertyName("cash_closer_no")]
    public long CashCloserNo { get; set; }

    [JsonPropertyName("pharmacy_no")]
    public long PharmacyNo { get; set; }

    [JsonPropertyName("branch_no")]
    public long BranchNo { get; set; }

    [JsonPropertyName("closed_by_user_no")]
    public long ClosedByUserNo { get; set; }

    [JsonPropertyName("register_id")]
    public string? RegisterId { get; set; }

    [JsonPropertyName("shift_no")]
    public int ShiftNo { get; set; } = 1;

    [JsonPropertyName("opening_balance")]
    public decimal OpeningBalance { get; set; }

    [JsonPropertyName("cash_sales_collected")]
    public decimal CashSalesCollected { get; set; }

    [JsonPropertyName("card_collected")]
    public decimal CardCollected { get; set; }

    [JsonPropertyName("bkash_collected")]
    public decimal BkashCollected { get; set; }

    [JsonPropertyName("nagad_collected")]
    public decimal NagadCollected { get; set; }

    [JsonPropertyName("rocket_collected")]
    public decimal RocketCollected { get; set; }

    [JsonPropertyName("split_collected")]
    public decimal SplitCollected { get; set; }

    [JsonPropertyName("due_collections")]
    public decimal DueCollections { get; set; }

    [JsonPropertyName("cash_returns_paid")]
    public decimal CashReturnsPaid { get; set; }

    [JsonPropertyName("expenses_paid")]
    public decimal ExpensesPaid { get; set; }

    [JsonPropertyName("calculated_closing_balance")]
    public decimal CalculatedClosingBalance { get; set; }

    [JsonPropertyName("actual_counted_balance")]
    public decimal ActualCountedBalance { get; set; }

    [JsonPropertyName("discrepancy_amount")]
    public decimal DiscrepancyAmount { get; set; }

    [JsonPropertyName("note")]
    public string? Note { get; set; }

    [JsonPropertyName("closing_date")]
    public DateTime ClosingDate { get; set; } = DateTime.UtcNow.Date;

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Joined
    [JsonPropertyName("closed_by_user_name")]
    public string? ClosedByUserName { get; set; }
}

public class CloseShiftPayload
{
    [JsonPropertyName("register_id")]
    public string? RegisterId { get; set; }

    [JsonPropertyName("shift_no")]
    public int ShiftNo { get; set; } = 1;

    [JsonPropertyName("opening_balance")]
    public decimal OpeningBalance { get; set; }

    [JsonPropertyName("actual_counted_balance")]
    public decimal ActualCountedBalance { get; set; }

    [JsonPropertyName("note")]
    public string? Note { get; set; }

    [JsonPropertyName("closing_date")]
    public DateTime? ClosingDate { get; set; }
}

public class DueCollection
{
    [JsonPropertyName("due_collection_no")]
    public long DueCollectionNo { get; set; }

    [JsonPropertyName("pharmacy_no")]
    public long PharmacyNo { get; set; }

    [JsonPropertyName("branch_no")]
    public long BranchNo { get; set; }

    [JsonPropertyName("customer_no")]
    public long CustomerNo { get; set; }

    [JsonPropertyName("account_no")]
    public long AccountNo { get; set; }

    [JsonPropertyName("sale_invoice_no")]
    public long? SaleInvoiceNo { get; set; }

    [JsonPropertyName("payment_number")]
    public string PaymentNumber { get; set; } = string.Empty;

    [JsonPropertyName("payment_method")]
    public short PaymentMethod { get; set; } = 1;

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("note")]
    public string? Note { get; set; }

    [JsonPropertyName("received_by_user_no")]
    public long? ReceivedByUserNo { get; set; }

    [JsonPropertyName("payment_date")]
    public DateTime PaymentDate { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Joined
    [JsonPropertyName("customer_name")]
    public string? CustomerName { get; set; }

    [JsonPropertyName("customer_phone")]
    public string? CustomerPhone { get; set; }

    [JsonPropertyName("account_name")]
    public string? AccountName { get; set; }
}

public class CollectDuePayload
{
    [JsonPropertyName("customer_no")]
    public long CustomerNo { get; set; }

    [JsonPropertyName("account_no")]
    public long? AccountNo { get; set; } = 1;

    [JsonPropertyName("sale_invoice_no")]
    public long? SaleInvoiceNo { get; set; }

    [JsonPropertyName("payment_method")]
    public short PaymentMethod { get; set; } = 1;

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("note")]
    public string? Note { get; set; }
}

public class CashHandoverItemDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("inv")]
    public string Inv { get; set; } = string.Empty;

    [JsonPropertyName("date")]
    public string Date { get; set; } = string.Empty;

    [JsonPropertyName("salesman")]
    public string Salesman { get; set; } = string.Empty;

    [JsonPropertyName("paid")]
    public decimal Paid { get; set; }

    [JsonPropertyName("total")]
    public decimal Total { get; set; }
}

public class ReconcileHandoverPayload
{
    [JsonPropertyName("invoice_nos")]
    public List<long>? InvoiceNos { get; set; }
}


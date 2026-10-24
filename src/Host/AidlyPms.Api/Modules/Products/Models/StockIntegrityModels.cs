using System.Text.Json.Serialization;

namespace AidlyPms.Api.Modules.Products.Models;

public class StockIntegrityResult
{
    [JsonPropertyName("is_clean")]
    public bool IsClean { get; set; }

    [JsonPropertyName("total_discrepancies")]
    public int TotalDiscrepancies { get; set; }

    [JsonPropertyName("discrepant_ledger_count")]
    public int DiscrepantLedgerCount { get; set; }

    [JsonPropertyName("negative_real_batches_count")]
    public int NegativeRealBatchesCount { get; set; }

    [JsonPropertyName("positive_placeholder_count")]
    public int PositivePlaceholderCount { get; set; }

    [JsonPropertyName("missing_ledger_batches_count")]
    public int MissingLedgerBatchesCount { get; set; }

    [JsonPropertyName("offending_batches")]
    public List<StockIntegrityDiscrepancyItem> OffendingBatches { get; set; } = new();
}

public class StockIntegrityDiscrepancyItem
{
    [JsonPropertyName("batch_no")]
    public long BatchNo { get; set; }

    [JsonPropertyName("batch_number")]
    public string BatchNumber { get; set; } = string.Empty;

    [JsonPropertyName("product_no")]
    public long ProductNo { get; set; }

    [JsonPropertyName("product_name")]
    public string ProductName { get; set; } = string.Empty;

    [JsonPropertyName("batch_quantity_pcs")]
    public int BatchQuantityPcs { get; set; }

    [JsonPropertyName("ledger_sum_pcs")]
    public int LedgerSumPcs { get; set; }

    [JsonPropertyName("is_placeholder")]
    public bool IsPlaceholder { get; set; }

    [JsonPropertyName("issue_type")]
    public string IssueType { get; set; } = string.Empty; // "LEDGER_MISMATCH", "NEGATIVE_REAL_STOCK", "POSITIVE_PLACEHOLDER", "MISSING_LEDGER"

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;
}

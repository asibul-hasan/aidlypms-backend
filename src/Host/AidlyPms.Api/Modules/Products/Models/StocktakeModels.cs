using System.Text.Json.Serialization;

namespace AidlyPms.Api.Modules.Products.Models;

public class InvStocktake
{
    [JsonPropertyName("stocktake_no")]
    public long StocktakeNo { get; set; }

    [JsonPropertyName("pharmacy_no")]
    public long PharmacyNo { get; set; }

    [JsonPropertyName("branch_no")]
    public long BranchNo { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "OPEN";

    [JsonPropertyName("note")]
    public string? Note { get; set; }

    [JsonPropertyName("started_at")]
    public DateTime StartedAt { get; set; }

    [JsonPropertyName("started_by")]
    public long? StartedBy { get; set; }

    [JsonPropertyName("posted_at")]
    public DateTime? PostedAt { get; set; }

    [JsonPropertyName("posted_by")]
    public long? PostedBy { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; }

    [JsonPropertyName("started_by_name")]
    public string? StartedByName { get; set; }

    [JsonPropertyName("posted_by_name")]
    public string? PostedByName { get; set; }

    [JsonPropertyName("total_items")]
    public int TotalItems { get; set; }

    [JsonPropertyName("total_counted")]
    public int TotalCounted { get; set; }

    [JsonPropertyName("total_variance")]
    public int TotalVariance { get; set; }

    [JsonPropertyName("items")]
    public List<InvStocktakeItem> Items { get; set; } = new();
}

public class InvStocktakeItem
{
    [JsonPropertyName("stocktake_item_no")]
    public long StocktakeItemNo { get; set; }

    [JsonPropertyName("pharmacy_no")]
    public long PharmacyNo { get; set; }

    [JsonPropertyName("branch_no")]
    public long BranchNo { get; set; }

    [JsonPropertyName("stocktake_no")]
    public long StocktakeNo { get; set; }

    [JsonPropertyName("product_no")]
    public long ProductNo { get; set; }

    [JsonPropertyName("batch_no")]
    public long? BatchNo { get; set; }

    [JsonPropertyName("system_qty_pcs")]
    public int SystemQtyPcs { get; set; }

    [JsonPropertyName("counted_qty_pcs")]
    public int? CountedQtyPcs { get; set; }

    [JsonPropertyName("variance_pcs")]
    public int? VariancePcs { get; set; }

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    // UI display helpers
    [JsonPropertyName("product_name")]
    public string? ProductName { get; set; }

    [JsonPropertyName("batch_number")]
    public string? BatchNumber { get; set; }

    [JsonPropertyName("expiry_date")]
    public DateTime? ExpiryDate { get; set; }

    [JsonPropertyName("unit_cost")]
    public decimal UnitCost { get; set; }

    [JsonPropertyName("company_name")]
    public string? CompanyName { get; set; }

    [JsonPropertyName("generic_name")]
    public string? GenericName { get; set; }

    [JsonPropertyName("rak_number")]
    public string? RakNumber { get; set; }
}

public class CreateStocktakeRequest
{
    [JsonPropertyName("filter_type")]
    public string? FilterType { get; set; } // ALL, RACK, COMPANY, PRODUCT

    [JsonPropertyName("filter_value")]
    public string? FilterValue { get; set; }

    [JsonPropertyName("product_ids")]
    public List<long>? ProductIds { get; set; }

    [JsonPropertyName("note")]
    public string? Note { get; set; }
}

public class SaveStocktakeCountsRequest
{
    [JsonPropertyName("items")]
    public List<StocktakeCountEntry> Items { get; set; } = new();
}

public class StocktakeCountEntry
{
    [JsonPropertyName("stocktake_item_no")]
    public long StocktakeItemNo { get; set; }

    [JsonPropertyName("counted_qty_pcs")]
    public int? CountedQtyPcs { get; set; }

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
}

public class StocktakeChangedBatch
{
    [JsonPropertyName("batch_no")]
    public long BatchNo { get; set; }

    [JsonPropertyName("batch_number")]
    public string BatchNumber { get; set; } = string.Empty;

    [JsonPropertyName("product_name")]
    public string ProductName { get; set; } = string.Empty;

    [JsonPropertyName("system_qty_snapshot")]
    public int SystemQtySnapshot { get; set; }

    [JsonPropertyName("current_system_qty")]
    public int CurrentSystemQty { get; set; }
}

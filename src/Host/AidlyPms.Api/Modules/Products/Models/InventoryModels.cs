using System.Text.Json.Serialization;

namespace AidlyPms.Api.Modules.Products.Models;

public class InvAdjustment
{
    [JsonPropertyName("adjustment_no")]
    public long AdjustmentNo { get; set; }

    [JsonPropertyName("pharmacy_no")]
    public long PharmacyNo { get; set; }

    [JsonPropertyName("branch_no")]
    public long BranchNo { get; set; }

    [JsonPropertyName("adjustment_number")]
    public string AdjustmentNumber { get; set; } = string.Empty;

    [JsonPropertyName("adjustment_type")]
    public short AdjustmentType { get; set; } = 1; // 1: Routine Stocktake, 2: Damage/Breakage, 3: Expiry Write-Off

    [JsonPropertyName("total_items")]
    public int TotalItems { get; set; }

    [JsonPropertyName("net_cost_adjustment")]
    public decimal NetCostAdjustment { get; set; }

    [JsonPropertyName("adjustment_status")]
    public short AdjustmentStatus { get; set; } = 2; // 1: Draft, 2: Approved/Posted

    [JsonPropertyName("approved_by_user_no")]
    public long? ApprovedByUserNo { get; set; }

    [JsonPropertyName("note")]
    public string? Note { get; set; }

    [JsonPropertyName("adjustment_date")]
    public DateTime AdjustmentDate { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("items")]
    public List<InvAdjustmentItem> Items { get; set; } = new();
}

public class InvAdjustmentItem
{
    [JsonPropertyName("adjustment_item_no")]
    public long AdjustmentItemNo { get; set; }

    [JsonPropertyName("pharmacy_no")]
    public long PharmacyNo { get; set; }

    [JsonPropertyName("branch_no")]
    public long BranchNo { get; set; }

    [JsonPropertyName("adjustment_no")]
    public long AdjustmentNo { get; set; }

    [JsonPropertyName("product_no")]
    public long ProductNo { get; set; }

    [JsonPropertyName("product_name")]
    public string? ProductName { get; set; }

    [JsonPropertyName("batch_no")]
    public long? BatchNo { get; set; }

    [JsonPropertyName("batch_number")]
    public string? BatchNumber { get; set; }

    [JsonPropertyName("counted_qty_pcs")]
    public int CountedQtyPcs { get; set; }

    [JsonPropertyName("system_qty_pcs")]
    public int SystemQtyPcs { get; set; }

    [JsonPropertyName("difference_qty_pcs")]
    public int DifferenceQtyPcs { get; set; }

    [JsonPropertyName("unit_cost")]
    public decimal UnitCost { get; set; }

    [JsonPropertyName("total_cost_impact")]
    public decimal TotalCostImpact { get; set; }

    [JsonPropertyName("reason_code")]
    public short? ReasonCode { get; set; }
}

public class CreateAdjustmentPayload
{
    [JsonPropertyName("adjustment_type")]
    public short AdjustmentType { get; set; } = 1;

    [JsonPropertyName("note")]
    public string? Note { get; set; }

    [JsonPropertyName("adjustment_date")]
    public DateTime? AdjustmentDate { get; set; }

    [JsonPropertyName("items")]
    public List<InvAdjustmentItem> Items { get; set; } = new();
}

public class InvTransfer
{
    [JsonPropertyName("transfer_no")]
    public long TransferNo { get; set; }

    [JsonPropertyName("pharmacy_no")]
    public long PharmacyNo { get; set; }

    [JsonPropertyName("branch_no")]
    public long BranchNo { get; set; }

    [JsonPropertyName("transfer_number")]
    public string TransferNumber { get; set; } = string.Empty;

    [JsonPropertyName("from_branch_no")]
    public long FromBranchNo { get; set; }

    [JsonPropertyName("from_branch_name")]
    public string? FromBranchName { get; set; }

    [JsonPropertyName("to_branch_no")]
    public long ToBranchNo { get; set; }

    [JsonPropertyName("to_branch_name")]
    public string? ToBranchName { get; set; }

    [JsonPropertyName("total_items")]
    public int TotalItems { get; set; }

    [JsonPropertyName("total_cost_amount")]
    public decimal TotalCostAmount { get; set; }

    [JsonPropertyName("transfer_status")]
    public short TransferStatus { get; set; } = 1; // 1: In-Transit, 2: Received, 3: Cancelled

    [JsonPropertyName("note")]
    public string? Note { get; set; }

    [JsonPropertyName("sent_by_user_no")]
    public long? SentByUserNo { get; set; }

    [JsonPropertyName("received_by_user_no")]
    public long? ReceivedByUserNo { get; set; }

    [JsonPropertyName("sent_at")]
    public DateTime SentAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("received_at")]
    public DateTime? ReceivedAt { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("items")]
    public List<InvTransferItem> Items { get; set; } = new();
}

public class InvTransferItem
{
    [JsonPropertyName("transfer_item_no")]
    public long TransferItemNo { get; set; }

    [JsonPropertyName("pharmacy_no")]
    public long PharmacyNo { get; set; }

    [JsonPropertyName("branch_no")]
    public long BranchNo { get; set; }

    [JsonPropertyName("transfer_no")]
    public long TransferNo { get; set; }

    [JsonPropertyName("product_no")]
    public long ProductNo { get; set; }

    [JsonPropertyName("product_name")]
    public string? ProductName { get; set; }

    [JsonPropertyName("batch_no")]
    public long? BatchNo { get; set; }

    [JsonPropertyName("batch_number")]
    public string? BatchNumber { get; set; }

    [JsonPropertyName("qty_pcs")]
    public int QtyPcs { get; set; }

    [JsonPropertyName("unit_cost")]
    public decimal UnitCost { get; set; }

    [JsonPropertyName("total_cost")]
    public decimal TotalCost { get; set; }

    [JsonPropertyName("expiry_date")]
    public DateTime? ExpiryDate { get; set; }
}

public class CreateTransferPayload
{
    [JsonPropertyName("to_branch_no")]
    public long ToBranchNo { get; set; }

    [JsonPropertyName("note")]
    public string? Note { get; set; }

    [JsonPropertyName("items")]
    public List<InvTransferItem> Items { get; set; } = new();
}

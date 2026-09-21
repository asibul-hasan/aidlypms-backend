using System.Text.Json.Serialization;

namespace AidlyPms.Api.Modules.Customers.Models;

public class CustCustomer
{
    [JsonPropertyName("customer_no")]
    public long CustomerNo { get; set; }

    [JsonPropertyName("uuid")]
    public Guid Uuid { get; set; } = Guid.NewGuid();

    [JsonPropertyName("pharmacy_no")]
    public long PharmacyNo { get; set; }

    [JsonPropertyName("branch_no")]
    public long BranchNo { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("phone")]
    public string Phone { get; set; } = string.Empty;

    [JsonPropertyName("gender")]
    public short Gender { get; set; } = 1;

    [JsonPropertyName("customer_type")]
    public short CustomerType { get; set; } = 1;

    [JsonPropertyName("discount_percent")]
    public decimal DiscountPercent { get; set; }

    [JsonPropertyName("address")]
    public string? Address { get; set; }

    [JsonPropertyName("has_due")]
    public bool HasDue { get; set; }

    [JsonPropertyName("current_due")]
    public decimal CurrentDue { get; set; }

    [JsonPropertyName("advance_balance")]
    public decimal AdvanceBalance { get; set; }

    [JsonPropertyName("is_loyalty_member")]
    public bool IsLoyaltyMember { get; set; }

    [JsonPropertyName("loyalty_points")]
    public decimal LoyaltyPoints { get; set; }

    [JsonPropertyName("is_active")]
    public bool IsActive { get; set; } = true;

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

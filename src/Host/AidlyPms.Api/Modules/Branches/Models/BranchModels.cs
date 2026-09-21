using System.Text.Json.Serialization;

namespace AidlyPms.Api.Modules.Branches.Models;

public class SysPharmacy
{
    [JsonPropertyName("pharmacy_no")]
    public long PharmacyNo { get; set; }

    [JsonPropertyName("uuid")]
    public Guid Uuid { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("contact_phone")]
    public string ContactPhone { get; set; } = string.Empty;

    [JsonPropertyName("contact_email")]
    public string? ContactEmail { get; set; }

    [JsonPropertyName("address")]
    public string Address { get; set; } = string.Empty;

    [JsonPropertyName("district")]
    public string District { get; set; } = string.Empty;

    [JsonPropertyName("is_active")]
    public bool IsActive { get; set; } = true;
}

public class SysBranch
{
    [JsonPropertyName("branch_no")]
    public long BranchNo { get; set; }

    [JsonPropertyName("pharmacy_no")]
    public long PharmacyNo { get; set; }

    [JsonPropertyName("uuid")]
    public Guid Uuid { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("drug_license_no")]
    public string? DrugLicenseNo { get; set; }

    [JsonPropertyName("contact_phone")]
    public string ContactPhone { get; set; } = string.Empty;

    [JsonPropertyName("contact_email")]
    public string? ContactEmail { get; set; }

    [JsonPropertyName("address")]
    public string Address { get; set; } = string.Empty;

    [JsonPropertyName("district")]
    public string District { get; set; } = string.Empty;

    [JsonPropertyName("postal_code")]
    public string? PostalCode { get; set; }

    [JsonPropertyName("is_active")]
    public bool IsActive { get; set; } = true;
}

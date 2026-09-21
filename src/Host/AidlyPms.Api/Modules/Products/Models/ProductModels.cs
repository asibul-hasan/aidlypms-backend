using System.Text.Json.Serialization;

namespace AidlyPms.Api.Modules.Products.Models;

public class ProdCompany
{
    [JsonPropertyName("company_no")]
    public long CompanyNo { get; set; }

    [JsonPropertyName("pharmacy_no")]
    public long PharmacyNo { get; set; }

    [JsonPropertyName("branch_no")]
    public long BranchNo { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("is_active")]
    public bool IsActive { get; set; } = true;

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class ProdGeneric
{
    [JsonPropertyName("generic_no")]
    public long GenericNo { get; set; }

    [JsonPropertyName("pharmacy_no")]
    public long PharmacyNo { get; set; }

    [JsonPropertyName("branch_no")]
    public long BranchNo { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("is_active")]
    public bool IsActive { get; set; } = true;

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class ProdProduct
{
    [JsonPropertyName("product_no")]
    public long ProductNo { get; set; }

    [JsonPropertyName("uuid")]
    public Guid Uuid { get; set; } = Guid.NewGuid();

    [JsonPropertyName("pharmacy_no")]
    public long PharmacyNo { get; set; }

    [JsonPropertyName("branch_no")]
    public long BranchNo { get; set; }

    [JsonPropertyName("company_no")]
    public long CompanyNo { get; set; }

    [JsonPropertyName("generic_no")]
    public long? GenericNo { get; set; }

    [JsonPropertyName("product_name")]
    public string ProductName { get; set; } = string.Empty;

    [JsonPropertyName("brand_name")]
    public string BrandName => ProductName;

    [JsonPropertyName("strength")]
    public string? Strength { get; set; }

    [JsonPropertyName("volume")]
    public string? Volume { get; set; }

    [JsonPropertyName("medicine_type")]
    public short MedicineType { get; set; } = 1;

    [JsonPropertyName("unit_type")]
    public short UnitType { get; set; } = 1;

    [JsonPropertyName("quantity_per_box")]
    public int QuantityPerBox { get; set; } = 1;

    [JsonPropertyName("cost_per_box")]
    public decimal CostPerBox { get; set; }

    [JsonPropertyName("sale_price_per_box")]
    public decimal SalePricePerBox { get; set; }

    [JsonPropertyName("avg_purchase_price_per_piece")]
    public decimal AvgPurchasePricePerPiece { get; set; }

    [JsonPropertyName("purchase_price_per_piece")]
    public decimal PurchasePricePerPiece { get; set; }

    [JsonPropertyName("sale_price_per_piece")]
    public decimal SalePricePerPiece { get; set; }

    [JsonPropertyName("wholesale_price_per_box")]
    public decimal WholesalePricePerBox { get; set; }

    [JsonPropertyName("wholesale_price_per_piece")]
    public decimal WholesalePricePerPiece { get; set; }

    [JsonPropertyName("country_of_origin")]
    public string? CountryOfOrigin { get; set; } = "Bangladesh";

    [JsonPropertyName("barcode_value")]
    public string? BarcodeValue { get; set; }

    [JsonPropertyName("is_medicine")]
    public bool IsMedicine { get; set; } = true;

    [JsonPropertyName("is_prescription_required")]
    public bool IsPrescriptionRequired { get; set; }

    [JsonPropertyName("is_narcotic")]
    public bool IsNarcotic { get; set; }

    [JsonPropertyName("min_stock_qty_pcs")]
    public int MinStockQtyPcs { get; set; } = 10;

    [JsonPropertyName("rak_number")]
    public string? RakNumber { get; set; }

    [JsonPropertyName("is_active")]
    public bool IsActive { get; set; } = true;

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Additional fields populated for UI
    [JsonPropertyName("company_name")]
    public string? CompanyName { get; set; }

    [JsonPropertyName("generic_name")]
    public string? GenericName { get; set; }

    [JsonPropertyName("stock_qty")]
    public int StockQty { get; set; }
}

public class ProdProductBatch
{
    [JsonPropertyName("batch_no")]
    public long BatchNo { get; set; }

    [JsonPropertyName("pharmacy_no")]
    public long PharmacyNo { get; set; }

    [JsonPropertyName("branch_no")]
    public long BranchNo { get; set; }

    [JsonPropertyName("product_no")]
    public long ProductNo { get; set; }

    [JsonPropertyName("batch_number")]
    public string BatchNumber { get; set; } = string.Empty;

    [JsonPropertyName("expiry_date")]
    public DateTime ExpiryDate { get; set; }

    [JsonPropertyName("box_quantity")]
    public int BoxQuantity { get; set; }

    [JsonPropertyName("quantity_in_box")]
    public int QuantityInBox { get; set; } = 1;

    [JsonPropertyName("total_quantity_pcs")]
    public int TotalQuantityPcs { get; set; }

    [JsonPropertyName("cost_per_box")]
    public decimal CostPerBox { get; set; }

    [JsonPropertyName("sale_price_per_box")]
    public decimal SalePricePerBox { get; set; }

    [JsonPropertyName("vat_percent")]
    public decimal VatPercent { get; set; }

    [JsonPropertyName("is_expired")]
    public bool IsExpired { get; set; }

    [JsonPropertyName("product_name")]
    public string? ProductName { get; set; }
}

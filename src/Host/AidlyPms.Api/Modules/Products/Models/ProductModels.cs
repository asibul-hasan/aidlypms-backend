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

    [JsonPropertyName("deleted_at")]
    public DateTime? DeletedAt { get; set; }

    [JsonPropertyName("deleted_by")]
    public long? DeletedBy { get; set; }

    [JsonPropertyName("deletion_reason")]
    public string? DeletionReason { get; set; }

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

    [JsonPropertyName("deleted_at")]
    public DateTime? DeletedAt { get; set; }

    [JsonPropertyName("deleted_by")]
    public long? DeletedBy { get; set; }

    [JsonPropertyName("deletion_reason")]
    public string? DeletionReason { get; set; }

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

    [JsonPropertyName("dar_number")]
    public string? DarNumber { get; set; }

    [JsonPropertyName("drug_schedule")]
    public string DrugSchedule { get; set; } = "OTC";

    [JsonPropertyName("is_active")]
    public bool IsActive { get; set; } = true;

    [JsonPropertyName("deleted_at")]
    public DateTime? DeletedAt { get; set; }

    [JsonPropertyName("deleted_by")]
    public long? DeletedBy { get; set; }

    [JsonPropertyName("deletion_reason")]
    public string? DeletionReason { get; set; }

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

public class ProdProductRequisition
{
    private string _requisitionCode = string.Empty;
    private int _totalSelectedItems;
    private decimal _totalCost;
    private string _status = "Pending";
    private DateTime _createdAt = DateTime.UtcNow;

    [JsonPropertyName("requisition_no")]
    public long RequisitionNo { get; set; }

    [JsonPropertyName("requisition_code")]
    public string RequisitionCode
    {
        get => _requisitionCode;
        set => _requisitionCode = value;
    }

    [JsonPropertyName("requisition_number")]
    public string RequisitionNumber
    {
        get => _requisitionCode;
        set { if (string.IsNullOrEmpty(_requisitionCode)) _requisitionCode = value; }
    }

    [JsonPropertyName("pharmacy_no")]
    public long PharmacyNo { get; set; }

    [JsonPropertyName("branch_no")]
    public long BranchNo { get; set; }

    [JsonPropertyName("company_no")]
    public long? CompanyNo { get; set; }

    [JsonPropertyName("company_name")]
    public string? CompanyName { get; set; }

    [JsonPropertyName("supplier_no")]
    public long? SupplierNo { get; set; }

    [JsonPropertyName("supplier_name")]
    public string? SupplierName { get; set; }

    [JsonPropertyName("total_selected_items")]
    public int TotalSelectedItems
    {
        get => _totalSelectedItems;
        set => _totalSelectedItems = value;
    }

    [JsonPropertyName("total_items")]
    public int TotalItems
    {
        get => _totalSelectedItems;
        set { if (_totalSelectedItems == 0) _totalSelectedItems = value; }
    }

    [JsonPropertyName("total_cost")]
    public decimal TotalCost
    {
        get => _totalCost;
        set => _totalCost = value;
    }

    [JsonPropertyName("total_amount")]
    public decimal TotalAmount
    {
        get => _totalCost;
        set { if (_totalCost == 0) _totalCost = value; }
    }

    [JsonPropertyName("is_as_per_sale")]
    public bool IsAsPerSale { get; set; }

    [JsonPropertyName("sale_velocity_start_date")]
    public DateTime? SaleVelocityStartDate { get; set; }

    [JsonPropertyName("sale_velocity_end_date")]
    public DateTime? SaleVelocityEndDate { get; set; }

    [JsonPropertyName("status")]
    public string Status
    {
        get => _status;
        set => _status = value;
    }

    [JsonPropertyName("requisition_status")]
    public short RequisitionStatus
    {
        get => _status switch
        {
            "Approved" => 2,
            "Cancelled" => 3,
            _ => 1
        };
        set
        {
            if (string.IsNullOrEmpty(_status) || _status == "Pending")
            {
                _status = value switch
                {
                    2 => "Approved",
                    3 => "Cancelled",
                    _ => "Pending"
                };
            }
        }
    }

    [JsonPropertyName("created_by_user_no")]
    public long? CreatedByUserNo { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt
    {
        get => _createdAt;
        set => _createdAt = value;
    }

    [JsonPropertyName("requisition_date")]
    public DateTime RequisitionDate
    {
        get => _createdAt;
        set { if (_createdAt == default) _createdAt = value; }
    }

    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("items")]
    public List<ProdProductRequisitionItem>? Items { get; set; }
}

public class ProdProductRequisitionItem
{
    [JsonPropertyName("requisition_item_no")]
    public long RequisitionItemNo { get; set; }

    [JsonPropertyName("pharmacy_no")]
    public long PharmacyNo { get; set; }

    [JsonPropertyName("branch_no")]
    public long BranchNo { get; set; }

    [JsonPropertyName("requisition_no")]
    public long RequisitionNo { get; set; }

    [JsonPropertyName("product_no")]
    public long ProductNo { get; set; }

    [JsonPropertyName("product_name")]
    public string? ProductName { get; set; }

    [JsonPropertyName("box_qty")]
    public int BoxQty { get; set; }

    [JsonPropertyName("min_qty_pcs")]
    public int MinQtyPcs { get; set; }

    [JsonPropertyName("sale_qty_6mth_pcs")]
    public int SaleQty6MthPcs { get; set; }

    [JsonPropertyName("quantity_in_box")]
    public int QuantityInBox { get; set; } = 1;

    [JsonPropertyName("btp")]
    public decimal Btp { get; set; }

    [JsonPropertyName("vat")]
    public decimal Vat { get; set; }

    [JsonPropertyName("btp_vat")]
    public decimal BtpVat { get; set; }

    [JsonPropertyName("total_price")]
    public decimal TotalPrice { get; set; }

    [JsonPropertyName("is_selected")]
    public bool IsSelected { get; set; } = true;
}

public class LowStockProductDto
{
    [JsonPropertyName("product_no")]
    public long ProductNo { get; set; }

    [JsonPropertyName("product_name")]
    public string ProductName { get; set; } = string.Empty;

    [JsonPropertyName("brand_name")]
    public string BrandName { get; set; } = string.Empty;

    [JsonPropertyName("generic_name")]
    public string GenericName { get; set; } = string.Empty;

    [JsonPropertyName("company_no")]
    public long? CompanyNo { get; set; }

    [JsonPropertyName("company_name")]
    public string CompanyName { get; set; } = string.Empty;

    [JsonPropertyName("current_stock_pcs")]
    public int CurrentStockPcs { get; set; }

    [JsonPropertyName("min_stock_qty_pcs")]
    public int MinStockQtyPcs { get; set; }

    [JsonPropertyName("shortfall_pcs")]
    public int ShortfallPcs { get; set; }

    [JsonPropertyName("quantity_per_box")]
    public int QuantityPerBox { get; set; } = 1;

    [JsonPropertyName("suggested_box_qty")]
    public int SuggestedBoxQty { get; set; } = 1;

    [JsonPropertyName("purchase_price_per_piece")]
    public decimal PurchasePricePerPiece { get; set; }

    [JsonPropertyName("cost_per_box")]
    public decimal CostPerBox { get; set; }

    [JsonPropertyName("sale_price_per_piece")]
    public decimal SalePricePerPiece { get; set; }

    [JsonPropertyName("vat_percent")]
    public decimal VatPercent { get; set; }

    [JsonPropertyName("sale_qty_6mth_pcs")]
    public int SaleQty6MthPcs { get; set; }

    [JsonPropertyName("estimated_total_cost")]
    public decimal EstimatedTotalCost { get; set; }
}

public class AdjustStockRequest
{
    [JsonPropertyName("target_stock_pcs")]
    public int TargetStockPcs { get; set; }

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    [JsonPropertyName("remarks")]
    public string? Remarks { get; set; }
}

public class ProductImportItemDto
{
    [JsonPropertyName("product_name")]
    public string ProductName { get; set; } = string.Empty;

    [JsonPropertyName("strength")]
    public string? Strength { get; set; }

    [JsonPropertyName("volume")]
    public string? Volume { get; set; }

    [JsonPropertyName("company_name")]
    public string? CompanyName { get; set; }

    [JsonPropertyName("generic_name")]
    public string? GenericName { get; set; }

    [JsonPropertyName("medicine_form")]
    public string? MedicineForm { get; set; }

    [JsonPropertyName("medicine_type")]
    public short? MedicineType { get; set; }

    [JsonPropertyName("unit_type_name")]
    public string? UnitTypeName { get; set; }

    [JsonPropertyName("unit_type")]
    public short? UnitType { get; set; }

    [JsonPropertyName("quantity_per_box")]
    public int QuantityPerBox { get; set; } = 1;

    [JsonPropertyName("cost_per_box")]
    public decimal CostPerBox { get; set; }

    [JsonPropertyName("sale_price_per_box")]
    public decimal SalePricePerBox { get; set; }

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

    [JsonPropertyName("rak_number")]
    public string? RakNumber { get; set; }

    [JsonPropertyName("min_stock_qty_pcs")]
    public int MinStockQtyPcs { get; set; } = 10;

    [JsonPropertyName("is_prescription_required")]
    public bool IsPrescriptionRequired { get; set; }

    [JsonPropertyName("is_narcotic")]
    public bool IsNarcotic { get; set; }

    [JsonPropertyName("dar_number")]
    public string? DarNumber { get; set; }

    [JsonPropertyName("drug_schedule")]
    public string? DrugSchedule { get; set; }

    [JsonPropertyName("opening_stock_pcs")]
    public int OpeningStockPcs { get; set; }
}

public class ProductImportRequest
{
    [JsonPropertyName("items")]
    public List<ProductImportItemDto> Items { get; set; } = new();
}

public class ProductImportResultDto
{
    [JsonPropertyName("total_rows")]
    public int TotalRows { get; set; }

    [JsonPropertyName("inserted")]
    public int Inserted { get; set; }

    [JsonPropertyName("updated")]
    public int Updated { get; set; }

    [JsonPropertyName("new_companies_created")]
    public int NewCompaniesCreated { get; set; }

    [JsonPropertyName("new_generics_created")]
    public int NewGenericsCreated { get; set; }

    [JsonPropertyName("errors")]
    public List<string> Errors { get; set; } = new();
}

public class StockReportSummaryDto
{
    [JsonPropertyName("total_products")]
    public int TotalProducts { get; set; }

    [JsonPropertyName("in_stock_items")]
    public int InStockItems { get; set; }

    [JsonPropertyName("low_stock_items")]
    public int LowStockItems { get; set; }

    [JsonPropertyName("out_of_stock_items")]
    public int OutOfStockItems { get; set; }

    [JsonPropertyName("total_stock_pcs")]
    public long TotalStockPcs { get; set; }

    [JsonPropertyName("total_cost_valuation")]
    public decimal TotalCostValuation { get; set; }

    [JsonPropertyName("total_sale_valuation")]
    public decimal TotalSaleValuation { get; set; }

    [JsonPropertyName("potential_profit")]
    public decimal PotentialProfit { get; set; }
}

public class StockReportItemDto
{
    [JsonPropertyName("product_no")]
    public long ProductNo { get; set; }

    [JsonPropertyName("product_name")]
    public string ProductName { get; set; } = string.Empty;

    [JsonPropertyName("strength")]
    public string? Strength { get; set; }

    [JsonPropertyName("company_name")]
    public string? CompanyName { get; set; }

    [JsonPropertyName("generic_name")]
    public string? GenericName { get; set; }

    [JsonPropertyName("quantity_per_box")]
    public int QuantityPerBox { get; set; } = 1;

    [JsonPropertyName("current_stock_pcs")]
    public int CurrentStockPcs { get; set; }

    [JsonPropertyName("current_stock_boxes")]
    public decimal CurrentStockBoxes { get; set; }

    [JsonPropertyName("min_stock_qty_pcs")]
    public int MinStockQtyPcs { get; set; }

    [JsonPropertyName("purchase_price_per_piece")]
    public decimal PurchasePricePerPiece { get; set; }

    [JsonPropertyName("sale_price_per_piece")]
    public decimal SalePricePerPiece { get; set; }

    [JsonPropertyName("cost_valuation")]
    public decimal CostValuation { get; set; }

    [JsonPropertyName("sale_valuation")]
    public decimal SaleValuation { get; set; }

    [JsonPropertyName("profit_margin_pct")]
    public decimal ProfitMarginPct { get; set; }

    [JsonPropertyName("stock_status")]
    public string StockStatus { get; set; } = "in_stock";

    [JsonPropertyName("rak_number")]
    public string? RakNumber { get; set; }
}

public class StockReportResultDto
{
    [JsonPropertyName("summary")]
    public StockReportSummaryDto Summary { get; set; } = new();

    [JsonPropertyName("items")]
    public List<StockReportItemDto> Items { get; set; } = new();

    [JsonPropertyName("total_count")]
    public int TotalCount { get; set; }
}



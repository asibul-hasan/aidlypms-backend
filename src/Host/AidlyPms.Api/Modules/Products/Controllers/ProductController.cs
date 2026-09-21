using AidlyPms.Api.Common;
using AidlyPms.Api.Common.Database;
using AidlyPms.Api.Infrastructure;
using AidlyPms.Api.Modules.Products.Models;
using Dapper;
using Microsoft.AspNetCore.Mvc;

namespace AidlyPms.Api.Modules.Products.Controllers;

[Route("api/v1/prod/products")]
public class ProductController : BaseController
{
    private readonly IDbConnectionFactory _db;
    private readonly IStockPostingService _stockPosting;

    public ProductController(IDbConnectionFactory db, IStockPostingService stockPosting)
    {
        _db = db;
        _stockPosting = stockPosting;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<object>>> GetProducts(
        [FromQuery] string? search = null,
        [FromQuery] long? companyNo = null,
        [FromQuery] long? genericNo = null,
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 50)
    {
        await using var conn = await _db.CreateOpenConnectionAsync();

        var offset = (pageIndex - 1) * pageSize;

        const string countSql = @"
            SELECT COUNT(*)
            FROM prod_products p
            WHERE p.pharmacy_no = @CurrentPharmacyNo AND p.branch_no = @CurrentBranchNo
              AND (@search IS NULL OR p.product_name ILIKE '%' || @search || '%' OR p.barcode_value ILIKE '%' || @search || '%')
              AND (@companyNo IS NULL OR p.company_no = @companyNo)
              AND (@genericNo IS NULL OR p.generic_no = @genericNo);";

        var total = await conn.ExecuteScalarAsync<int>(countSql, new
        {
            CurrentPharmacyNo,
            CurrentBranchNo,
            search,
            companyNo,
            genericNo
        });

        const string dataSql = @"
            SELECT 
                p.product_no, p.uuid, p.pharmacy_no, p.branch_no, p.company_no, p.generic_no,
                p.product_name, p.strength, p.volume, p.medicine_type, p.unit_type,
                p.quantity_per_box, p.cost_per_box, p.sale_price_per_box,
                p.avg_purchase_price_per_piece, p.purchase_price_per_piece, p.sale_price_per_piece,
                p.wholesale_price_per_box, p.wholesale_price_per_piece,
                p.country_of_origin, p.barcode_value, p.is_medicine,
                p.is_prescription_required, p.is_narcotic, p.min_stock_qty_pcs,
                p.rak_number, p.is_active, p.created_at, p.updated_at,
                c.name AS company_name,
                g.name AS generic_name,
                COALESCE((
                    SELECT SUM(b.total_quantity_pcs)
                    FROM prod_product_batches b
                    WHERE b.product_no = p.product_no AND b.pharmacy_no = p.pharmacy_no AND b.branch_no = p.branch_no
                ), 0) AS stock_qty
            FROM prod_products p
            LEFT JOIN prod_companies c ON p.company_no = c.company_no
            LEFT JOIN prod_generics g ON p.generic_no = g.generic_no
            WHERE p.pharmacy_no = @CurrentPharmacyNo AND p.branch_no = @CurrentBranchNo
              AND (@search IS NULL OR p.product_name ILIKE '%' || @search || '%' OR p.barcode_value ILIKE '%' || @search || '%')
              AND (@companyNo IS NULL OR p.company_no = @companyNo)
              AND (@genericNo IS NULL OR p.generic_no = @genericNo)
            ORDER BY p.product_name ASC
            OFFSET @offset LIMIT @pageSize;";

        var items = (await conn.QueryAsync<ProdProduct>(dataSql, new
        {
            CurrentPharmacyNo,
            CurrentBranchNo,
            search,
            companyNo,
            genericNo,
            offset,
            pageSize
        })).AsList();

        // If search is provided and pageSize wasn't explicitly paged, support direct array responses for autocomplete
        if (!string.IsNullOrEmpty(search) && pageSize == 50 && pageIndex == 1)
        {
            return OkResponse<object>(items);
        }

        var paged = new PagedResult<ProdProduct>(items, total, pageIndex, pageSize);
        return OkResponse<object>(paged);
    }

    [HttpGet("{id:long}")]
    public async Task<ActionResult<ApiResponse<ProdProduct>>> GetProductById(long id)
    {
        await using var conn = await _db.CreateOpenConnectionAsync();
        const string sql = @"
            SELECT 
                p.product_no, p.uuid, p.pharmacy_no, p.branch_no, p.company_no, p.generic_no,
                p.product_name, p.strength, p.volume, p.medicine_type, p.unit_type,
                p.quantity_per_box, p.cost_per_box, p.sale_price_per_box,
                p.avg_purchase_price_per_piece, p.purchase_price_per_piece, p.sale_price_per_piece,
                p.wholesale_price_per_box, p.wholesale_price_per_piece,
                p.country_of_origin, p.barcode_value, p.is_medicine,
                p.is_prescription_required, p.is_narcotic, p.min_stock_qty_pcs,
                p.rak_number, p.is_active, p.created_at, p.updated_at,
                c.name AS company_name,
                g.name AS generic_name,
                COALESCE((
                    SELECT SUM(b.total_quantity_pcs)
                    FROM prod_product_batches b
                    WHERE b.product_no = p.product_no AND b.pharmacy_no = p.pharmacy_no AND b.branch_no = p.branch_no
                ), 0) AS stock_qty
            FROM prod_products p
            LEFT JOIN prod_companies c ON p.company_no = c.company_no
            LEFT JOIN prod_generics g ON p.generic_no = g.generic_no
            WHERE p.product_no = @id AND p.pharmacy_no = @CurrentPharmacyNo AND p.branch_no = @CurrentBranchNo;";

        var product = await conn.QuerySingleOrDefaultAsync<ProdProduct>(sql, new
        {
            id,
            CurrentPharmacyNo,
            CurrentBranchNo
        });

        if (product == null)
        {
            return FailResponse<ProdProduct>("Product not found or access denied.", statusCode: 404);
        }

        return OkResponse(product);
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<ProdProduct>>> CreateProduct([FromBody] ProdProduct model)
    {
        if (string.IsNullOrWhiteSpace(model.ProductName))
        {
            return FailResponse<ProdProduct>("Product name is required.");
        }

        if (model.CompanyNo <= 0)
        {
            return FailResponse<ProdProduct>("Valid pharmaceutical company is required.");
        }

        var qtyPerBox = model.QuantityPerBox > 0 ? model.QuantityPerBox : 1;
        var purchasePerPiece = qtyPerBox > 0 && model.CostPerBox > 0 
            ? Math.Round(model.CostPerBox / qtyPerBox, 4) 
            : model.PurchasePricePerPiece;
        var salePerPiece = qtyPerBox > 0 && model.SalePricePerBox > 0 
            ? Math.Round(model.SalePricePerBox / qtyPerBox, 4) 
            : model.SalePricePerPiece;

        await using var conn = await _db.CreateOpenConnectionAsync();
        await using var tran = await conn.BeginTransactionAsync();

        try
        {
            var productNo = await conn.ExecuteScalarAsync<long>(
                "SELECT COALESCE(MAX(product_no), 0) + 1 FROM prod_products;", transaction: tran);

            var barcode = !string.IsNullOrWhiteSpace(model.BarcodeValue) 
                ? model.BarcodeValue 
                : $"894{CurrentBranchNo:D2}{productNo:D6}";

            const string insertSql = @"
                INSERT INTO prod_products (
                    product_no, uuid, pharmacy_no, branch_no, company_no, generic_no,
                    product_name, strength, volume, medicine_type, unit_type,
                    quantity_per_box, cost_per_box, sale_price_per_box,
                    avg_purchase_price_per_piece, purchase_price_per_piece, sale_price_per_piece,
                    wholesale_price_per_box, wholesale_price_per_piece,
                    country_of_origin, barcode_value, is_medicine,
                    is_prescription_required, is_narcotic, min_stock_qty_pcs,
                    rak_number, is_active, created_at, updated_at
                ) VALUES (
                    @productNo, gen_random_uuid(), @CurrentPharmacyNo, @CurrentBranchNo, @CompanyNo, @GenericNo,
                    @ProductName, @Strength, @Volume, @MedicineType, @UnitType,
                    @qtyPerBox, @CostPerBox, @SalePricePerBox,
                    @purchasePerPiece, @purchasePerPiece, @salePerPiece,
                    @WholesalePricePerBox, @WholesalePricePerPiece,
                    @CountryOfOrigin, @barcode, @IsMedicine,
                    @IsPrescriptionRequired, @IsNarcotic, @MinStockQtyPcs,
                    @RakNumber, @IsActive, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
                );";

            await conn.ExecuteAsync(insertSql, new
            {
                productNo,
                CurrentPharmacyNo,
                CurrentBranchNo,
                model.CompanyNo,
                model.GenericNo,
                model.ProductName,
                model.Strength,
                model.Volume,
                model.MedicineType,
                model.UnitType,
                qtyPerBox,
                model.CostPerBox,
                model.SalePricePerBox,
                purchasePerPiece,
                salePerPiece,
                model.WholesalePricePerBox,
                model.WholesalePricePerPiece,
                model.CountryOfOrigin,
                barcode,
                model.IsMedicine,
                model.IsPrescriptionRequired,
                model.IsNarcotic,
                model.MinStockQtyPcs,
                model.RakNumber,
                model.IsActive
            }, transaction: tran);

            // If initial stock was provided, create initial opening batch
            if (model.StockQty > 0)
            {
                var batchNo = await conn.ExecuteScalarAsync<long>(
                    "SELECT COALESCE(MAX(batch_no), 0) + 1 FROM prod_product_batches;", transaction: tran);

                var boxQty = qtyPerBox > 0 ? model.StockQty / qtyPerBox : 0;
                var expiry = DateTime.UtcNow.AddYears(2).Date;

                const string insertBatch = @"
                    INSERT INTO prod_product_batches (
                        batch_no, pharmacy_no, branch_no, product_no, batch_number,
                        expiry_date, box_quantity, quantity_in_box, total_quantity_pcs,
                        cost_per_box, sale_price_per_box, vat_percent, is_expired,
                        created_at, updated_at
                    ) VALUES (
                        @batchNo, @CurrentPharmacyNo, @CurrentBranchNo, @productNo, 'INIT-BATCH',
                        @expiry, @boxQty, @qtyPerBox, @StockQty,
                        @CostPerBox, @SalePricePerBox, 0, false,
                        NOW(), NOW()
                    );";

                await conn.ExecuteAsync(insertBatch, new
                {
                    batchNo,
                    CurrentPharmacyNo,
                    CurrentBranchNo,
                    productNo,
                    expiry,
                    boxQty,
                    qtyPerBox,
                    model.StockQty,
                    model.CostPerBox,
                    model.SalePricePerBox
                }, transaction: tran);

                // Stock movement (type 2: Opening Stock)
                await _stockPosting.PostMovementAsync(conn, tran, new StockMovementEntry
                {
                    PharmacyNo = CurrentPharmacyNo,
                    BranchNo = CurrentBranchNo,
                    ProductNo = productNo,
                    BatchNo = batchNo,
                    MovementType = 2, // Opening Stock
                    ReferenceDocType = "OPENING_STOCK",
                    ReferenceDocNo = $"OP-{CurrentBranchNo:D2}-{DateTime.UtcNow.Year}-{productNo:D4}",
                    ReferenceDocId = productNo,
                    QtyPcs = model.StockQty,
                    UnitCost = purchasePerPiece,
                    Remarks = "Initial opening stock registration"
                });
            }

            await tran.CommitAsync();

            model.ProductNo = productNo;
            model.BarcodeValue = barcode;
            model.PharmacyNo = CurrentPharmacyNo;
            model.BranchNo = CurrentBranchNo;
            return OkResponse(model, "Product registered successfully.");
        }
        catch (Exception ex)
        {
            await tran.RollbackAsync();
            return FailResponse<ProdProduct>($"Failed to register product: {ex.Message}");
        }
    }

    [HttpPut("{id:long}")]
    public async Task<ActionResult<ApiResponse<ProdProduct>>> UpdateProduct(long id, [FromBody] ProdProduct model)
    {
        if (string.IsNullOrWhiteSpace(model.ProductName))
        {
            return FailResponse<ProdProduct>("Product name is required.");
        }

        var qtyPerBox = model.QuantityPerBox > 0 ? model.QuantityPerBox : 1;
        var purchasePerPiece = qtyPerBox > 0 && model.CostPerBox > 0 
            ? Math.Round(model.CostPerBox / qtyPerBox, 4) 
            : model.PurchasePricePerPiece;
        var salePerPiece = qtyPerBox > 0 && model.SalePricePerBox > 0 
            ? Math.Round(model.SalePricePerBox / qtyPerBox, 4) 
            : model.SalePricePerPiece;

        await using var conn = await _db.CreateOpenConnectionAsync();

        const string sql = @"
            UPDATE prod_products
            SET company_no = @CompanyNo,
                generic_no = @GenericNo,
                product_name = @ProductName,
                strength = @Strength,
                volume = @Volume,
                medicine_type = @MedicineType,
                unit_type = @UnitType,
                quantity_per_box = @qtyPerBox,
                cost_per_box = @CostPerBox,
                sale_price_per_box = @SalePricePerBox,
                purchase_price_per_piece = @purchasePerPiece,
                sale_price_per_piece = @salePerPiece,
                wholesale_price_per_box = @WholesalePricePerBox,
                wholesale_price_per_piece = @WholesalePricePerPiece,
                country_of_origin = @CountryOfOrigin,
                barcode_value = @BarcodeValue,
                is_medicine = @IsMedicine,
                is_prescription_required = @IsPrescriptionRequired,
                is_narcotic = @IsNarcotic,
                min_stock_qty_pcs = @MinStockQtyPcs,
                rak_number = @RakNumber,
                is_active = @IsActive,
                updated_at = CURRENT_TIMESTAMP
            WHERE product_no = @id AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;";

        var rows = await conn.ExecuteAsync(sql, new
        {
            id,
            CurrentPharmacyNo,
            CurrentBranchNo,
            model.CompanyNo,
            model.GenericNo,
            model.ProductName,
            model.Strength,
            model.Volume,
            model.MedicineType,
            model.UnitType,
            qtyPerBox,
            model.CostPerBox,
            model.SalePricePerBox,
            purchasePerPiece,
            salePerPiece,
            model.WholesalePricePerBox,
            model.WholesalePricePerPiece,
            model.CountryOfOrigin,
            model.BarcodeValue,
            model.IsMedicine,
            model.IsPrescriptionRequired,
            model.IsNarcotic,
            model.MinStockQtyPcs,
            model.RakNumber,
            model.IsActive
        });

        if (rows == 0)
        {
            return FailResponse<ProdProduct>("Product not found or access denied.", statusCode: 404);
        }

        return OkResponse(model, "Product updated successfully.");
    }
}

using System.Text.Json.Serialization;
using AidlyPms.Api.Common;
using AidlyPms.Api.Common.Database;
using AidlyPms.Api.Modules.Products.Models;
using Dapper;
using Microsoft.AspNetCore.Mvc;

namespace AidlyPms.Api.Modules.Products.Controllers;

public class RequisitionPayload
{
    [JsonPropertyName("company_no")]
    public long? CompanyNo { get; set; }

    [JsonPropertyName("supplier_no")]
    public long? SupplierNo { get; set; }

    [JsonPropertyName("is_as_per_sale")]
    public bool? IsAsPerSale { get; set; }

    [JsonPropertyName("sale_velocity_start_date")]
    public DateTime? SaleVelocityStartDate { get; set; }

    [JsonPropertyName("sale_velocity_end_date")]
    public DateTime? SaleVelocityEndDate { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("items")]
    public List<RequisitionItemPayload> Items { get; set; } = new();
}

public class RequisitionItemPayload
{
    [JsonPropertyName("product_no")]
    public long ProductNo { get; set; }

    [JsonPropertyName("box_qty")]
    public int? BoxQty { get; set; }

    [JsonPropertyName("requested_box_qty")]
    public int? RequestedBoxQty { get; set; }

    [JsonPropertyName("quantity_in_box")]
    public int? QuantityInBox { get; set; }

    [JsonPropertyName("min_qty_pcs")]
    public int? MinQtyPcs { get; set; }

    [JsonPropertyName("requested_pcs_qty")]
    public int? RequestedPcsQty { get; set; }

    [JsonPropertyName("sale_qty_6mth_pcs")]
    public int? SaleQty6MthPcs { get; set; }

    [JsonPropertyName("btp")]
    public decimal? Btp { get; set; }

    [JsonPropertyName("estimated_unit_cost")]
    public decimal? EstimatedUnitCost { get; set; }

    [JsonPropertyName("vat")]
    public decimal? Vat { get; set; }

    [JsonPropertyName("vat_percent")]
    public decimal? VatPercent { get; set; }

    [JsonPropertyName("btp_vat")]
    public decimal? BtpVat { get; set; }

    [JsonPropertyName("total_price")]
    public decimal? TotalPrice { get; set; }

    [JsonPropertyName("is_selected")]
    public bool? IsSelected { get; set; }

    [JsonPropertyName("remarks")]
    public string? Remarks { get; set; }

    [JsonPropertyName("note")]
    public string? Note { get; set; }
}

public class UpdateRequisitionStatusRequest
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;
}

[Route("api/v1/prod/requisitions")]
public class RequisitionController : BaseController
{
    private readonly IDbConnectionFactory _db;

    public RequisitionController(IDbConnectionFactory db)
    {
        _db = db;
    }

    [HttpGet("low-stock-suggestions")]
    public async Task<ActionResult<ApiResponse<List<LowStockProductDto>>>> GetLowStockSuggestions(
        [FromQuery] long? companyNo = null,
        [FromQuery] bool fallbackLowest = false)
    {
        await using var conn = await _db.CreateOpenConnectionAsync();

        var sql = @"
            SELECT 
                p.product_no,
                p.product_name,
                p.product_name AS brand_name,
                COALESCE(g.name, '') AS generic_name,
                p.company_no,
                COALESCE(c.name, '') AS company_name,
                COALESCE(SUM(b.total_quantity_pcs), 0)::int AS current_stock_pcs,
                COALESCE(p.min_stock_qty_pcs, 0)::int AS min_stock_qty_pcs,
                GREATEST(0, COALESCE(p.min_stock_qty_pcs, 0) - COALESCE(SUM(b.total_quantity_pcs), 0))::int AS shortfall_pcs,
                GREATEST(1, COALESCE(p.quantity_per_box, 1))::int AS quantity_per_box,
                CASE 
                    WHEN GREATEST(0, COALESCE(p.min_stock_qty_pcs, 0) - COALESCE(SUM(b.total_quantity_pcs), 0)) > 0 
                    THEN CEIL(GREATEST(0, COALESCE(p.min_stock_qty_pcs, 0) - COALESCE(SUM(b.total_quantity_pcs), 0))::numeric / GREATEST(1, COALESCE(p.quantity_per_box, 1)))::int
                    ELSE 1 
                END AS suggested_box_qty,
                COALESCE(p.purchase_price_per_piece, 0) AS purchase_price_per_piece,
                COALESCE(p.cost_per_box, 0) AS cost_per_box,
                COALESCE(p.sale_price_per_piece, 0) AS sale_price_per_piece,
                COALESCE(MAX(b.vat_percent), 0) AS vat_percent,
                COALESCE((
                    SELECT SUM(sii.sale_qty)
                    FROM sale_invoice_items sii
                    JOIN sale_invoices si ON sii.sale_invoice_no = si.sale_invoice_no
                    WHERE sii.product_no = p.product_no
                      AND si.pharmacy_no = @CurrentPharmacyNo
                      AND si.branch_no = @CurrentBranchNo
                      AND si.sale_timestamp >= CURRENT_DATE - INTERVAL '180 days'
                      AND si.document_status = 2
                ), 0)::int AS sale_qty_6mth_pcs
            FROM prod_products p
            LEFT JOIN prod_product_batches b 
                ON p.product_no = b.product_no 
                AND b.pharmacy_no = @CurrentPharmacyNo 
                AND b.branch_no = @CurrentBranchNo
                AND b.total_quantity_pcs > 0
                AND b.expiry_date >= CURRENT_DATE
            LEFT JOIN prod_generics g ON p.generic_no = g.generic_no
            LEFT JOIN prod_companies c ON p.company_no = c.company_no
            WHERE p.pharmacy_no = @CurrentPharmacyNo 
              AND p.branch_no = @CurrentBranchNo
              AND p.is_active = TRUE
              AND (@companyNo IS NULL OR p.company_no = @companyNo)
            GROUP BY p.product_no, p.product_name, g.name, p.company_no, c.name, p.min_stock_qty_pcs, p.quantity_per_box, p.purchase_price_per_piece, p.cost_per_box, p.sale_price_per_piece
            HAVING COALESCE(SUM(b.total_quantity_pcs), 0) <= p.min_stock_qty_pcs
            ORDER BY shortfall_pcs DESC, current_stock_pcs ASC, p.product_name ASC;";

        var items = (await conn.QueryAsync<LowStockProductDto>(sql, new
        {
            CurrentPharmacyNo,
            CurrentBranchNo,
            companyNo
        })).AsList();

        if (items.Count == 0 && fallbackLowest)
        {
            var fallbackSql = @"
                SELECT 
                    p.product_no,
                    p.product_name,
                    p.product_name AS brand_name,
                    COALESCE(g.name, '') AS generic_name,
                    p.company_no,
                    COALESCE(c.name, '') AS company_name,
                    COALESCE(SUM(b.total_quantity_pcs), 0)::int AS current_stock_pcs,
                    COALESCE(p.min_stock_qty_pcs, 0)::int AS min_stock_qty_pcs,
                    GREATEST(0, COALESCE(p.min_stock_qty_pcs, 0) - COALESCE(SUM(b.total_quantity_pcs), 0))::int AS shortfall_pcs,
                    GREATEST(1, COALESCE(p.quantity_per_box, 1))::int AS quantity_per_box,
                    1 AS suggested_box_qty,
                    COALESCE(p.purchase_price_per_piece, 0) AS purchase_price_per_piece,
                    COALESCE(p.cost_per_box, 0) AS cost_per_box,
                    COALESCE(p.sale_price_per_piece, 0) AS sale_price_per_piece,
                    COALESCE(MAX(b.vat_percent), 0) AS vat_percent,
                    COALESCE((
                        SELECT SUM(sii.sale_qty)
                        FROM sale_invoice_items sii
                        JOIN sale_invoices si ON sii.sale_invoice_no = si.sale_invoice_no
                        WHERE sii.product_no = p.product_no
                          AND si.pharmacy_no = @CurrentPharmacyNo
                          AND si.branch_no = @CurrentBranchNo
                          AND si.sale_timestamp >= CURRENT_DATE - INTERVAL '180 days'
                          AND si.document_status = 2
                    ), 0)::int AS sale_qty_6mth_pcs
                FROM prod_products p
                LEFT JOIN prod_product_batches b 
                    ON p.product_no = b.product_no 
                    AND b.pharmacy_no = @CurrentPharmacyNo 
                    AND b.branch_no = @CurrentBranchNo
                    AND b.total_quantity_pcs > 0
                    AND b.expiry_date >= CURRENT_DATE
                LEFT JOIN prod_generics g ON p.generic_no = g.generic_no
                LEFT JOIN prod_companies c ON p.company_no = c.company_no
                WHERE p.pharmacy_no = @CurrentPharmacyNo 
                  AND p.branch_no = @CurrentBranchNo
                  AND p.is_active = TRUE
                  AND (@companyNo IS NULL OR p.company_no = @companyNo)
                GROUP BY p.product_no, p.product_name, g.name, p.company_no, c.name, p.min_stock_qty_pcs, p.quantity_per_box, p.purchase_price_per_piece, p.cost_per_box, p.sale_price_per_piece
                ORDER BY (COALESCE(SUM(b.total_quantity_pcs), 0) - p.min_stock_qty_pcs) ASC, p.product_name ASC
                LIMIT 5;";

            items = (await conn.QueryAsync<LowStockProductDto>(fallbackSql, new
            {
                CurrentPharmacyNo,
                CurrentBranchNo,
                companyNo
            })).AsList();
        }

        foreach (var item in items)
        {
            var unitCost = item.PurchasePricePerPiece > 0 ? item.PurchasePricePerPiece : (item.CostPerBox > 0 && item.QuantityPerBox > 0 ? item.CostPerBox / item.QuantityPerBox : 0);
            var btpWithVat = unitCost * (1 + (item.VatPercent / 100m));
            item.EstimatedTotalCost = Math.Round(item.SuggestedBoxQty * item.QuantityPerBox * btpWithVat, 2);
        }

        return OkResponse(items);
    }

    [HttpGet]
    [HttpGet("records")]
    public async Task<ActionResult<ApiResponse<PagedResult<ProdProductRequisition>>>> GetRequisitions(
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] long? supplierNo = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null)
    {
        await using var conn = await _db.CreateOpenConnectionAsync();
        var offset = (pageIndex - 1) * pageSize;

        var pms = new DynamicParameters();
        pms.Add("CurrentPharmacyNo", CurrentPharmacyNo);
        pms.Add("CurrentBranchNo", CurrentBranchNo);

        var whereClause = "WHERE r.pharmacy_no = @CurrentPharmacyNo AND r.branch_no = @CurrentBranchNo";

        if (!string.IsNullOrWhiteSpace(search))
        {
            whereClause += " AND r.requisition_code ILIKE @Search";
            pms.Add("Search", $"%{search.Trim()}%");
        }

        if (supplierNo.HasValue && supplierNo.Value > 0)
        {
            whereClause += " AND r.supplier_no = @SupplierNo";
            pms.Add("SupplierNo", supplierNo.Value);
        }

        if (fromDate.HasValue)
        {
            whereClause += " AND r.created_at >= @FromDate";
            pms.Add("FromDate", fromDate.Value.ToUniversalTime());
        }

        if (toDate.HasValue)
        {
            whereClause += " AND r.created_at <= @ToDate";
            pms.Add("ToDate", toDate.Value.ToUniversalTime());
        }

        var countSql = $"SELECT COUNT(*) FROM prod_product_requisitions r {whereClause};";
        var total = await conn.ExecuteScalarAsync<int>(countSql, pms);

        pms.Add("offset", offset);
        pms.Add("pageSize", pageSize);

        var dataSql = $@"
            SELECT 
                r.requisition_no,
                r.requisition_code,
                r.pharmacy_no,
                r.branch_no,
                r.company_no,
                c.name AS company_name,
                r.supplier_no,
                s.name AS supplier_name,
                r.total_selected_items,
                r.total_cost,
                r.is_as_per_sale,
                r.sale_velocity_start_date,
                r.sale_velocity_end_date,
                r.status,
                r.created_by_user_no,
                r.created_at,
                r.updated_at
            FROM prod_product_requisitions r
            LEFT JOIN supp_suppliers s ON r.supplier_no = s.supplier_no
            LEFT JOIN prod_companies c ON r.company_no = c.company_no
            {whereClause}
            ORDER BY r.created_at DESC
            OFFSET @offset LIMIT @pageSize;";

        var items = (await conn.QueryAsync<ProdProductRequisition>(dataSql, pms)).AsList();

        return PagedResponse(new PagedResult<ProdProductRequisition>(items, total, pageIndex, pageSize));
    }

    [HttpGet("{requisitionNo:long}")]
    public async Task<ActionResult<ApiResponse<ProdProductRequisition>>> GetRequisitionById(long requisitionNo)
    {
        await using var conn = await _db.CreateOpenConnectionAsync();

        const string headerSql = @"
            SELECT 
                r.requisition_no,
                r.requisition_code,
                r.pharmacy_no,
                r.branch_no,
                r.company_no,
                c.name AS company_name,
                r.supplier_no,
                s.name AS supplier_name,
                r.total_selected_items,
                r.total_cost,
                r.is_as_per_sale,
                r.sale_velocity_start_date,
                r.sale_velocity_end_date,
                r.status,
                r.created_by_user_no,
                r.created_at,
                r.updated_at
            FROM prod_product_requisitions r
            LEFT JOIN supp_suppliers s ON r.supplier_no = s.supplier_no
            LEFT JOIN prod_companies c ON r.company_no = c.company_no
            WHERE r.requisition_no = @requisitionNo
              AND r.pharmacy_no = @CurrentPharmacyNo
              AND r.branch_no = @CurrentBranchNo;";

        var header = await conn.QuerySingleOrDefaultAsync<ProdProductRequisition>(headerSql, new
        {
            requisitionNo,
            CurrentPharmacyNo,
            CurrentBranchNo
        });

        if (header == null)
        {
            return NotFoundResponse<ProdProductRequisition>("Requisition not found.");
        }

        const string itemsSql = @"
            SELECT 
                i.requisition_item_no,
                i.pharmacy_no,
                i.branch_no,
                i.requisition_no,
                i.product_no,
                p.product_name,
                i.box_qty,
                i.min_qty_pcs,
                i.sale_qty_6mth_pcs,
                i.quantity_in_box,
                i.btp,
                i.vat,
                i.btp_vat,
                i.total_price,
                i.is_selected
            FROM prod_product_requisition_items i
            LEFT JOIN prod_products p ON i.product_no = p.product_no
            WHERE i.requisition_no = @requisitionNo
              AND i.pharmacy_no = @CurrentPharmacyNo
              AND i.branch_no = @CurrentBranchNo
            ORDER BY i.requisition_item_no ASC;";

        header.Items = (await conn.QueryAsync<ProdProductRequisitionItem>(itemsSql, new
        {
            requisitionNo,
            CurrentPharmacyNo,
            CurrentBranchNo
        })).AsList();

        return OkResponse(header);
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<ProdProductRequisition>>> CreateRequisition([FromBody] RequisitionPayload payload)
    {
        if (payload.Items == null || payload.Items.Count == 0)
        {
            return FailResponse<ProdProductRequisition>("Requisition must contain at least one item.");
        }

        await using var conn = await _db.CreateOpenConnectionAsync();
        await using var tran = await conn.BeginTransactionAsync();

        try
        {
            var reqNo = await conn.ExecuteScalarAsync<long>(
                "SELECT COALESCE(MAX(requisition_no), 0) + 1 FROM prod_product_requisitions;", transaction: tran);

            var reqCode = $"REQ-{CurrentBranchNo:D2}-{DateTime.UtcNow.Year}-{reqNo:D5}";

            var maxItemNo = await conn.ExecuteScalarAsync<long>(
                "SELECT COALESCE(MAX(requisition_item_no), 0) FROM prod_product_requisition_items;", transaction: tran);

            var processedItems = new List<ProdProductRequisitionItem>();
            decimal grandTotalCost = 0;
            int totalSelectedItems = 0;

            foreach (var item in payload.Items)
            {
                maxItemNo++;
                var boxQty = item.RequestedBoxQty ?? item.BoxQty ?? 1;
                var qtyInBox = item.QuantityInBox.HasValue && item.QuantityInBox.Value > 0 ? item.QuantityInBox.Value : 1;
                var minQtyPcs = item.MinQtyPcs ?? item.RequestedPcsQty ?? (boxQty * qtyInBox);
                var saleQty = item.SaleQty6MthPcs ?? 0;
                var btp = item.Btp ?? item.EstimatedUnitCost ?? 0m;
                var vat = item.Vat ?? item.VatPercent ?? 0m;
                var btpVat = item.BtpVat ?? (btp + (btp * vat / 100m));

                var pcs = item.RequestedPcsQty.HasValue && item.RequestedPcsQty.Value > 0
                    ? item.RequestedPcsQty.Value
                    : (boxQty * qtyInBox);
                var totalPrice = item.TotalPrice ?? (pcs * (btpVat > 0 ? btpVat : btp));
                var isSelected = item.IsSelected ?? true;

                if (isSelected)
                {
                    totalSelectedItems++;
                    grandTotalCost += totalPrice;
                }

                processedItems.Add(new ProdProductRequisitionItem
                {
                    RequisitionItemNo = maxItemNo,
                    PharmacyNo = CurrentPharmacyNo,
                    BranchNo = CurrentBranchNo,
                    RequisitionNo = reqNo,
                    ProductNo = item.ProductNo,
                    BoxQty = boxQty,
                    MinQtyPcs = minQtyPcs,
                    SaleQty6MthPcs = saleQty,
                    QuantityInBox = qtyInBox,
                    Btp = btp,
                    Vat = vat,
                    BtpVat = btpVat,
                    TotalPrice = totalPrice,
                    IsSelected = isSelected
                });
            }

            var isAsPerSale = payload.IsAsPerSale ?? false;
            var status = string.IsNullOrWhiteSpace(payload.Status) ? "Pending" : payload.Status;
            long? companyNo = payload.CompanyNo.HasValue && payload.CompanyNo.Value > 0 ? payload.CompanyNo.Value : null;
            long? supplierNo = payload.SupplierNo.HasValue && payload.SupplierNo.Value > 0 ? payload.SupplierNo.Value : null;

            const string insertHeader = @"
                INSERT INTO prod_product_requisitions (
                    requisition_no, requisition_code, pharmacy_no, branch_no, company_no, supplier_no,
                    total_selected_items, total_cost, is_as_per_sale, sale_velocity_start_date,
                    sale_velocity_end_date, status, created_by_user_no, created_at, updated_at
                ) VALUES (
                    @reqNo, @reqCode, @CurrentPharmacyNo, @CurrentBranchNo, @companyNo, @supplierNo,
                    @totalSelectedItems, @grandTotalCost, @isAsPerSale, @SaleVelocityStartDate,
                    @SaleVelocityEndDate, @status, @CurrentUserNo, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
                );";

            await conn.ExecuteAsync(insertHeader, new
            {
                reqNo,
                reqCode,
                CurrentPharmacyNo,
                CurrentBranchNo,
                companyNo,
                supplierNo,
                totalSelectedItems,
                grandTotalCost,
                isAsPerSale,
                payload.SaleVelocityStartDate,
                payload.SaleVelocityEndDate,
                status,
                CurrentUserNo
            }, transaction: tran);

            const string insertItemSql = @"
                INSERT INTO prod_product_requisition_items (
                    requisition_item_no, pharmacy_no, branch_no, requisition_no, product_no,
                    box_qty, min_qty_pcs, sale_qty_6mth_pcs, quantity_in_box,
                    btp, vat, btp_vat, total_price, is_selected
                ) VALUES (
                    @RequisitionItemNo, @PharmacyNo, @BranchNo, @RequisitionNo, @ProductNo,
                    @BoxQty, @MinQtyPcs, @SaleQty6MthPcs, @QuantityInBox,
                    @Btp, @Vat, @BtpVat, @TotalPrice, @IsSelected
                );";

            foreach (var pi in processedItems)
            {
                await conn.ExecuteAsync(insertItemSql, pi, transaction: tran);
            }

            await tran.CommitAsync();

            var now = DateTime.UtcNow;
            var result = new ProdProductRequisition
            {
                RequisitionNo = reqNo,
                RequisitionCode = reqCode,
                PharmacyNo = CurrentPharmacyNo,
                BranchNo = CurrentBranchNo,
                CompanyNo = companyNo,
                SupplierNo = supplierNo,
                TotalSelectedItems = totalSelectedItems,
                TotalCost = grandTotalCost,
                IsAsPerSale = isAsPerSale,
                SaleVelocityStartDate = payload.SaleVelocityStartDate,
                SaleVelocityEndDate = payload.SaleVelocityEndDate,
                Status = status,
                CreatedByUserNo = CurrentUserNo,
                CreatedAt = now,
                UpdatedAt = now,
                Items = processedItems
            };

            return OkResponse(result, "Requisition created successfully.");
        }
        catch (Exception ex)
        {
            await tran.RollbackAsync();
            return FailResponse<ProdProductRequisition>($"Failed to create requisition: {ex.Message}");
        }
    }

    [HttpPut("{requisitionNo:long}/status")]
    public async Task<ActionResult<ApiResponse<bool>>> UpdateStatus(
        long requisitionNo,
        [FromBody] UpdateRequisitionStatusRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Status))
        {
            return FailResponse<bool>("Status is required.");
        }

        await using var conn = await _db.CreateOpenConnectionAsync();
        const string sql = @"
            UPDATE prod_product_requisitions
            SET status = @Status, updated_at = CURRENT_TIMESTAMP
            WHERE requisition_no = @requisitionNo
              AND pharmacy_no = @CurrentPharmacyNo
              AND branch_no = @CurrentBranchNo;";

        var rows = await conn.ExecuteAsync(sql, new
        {
            requisitionNo,
            CurrentPharmacyNo,
            CurrentBranchNo,
            request.Status
        });

        if (rows == 0)
        {
            return NotFoundResponse<bool>("Requisition not found.");
        }

        return OkResponse(true, "Requisition status updated successfully.");
    }
}


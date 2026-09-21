using AidlyPms.Api.Common;
using AidlyPms.Api.Common.Database;
using AidlyPms.Api.Modules.Products.Models;
using Dapper;
using Microsoft.AspNetCore.Mvc;

namespace AidlyPms.Api.Modules.Products.Controllers;

[Route("api/v1/inv/batches")]
public class BatchController : BaseController
{
    private readonly IDbConnectionFactory _db;

    public BatchController(IDbConnectionFactory db)
    {
        _db = db;
    }

    [HttpGet("active")]
    public async Task<ActionResult<ApiResponse<List<ProdProductBatch>>>> GetActiveBatches([FromQuery] long? productNo = null)
    {
        await using var conn = await _db.CreateOpenConnectionAsync();
        const string sql = @"
            SELECT 
                b.batch_no, b.pharmacy_no, b.branch_no, b.product_no, b.batch_number,
                b.expiry_date, b.box_quantity, b.quantity_in_box, b.total_quantity_pcs,
                b.cost_per_box, b.sale_price_per_box, b.vat_percent, b.is_expired,
                p.product_name
            FROM prod_product_batches b
            JOIN prod_products p ON b.product_no = p.product_no
            WHERE b.pharmacy_no = @CurrentPharmacyNo AND b.branch_no = @CurrentBranchNo
              AND b.total_quantity_pcs > 0
              AND b.expiry_date >= CURRENT_DATE
              AND (@productNo IS NULL OR b.product_no = @productNo)
            ORDER BY b.expiry_date ASC;";

        var list = (await conn.QueryAsync<ProdProductBatch>(sql, new { CurrentPharmacyNo, CurrentBranchNo, productNo })).AsList();
        return OkResponse(list);
    }

    [HttpGet("expired")]
    public async Task<ActionResult<ApiResponse<List<ProdProductBatch>>>> GetExpiredBatches()
    {
        await using var conn = await _db.CreateOpenConnectionAsync();
        const string sql = @"
            SELECT 
                b.batch_no, b.pharmacy_no, b.branch_no, b.product_no, b.batch_number,
                b.expiry_date, b.box_quantity, b.quantity_in_box, b.total_quantity_pcs,
                b.cost_per_box, b.sale_price_per_box, b.vat_percent, b.is_expired,
                p.product_name
            FROM prod_product_batches b
            JOIN prod_products p ON b.product_no = p.product_no
            WHERE b.pharmacy_no = @CurrentPharmacyNo AND b.branch_no = @CurrentBranchNo
              AND b.expiry_date < CURRENT_DATE
            ORDER BY b.expiry_date ASC;";

        var list = (await conn.QueryAsync<ProdProductBatch>(sql, new { CurrentPharmacyNo, CurrentBranchNo })).AsList();
        return OkResponse(list);
    }

    [HttpGet("near-expiry")]
    public async Task<ActionResult<ApiResponse<List<ProdProductBatch>>>> GetNearExpiryBatches([FromQuery] int daysThreshold = 90)
    {
        await using var conn = await _db.CreateOpenConnectionAsync();
        const string sql = @"
            SELECT 
                b.batch_no, b.pharmacy_no, b.branch_no, b.product_no, b.batch_number,
                b.expiry_date, b.box_quantity, b.quantity_in_box, b.total_quantity_pcs,
                b.cost_per_box, b.sale_price_per_box, b.vat_percent, b.is_expired,
                p.product_name
            FROM prod_product_batches b
            JOIN prod_products p ON b.product_no = p.product_no
            WHERE b.pharmacy_no = @CurrentPharmacyNo AND b.branch_no = @CurrentBranchNo
              AND b.expiry_date >= CURRENT_DATE
              AND b.expiry_date <= (CURRENT_DATE + @daysThreshold * INTERVAL '1 day')
            ORDER BY b.expiry_date ASC;";

        var list = (await conn.QueryAsync<ProdProductBatch>(sql, new { CurrentPharmacyNo, CurrentBranchNo, daysThreshold })).AsList();
        return OkResponse(list);
    }

    [HttpGet("product/{productNo:long}")]
    public async Task<ActionResult<ApiResponse<List<ProdProductBatch>>>> GetProductBatches(long productNo)
    {
        await using var conn = await _db.CreateOpenConnectionAsync();
        const string sql = @"
            SELECT 
                b.batch_no, b.pharmacy_no, b.branch_no, b.product_no, b.batch_number,
                b.expiry_date, b.box_quantity, b.quantity_in_box, b.total_quantity_pcs,
                b.cost_per_box, b.sale_price_per_box, b.vat_percent, b.is_expired,
                p.product_name
            FROM prod_product_batches b
            JOIN prod_products p ON b.product_no = p.product_no
            WHERE b.product_no = @productNo AND b.pharmacy_no = @CurrentPharmacyNo AND b.branch_no = @CurrentBranchNo
            ORDER BY b.expiry_date ASC;";

        var list = (await conn.QueryAsync<ProdProductBatch>(sql, new { productNo, CurrentPharmacyNo, CurrentBranchNo })).AsList();
        return OkResponse(list);
    }
}

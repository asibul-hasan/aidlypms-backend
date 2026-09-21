using System.Text.Json.Serialization;
using AidlyPms.Api.Common;
using AidlyPms.Api.Common.Database;
using Dapper;
using Microsoft.AspNetCore.Mvc;

namespace AidlyPms.Api.Modules.Products.Controllers;

public class RequisitionPayload
{
    [JsonPropertyName("supplier_no")]
    public long? SupplierNo { get; set; }

    [JsonPropertyName("items")]
    public List<RequisitionItemPayload> Items { get; set; } = new();
}

public class RequisitionItemPayload
{
    [JsonPropertyName("product_no")]
    public long ProductNo { get; set; }

    [JsonPropertyName("requested_box_qty")]
    public int RequestedBoxQty { get; set; }

    [JsonPropertyName("requested_pcs_qty")]
    public int RequestedPcsQty { get; set; }

    [JsonPropertyName("estimated_unit_cost")]
    public decimal EstimatedUnitCost { get; set; }

    [JsonPropertyName("note")]
    public string? Note { get; set; }
}

public class ProdRequisitionHeader
{
    [JsonPropertyName("requisition_no")]
    public long RequisitionNo { get; set; }

    [JsonPropertyName("pharmacy_no")]
    public long PharmacyNo { get; set; }

    [JsonPropertyName("branch_no")]
    public long BranchNo { get; set; }

    [JsonPropertyName("requisition_number")]
    public string RequisitionNumber { get; set; } = string.Empty;

    [JsonPropertyName("supplier_no")]
    public long? SupplierNo { get; set; }

    [JsonPropertyName("supplier_name")]
    public string? SupplierName { get; set; }

    [JsonPropertyName("total_items")]
    public int TotalItems { get; set; }

    [JsonPropertyName("total_amount")]
    public decimal TotalAmount { get; set; }

    [JsonPropertyName("requisition_status")]
    public short RequisitionStatus { get; set; }

    [JsonPropertyName("requisition_date")]
    public DateTime RequisitionDate { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }
}

[Route("api/v1/prod/requisitions")]
public class RequisitionController : BaseController
{
    private readonly IDbConnectionFactory _db;

    public RequisitionController(IDbConnectionFactory db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<ProdRequisitionHeader>>>> GetRequisitions(
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 20)
    {
        await using var conn = await _db.CreateOpenConnectionAsync();
        var offset = (pageIndex - 1) * pageSize;

        const string countSql = @"
            SELECT COUNT(*)
            FROM prod_product_requisitions
            WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;";

        var total = await conn.ExecuteScalarAsync<int>(countSql, new { CurrentPharmacyNo, CurrentBranchNo });

        const string dataSql = @"
            SELECT 
                r.requisition_no, r.pharmacy_no, r.branch_no, r.requisition_number,
                r.supplier_no, s.name AS supplier_name,
                r.total_items, r.total_amount, r.requisition_status,
                r.requisition_date, r.created_at
            FROM prod_product_requisitions r
            LEFT JOIN supp_suppliers s ON r.supplier_no = s.supplier_no
            WHERE r.pharmacy_no = @CurrentPharmacyNo AND r.branch_no = @CurrentBranchNo
            ORDER BY r.created_at DESC
            OFFSET @offset LIMIT @pageSize;";

        var items = (await conn.QueryAsync<ProdRequisitionHeader>(dataSql, new
        {
            CurrentPharmacyNo,
            CurrentBranchNo,
            offset,
            pageSize
        })).AsList();

        return PagedResponse(new PagedResult<ProdRequisitionHeader>(items, total, pageIndex, pageSize));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<ProdRequisitionHeader>>> CreateRequisition([FromBody] RequisitionPayload payload)
    {
        if (payload.Items == null || payload.Items.Count == 0)
        {
            return FailResponse<ProdRequisitionHeader>("Requisition must contain at least one item.");
        }

        await using var conn = await _db.CreateOpenConnectionAsync();
        await using var tran = await conn.BeginTransactionAsync();

        try
        {
            var reqNo = await conn.ExecuteScalarAsync<long>(
                "SELECT COALESCE(MAX(requisition_no), 0) + 1 FROM prod_product_requisitions;", transaction: tran);

            var reqNumber = $"REQ-{CurrentBranchNo:D2}-{DateTime.UtcNow.Year}-{reqNo:D5}";
            var totalItems = payload.Items.Count;
            var totalAmount = payload.Items.Sum(i => i.RequestedPcsQty * i.EstimatedUnitCost);

            const string insertHeader = @"
                INSERT INTO prod_product_requisitions (
                    requisition_no, pharmacy_no, branch_no, requisition_number, supplier_no,
                    total_items, total_amount, requisition_status, created_by_user_no,
                    requisition_date, created_at
                ) VALUES (
                    @reqNo, @CurrentPharmacyNo, @CurrentBranchNo, @reqNumber, @SupplierNo,
                    @totalItems, @totalAmount, 1, @CurrentUserNo,
                    CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
                );";

            await conn.ExecuteAsync(insertHeader, new
            {
                reqNo,
                CurrentPharmacyNo,
                CurrentBranchNo,
                reqNumber,
                payload.SupplierNo,
                totalItems,
                totalAmount,
                CurrentUserNo
            }, transaction: tran);

            foreach (var item in payload.Items)
            {
                var itemNo = await conn.ExecuteScalarAsync<long>(
                    "SELECT COALESCE(MAX(requisition_item_no), 0) + 1 FROM prod_product_requisition_items;", transaction: tran);

                var estTotal = item.RequestedPcsQty * item.EstimatedUnitCost;

                const string insertItem = @"
                    INSERT INTO prod_product_requisition_items (
                        requisition_item_no, requisition_no, product_no,
                        requested_box_qty, requested_pcs_qty, estimated_unit_cost, estimated_total_cost, note
                    ) VALUES (
                        @itemNo, @reqNo, @ProductNo,
                        @RequestedBoxQty, @RequestedPcsQty, @EstimatedUnitCost, @estTotal, @Note
                    );";

                await conn.ExecuteAsync(insertItem, new
                {
                    itemNo,
                    reqNo,
                    item.ProductNo,
                    item.RequestedBoxQty,
                    item.RequestedPcsQty,
                    item.EstimatedUnitCost,
                    estTotal,
                    item.Note
                }, transaction: tran);
            }

            await tran.CommitAsync();

            var result = new ProdRequisitionHeader
            {
                RequisitionNo = reqNo,
                PharmacyNo = CurrentPharmacyNo,
                BranchNo = CurrentBranchNo,
                RequisitionNumber = reqNumber,
                SupplierNo = payload.SupplierNo,
                TotalItems = totalItems,
                TotalAmount = totalAmount,
                RequisitionStatus = 1,
                RequisitionDate = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };

            return OkResponse(result, "Requisition created successfully.");
        }
        catch (Exception ex)
        {
            await tran.RollbackAsync();
            return FailResponse<ProdRequisitionHeader>($"Failed to create requisition: {ex.Message}");
        }
    }
}

using System.Text.Json.Serialization;
using AidlyPms.Api.Common;
using AidlyPms.Api.Common.Database;
using Dapper;
using Microsoft.AspNetCore.Mvc;

namespace AidlyPms.Api.Modules.Customers.Controllers;

public class CustDueRecordDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("customer")]
    public string Customer { get; set; } = string.Empty;

    [JsonPropertyName("phone")]
    public string Phone { get; set; } = string.Empty;

    [JsonPropertyName("inv")]
    public string Inv { get; set; } = string.Empty;

    [JsonPropertyName("saleDate")]
    public string SaleDate { get; set; } = string.Empty;

    [JsonPropertyName("dueDate")]
    public string DueDate { get; set; } = string.Empty;

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }
}

public class CollectDueDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("customer")]
    public string? Customer { get; set; }

    [JsonPropertyName("phone")]
    public string? Phone { get; set; }

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("method")]
    public string Method { get; set; } = "Cash";

    [JsonPropertyName("account_no")]
    public long? AccountNo { get; set; }

    [JsonPropertyName("note")]
    public string? Note { get; set; }
}

[Route("api/v1/cust/dues")]
public class DueController : BaseController
{
    private readonly IDbConnectionFactory _db;

    public DueController(IDbConnectionFactory db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<CustDueRecordDto>>>> GetDueRecords(
        [FromQuery] string? search = null,
        [FromQuery] string? fromDate = null,
        [FromQuery] string? toDate = null)
    {
        await using var conn = await _db.CreateOpenConnectionAsync();

        const string sql = @"
            SELECT 
                s.sale_no::text AS id,
                COALESCE(c.name, s.customer_name, 'Walking Customer') AS customer,
                COALESCE(c.phone, s.customer_mobile, '') AS phone,
                s.invoice_no AS inv,
                TO_CHAR(s.sale_date, 'YYYY-MM-DD') AS saledate,
                TO_CHAR(COALESCE(s.due_date, s.sale_date + INTERVAL '7 days'), 'YYYY-MM-DD') AS duedate,
                s.due_amount AS amount
            FROM sales_pos_sales s
            LEFT JOIN cust_customers c ON s.customer_no = c.customer_no
            WHERE s.pharmacy_no = @CurrentPharmacyNo AND s.branch_no = @CurrentBranchNo
              AND s.due_amount > 0
              AND (@search IS NULL OR c.name ILIKE '%' || @search || '%' OR s.invoice_no ILIKE '%' || @search || '%')
            ORDER BY s.sale_date DESC;";

        var list = (await conn.QueryAsync<CustDueRecordDto>(sql, new { CurrentPharmacyNo, CurrentBranchNo, search })).AsList();
        return OkResponse(list);
    }

    [HttpPost("collect")]
    public async Task<ActionResult<ApiResponse<object>>> CollectDue([FromBody] CollectDueDto req)
    {
        if (req.Amount <= 0)
        {
            return FailResponse<object>("Valid collection amount is required.");
        }

        await using var conn = await _db.CreateOpenConnectionAsync();
        await using var tran = await conn.BeginTransactionAsync();

        try
        {
            if (long.TryParse(req.Id, out var saleNo) && saleNo > 0)
            {
                await conn.ExecuteAsync(@"
                    UPDATE sales_pos_sales 
                    SET due_amount = GREATEST(0, due_amount - @Amount),
                        customer_will_pay = customer_will_pay + @Amount,
                        updated_at = CURRENT_TIMESTAMP
                    WHERE sale_no = @saleNo AND pharmacy_no = @CurrentPharmacyNo;",
                    new { saleNo, CurrentPharmacyNo, req.Amount }, transaction: tran);
            }

            if (!string.IsNullOrWhiteSpace(req.Phone))
            {
                await conn.ExecuteAsync(@"
                    UPDATE cust_customers
                    SET current_due = GREATEST(0, current_due - @Amount),
                        updated_at = CURRENT_TIMESTAMP
                    WHERE phone = @Phone AND pharmacy_no = @CurrentPharmacyNo;",
                    new { req.Phone, CurrentPharmacyNo, req.Amount }, transaction: tran);
            }

            await tran.CommitAsync();

            var receiptNo = $"RCT-{CurrentBranchNo:D2}-{DateTime.UtcNow.Year}-{DateTime.UtcNow.Ticks % 100000:D5}";
            return OkResponse<object>(new
            {
                receipt = receiptNo,
                collected = req.Amount,
                customer = req.Customer,
                date = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
            }, "Due collection recorded successfully.");
        }
        catch (Exception ex)
        {
            await tran.RollbackAsync();
            return FailResponse<object>($"Failed to record due collection: {ex.Message}");
        }
    }

    [HttpGet("history")]
    public async Task<ActionResult<ApiResponse<List<object>>>> GetDueHistory()
    {
        await using var conn = await _db.CreateOpenConnectionAsync();
        const string sql = @"
            SELECT 
                s.sale_no::text AS id,
                COALESCE(c.name, s.customer_name, 'Walking Customer') AS customer,
                COALESCE(c.phone, s.customer_mobile, '') AS phone,
                s.invoice_no AS receipt,
                s.payment_method AS method,
                s.net_total AS amount,
                'POS Settlement' AS note
            FROM sales_pos_sales s
            LEFT JOIN cust_customers c ON s.customer_no = c.customer_no
            WHERE s.pharmacy_no = @CurrentPharmacyNo AND s.branch_no = @CurrentBranchNo
              AND s.due_amount = 0
            ORDER BY s.sale_date DESC
            LIMIT 50;";

        var list = (await conn.QueryAsync<object>(sql, new { CurrentPharmacyNo, CurrentBranchNo })).AsList();
        return OkResponse(list);
    }
}

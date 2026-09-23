using AidlyPms.Api.Common;
using AidlyPms.Api.Common.Database;
using AidlyPms.Api.Modules.Suppliers.Models;
using Dapper;
using Microsoft.AspNetCore.Mvc;

namespace AidlyPms.Api.Modules.Suppliers.Controllers;

[Route("api/v1/supp/suppliers")]
[Route("api/v1/pur/suppliers")]
public class SupplierController : BaseController
{
    private readonly IDbConnectionFactory _db;

    public SupplierController(IDbConnectionFactory db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<SuppSupplier>>>> GetSuppliers(
        [FromQuery] QueryFilter filter,
        [FromQuery] string? search,
        [FromQuery] string? phone,
        [FromQuery] bool? dueOnly)
    {
        using var conn = _db.CreateConnection();
        var sql = @"
            SELECT * FROM supp_suppliers
            WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo";

        var countSql = @"
            SELECT COUNT(1) FROM supp_suppliers
            WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo";

        var p = new DynamicParameters();
        p.Add("CurrentPharmacyNo", CurrentPharmacyNo);
        p.Add("CurrentBranchNo", CurrentBranchNo);

        if (!string.IsNullOrWhiteSpace(search))
        {
            sql += " AND (name ILIKE @Search OR phone ILIKE @Search)";
            countSql += " AND (name ILIKE @Search OR phone ILIKE @Search)";
            p.Add("Search", $"%{search.Trim()}%");
        }

        if (!string.IsNullOrWhiteSpace(phone))
        {
            sql += " AND phone ILIKE @Phone";
            countSql += " AND phone ILIKE @Phone";
            p.Add("Phone", $"%{phone.Trim()}%");
        }

        if (dueOnly == true)
        {
            sql += " AND current_due_balance > 0";
            countSql += " AND current_due_balance > 0";
        }

        sql += " ORDER BY name ASC LIMIT @PageSize OFFSET @Offset;";
        p.Add("PageSize", filter.PageSize);
        p.Add("Offset", (filter.PageIndex - 1) * filter.PageSize);

        var total = await conn.ExecuteScalarAsync<int>(countSql, p);
        var items = (await conn.QueryAsync<SuppSupplier>(sql, p)).ToList();

        var result = new PagedResult<SuppSupplier>(items, total, filter.PageIndex, filter.PageSize);
        return PagedResponse(result);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<SuppSupplier>>> GetSupplierById(long id)
    {
        using var conn = _db.CreateConnection();
        var supplier = await conn.QuerySingleOrDefaultAsync<SuppSupplier>(@"
            SELECT * FROM supp_suppliers
            WHERE supplier_no = @Id AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
            new { Id = id, CurrentPharmacyNo, CurrentBranchNo });

        if (supplier == null)
            return FailResponse<SuppSupplier>("Supplier not found.", statusCode: 404);

        return OkResponse(supplier);
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<SuppSupplier>>> CreateSupplier([FromBody] SuppSupplier model)
    {
        if (string.IsNullOrWhiteSpace(model.Name))
            return FailResponse<SuppSupplier>("Supplier name is required.");

        if (string.IsNullOrWhiteSpace(model.Phone))
            return FailResponse<SuppSupplier>("Supplier phone is required.");

        using var conn = _db.CreateConnection();
        conn.Open();

        // Check unique phone per pharmacy & branch
        var exists = await conn.ExecuteScalarAsync<bool>(@"
            SELECT EXISTS(
                SELECT 1 FROM supp_suppliers
                WHERE phone = @Phone AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
            );",
            new { model.Phone, CurrentPharmacyNo, CurrentBranchNo });

        if (exists)
            return FailResponse<SuppSupplier>($"Supplier with phone '{model.Phone}' already exists in this branch.");

        var nextNo = await conn.ExecuteScalarAsync<long>(
            "SELECT COALESCE(MAX(supplier_no), 0) + 1 FROM supp_suppliers;");

        model.SupplierNo = nextNo;
        model.PharmacyNo = CurrentPharmacyNo;
        model.BranchNo = CurrentBranchNo;
        model.CurrentDueBalance = model.CurrentDueBalance >= 0 ? model.CurrentDueBalance : 0m;
        model.IsActive = true;
        model.CreatedAt = DateTime.UtcNow;
        model.UpdatedAt = DateTime.UtcNow;

        await conn.ExecuteAsync(@"
            INSERT INTO supp_suppliers (
                supplier_no, pharmacy_no, branch_no, name, phone, note,
                current_due_balance, is_active, created_at, updated_at
            ) VALUES (
                @SupplierNo, @PharmacyNo, @BranchNo, @Name, @Phone, @Note,
                @CurrentDueBalance, @IsActive, @CreatedAt, @UpdatedAt
            );", model);

        return OkResponse(model, $"Supplier '{model.Name}' created successfully.");
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<ApiResponse<SuppSupplier>>> UpdateSupplier(long id, [FromBody] SuppSupplier model)
    {
        using var conn = _db.CreateConnection();
        var existing = await conn.QuerySingleOrDefaultAsync<SuppSupplier>(@"
            SELECT * FROM supp_suppliers
            WHERE supplier_no = @Id AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
            new { Id = id, CurrentPharmacyNo, CurrentBranchNo });

        if (existing == null)
            return FailResponse<SuppSupplier>("Supplier not found.", statusCode: 404);

        if (!string.IsNullOrWhiteSpace(model.Phone) && model.Phone != existing.Phone)
        {
            var phoneExists = await conn.ExecuteScalarAsync<bool>(@"
                SELECT EXISTS(
                    SELECT 1 FROM supp_suppliers
                    WHERE phone = @Phone AND supplier_no <> @Id
                      AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
                );",
                new { model.Phone, Id = id, CurrentPharmacyNo, CurrentBranchNo });

            if (phoneExists)
                return FailResponse<SuppSupplier>($"Phone number '{model.Phone}' is already in use by another supplier.");
        }

        existing.Name = !string.IsNullOrWhiteSpace(model.Name) ? model.Name : existing.Name;
        existing.Phone = !string.IsNullOrWhiteSpace(model.Phone) ? model.Phone : existing.Phone;
        existing.Note = model.Note ?? existing.Note;
        existing.IsActive = model.IsActive;
        existing.UpdatedAt = DateTime.UtcNow;

        await conn.ExecuteAsync(@"
            UPDATE supp_suppliers
            SET name = @Name, phone = @Phone, note = @Note,
                is_active = @IsActive, updated_at = @UpdatedAt
            WHERE supplier_no = @SupplierNo AND pharmacy_no = @PharmacyNo AND branch_no = @BranchNo;",
            existing);

        return OkResponse(existing, $"Supplier '{existing.Name}' updated successfully.");
    }
}

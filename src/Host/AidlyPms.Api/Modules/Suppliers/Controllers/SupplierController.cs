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

    [HttpGet("{id:long}/ledger")]
    public async Task<ActionResult<ApiResponse<object>>> GetSupplierLedger(long id)
    {
        using var conn = _db.CreateConnection();

        var supplier = await conn.QuerySingleOrDefaultAsync<SuppSupplier>(@"
            SELECT supplier_no, name, phone, address, current_due_balance, note
            FROM supp_suppliers
            WHERE supplier_no = @id AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo;",
            new { id, CurrentPharmacyNo, CurrentBranchNo });

        if (supplier == null)
        {
            return FailResponse<object>("Supplier not found.", statusCode: 404);
        }

        const string sql = @"
            SELECT 
                p.inventory_number AS doc_no,
                'Purchase Invoice' AS doc_type,
                p.invoice_date AS doc_date,
                p.total_net_amount AS debit,
                (p.total_net_amount - p.current_due_amount) AS credit,
                CONCAT('Purchase Invoice #', p.supplier_invoice_no) AS note
            FROM pur_purchase_invoices p
            WHERE p.supplier_no = @id AND p.pharmacy_no = @CurrentPharmacyNo AND p.branch_no = @CurrentBranchNo

            UNION ALL

            SELECT 
                sp.payment_number AS doc_no,
                'Supplier Payment' AS doc_type,
                sp.payment_date AS doc_date,
                0 AS debit,
                sp.amount AS credit,
                sp.note
            FROM pur_supplier_payments sp
            WHERE sp.supplier_no = @id AND sp.pharmacy_no = @CurrentPharmacyNo AND sp.branch_no = @CurrentBranchNo

            ORDER BY doc_date ASC;";

        var rows = (await conn.QueryAsync(sql, new { id, CurrentPharmacyNo, CurrentBranchNo })).ToList();

        decimal running = 0;
        var ledger = new List<object>();

        foreach (var r in rows)
        {
            IDictionary<string, object> dict = (IDictionary<string, object>)r;
            var debit = Convert.ToDecimal(dict["debit"] ?? 0);
            var credit = Convert.ToDecimal(dict["credit"] ?? 0);
            running += (debit - credit);

            ledger.Add(new
            {
                doc_no = dict["doc_no"]?.ToString() ?? "",
                doc_type = dict["doc_type"]?.ToString() ?? "",
                doc_date = dict["doc_date"],
                debit,
                credit,
                balance = running,
                note = dict["note"]?.ToString() ?? ""
            });
        }

        return OkResponse<object>(new
        {
            supplier = new
            {
                supplier_no = supplier.SupplierNo,
                name = supplier.Name,
                phone = supplier.Phone,
                current_due = supplier.CurrentDueBalance,
                note = supplier.Note
            },
            ledger
        });
    }
}


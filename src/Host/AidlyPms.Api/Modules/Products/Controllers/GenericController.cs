using AidlyPms.Api.Common;
using AidlyPms.Api.Common.Database;
using AidlyPms.Api.Modules.Products.Models;
using Dapper;
using Microsoft.AspNetCore.Mvc;

namespace AidlyPms.Api.Modules.Products.Controllers;

[Route("api/v1/prod/generics")]
public class GenericController : BaseController
{
    private readonly IDbConnectionFactory _db;

    public GenericController(IDbConnectionFactory db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<ProdGeneric>>>> GetGenerics([FromQuery] string? search = null)
    {
        await using var conn = await _db.CreateOpenConnectionAsync();
        var sql = @"
            SELECT generic_no, pharmacy_no, branch_no, name, is_active, created_at, updated_at
            FROM prod_generics
            WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
              AND (@search IS NULL OR name ILIKE '%' || @search || '%')
            ORDER BY name ASC;";

        var list = (await conn.QueryAsync<ProdGeneric>(sql, new { CurrentPharmacyNo, CurrentBranchNo, search })).AsList();
        return OkResponse(list);
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<ProdGeneric>>> CreateGeneric([FromBody] ProdGeneric model)
    {
        if (string.IsNullOrWhiteSpace(model.Name))
        {
            return FailResponse<ProdGeneric>("Generic formulation name is required.");
        }

        await using var conn = await _db.CreateOpenConnectionAsync();
        
        var genericNo = await conn.ExecuteScalarAsync<long>(
            "SELECT COALESCE(MAX(generic_no), 0) + 1 FROM prod_generics;");

        const string sql = @"
            INSERT INTO prod_generics (generic_no, pharmacy_no, branch_no, name, is_active, created_at, updated_at)
            VALUES (@genericNo, @CurrentPharmacyNo, @CurrentBranchNo, @Name, @IsActive, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
            RETURNING generic_no, pharmacy_no, branch_no, name, is_active, created_at, updated_at;";

        var created = await conn.QuerySingleAsync<ProdGeneric>(sql, new
        {
            genericNo,
            CurrentPharmacyNo,
            CurrentBranchNo,
            model.Name,
            model.IsActive
        });

        return OkResponse(created, "Generic formulation created successfully.");
    }

    [HttpPut("{id:long}")]
    public async Task<ActionResult<ApiResponse<ProdGeneric>>> UpdateGeneric(long id, [FromBody] ProdGeneric model)
    {
        if (string.IsNullOrWhiteSpace(model.Name))
        {
            return FailResponse<ProdGeneric>("Generic formulation name is required.");
        }

        await using var conn = await _db.CreateOpenConnectionAsync();
        const string sql = @"
            UPDATE prod_generics
            SET name = @Name, is_active = @IsActive, updated_at = CURRENT_TIMESTAMP
            WHERE generic_no = @id AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
            RETURNING generic_no, pharmacy_no, branch_no, name, is_active, created_at, updated_at;";

        var updated = await conn.QuerySingleOrDefaultAsync<ProdGeneric>(sql, new
        {
            id,
            CurrentPharmacyNo,
            CurrentBranchNo,
            model.Name,
            model.IsActive
        });

        if (updated == null)
        {
            return FailResponse<ProdGeneric>("Generic formulation not found or access denied.", statusCode: 404);
        }

        return OkResponse(updated, "Generic formulation updated successfully.");
    }
}

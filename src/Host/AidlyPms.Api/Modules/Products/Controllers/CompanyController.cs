using AidlyPms.Api.Common;
using AidlyPms.Api.Common.Database;
using AidlyPms.Api.Modules.Products.Models;
using Dapper;
using Microsoft.AspNetCore.Mvc;

namespace AidlyPms.Api.Modules.Products.Controllers;

[Route("api/v1/prod/companies")]
public class CompanyController : BaseController
{
    private readonly IDbConnectionFactory _db;

    public CompanyController(IDbConnectionFactory db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<ProdCompany>>>> GetCompanies([FromQuery] string? search = null)
    {
        await using var conn = await _db.CreateOpenConnectionAsync();
        var sql = @"
            SELECT company_no, pharmacy_no, branch_no, name, is_active, created_at, updated_at
            FROM prod_companies
            WHERE pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
              AND (@search IS NULL OR name ILIKE '%' || @search || '%')
            ORDER BY name ASC;";

        var list = (await conn.QueryAsync<ProdCompany>(sql, new { CurrentPharmacyNo, CurrentBranchNo, search })).AsList();
        return OkResponse(list);
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<ProdCompany>>> CreateCompany([FromBody] ProdCompany model)
    {
        if (string.IsNullOrWhiteSpace(model.Name))
        {
            return FailResponse<ProdCompany>("Company name is required.");
        }

        await using var conn = await _db.CreateOpenConnectionAsync();
        
        var companyNo = await conn.ExecuteScalarAsync<long>(
            "SELECT COALESCE(MAX(company_no), 0) + 1 FROM prod_companies;");

        const string sql = @"
            INSERT INTO prod_companies (company_no, pharmacy_no, branch_no, name, is_active, created_at, updated_at)
            VALUES (@companyNo, @CurrentPharmacyNo, @CurrentBranchNo, @Name, @IsActive, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
            RETURNING company_no, pharmacy_no, branch_no, name, is_active, created_at, updated_at;";

        var created = await conn.QuerySingleAsync<ProdCompany>(sql, new
        {
            companyNo,
            CurrentPharmacyNo,
            CurrentBranchNo,
            model.Name,
            model.IsActive
        });

        return OkResponse(created, "Pharmaceutical company created successfully.");
    }

    [HttpPut("{id:long}")]
    public async Task<ActionResult<ApiResponse<ProdCompany>>> UpdateCompany(long id, [FromBody] ProdCompany model)
    {
        if (string.IsNullOrWhiteSpace(model.Name))
        {
            return FailResponse<ProdCompany>("Company name is required.");
        }

        await using var conn = await _db.CreateOpenConnectionAsync();
        const string sql = @"
            UPDATE prod_companies
            SET name = @Name, is_active = @IsActive, updated_at = CURRENT_TIMESTAMP
            WHERE company_no = @id AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo
            RETURNING company_no, pharmacy_no, branch_no, name, is_active, created_at, updated_at;";

        var updated = await conn.QuerySingleOrDefaultAsync<ProdCompany>(sql, new
        {
            id,
            CurrentPharmacyNo,
            CurrentBranchNo,
            model.Name,
            model.IsActive
        });

        if (updated == null)
        {
            return FailResponse<ProdCompany>("Company not found or access denied.", statusCode: 404);
        }

        return OkResponse(updated, "Pharmaceutical company updated successfully.");
    }
}

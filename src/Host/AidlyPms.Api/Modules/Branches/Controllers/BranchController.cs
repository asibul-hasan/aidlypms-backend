using AidlyPms.Api.Common;
using AidlyPms.Api.Common.Database;
using AidlyPms.Api.Modules.Branches.Models;
using Dapper;
using Microsoft.AspNetCore.Mvc;

namespace AidlyPms.Api.Modules.Branches.Controllers;

[Route("api/v1/sys")]
public class BranchController : BaseController
{
    private readonly IDbConnectionFactory _db;

    public BranchController(IDbConnectionFactory db)
    {
        _db = db;
    }

    [HttpGet("pharmacies")]
    public async Task<ActionResult<ApiResponse<List<SysPharmacy>>>> GetPharmacies()
    {
        using var conn = _db.CreateConnection();
        var list = (await conn.QueryAsync<SysPharmacy>(@"
            SELECT pharmacy_no, uuid, name, contact_phone, contact_email, address, district, is_active
            FROM sys_pharmacies
            ORDER BY pharmacy_no ASC;")).ToList();

        return OkResponse(list);
    }

    [HttpGet("branches")]
    public async Task<ActionResult<ApiResponse<List<SysBranch>>>> GetBranches()
    {
        using var conn = _db.CreateConnection();
        var list = (await conn.QueryAsync<SysBranch>(@"
            SELECT branch_no, pharmacy_no, uuid, name, drug_license_no, contact_phone,
                   contact_email, address, district, postal_code, is_active
            FROM sys_branches
            WHERE pharmacy_no = @CurrentPharmacyNo
            ORDER BY branch_no ASC;",
            new { CurrentPharmacyNo })).ToList();

        return OkResponse(list);
    }
}

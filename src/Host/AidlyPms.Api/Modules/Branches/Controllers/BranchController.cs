using AidlyPms.Api.Common.Security;
using AidlyPms.Api.Common;
using AidlyPms.Api.Common.Database;
using AidlyPms.Api.Modules.Auth.Models;
using AidlyPms.Api.Modules.Branches.Models;
using Dapper;
using Microsoft.AspNetCore.Mvc;

namespace AidlyPms.Api.Modules.Branches.Controllers;

[Route("api/v1/sys")]
[RequireMenu("PHAR_2201")]
public class BranchController : BaseController
{
    private readonly IDbConnectionFactory _db;
    private readonly IConfiguration _config;

    public BranchController(IDbConnectionFactory db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    [HttpGet("pharmacies")]
    [AuthenticatedOnly]
    public async Task<ActionResult<ApiResponse<List<SysPharmacy>>>> GetPharmacies()
    {
        using var conn = _db.CreateConnection();
        var list = (await conn.QueryAsync<SysPharmacy>(@"
            SELECT pharmacy_no, uuid, name, contact_phone, contact_email, address, district, is_active
            FROM sys_pharmacies p
            WHERE @Owner OR p.pharmacy_no = @CurrentPharmacyNo
            ORDER BY pharmacy_no ASC;", new { Owner = IsPlatformOwner, CurrentPharmacyNo, UserNo = CurrentUserNo })).ToList();

        return OkResponse(list);
    }

    [HttpGet("branches")]
    [AuthenticatedOnly]
    public async Task<ActionResult<ApiResponse<List<SysBranch>>>> GetBranches()
    {
        using var conn = _db.CreateConnection();
        var sql = IsAdmin
            ? @"SELECT branch_no, pharmacy_no, uuid, name, drug_license_no, contact_phone,
                       contact_email, address, district, postal_code, is_active
                FROM sys_branches
                ORDER BY pharmacy_no ASC, branch_no ASC;"
            : @"SELECT branch_no, pharmacy_no, uuid, name, drug_license_no, contact_phone,
                       contact_email, address, district, postal_code, is_active
                FROM sys_branches
                WHERE pharmacy_no = @CurrentPharmacyNo AND is_active = TRUE
                  AND (branch_no = @HomeBranchNo OR EXISTS (SELECT 1 FROM sys_user_pharmacy_access a
                       WHERE a.user_no = @UserNo AND a.pharmacy_no = sys_branches.pharmacy_no AND a.branch_no = sys_branches.branch_no))
                ORDER BY branch_no ASC;";

        var list = (await conn.QueryAsync<SysBranch>(sql, new { CurrentPharmacyNo, UserNo = CurrentUserNo, HomeBranchNo = await conn.ExecuteScalarAsync<long>("SELECT branch_no FROM sys_users WHERE user_no = @UserNo;", new { UserNo = CurrentUserNo }) })).ToList();
        return OkResponse(list);
    }

    [HttpPost("switch-branch")]
    [AuthenticatedOnly]
    public async Task<ActionResult<ApiResponse<SwitchBranchResponse>>> SwitchBranch([FromBody] SwitchBranchRequest request)
    {
        if (CurrentUserNo <= 0)
        {
            return FailResponse<SwitchBranchResponse>("Unauthorized", statusCode: 401);
        }

        if (request == null || request.BranchNo <= 0)
        {
            return FailResponse<SwitchBranchResponse>("A valid branch_no is required.", statusCode: 400);
        }

        using var conn = _db.CreateConnection();

        // Fetch target branch info
        var branch = await conn.QuerySingleOrDefaultAsync<BranchDto>(@"
            SELECT b.branch_no, b.pharmacy_no, b.name,
                   COALESCE(b.contact_phone, p.contact_phone) as contact_phone,
                   COALESCE(b.address, p.address) as address,
                   COALESCE(b.district, p.district) as district,
                   p.drug_license_no, p.contact_email
            FROM sys_branches b
            JOIN sys_pharmacies p ON b.pharmacy_no = p.pharmacy_no
            WHERE b.branch_no = @BranchNo AND (@Owner OR (b.is_active = TRUE AND p.is_active = TRUE));",
            new { BranchNo = request.BranchNo, Owner = IsPlatformOwner });

        if (branch == null) return NotFoundResponse<SwitchBranchResponse>("Branch not found.");
        var sessions = HttpContext.RequestServices.GetRequiredService<IAuthSessions>();
        var targetPharmacyNo = branch.PharmacyNo;
        (string Token, DateTime ExpiresAt) issued;
        try { issued = await sessions.CreateAsync(CurrentUserNo, targetPharmacyNo, request.BranchNo, HttpContext.RequestAborted, verifiedUserUpdatedAt: Access!.UserUpdatedAt); }
        catch (UnauthorizedAccessException) { return FailResponse<SwitchBranchResponse>("Branch access denied.", statusCode: 403); }
        await sessions.RevokeAsync(User, HttpContext.RequestAborted);
        BrowserSession.Write(HttpContext, issued.Token, issued.ExpiresAt);

        return OkResponse(new SwitchBranchResponse
        {
            Token = "session",
            BranchNo = request.BranchNo,
            PharmacyNo = targetPharmacyNo,
            Branch = branch
        }, $"Switched to branch #{request.BranchNo} successfully.");
    }

    [HttpPost("pharmacies")]
    [AuthenticatedOnly]
    public async Task<ActionResult<ApiResponse<object>>> CreatePharmacy([FromBody] SysPharmacy model)
    {
        if (!IsPlatformOwner) return FailResponse<object>("Only the platform owner can provision a company.", statusCode: 403);
        if (string.IsNullOrWhiteSpace(model.Name) || string.IsNullOrWhiteSpace(model.ContactPhone) ||
            string.IsNullOrWhiteSpace(model.Address) || string.IsNullOrWhiteSpace(model.District))
            return FailResponse<object>("Company name, contact phone, address and district are required.");
        await using var conn = await _db.CreateOpenConnectionAsync(HttpContext.RequestAborted);
        await using var transaction = await conn.BeginTransactionAsync(HttpContext.RequestAborted);
        model.PharmacyNo = await conn.ExecuteScalarAsync<long>("SELECT nextval('seq_sys_pharmacy_no');", transaction: transaction);
        model.Uuid = Guid.NewGuid();
        await conn.ExecuteAsync(@"
            INSERT INTO sys_pharmacies (pharmacy_no, uuid, name, trade_license_no, tin_no, bin_no, drug_license_no,
                contact_phone, contact_email, address, area, district, postal_code, is_active, created_at, updated_at)
            VALUES (@PharmacyNo, @Uuid, @Name, NULL, NULL, NULL, NULL, @ContactPhone, @ContactEmail, @Address, NULL, @District, NULL, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);", model, transaction);
        var branchNo = await conn.ExecuteScalarAsync<long>("SELECT nextval('seq_sys_branch_no');", transaction: transaction);
        await conn.ExecuteAsync(@"
            INSERT INTO sys_branches (branch_no, pharmacy_no, uuid, name, drug_license_no, contact_phone,
                contact_email, address, area, district, postal_code, is_active, created_at, updated_at)
            VALUES (@BranchNo, @PharmacyNo, @Uuid, 'Main Branch', NULL, @ContactPhone, @ContactEmail,
                @Address, NULL, @District, NULL, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);",
            new { BranchNo = branchNo, model.PharmacyNo, Uuid = Guid.NewGuid(), model.ContactPhone, model.ContactEmail, model.Address, model.District }, transaction);
        await HttpContext.RequestServices.GetRequiredService<IPharmacyChartSeeder>()
            .SeedDefaultChartForPharmacyAsync(conn, transaction, model.PharmacyNo, branchNo);
        await transaction.CommitAsync(HttpContext.RequestAborted);
        return OkResponse<object>(new { pharmacy = model, branch_no = branchNo }, "Company and main branch created.");
    }

    [HttpPost("branches")]
    [AuthenticatedOnly]
    public async Task<ActionResult<ApiResponse<SysBranch>>> CreateBranch([FromBody] SysBranch model)
    {
        if (!IsPlatformOwner) return FailResponse<SysBranch>("Only the platform owner can provision a branch.", statusCode: 403);
        if (string.IsNullOrWhiteSpace(model.Name) || string.IsNullOrWhiteSpace(model.ContactPhone) ||
            string.IsNullOrWhiteSpace(model.Address) || string.IsNullOrWhiteSpace(model.District))
            return FailResponse<SysBranch>("Branch name, contact phone, address and district are required.");
        await using var conn = await _db.CreateOpenConnectionAsync(HttpContext.RequestAborted);
        await using var transaction = await conn.BeginTransactionAsync(HttpContext.RequestAborted);
        if (!await conn.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM sys_pharmacies WHERE pharmacy_no = @PharmacyNo AND is_active = TRUE);", new { model.PharmacyNo }, transaction))
            return NotFoundResponse<SysBranch>("Company not found.");
        model.BranchNo = await conn.ExecuteScalarAsync<long>("SELECT nextval('seq_sys_branch_no');", transaction: transaction);
        model.Uuid = Guid.NewGuid();
        model.IsActive = true;
        await conn.ExecuteAsync(@"
            INSERT INTO sys_branches (branch_no, pharmacy_no, uuid, name, drug_license_no, contact_phone,
                contact_email, address, area, district, postal_code, is_active, created_at, updated_at)
            VALUES (@BranchNo, @PharmacyNo, @Uuid, @Name, @DrugLicenseNo, @ContactPhone, @ContactEmail,
                @Address, NULL, @District, @PostalCode, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);", model, transaction);
        await HttpContext.RequestServices.GetRequiredService<IPharmacyChartSeeder>()
            .SeedDefaultChartForPharmacyAsync(conn, transaction, model.PharmacyNo, model.BranchNo);
        await transaction.CommitAsync(HttpContext.RequestAborted);
        return OkResponse(model, "Branch created.");
    }

}

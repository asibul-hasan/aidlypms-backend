using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AidlyPms.Api.Common;
using AidlyPms.Api.Common.Database;
using AidlyPms.Api.Modules.Auth.Models;
using AidlyPms.Api.Modules.Branches.Models;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;

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
            FROM sys_pharmacies
            ORDER BY pharmacy_no ASC;")).ToList();

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
                WHERE pharmacy_no = @CurrentPharmacyNo
                ORDER BY branch_no ASC;";

        var list = (await conn.QueryAsync<SysBranch>(sql, new { CurrentPharmacyNo })).ToList();
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

        // Check sys_user_pharmacy_access (Admin has universal branch access)
        if (!IsAdmin)
        {
            var hasAccess = await conn.ExecuteScalarAsync<bool>(@"
                SELECT EXISTS(
                    SELECT 1 FROM sys_user_pharmacy_access
                    WHERE user_no = @UserNo AND branch_no = @BranchNo
                );",
                new { UserNo = CurrentUserNo, BranchNo = request.BranchNo });

            if (!hasAccess)
            {
                return FailResponse<SwitchBranchResponse>(
                    $"User does not have authorization to access branch #{request.BranchNo}.",
                    statusCode: 403);
            }
        }

        // Fetch user info
        var user = await conn.QuerySingleOrDefaultAsync<dynamic>(@"
            SELECT u.user_no, u.pharmacy_no, u.branch_no, u.role_no,
                   u.first_name, u.last_name, r.alias as role_name
            FROM sys_users u
            LEFT JOIN sys_roles r ON u.role_no = r.role_no
            WHERE u.user_no = @UserNo;",
            new { UserNo = CurrentUserNo });

        if (user == null)
        {
            return NotFoundResponse<SwitchBranchResponse>("User not found.");
        }

        // Fetch target branch info
        var branch = await conn.QuerySingleOrDefaultAsync<BranchDto>(@"
            SELECT b.branch_no, b.pharmacy_no, b.name,
                   COALESCE(b.contact_phone, p.contact_phone) as contact_phone,
                   COALESCE(b.address, p.address) as address,
                   COALESCE(b.district, p.district) as district,
                   p.drug_license_no, p.contact_email
            FROM sys_branches b
            JOIN sys_pharmacies p ON b.pharmacy_no = p.pharmacy_no
            WHERE b.branch_no = @BranchNo;",
            new { BranchNo = request.BranchNo });

        var targetPharmacyNo = branch?.PharmacyNo ?? (long)user.pharmacy_no;
        var roleNo = (long)user.role_no;
        var fullName = $"{user.first_name} {user.last_name}".Trim();

        var newToken = GenerateJwtToken(CurrentUserNo, targetPharmacyNo, request.BranchNo, roleNo, fullName, (string?)user.role_name);

        return OkResponse(new SwitchBranchResponse
        {
            Token = newToken,
            BranchNo = request.BranchNo,
            PharmacyNo = targetPharmacyNo,
            Branch = branch
        }, $"Switched to branch #{request.BranchNo} successfully.");
    }

    private string GenerateJwtToken(long userNo, long pharmacyNo, long branchNo, long roleNo, string name, string? roleName)
    {
        var jwtConfig = _config.GetSection("Jwt");
        var secretKey = jwtConfig["Key"] ?? "AidlyPMS_SuperSecret_Jwt_SigningKey_2026_Enterprise_Secure!";
        var issuer = jwtConfig["Issuer"] ?? "AidlyPmsBackend";
        var audience = jwtConfig["Audience"] ?? "AidlyPmsFrontend";
        var expiryDays = int.TryParse(jwtConfig["ExpiryDays"], out var days) ? days : 7;

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userNo.ToString()),
            new(ClaimTypes.Name, name),
            new(ClaimTypes.Role, roleName ?? roleNo.ToString()),
            new("user_no", userNo.ToString()),
            new("pharmacy_no", pharmacyNo.ToString()),
            new("branch_no", branchNo.ToString()),
            new("role_no", roleNo.ToString())
        };

        if (roleNo == 1 || string.Equals(roleName, "Admin", StringComparison.OrdinalIgnoreCase))
        {
            claims.Add(new Claim("platform_owner", "true"));
            claims.Add(new Claim("is_admin", "true"));
        }

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddDays(expiryDays),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

using System.Security.Cryptography;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AidlyPms.Api.Common.Database;
using Dapper;
using Microsoft.IdentityModel.Tokens;

namespace AidlyPms.Api.Common.Security;

public sealed class JwtSettings
{
    public byte[] SigningKey { get; init; } = [];
    public string Issuer { get; init; } = "AidlyPmsBackend";
    public string Audience { get; init; } = "AidlyPmsFrontend";
    public int LifetimeMinutes { get; init; } = 30;
    public static JwtSettings Load(IConfiguration configuration)
    {
        // Legacy Jwt:Key and the old source-known fallback are deliberately not used.
        var value = configuration["JWT_SIGNING_KEY"] ?? configuration["Jwt:SigningKey"];
        if (string.IsNullOrWhiteSpace(value) || Encoding.UTF8.GetByteCount(value) < 32)
            throw new InvalidOperationException("Configure JWT_SIGNING_KEY with at least 32 bytes of random signing material.");
        if (Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))) == "0B30BB09C9D8971BC75343EB74591D7BBBF54FAFAEF096B926E9C971B2135F1D")
            throw new InvalidOperationException("The previously published signing key is not allowed. Configure a fresh random key.");
        return new JwtSettings
        {
            SigningKey = Encoding.UTF8.GetBytes(value),
            Issuer = configuration["Jwt:Issuer"] ?? "AidlyPmsBackend",
            Audience = configuration["Jwt:Audience"] ?? "AidlyPmsFrontend",
            LifetimeMinutes = Math.Clamp(configuration.GetValue<int?>("Jwt:LifetimeMinutes") ?? 30, 5, 60)
        };
    }
}

public static class PasswordVerification
{
    private static readonly string DummyHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString("N"));
    public static bool IsValidNewPassword(string? password) => !string.IsNullOrWhiteSpace(password) && password.Length >= 12 && Encoding.UTF8.GetByteCount(password) <= 72;
    public static bool Verify(string password, string? hash)
    {
        if (string.IsNullOrEmpty(password)) return false;
        try
        {
            if (!string.IsNullOrEmpty(hash)) return BCrypt.Net.BCrypt.Verify(password, hash);
        }
        catch (BCrypt.Net.SaltParseException) { }
        catch (ArgumentException) { }
        // Unknown users and invalid hashes still incur password-verification work.
        BCrypt.Net.BCrypt.Verify("invalid-login-attempt", DummyHash);
        return false;
    }
}

public interface IAuthSessions
{
    Task<(string Token, DateTime ExpiresAt)> CreateAsync(long userNo, long pharmacyNo, long branchNo, CancellationToken cancellationToken = default, DateTime? verifiedUserUpdatedAt = null);
    Task<RequestAccess?> ValidateAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default);
    Task RevokeAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default);
}

public sealed class AuthSessions(IDbConnectionFactory db, JwtSettings settings) : IAuthSessions
{
    public async Task<(string Token, DateTime ExpiresAt)> CreateAsync(long userNo, long pharmacyNo, long branchNo, CancellationToken cancellationToken = default, DateTime? verifiedUserUpdatedAt = null)
    {
        await using var conn = await db.CreateOpenConnectionAsync(cancellationToken);
        var sessionId = Guid.NewGuid();
        var expiresAt = DateTime.UtcNow.AddMinutes(settings.LifetimeMinutes);
        // Membership, active state, tenant-role agreement and target branch are checked atomically with creation.
        var roleNo = await conn.QuerySingleOrDefaultAsync<long?>(new CommandDefinition(@"
            INSERT INTO sys_auth_sessions (session_id, user_no, role_no, pharmacy_no, branch_no, user_updated_at, expires_at, revoked_at)
            SELECT @SessionId, u.user_no, u.role_no, @PharmacyNo, @BranchNo, u.updated_at, @ExpiresAt, NULL
            FROM sys_users u JOIN sys_roles r ON r.role_no = u.role_no
            LEFT JOIN sys_role_capabilities cap ON cap.role_no = r.role_no
            JOIN sys_branches b ON b.branch_no = @BranchNo AND b.pharmacy_no = @PharmacyNo
            JOIN sys_pharmacies p ON p.pharmacy_no = b.pharmacy_no
            WHERE u.user_no = @UserNo AND u.is_active = TRUE AND r.status = TRUE
              AND (CAST(@VerifiedUserUpdatedAt AS timestamptz) IS NULL OR u.updated_at = @VerifiedUserUpdatedAt)
              AND (cap.access_scope = 'PLATFORM_OWNER' OR
                 (u.pharmacy_no = @PharmacyNo AND r.pharmacy_no = @PharmacyNo AND b.is_active = TRUE AND p.is_active = TRUE
                  AND (r.branch_no IS NULL OR r.branch_no = u.branch_no)
                  AND (u.branch_no = @BranchNo OR EXISTS (
                      SELECT 1 FROM sys_user_pharmacy_access a
                      WHERE a.user_no = u.user_no AND a.pharmacy_no = @PharmacyNo AND a.branch_no = @BranchNo))))
            RETURNING role_no;",
            new { SessionId = sessionId, UserNo = userNo, PharmacyNo = pharmacyNo, BranchNo = branchNo, ExpiresAt = expiresAt, VerifiedUserUpdatedAt = verifiedUserUpdatedAt }, cancellationToken: cancellationToken));
        if (!roleNo.HasValue) throw new UnauthorizedAccessException("Account or branch access is no longer valid.");
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Jti, sessionId.ToString()),
            new Claim("user_no", userNo.ToString()), new Claim("role_no", roleNo.Value.ToString()),
            new Claim("pharmacy_no", pharmacyNo.ToString()), new Claim("branch_no", branchNo.ToString()),
            new Claim(ClaimTypes.NameIdentifier, userNo.ToString())
        };
        var token = new JwtSecurityToken(settings.Issuer, settings.Audience, claims,
            notBefore: DateTime.UtcNow, expires: expiresAt,
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(settings.SigningKey), SecurityAlgorithms.HmacSha256));
        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }

    public async Task<RequestAccess?> ValidateAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(principal.FindFirst(JwtRegisteredClaimNames.Jti)?.Value, out var sessionId)) return null;
        if (!long.TryParse(principal.FindFirst("user_no")?.Value, out var userNo) ||
            !long.TryParse(principal.FindFirst("role_no")?.Value, out var roleNo) ||
            !long.TryParse(principal.FindFirst("pharmacy_no")?.Value, out var pharmacyNo) ||
            !long.TryParse(principal.FindFirst("branch_no")?.Value, out var branchNo)) return null;
        await using var conn = await db.CreateOpenConnectionAsync(cancellationToken);
        // One round trip resolves session validity and both kinds of grants. No cross-request permission cache.
        using var results = await conn.QueryMultipleAsync(new CommandDefinition(@"
            SELECT u.user_no, u.role_no, s.pharmacy_no, s.branch_no, u.updated_at AS user_updated_at, cap.access_scope
            FROM sys_auth_sessions s JOIN sys_users u ON u.user_no = s.user_no
            JOIN sys_roles r ON r.role_no = u.role_no
            LEFT JOIN sys_role_capabilities cap ON cap.role_no = r.role_no
            JOIN sys_branches b ON b.branch_no = s.branch_no AND b.pharmacy_no = s.pharmacy_no
            JOIN sys_pharmacies p ON p.pharmacy_no = s.pharmacy_no
            WHERE s.session_id = @SessionId AND s.user_no = @UserNo AND s.role_no = @RoleNo
              AND s.pharmacy_no = @PharmacyNo AND s.branch_no = @BranchNo
              AND s.revoked_at IS NULL AND s.expires_at > CURRENT_TIMESTAMP
              AND u.is_active = TRUE AND r.status = TRUE
              AND s.user_updated_at = u.updated_at AND s.role_no = u.role_no
              AND (cap.access_scope = 'PLATFORM_OWNER' OR
                  (u.pharmacy_no = s.pharmacy_no AND r.pharmacy_no = s.pharmacy_no AND b.is_active = TRUE AND p.is_active = TRUE
                   AND (r.branch_no IS NULL OR r.branch_no = u.branch_no)
                   AND (u.branch_no = s.branch_no OR EXISTS (
                       SELECT 1 FROM sys_user_pharmacy_access a
                       WHERE a.user_no = u.user_no AND a.pharmacy_no = s.pharmacy_no AND a.branch_no = s.branch_no))));
            SELECT p.code FROM sys_role_permissions rp JOIN sys_permissions p ON p.permission_no = rp.permission_no
            WHERE rp.pharmacy_no = @PharmacyNo AND rp.role_no = @RoleNo;
            SELECT m.menu_code, rmp.can_view, rmp.can_create, rmp.can_edit, rmp.can_delete
            FROM sys_role_menu_permissions rmp JOIN sys_menus m ON m.menu_no = rmp.menu_no AND m.status = TRUE
            WHERE rmp.pharmacy_no = @PharmacyNo AND rmp.role_no = @RoleNo;
            SELECT COALESCE(u.user_no, -1) AS user_no, cap.role_no, TRIM(CONCAT(u.first_name, ' ', u.last_name)) AS display_name
            FROM sys_role_capabilities cap LEFT JOIN sys_users u ON u.role_no = cap.role_no
            WHERE cap.access_scope = 'PLATFORM_OWNER';",
            new { SessionId = sessionId, UserNo = userNo, RoleNo = roleNo, PharmacyNo = pharmacyNo, BranchNo = branchNo }, cancellationToken: cancellationToken));
        var access = await results.ReadSingleOrDefaultAsync<RequestAccess>();
        var permissions = (await results.ReadAsync<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var menus = (await results.ReadAsync<MenuGrant>()).ToDictionary(g => g.MenuCode, StringComparer.OrdinalIgnoreCase);
        var identities = (await results.ReadAsync<PlatformIdentity>()).ToList();
        if (access == null) return null;
        access.PlatformIdentities = identities;
        access.Permissions = permissions;
        access.Menus = menus;
        return access;
    }

    public async Task RevokeAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(principal.FindFirst(JwtRegisteredClaimNames.Jti)?.Value, out var sessionId)) return;
        await using var conn = await db.CreateOpenConnectionAsync(cancellationToken);
        await conn.ExecuteAsync(new CommandDefinition("UPDATE sys_auth_sessions SET revoked_at = CURRENT_TIMESTAMP WHERE session_id = @SessionId AND revoked_at IS NULL;",
            new { SessionId = sessionId }, cancellationToken: cancellationToken));
    }
}

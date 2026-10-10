using System.Security.Claims;

namespace AidlyPms.Api.Common;

public interface ITenantContext
{
    long PharmacyNo { get; }
    long BranchNo { get; }
    long UserNo { get; }
    long RoleNo { get; }
}

public class TenantContext : ITenantContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public TenantContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public long PharmacyNo
    {
        get
        {
            var ctx = _httpContextAccessor.HttpContext;
            if (ctx == null) return 1;

            // 1. Authenticated JWT claim
            var claimVal = ctx.User?.FindFirst("pharmacy_no")?.Value;
            if (!string.IsNullOrEmpty(claimVal) && long.TryParse(claimVal, out var cp))
            {
                return cp;
            }

            // 2. HTTP Header fallback
            if (ctx.Request.Headers.TryGetValue("X-Pharmacy-No", out var phVal) &&
                long.TryParse(phVal.ToString(), out var p))
            {
                return p;
            }

            // 3. Query string fallback
            if (ctx.Request.Query.TryGetValue("pharmacy_no", out var qVal) &&
                long.TryParse(qVal.ToString(), out var qp))
            {
                return qp;
            }

            return 1;
        }
    }

    public long BranchNo
    {
        get
        {
            var ctx = _httpContextAccessor.HttpContext;
            if (ctx == null) return 1;

            // 1. Authenticated JWT claim
            var claimVal = ctx.User?.FindFirst("branch_no")?.Value;
            if (!string.IsNullOrEmpty(claimVal) && long.TryParse(claimVal, out var cb))
            {
                return cb;
            }

            // 2. HTTP Header fallback
            if (ctx.Request.Headers.TryGetValue("X-Branch-No", out var brVal) &&
                long.TryParse(brVal.ToString(), out var b))
            {
                return b;
            }

            // 3. Query string fallback
            if (ctx.Request.Query.TryGetValue("branch_no", out var qVal) &&
                long.TryParse(qVal.ToString(), out var qb))
            {
                return qb;
            }

            return 1;
        }
    }

    public long UserNo
    {
        get
        {
            var ctx = _httpContextAccessor.HttpContext;
            if (ctx == null) return 1;

            // 1. Authenticated JWT claim
            var claimVal = ctx.User?.FindFirst("user_no")?.Value ??
                           ctx.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!string.IsNullOrEmpty(claimVal) && long.TryParse(claimVal, out var cu))
            {
                return cu;
            }

            // 2. HTTP Header fallback
            if (ctx.Request.Headers.TryGetValue("X-User-No", out var uVal) &&
                long.TryParse(uVal.ToString(), out var u))
            {
                return u;
            }

            return 1;
        }
    }

    public long RoleNo
    {
        get
        {
            var ctx = _httpContextAccessor.HttpContext;
            if (ctx == null) return 0;

            // 1. Authenticated JWT claim
            var claimVal = ctx.User?.FindFirst("role_no")?.Value ??
                           ctx.User?.FindFirst(ClaimTypes.Role)?.Value;
            if (!string.IsNullOrEmpty(claimVal) && long.TryParse(claimVal, out var cr))
            {
                return cr;
            }

            // 2. HTTP Header fallback
            if (ctx.Request.Headers.TryGetValue("X-Role-No", out var rVal) &&
                long.TryParse(rVal.ToString(), out var r))
            {
                return r;
            }

            return 0;
        }
    }
}

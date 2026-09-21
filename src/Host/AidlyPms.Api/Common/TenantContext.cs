namespace AidlyPms.Api.Common;

public interface ITenantContext
{
    long PharmacyNo { get; }
    long BranchNo { get; }
    long UserNo { get; }
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

            if (ctx.Request.Headers.TryGetValue("X-Pharmacy-No", out var phVal) &&
                long.TryParse(phVal.ToString(), out var p))
            {
                return p;
            }

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

            if (ctx.Request.Headers.TryGetValue("X-Branch-No", out var brVal) &&
                long.TryParse(brVal.ToString(), out var b))
            {
                return b;
            }

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

            if (ctx.Request.Headers.TryGetValue("X-User-No", out var uVal) &&
                long.TryParse(uVal.ToString(), out var u))
            {
                return u;
            }

            return 1;
        }
    }
}

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
            var claimVal = ctx?.User?.FindFirst("pharmacy_no")?.Value;
            return !string.IsNullOrEmpty(claimVal) && long.TryParse(claimVal, out var cp) ? cp : 0;
        }
    }

    public long BranchNo
    {
        get
        {
            var ctx = _httpContextAccessor.HttpContext;
            var claimVal = ctx?.User?.FindFirst("branch_no")?.Value;
            return !string.IsNullOrEmpty(claimVal) && long.TryParse(claimVal, out var cb) ? cb : 0;
        }
    }

    public long UserNo
    {
        get
        {
            var ctx = _httpContextAccessor.HttpContext;
            var claimVal = ctx?.User?.FindFirst("user_no")?.Value ??
                           ctx?.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return !string.IsNullOrEmpty(claimVal) && long.TryParse(claimVal, out var cu) ? cu : 0;
        }
    }

    public long RoleNo
    {
        get
        {
            var ctx = _httpContextAccessor.HttpContext;
            var claimVal = ctx?.User?.FindFirst("role_no")?.Value ??
                           ctx?.User?.FindFirst(ClaimTypes.Role)?.Value;
            return !string.IsNullOrEmpty(claimVal) && long.TryParse(claimVal, out var cr) ? cr : 0;
        }
    }
}

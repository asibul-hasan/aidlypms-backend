using Microsoft.AspNetCore.Mvc;

namespace AidlyPms.Api.Common;

[ApiController]
public abstract class BaseController : ControllerBase
{
    private ITenantContext? _tenantContext;

    protected ITenantContext TenantContext =>
        _tenantContext ??= HttpContext.RequestServices.GetRequiredService<ITenantContext>();

    protected long CurrentPharmacyNo => TenantContext.PharmacyNo;
    protected long CurrentBranchNo => TenantContext.BranchNo;
    protected long CurrentUserNo => TenantContext.UserNo;

    protected ActionResult<ApiResponse<T>> OkResponse<T>(T data, string? message = null)
    {
        return Ok(ApiResponse<T>.Ok(data, message));
    }

    protected ActionResult<ApiResponse<PagedResult<T>>> PagedResponse<T>(PagedResult<T> paged, string? message = null)
    {
        return Ok(ApiResponse<PagedResult<T>>.Ok(paged, message));
    }

    protected ActionResult<ApiResponse<T>> FailResponse<T>(string error, string? message = null, int statusCode = 400)
    {
        return StatusCode(statusCode, ApiResponse<T>.Fail(error, message));
    }

    protected ActionResult<ApiResponse<T>> NotFoundResponse<T>(string message = "Resource not found")
    {
        return StatusCode(404, ApiResponse<T>.Fail("Not Found", message));
    }
}

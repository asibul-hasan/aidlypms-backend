using AidlyPms.Api.Common.Database;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AidlyPms.Api.Common;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public class RequirePermissionAttribute : Attribute, IAsyncActionFilter
{
    private readonly string _permissionCode;
    public string PermissionCode => _permissionCode;

    public RequirePermissionAttribute(string permissionCode)
    {
        _permissionCode = permissionCode;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        // Allow anonymous endpoints to bypass
        if (context.ActionDescriptor.EndpointMetadata.Any(em => em is Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute))
        {
            await next();
            return;
        }

        var access = AidlyPms.Api.Common.Security.RequestAccess.From(context.HttpContext);
        if (access == null || (!access.IsFullAccess && !access.Permissions.Contains(_permissionCode)))
        {
            context.Result = new ObjectResult(ApiResponse<object>.Fail("Forbidden", "Required special permission is not granted.")) { StatusCode = 403 };
            return;
        }

        await next();
    }
}

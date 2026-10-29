using AidlyPms.Api.Common.Database;
using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AidlyPms.Api.Common;

public enum MenuOperation
{
    Auto = 0,
    View = 1,
    Create = 2,
    Edit = 3,
    Delete = 4
}

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public class RequireMenuAttribute : Attribute, IAsyncActionFilter
{
    private readonly string[] _menuCodes;
    public IReadOnlyList<string> MenuCodes => _menuCodes;
    public MenuOperation Operation { get; set; } = MenuOperation.Auto;

    public RequireMenuAttribute(params string[] menuCodes)
    {
        _menuCodes = menuCodes ?? Array.Empty<string>();
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        // Allow anonymous or authenticated-only endpoints to bypass menu checks
        if (context.ActionDescriptor.EndpointMetadata.Any(em => em is Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute || em is AuthenticatedOnlyAttribute))
        {
            await next();
            return;
        }

        var access = AidlyPms.Api.Common.Security.RequestAccess.From(context.HttpContext);
        var operation = Operation == MenuOperation.Auto ? context.HttpContext.Request.Method.ToUpperInvariant() switch
        {
            "GET" or "HEAD" => MenuOperation.View, "POST" => MenuOperation.Create,
            "PUT" or "PATCH" => MenuOperation.Edit, "DELETE" => MenuOperation.Delete, _ => MenuOperation.View
        } : Operation;
        if (access == null || !access.HasMenu(_menuCodes, operation))
        {
            context.Result = new ObjectResult(ApiResponse<object>.Fail("Forbidden", "Required menu access is not granted.")) { StatusCode = 403 };
            return;
        }

        await next();
    }
}

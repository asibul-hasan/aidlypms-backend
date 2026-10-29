using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AidlyPms.Api.Common.Security;

public static class BrowserSession
{
    public const string CookieName = "AidlyPms.Session";
    public static CookieOptions CookieOptions(HttpContext context, DateTime? expires = null)
    {
        var development = context.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment();
        return new CookieOptions
        {
            HttpOnly = true, Secure = !development || context.Request.IsHttps,
            SameSite = development ? SameSiteMode.Lax : SameSiteMode.None,
            Path = "/api", Expires = expires, IsEssential = true
        };
    }
    public static void Write(HttpContext context, string token, DateTime expires) =>
        context.Response.Cookies.Append(CookieName, token, CookieOptions(context, expires));
    public static void Clear(HttpContext context) => context.Response.Cookies.Delete(CookieName, CookieOptions(context));
}

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class BrowserCsrfRequiredAttribute : Attribute { }

public sealed class BrowserRequestValidationFilter(IAntiforgery antiforgery) : IAsyncActionFilter, IOrderedFilter
{
    public int Order => -1000;
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var request = context.HttpContext.Request;
        var mutating = !HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method) && !HttpMethods.IsOptions(request.Method);
        // Endpoint metadata covers every route variant, including a trailing slash.
        var csrfRequired = context.ActionDescriptor.EndpointMetadata.Any(metadata => metadata is BrowserCsrfRequiredAttribute);
        if (mutating && (csrfRequired || request.Cookies.ContainsKey(BrowserSession.CookieName)))
        {
            try { await antiforgery.ValidateRequestAsync(context.HttpContext); }
            catch (AntiforgeryValidationException)
            {
                context.Result = new BadRequestObjectResult(ApiResponse<object>.Fail("Invalid browser request. Refresh the session and try again."));
                return;
            }
        }
        await next();
    }
}

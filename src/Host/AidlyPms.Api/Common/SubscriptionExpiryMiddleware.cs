using System.Text.Json;
using AidlyPms.Api.Common.Database;
using Dapper;
using AidlyPms.Api.Common.Security;

namespace AidlyPms.Api.Common;

public class SubscriptionExpiryMiddleware
{
    private readonly RequestDelegate _next;

    private static readonly HashSet<string> ExcludedPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/v1/auth",
        "/api/v1/sub",
        "/api/v1/config/seed-defaults",
        "/health",
        "/api/health"
    };

    public SubscriptionExpiryMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ITenantContext tenant, IDbConnectionFactory dbFactory)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        // Skip bypassed endpoints
        if (ExcludedPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            await _next(context);
            return;
        }

        if (RequestAccess.From(context)?.IsPlatformOwner == true)
        {
            await _next(context);
            return;
        }

        // Only enforce for authenticated requests with a valid tenant/pharmacy
        if (tenant.PharmacyNo > 0)
        {
            try
            {
                using var conn = dbFactory.CreateConnection();
                const string query = @"
                    SELECT subscription_end_date AS end_date, COALESCE(grace_period_days, 0) AS grace_period_days, subscription_status AS status
                    FROM sub_pharmacy_subscriptions
                    WHERE pharmacy_no = @PharmacyNo
                    ORDER BY (branch_no = @BranchNo) DESC, subscription_no DESC
                    LIMIT 1;";

                var sub = await conn.QuerySingleOrDefaultAsync<SubscriptionExpiryCheckDto>(
                    query, new { PharmacyNo = tenant.PharmacyNo, BranchNo = tenant.BranchNo });

                if (sub != null)
                {
                    var effectiveExpiry = sub.EndDate.Date.AddDays(sub.GracePeriodDays);

                    if (sub.Status == 4 /* Suspended */ || sub.Status == 5 /* Cancelled */ || DateTime.UtcNow.Date > effectiveExpiry)
                    {
                        context.Response.StatusCode = StatusCodes.Status402PaymentRequired;
                        context.Response.ContentType = "application/json";

                        var response = new ApiResponse<object>
                        {
                            Success = false,
                            Message = "Subscription expired. Your grace period has ended. Please renew your subscription to continue.",
                            Data = new
                            {
                                subscription_expired = true,
                                subscription_end_date = sub.EndDate.ToString("yyyy-MM-dd"),
                                grace_period_days = sub.GracePeriodDays
                            }
                        };

                        await context.Response.WriteAsync(JsonSerializer.Serialize(response, new JsonSerializerOptions
                        {
                            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                        }));
                        return;
                    }
                }
            }
            catch
            {
                context.RequestServices.GetRequiredService<ILogger<SubscriptionExpiryMiddleware>>()
                    .LogError("Subscription validation unavailable for pharmacy {PharmacyNo}.", tenant.PharmacyNo);
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                await context.Response.WriteAsJsonAsync(ApiResponse<object>.Fail("Subscription validation temporarily unavailable."));
                return;
            }
        }

        await _next(context);
    }

    private class SubscriptionExpiryCheckDto
    {
        public DateTime EndDate { get; set; }
        public int GracePeriodDays { get; set; }
        public short Status { get; set; }
    }
}

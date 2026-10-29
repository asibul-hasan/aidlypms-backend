using System.Security.Claims;
using AidlyPms.Api.Common;
using AidlyPms.Api.Common.Database;
using AidlyPms.Api.Modules.Subscription.Models;
using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace AidlyPms.Api.Modules.Subscription.Controllers;

[ApiController]
[Route("api/v1/owner/subscriptions")]
[AuthenticatedOnly]
public class PlatformOwnerSubscriptionController : ControllerBase
{
    private readonly IDbConnectionFactory _db;
    private readonly IConfiguration _config;

    public PlatformOwnerSubscriptionController(IDbConnectionFactory db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    [HttpPost("payments/{paymentNo:long}/confirm")]
    public async Task<ActionResult<ApiResponse<SubSubscriptionPayment>>> ConfirmPayment(long paymentNo)
    {
        if (AidlyPms.Api.Common.Security.RequestAccess.From(HttpContext)?.IsPlatformOwner != true)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<SubSubscriptionPayment>.Fail(
                "Forbidden", "Access denied: Platform owner authorization required."));
        }

        await using var conn = await _db.CreateOpenConnectionAsync();
        await using var tran = await conn.BeginTransactionAsync();

        try
        {
            // Global lookup: platform owner can confirm payments across any pharmacy tenant
            var payment = await conn.QuerySingleOrDefaultAsync<SubSubscriptionPayment>(@"
                SELECT * FROM sub_subscription_payments
                WHERE subscription_payment_no = @paymentNo;",
                new { paymentNo }, tran);

            if (payment == null)
            {
                return NotFound(ApiResponse<SubSubscriptionPayment>.Fail(
                    "Not Found", "Subscription payment record not found."));
            }

            if (string.Equals(payment.PaymentStatus, "Completed", StringComparison.OrdinalIgnoreCase))
            {
                return Ok(ApiResponse<SubSubscriptionPayment>.Ok(payment, "Payment is already confirmed and completed."));
            }

            await conn.ExecuteAsync(@"
                UPDATE sub_subscription_payments
                SET payment_status = 'Completed'
                WHERE subscription_payment_no = @paymentNo;",
                new { paymentNo }, tran);

            // Derive extension days from the plan or billing cycle stored on the payment
            int durationDays = 30;
            short billingCycleInt = payment.BillingCycle ?? 1;

            if (payment.BillingCycle.HasValue)
            {
                billingCycleInt = payment.BillingCycle.Value;
                durationDays = billingCycleInt == 2 ? 365 : 30;
            }
            else if (payment.PlanNo.HasValue)
            {
                var plan = await conn.QuerySingleOrDefaultAsync<SubSubscriptionPlan>(
                    "SELECT * FROM sub_subscription_plans WHERE plan_no = @PlanNo;",
                    new { PlanNo = payment.PlanNo.Value }, tran);

                if (plan != null && plan.YearlyFee > 0 && payment.Subtotal >= plan.YearlyFee)
                {
                    durationDays = 365;
                    billingCycleInt = 2;
                }
            }
            else
            {
                var existingCycle = await conn.ExecuteScalarAsync<short?>(@"
                    SELECT billing_cycle FROM sub_pharmacy_subscriptions
                    WHERE subscription_no = @SubscriptionNo;",
                    new { payment.SubscriptionNo }, tran);

                if (existingCycle == 2)
                {
                    durationDays = 365;
                    billingCycleInt = 2;
                }
            }

            const string updateSubSql = @"
                UPDATE sub_pharmacy_subscriptions
                SET subscription_status = 2,
                    billing_cycle = @billingCycleInt,
                    monthly_rate = @MonthlyRate,
                    subscription_end_date = GREATEST(subscription_end_date, CURRENT_DATE) + (@Days || ' days')::INTERVAL,
                    next_payment_date = GREATEST(next_payment_date, CURRENT_DATE) + (@Days || ' days')::INTERVAL,
                    updated_at = NOW()
                WHERE subscription_no = @SubscriptionNo;";

            await conn.ExecuteAsync(updateSubSql, new
            {
                Days = durationDays,
                MonthlyRate = payment.Subtotal,
                billingCycleInt,
                SubscriptionNo = payment.SubscriptionNo
            }, tran);

            await tran.CommitAsync();

            payment.PaymentStatus = "Completed";
            return Ok(ApiResponse<SubSubscriptionPayment>.Ok(payment, "Payment confirmed successfully by platform owner. Subscription extended."));
        }
        catch
        {
            await tran.RollbackAsync();
            throw;
        }
    }
}

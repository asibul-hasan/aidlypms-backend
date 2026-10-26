using System.Security.Cryptography;
using System.Text;
using AidlyPms.Api.Common;
using AidlyPms.Api.Common.Database;
using AidlyPms.Api.Modules.Subscription.Models;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace AidlyPms.Api.Modules.Subscription.Controllers;

[ApiController]
[AllowAnonymous]
public class SubscriptionCallbackController : ControllerBase
{
    private readonly IDbConnectionFactory _db;
    private readonly IConfiguration _config;

    public SubscriptionCallbackController(IDbConnectionFactory db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    [HttpPost("api/v1/sub/subscriptions/payments/callback")]
    [HttpPost("api/v1/sub/callback")]
    [HttpPost("api/v1/sub/callback/{gateway}")]
    [AllowAnonymous]
    public async Task<IActionResult> ProcessGatewayCallback([FromBody] GatewayCallbackPayload payload, [FromRoute] string? gateway = null)
    {
        var secret = Environment.GetEnvironmentVariable("GATEWAY_HMAC_SECRET")
            ?? _config["Subscription:GatewayHmacSecret"]
            ?? string.Empty;

        // 1. Extract signature from HTTP headers or payload body
        string? receivedSig = null;
        if (Request.Headers.TryGetValue("X-Signature", out var headerSig1) && !string.IsNullOrWhiteSpace(headerSig1))
        {
            receivedSig = headerSig1.ToString().Trim();
        }
        else if (Request.Headers.TryGetValue("X-HMAC-Signature", out var headerSig2) && !string.IsNullOrWhiteSpace(headerSig2))
        {
            receivedSig = headerSig2.ToString().Trim();
        }
        else if (!string.IsNullOrWhiteSpace(payload.Signature))
        {
            receivedSig = payload.Signature.Trim();
        }

        if (string.IsNullOrWhiteSpace(receivedSig) || string.IsNullOrWhiteSpace(secret))
        {
            return StatusCode(StatusCodes.Status401Unauthorized, ApiResponse<object>.Fail(
                "Unauthorized", "Missing or invalid HMAC signature."));
        }

        // 2. Verify HMAC-SHA256 signature against candidate payloads
        var trxRef = payload.TransactionReference ?? string.Empty;
        var amountStr1 = payload.Amount?.ToString() ?? string.Empty;
        var amountStr2 = payload.Amount?.ToString("0.00") ?? string.Empty;

        var isValid = VerifyHmac(secret, receivedSig,
            $"{trxRef}:{amountStr1}",
            $"{trxRef}:{amountStr2}",
            trxRef);

        if (!isValid)
        {
            return StatusCode(StatusCodes.Status401Unauthorized, ApiResponse<object>.Fail(
                "Unauthorized", "HMAC signature verification failed."));
        }

        // 3. Look payment up exclusively by transaction_reference (never read pharmacy_no from request)
        if (string.IsNullOrWhiteSpace(payload.TransactionReference))
        {
            return BadRequest(ApiResponse<object>.Fail(
                "Bad Request", "Transaction reference is required for payment lookup."));
        }

        await using var conn = await _db.CreateOpenConnectionAsync();
        await using var tran = await conn.BeginTransactionAsync();

        try
        {
            var payment = await conn.QuerySingleOrDefaultAsync<SubSubscriptionPayment>(@"
                SELECT * FROM sub_subscription_payments
                WHERE transaction_reference = @TrxRef;",
                new { TrxRef = payload.TransactionReference }, tran);

            if (payment == null)
            {
                return NotFound(ApiResponse<object>.Fail(
                    "Not Found", $"Subscription payment record not found for transaction reference '{payload.TransactionReference}'."));
            }

            // 4. Idempotency guard: already Completed means immediate no-op
            if (string.Equals(payment.PaymentStatus, "Completed", StringComparison.OrdinalIgnoreCase))
            {
                return Ok(ApiResponse<object>.Ok(
                    new { payment_no = payment.SubscriptionPaymentNo, status = "Completed" },
                    "Payment was already completed. Callback processed as idempotent no-op."));
            }

            // 5. Amount integrity check: must equal FinalAmount
            if (payload.Amount.HasValue && payload.Amount.Value != payment.FinalAmount)
            {
                return BadRequest(ApiResponse<object>.Fail(
                    "Bad Request",
                    $"Callback amount ({payload.Amount.Value}) does not equal expected FinalAmount ({payment.FinalAmount})."));
            }

            var isFailed = string.Equals(payload.Status, "Failed", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(payload.Status, "Cancelled", StringComparison.OrdinalIgnoreCase);

            if (isFailed)
            {
                await conn.ExecuteAsync(@"
                    UPDATE sub_subscription_payments
                    SET payment_status = 'Failed'
                    WHERE subscription_payment_no = @PaymentNo;",
                    new { PaymentNo = payment.SubscriptionPaymentNo }, tran);

                await tran.CommitAsync();
                return Ok(ApiResponse<object>.Ok(
                    new { payment_no = payment.SubscriptionPaymentNo, status = "Failed" },
                    "Gateway reported payment failure. Payment marked as Failed."));
            }

            // 6. Mark payment completed and derive extension days from payment's plan or billing cycle
            await conn.ExecuteAsync(@"
                UPDATE sub_subscription_payments
                SET payment_status = 'Completed'
                WHERE subscription_payment_no = @PaymentNo;",
                new { PaymentNo = payment.SubscriptionPaymentNo }, tran);

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

            return Ok(ApiResponse<object>.Ok(
                new { payment_no = payment.SubscriptionPaymentNo, status = "Completed" },
                "Gateway callback verified and subscription activated successfully."));
        }
        catch
        {
            await tran.RollbackAsync();
            throw;
        }
    }

    private static bool VerifyHmac(string secret, string receivedSig, params string[] candidates)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrEmpty(candidate)) continue;
            var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(candidate));
            var hexHash = Convert.ToHexString(hashBytes);
            if (string.Equals(receivedSig, hexHash, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var base64Hash = Convert.ToBase64String(hashBytes);
            if (string.Equals(receivedSig, base64Hash, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }
}

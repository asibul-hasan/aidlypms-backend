using AidlyPms.Api.Common;
using AidlyPms.Api.Common.Database;
using AidlyPms.Api.Modules.Subscription.Models;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace AidlyPms.Api.Modules.Subscription.Controllers;

[Route("api/v1/sub")]
[RequirePermission("Pharmacy-Module")]
public class SubscriptionController : BaseController
{
    private readonly IDbConnectionFactory _db;
    private readonly IConfiguration _config;

    public SubscriptionController(IDbConnectionFactory db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    [HttpGet("plans")]
    public async Task<ActionResult<ApiResponse<List<SubSubscriptionPlan>>>> GetPlans()
    {
        await using var conn = await _db.CreateOpenConnectionAsync();
        const string sql = @"
            SELECT plan_no, plan_name, monthly_fee, yearly_fee, trial_days, is_active
            FROM sub_subscription_plans
            WHERE is_active = TRUE
            ORDER BY plan_no ASC;";

        var plans = (await conn.QueryAsync<SubSubscriptionPlan>(sql)).AsList();
        return OkResponse(plans);
    }

    [HttpGet("subscription")]
    public async Task<ActionResult<ApiResponse<SubPharmacySubscription>>> GetSubscription()
    {
        await using var conn = await _db.CreateOpenConnectionAsync();

        const string querySql = @"
            SELECT 
                s.subscription_no, s.pharmacy_no, s.branch_no, s.plan_no,
                s.billing_cycle, s.subscription_status, s.trial_end_date,
                s.subscription_start_date, s.subscription_end_date,
                s.next_payment_date, s.monthly_rate, s.updated_at,
                p.plan_name
            FROM sub_pharmacy_subscriptions s
            JOIN sub_subscription_plans p ON s.plan_no = p.plan_no
            WHERE s.pharmacy_no = @CurrentPharmacyNo
            ORDER BY (s.branch_no = @CurrentBranchNo) DESC, s.subscription_no DESC
            LIMIT 1;";

        var sub = await conn.QuerySingleOrDefaultAsync<SubPharmacySubscription>(querySql, new
        {
            CurrentPharmacyNo,
            CurrentBranchNo
        });

        if (sub == null)
        {
            // Auto-provision standard trial/active subscription if none exists yet
            const string insertSql = @"
                INSERT INTO sub_pharmacy_subscriptions (
                    subscription_no, pharmacy_no, branch_no, plan_no,
                    billing_cycle, subscription_status, trial_end_date,
                    subscription_start_date, subscription_end_date,
                    next_payment_date, monthly_rate, updated_at
                ) VALUES (
                    nextval('seq_subscription_no'), @CurrentPharmacyNo, @CurrentBranchNo, 1,
                    1, 2, CURRENT_DATE + INTERVAL '14 days',
                    CURRENT_DATE, CURRENT_DATE + INTERVAL '30 days',
                    CURRENT_DATE + INTERVAL '30 days', 500, NOW()
                )
                ON CONFLICT (subscription_no) DO UPDATE SET updated_at = NOW()
                RETURNING subscription_no, pharmacy_no, branch_no, plan_no,
                          billing_cycle, subscription_status, trial_end_date,
                          subscription_start_date, subscription_end_date,
                          next_payment_date, monthly_rate, updated_at;";

            sub = await conn.QuerySingleOrDefaultAsync<SubPharmacySubscription>(insertSql, new
            {
                CurrentPharmacyNo,
                CurrentBranchNo
            });

            if (sub != null)
            {
                sub.PlanName = "Standard Pharmacy Cloud";
            }
        }

        return OkResponse(sub!);
    }

    [HttpGet("payments")]
    public async Task<ActionResult<ApiResponse<List<SubSubscriptionPayment>>>> GetPayments()
    {
        await using var conn = await _db.CreateOpenConnectionAsync();

        const string sql = @"
            SELECT 
                subscription_payment_no, pharmacy_no, branch_no, subscription_no,
                transaction_reference, payment_gateway, subtotal, discount_code,
                discount_amount, final_amount, payment_status, payment_timestamp,
                download_invoice_url
            FROM sub_subscription_payments
            WHERE pharmacy_no = @CurrentPharmacyNo
            ORDER BY payment_timestamp DESC;";

        var payments = (await conn.QueryAsync<SubSubscriptionPayment>(sql, new
        {
            CurrentPharmacyNo
        })).AsList();

        return OkResponse(payments);
    }

    [HttpPost("pay-bkash")]
    public async Task<ActionResult<ApiResponse<SubSubscriptionPayment>>> PayBkash(
        [FromBody] RenewalPaymentPayload payload)
    {
        var isAutoConfirm = _config.GetValue<bool>("Subscription:DemoAutoActivate", false);
        return await ProcessPaymentInternalAsync(payload, isAutoConfirm: isAutoConfirm);
    }

    [HttpPost("renew")]
    public async Task<ActionResult<ApiResponse<SubSubscriptionPayment>>> RenewSubscription(
        [FromBody] RenewalPaymentPayload payload)
    {
        var isAutoConfirm = _config.GetValue<bool>("Subscription:DemoAutoActivate", false);
        return await ProcessPaymentInternalAsync(payload, isAutoConfirm: isAutoConfirm);
    }

    private async Task<ActionResult<ApiResponse<SubSubscriptionPayment>>> ProcessPaymentInternalAsync(
        RenewalPaymentPayload payload, bool isAutoConfirm)
    {
        await using var conn = await _db.CreateOpenConnectionAsync();
        await using var tran = await conn.BeginTransactionAsync();

        try
        {
            var plan = await conn.QuerySingleOrDefaultAsync<SubSubscriptionPlan>(
                "SELECT * FROM sub_subscription_plans WHERE plan_no = @PlanNo;",
                new { payload.PlanNo }, tran);

            if (plan == null)
            {
                plan = new SubSubscriptionPlan
                {
                    PlanNo = 1,
                    PlanName = "Standard Pharmacy Cloud",
                    MonthlyFee = 500,
                    YearlyFee = 5000
                };
            }

            var isYearly = string.Equals(payload.BillingCycle, "Yearly", StringComparison.OrdinalIgnoreCase);
            var subtotal = isYearly ? plan.YearlyFee : plan.MonthlyFee;
            var discount = 0m;
            var finalAmount = subtotal - discount;

            var nextPaymentNo = await conn.ExecuteScalarAsync<long>(
                "SELECT nextval('seq_subscription_payment_no');",
                transaction: tran);

            // Retrieve existing subscription for this pharmacy, or auto-provision if missing
            var subscriptionNo = await conn.ExecuteScalarAsync<long?>(@"
                SELECT subscription_no FROM sub_pharmacy_subscriptions
                WHERE pharmacy_no = @CurrentPharmacyNo
                ORDER BY (branch_no = @CurrentBranchNo) DESC, subscription_no DESC LIMIT 1;",
                new { CurrentPharmacyNo, CurrentBranchNo }, tran);

            if (!subscriptionNo.HasValue)
            {
                var newSubNo = await conn.ExecuteScalarAsync<long>(
                    "SELECT nextval('seq_subscription_no');", transaction: tran);

                await conn.ExecuteAsync(@"
                    INSERT INTO sub_pharmacy_subscriptions (
                        subscription_no, pharmacy_no, branch_no, plan_no,
                        billing_cycle, subscription_status, trial_end_date,
                        subscription_start_date, subscription_end_date,
                        next_payment_date, monthly_rate, updated_at
                    ) VALUES (
                        @newSubNo, @CurrentPharmacyNo, @CurrentBranchNo, @PlanNo,
                        1, 1, CURRENT_DATE + INTERVAL '14 days',
                        CURRENT_DATE, CURRENT_DATE + INTERVAL '30 days',
                        CURRENT_DATE + INTERVAL '30 days', 500, NOW()
                    );", new { newSubNo, CurrentPharmacyNo, CurrentBranchNo, PlanNo = plan.PlanNo }, tran);

                subscriptionNo = newSubNo;
            }

            var trxRef = string.IsNullOrWhiteSpace(payload.TransactionReference)
                ? $"bKash-TRX-{DateTime.UtcNow:yyyyMMddHHmmss}"
                : payload.TransactionReference;

            var paymentStatus = isAutoConfirm ? "Completed" : "Pending";
            var billingCycleInt = (short)(isYearly ? 2 : 1);

            var payment = new SubSubscriptionPayment
            {
                SubscriptionPaymentNo = nextPaymentNo,
                PharmacyNo = CurrentPharmacyNo,
                BranchNo = CurrentBranchNo,
                SubscriptionNo = subscriptionNo.Value,
                TransactionReference = trxRef,
                PaymentGateway = payload.PaymentGateway ?? "bKash",
                Subtotal = subtotal,
                DiscountCode = payload.DiscountCode,
                DiscountAmount = discount,
                FinalAmount = finalAmount,
                PaymentStatus = paymentStatus,
                PaymentTimestamp = DateTime.UtcNow,
                DownloadInvoiceUrl = $"/invoices/sub-{nextPaymentNo}.pdf",
                BillingCycle = billingCycleInt,
                PlanNo = plan.PlanNo
            };

            const string insertPaymentSql = @"
                INSERT INTO sub_subscription_payments (
                    subscription_payment_no, pharmacy_no, branch_no, subscription_no,
                    transaction_reference, payment_gateway, subtotal, discount_code,
                    discount_amount, final_amount, payment_status, payment_timestamp,
                    download_invoice_url, billing_cycle, plan_no
                ) VALUES (
                    @SubscriptionPaymentNo, @PharmacyNo, @BranchNo, @SubscriptionNo,
                    @TransactionReference, @PaymentGateway, @Subtotal, @DiscountCode,
                    @DiscountAmount, @FinalAmount, @PaymentStatus, @PaymentTimestamp,
                    @DownloadInvoiceUrl, @BillingCycle, @PlanNo
                );";

            await conn.ExecuteAsync(insertPaymentSql, payment, tran);

            if (isAutoConfirm)
            {
                var durationDays = billingCycleInt == 2 ? 365 : 30;
                const string updateSubSql = @"
                    UPDATE sub_pharmacy_subscriptions
                    SET subscription_status = 2,
                        billing_cycle = @billingCycleInt,
                        monthly_rate = @monthlyRate,
                        subscription_end_date = GREATEST(subscription_end_date, CURRENT_DATE) + (@Days || ' days')::INTERVAL,
                        next_payment_date = GREATEST(next_payment_date, CURRENT_DATE) + (@Days || ' days')::INTERVAL,
                        updated_at = NOW()
                    WHERE subscription_no = @subscriptionNo;";

                await conn.ExecuteAsync(updateSubSql, new
                {
                    billingCycleInt,
                    monthlyRate = subtotal,
                    Days = durationDays,
                    subscriptionNo = subscriptionNo.Value
                }, tran);
            }

            await tran.CommitAsync();

            var message = isAutoConfirm
                ? "bKash payment verified and subscription activated successfully."
                : "Subscription payment initiated as Pending. Awaiting payment gateway callback or manual confirmation.";

            return OkResponse(payment, message);
        }
        catch
        {
            await tran.RollbackAsync();
            throw;
        }
    }
}

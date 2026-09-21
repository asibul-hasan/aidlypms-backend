using System.Text.Json.Serialization;

namespace AidlyPms.Api.Modules.Subscription.Models;

public class SubSubscriptionPlan
{
    [JsonPropertyName("plan_no")]
    public long PlanNo { get; set; }

    [JsonPropertyName("plan_name")]
    public string PlanName { get; set; } = string.Empty;

    [JsonPropertyName("monthly_fee")]
    public decimal MonthlyFee { get; set; }

    [JsonPropertyName("yearly_fee")]
    public decimal YearlyFee { get; set; }

    [JsonPropertyName("trial_days")]
    public int TrialDays { get; set; }

    [JsonPropertyName("is_active")]
    public bool IsActive { get; set; }
}

public class SubPharmacySubscription
{
    [JsonPropertyName("subscription_no")]
    public long SubscriptionNo { get; set; }

    [JsonPropertyName("pharmacy_no")]
    public long PharmacyNo { get; set; }

    [JsonPropertyName("branch_no")]
    public long BranchNo { get; set; }

    [JsonPropertyName("plan_no")]
    public long PlanNo { get; set; }

    [JsonPropertyName("billing_cycle")]
    public short BillingCycle { get; set; } // 1: Monthly, 2: Yearly

    [JsonPropertyName("subscription_status")]
    public short SubscriptionStatus { get; set; } // 1: Trial, 2: Active, 3: Overdue, 4: Suspended, 5: Cancelled

    [JsonPropertyName("trial_end_date")]
    public DateTime? TrialEndDate { get; set; }

    [JsonPropertyName("subscription_start_date")]
    public DateTime SubscriptionStartDate { get; set; }

    [JsonPropertyName("subscription_end_date")]
    public DateTime SubscriptionEndDate { get; set; }

    [JsonPropertyName("next_payment_date")]
    public DateTime NextPaymentDate { get; set; }

    [JsonPropertyName("monthly_rate")]
    public decimal MonthlyRate { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; }

    // Joined / Navigation properties
    [JsonPropertyName("plan_name")]
    public string? PlanName { get; set; }

    [JsonPropertyName("days_remaining")]
    public int DaysRemaining => Math.Max(0, (SubscriptionEndDate.Date - DateTime.UtcNow.Date).Days);
}

public class SubSubscriptionPayment
{
    [JsonPropertyName("subscription_payment_no")]
    public long SubscriptionPaymentNo { get; set; }

    [JsonPropertyName("pharmacy_no")]
    public long PharmacyNo { get; set; }

    [JsonPropertyName("branch_no")]
    public long BranchNo { get; set; }

    [JsonPropertyName("subscription_no")]
    public long SubscriptionNo { get; set; }

    [JsonPropertyName("transaction_reference")]
    public string TransactionReference { get; set; } = string.Empty;

    [JsonPropertyName("payment_gateway")]
    public string PaymentGateway { get; set; } = string.Empty;

    [JsonPropertyName("subtotal")]
    public decimal Subtotal { get; set; }

    [JsonPropertyName("discount_code")]
    public string? DiscountCode { get; set; }

    [JsonPropertyName("discount_amount")]
    public decimal DiscountAmount { get; set; }

    [JsonPropertyName("final_amount")]
    public decimal FinalAmount { get; set; }

    [JsonPropertyName("payment_status")]
    public string PaymentStatus { get; set; } = "Completed";

    [JsonPropertyName("payment_timestamp")]
    public DateTime PaymentTimestamp { get; set; }

    [JsonPropertyName("download_invoice_url")]
    public string? DownloadInvoiceUrl { get; set; }
}

public class RenewalPaymentPayload
{
    [JsonPropertyName("plan_no")]
    public long PlanNo { get; set; } = 1;

    [JsonPropertyName("billing_cycle")]
    public string BillingCycle { get; set; } = "Monthly"; // "Monthly" or "Yearly"

    [JsonPropertyName("payment_gateway")]
    public string PaymentGateway { get; set; } = "bKash"; // "bKash", "Nagad", "Bank"

    [JsonPropertyName("transaction_reference")]
    public string? TransactionReference { get; set; }

    [JsonPropertyName("discount_code")]
    public string? DiscountCode { get; set; }
}

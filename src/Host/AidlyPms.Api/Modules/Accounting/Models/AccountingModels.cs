using System.Text.Json.Serialization;

namespace AidlyPms.Api.Modules.Accounting.Models;

public class AccTransactionAccount
{
    [JsonPropertyName("account_no")]
    public long AccountNo { get; set; }

    [JsonPropertyName("pharmacy_no")]
    public long PharmacyNo { get; set; }

    [JsonPropertyName("branch_no")]
    public long BranchNo { get; set; }

    [JsonPropertyName("account_category")]
    public short AccountCategory { get; set; } = 1; // 1: Asset

    [JsonPropertyName("account_code")]
    public string? AccountCode { get; set; }

    [JsonPropertyName("account_name")]
    public string AccountName { get; set; } = string.Empty;

    [JsonPropertyName("phone_number")]
    public string? PhoneNumber { get; set; }

    [JsonPropertyName("account_type")]
    public short AccountType { get; set; } = 1; // 1: Cash, 2: Bank, 4: bKash

    [JsonPropertyName("bank_account_number")]
    public string? BankAccountNumber { get; set; }

    [JsonPropertyName("bank_name")]
    public string? BankName { get; set; }

    [JsonPropertyName("current_balance")]
    public decimal CurrentBalance { get; set; }

    [JsonPropertyName("is_active")]
    public bool IsActive { get; set; } = true;

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class AccExpenseCategory
{
    [JsonPropertyName("expense_category_no")]
    public long ExpenseCategoryNo { get; set; }

    [JsonPropertyName("pharmacy_no")]
    public long PharmacyNo { get; set; }

    [JsonPropertyName("branch_no")]
    public long BranchNo { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("is_active")]
    public bool IsActive { get; set; } = true;

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class AccExpense
{
    [JsonPropertyName("expense_no")]
    public long ExpenseNo { get; set; }

    [JsonPropertyName("pharmacy_no")]
    public long PharmacyNo { get; set; }

    [JsonPropertyName("branch_no")]
    public long BranchNo { get; set; }

    [JsonPropertyName("voucher_no")]
    public string VoucherNo { get; set; } = string.Empty;

    [JsonPropertyName("expense_category_no")]
    public long ExpenseCategoryNo { get; set; }

    [JsonPropertyName("account_no")]
    public long AccountNo { get; set; }

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("payment_type")]
    public string PaymentType { get; set; } = "Cash";

    [JsonPropertyName("note")]
    public string? Note { get; set; }

    [JsonPropertyName("created_by_user_no")]
    public long? CreatedByUserNo { get; set; }

    [JsonPropertyName("expense_date")]
    public DateTime ExpenseDate { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Joined
    [JsonPropertyName("category_name")]
    public string? CategoryName { get; set; }

    [JsonPropertyName("account_name")]
    public string? AccountName { get; set; }
}

public class AccIncomeCategory
{
    [JsonPropertyName("income_category_no")]
    public long IncomeCategoryNo { get; set; }

    [JsonPropertyName("pharmacy_no")]
    public long PharmacyNo { get; set; }

    [JsonPropertyName("branch_no")]
    public long BranchNo { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("is_active")]
    public bool IsActive { get; set; } = true;

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class AccIncome
{
    [JsonPropertyName("income_no")]
    public long IncomeNo { get; set; }

    [JsonPropertyName("pharmacy_no")]
    public long PharmacyNo { get; set; }

    [JsonPropertyName("branch_no")]
    public long BranchNo { get; set; }

    [JsonPropertyName("receipt_no")]
    public string ReceiptNo { get; set; } = string.Empty;

    [JsonPropertyName("income_category_no")]
    public long IncomeCategoryNo { get; set; }

    [JsonPropertyName("account_no")]
    public long AccountNo { get; set; }

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("payment_type")]
    public string PaymentType { get; set; } = "Cash";

    [JsonPropertyName("received_from")]
    public string? ReceivedFrom { get; set; }

    [JsonPropertyName("note")]
    public string? Note { get; set; }

    [JsonPropertyName("created_by_user_no")]
    public long? CreatedByUserNo { get; set; }

    [JsonPropertyName("income_date")]
    public DateTime IncomeDate { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Joined
    [JsonPropertyName("category_name")]
    public string? CategoryName { get; set; }

    [JsonPropertyName("account_name")]
    public string? AccountName { get; set; }
}

public class AccAdditionalBalance
{
    [JsonPropertyName("additional_balance_no")]
    public long AdditionalBalanceNo { get; set; }

    [JsonPropertyName("pharmacy_no")]
    public long PharmacyNo { get; set; }

    [JsonPropertyName("branch_no")]
    public long BranchNo { get; set; }

    [JsonPropertyName("account_no")]
    public long? AccountNo { get; set; }

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("entry_date")]
    public DateTime EntryDate { get; set; } = DateTime.UtcNow.Date;

    [JsonPropertyName("note")]
    public string? Note { get; set; }

    [JsonPropertyName("created_by_user_no")]
    public long? CreatedByUserNo { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Joined
    [JsonPropertyName("account_name")]
    public string? AccountName { get; set; }
}

public class AccReconciliation
{
    [JsonPropertyName("reconciliation_no")]
    public long ReconciliationNo { get; set; }

    [JsonPropertyName("pharmacy_no")]
    public long PharmacyNo { get; set; }

    [JsonPropertyName("branch_no")]
    public long BranchNo { get; set; }

    [JsonPropertyName("voucher_no")]
    public string VoucherNo { get; set; } = string.Empty;

    [JsonPropertyName("payment_from_account_no")]
    public long PaymentFromAccountNo { get; set; }

    [JsonPropertyName("payment_to_account_no")]
    public long PaymentToAccountNo { get; set; }

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("note")]
    public string? Note { get; set; }

    [JsonPropertyName("created_by_user_no")]
    public long? CreatedByUserNo { get; set; }

    [JsonPropertyName("transfer_date")]
    public DateTime TransferDate { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Joined
    [JsonPropertyName("from_account_name")]
    public string? FromAccountName { get; set; }

    [JsonPropertyName("to_account_name")]
    public string? ToAccountName { get; set; }
}

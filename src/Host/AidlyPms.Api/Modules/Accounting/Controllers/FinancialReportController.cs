using System.Text.Json.Serialization;
using AidlyPms.Api.Common;
using AidlyPms.Api.Common.Database;
using Dapper;
using Microsoft.AspNetCore.Mvc;

namespace AidlyPms.Api.Modules.Accounting.Controllers;

public class TrialBalanceRow
{
    [JsonPropertyName("account_no")]
    public long AccountNo { get; set; }

    [JsonPropertyName("account_code")]
    public string? AccountCode { get; set; }

    [JsonPropertyName("account_name")]
    public string AccountName { get; set; } = string.Empty;

    [JsonPropertyName("account_category")]
    public short AccountCategory { get; set; }

    [JsonPropertyName("total_debit")]
    public decimal TotalDebit { get; set; }

    [JsonPropertyName("total_credit")]
    public decimal TotalCredit { get; set; }

    [JsonPropertyName("net_balance")]
    public decimal NetBalance { get; set; }
}

public class LedgerStatementRow
{
    [JsonPropertyName("ledger_no")]
    public long LedgerNo { get; set; }

    [JsonPropertyName("transaction_date")]
    public DateTime TransactionDate { get; set; }

    [JsonPropertyName("transaction_type")]
    public short TransactionType { get; set; }

    [JsonPropertyName("source_document_type")]
    public string SourceDocumentType { get; set; } = string.Empty;

    [JsonPropertyName("source_document_no")]
    public string SourceDocumentNo { get; set; } = string.Empty;

    [JsonPropertyName("debit_amount")]
    public decimal DebitAmount { get; set; }

    [JsonPropertyName("credit_amount")]
    public decimal CreditAmount { get; set; }

    [JsonPropertyName("balance_after")]
    public decimal BalanceAfter { get; set; }

    [JsonPropertyName("narration")]
    public string? Narration { get; set; }
}

[Route("api/v1/acc")]
public class FinancialReportController : BaseController
{
    private readonly IDbConnectionFactory _db;

    public FinancialReportController(IDbConnectionFactory db)
    {
        _db = db;
    }

    [HttpGet("trial-balance")]
    public async Task<ActionResult<ApiResponse<List<TrialBalanceRow>>>> GetTrialBalance(
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate)
    {
        using var conn = _db.CreateConnection();
        var sql = @"
            SELECT a.account_no, a.account_code, a.account_name, a.account_category,
                   COALESCE(SUM(l.debit_amount), 0) AS total_debit,
                   COALESCE(SUM(l.credit_amount), 0) AS total_credit,
                   CASE 
                     WHEN a.account_category IN (1, 5) THEN COALESCE(SUM(l.debit_amount), 0) - COALESCE(SUM(l.credit_amount), 0)
                     ELSE COALESCE(SUM(l.credit_amount), 0) - COALESCE(SUM(l.debit_amount), 0)
                   END AS net_balance
            FROM acc_transaction_accounts a
            LEFT JOIN acc_transaction_ledger l 
              ON a.account_no = l.account_no
              AND l.pharmacy_no = @CurrentPharmacyNo AND l.branch_no = @CurrentBranchNo";

        var pms = new DynamicParameters();
        pms.Add("CurrentPharmacyNo", CurrentPharmacyNo);
        pms.Add("CurrentBranchNo", CurrentBranchNo);

        if (fromDate.HasValue)
        {
            sql += " AND l.transaction_date >= @FromDate";
            pms.Add("FromDate", fromDate.Value.ToUniversalTime());
        }

        if (toDate.HasValue)
        {
            sql += " AND l.transaction_date <= @ToDate";
            pms.Add("ToDate", toDate.Value.ToUniversalTime());
        }

        sql += @"
            WHERE a.pharmacy_no = @CurrentPharmacyNo AND a.branch_no = @CurrentBranchNo
            GROUP BY a.account_no, a.account_code, a.account_name, a.account_category
            ORDER BY a.account_category ASC, a.account_code ASC;";

        var rows = (await conn.QueryAsync<TrialBalanceRow>(sql, pms)).ToList();
        return OkResponse(rows);
    }

    [HttpGet("ledger/{accountNo}")]
    public async Task<ActionResult<ApiResponse<List<LedgerStatementRow>>>> GetAccountLedger(
        long accountNo,
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate)
    {
        using var conn = _db.CreateConnection();
        var sql = @"
            SELECT ledger_no, transaction_date, transaction_type,
                   source_document_type, source_document_no,
                   debit_amount, credit_amount, balance_after, narration
            FROM acc_transaction_ledger
            WHERE account_no = @AccountNo AND pharmacy_no = @CurrentPharmacyNo AND branch_no = @CurrentBranchNo";

        var pms = new DynamicParameters();
        pms.Add("AccountNo", accountNo);
        pms.Add("CurrentPharmacyNo", CurrentPharmacyNo);
        pms.Add("CurrentBranchNo", CurrentBranchNo);

        if (fromDate.HasValue)
        {
            sql += " AND transaction_date >= @FromDate";
            pms.Add("FromDate", fromDate.Value.ToUniversalTime());
        }

        if (toDate.HasValue)
        {
            sql += " AND transaction_date <= @ToDate";
            pms.Add("ToDate", toDate.Value.ToUniversalTime());
        }

        sql += " ORDER BY ledger_no ASC LIMIT 500;";
        var rows = (await conn.QueryAsync<LedgerStatementRow>(sql, pms)).ToList();

        return OkResponse(rows);
    }
}

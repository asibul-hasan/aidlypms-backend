using Dapper;
using Npgsql;

namespace AidlyPms.Api.Infrastructure;

public class LedgerLeg
{
    public long AccountNo { get; set; }
    public decimal DebitAmount { get; set; }
    public decimal CreditAmount { get; set; }
    public short? PartyType { get; set; }
    public long? PartyNo { get; set; }
    public string? Narration { get; set; }
}

public class CompoundJournalEntry
{
    public long PharmacyNo { get; set; }
    public long BranchNo { get; set; }
    public DateTime TransactionDate { get; set; } = DateTime.UtcNow;
    public short TransactionType { get; set; }
    public string SourceDocumentType { get; set; } = string.Empty;
    public string SourceDocumentNo { get; set; } = string.Empty;
    public long SourceDocumentId { get; set; }
    public long CreatedByUserNo { get; set; }
    public List<LedgerLeg> Legs { get; set; } = new();
}

public interface ILedgerPostingService
{
    Task<long> PostJournalAsync(NpgsqlConnection conn, NpgsqlTransaction tran, CompoundJournalEntry entry);
}

public class LedgerPostingService : ILedgerPostingService
{
    public async Task<long> PostJournalAsync(NpgsqlConnection conn, NpgsqlTransaction tran, CompoundJournalEntry entry)
    {
        if (entry.Legs == null || entry.Legs.Count == 0)
        {
            throw new ArgumentException("A journal entry must contain at least one debit and one credit leg.");
        }

        var totalDebit = entry.Legs.Sum(l => l.DebitAmount);
        var totalCredit = entry.Legs.Sum(l => l.CreditAmount);

        if (Math.Round(totalDebit, 4) != Math.Round(totalCredit, 4))
        {
            throw new InvalidOperationException($"Double-entry imbalance! Total Debit ({totalDebit}) != Total Credit ({totalCredit}).");
        }

        // Generate next journal_no
        var journalNo = await conn.ExecuteScalarAsync<long>(
            "SELECT COALESCE(MAX(journal_no), 0) + 1 FROM acc_transaction_ledger;", transaction: tran);

        foreach (var leg in entry.Legs)
        {
            if (leg.DebitAmount == 0 && leg.CreditAmount == 0) continue;

            // Fetch account category to calculate balance_after accurately
            var acc = await conn.QuerySingleOrDefaultAsync<(short Category, decimal CurrentBalance)>(@"
                SELECT account_category AS Category, current_balance AS CurrentBalance
                FROM acc_transaction_accounts
                WHERE account_no = @AccountNo AND pharmacy_no = @PharmacyNo AND branch_no = @BranchNo
                FOR UPDATE;",
                new { leg.AccountNo, entry.PharmacyNo, entry.BranchNo },
                transaction: tran);

            if (acc.Category == 0)
            {
                throw new InvalidOperationException($"Account {leg.AccountNo} not found in pharmacy {entry.PharmacyNo} branch {entry.BranchNo}.");
            }

            decimal newBalance;
            // 1: ASSET, 2: LIABILITY, 3: EQUITY, 4: REVENUE, 5: EXPENSE
            if (acc.Category == 1 || acc.Category == 5)
            {
                newBalance = acc.CurrentBalance + leg.DebitAmount - leg.CreditAmount;
            }
            else
            {
                newBalance = acc.CurrentBalance + leg.CreditAmount - leg.DebitAmount;
            }

            // Update account balance
            await conn.ExecuteAsync(@"
                UPDATE acc_transaction_accounts
                SET current_balance = @newBalance, updated_at = CURRENT_TIMESTAMP
                WHERE account_no = @AccountNo;",
                new { newBalance, leg.AccountNo },
                transaction: tran);

            // Generate ledger_no
            var ledgerNo = await conn.ExecuteScalarAsync<long>(
                "SELECT COALESCE(MAX(ledger_no), 0) + 1 FROM acc_transaction_ledger;", transaction: tran);

            // Append ledger entry
            const string insertLedger = @"
                INSERT INTO acc_transaction_ledger (
                    ledger_no, pharmacy_no, branch_no, journal_no, account_no, transaction_date,
                    transaction_type, debit_amount, credit_amount, balance_after,
                    source_document_type, source_document_no, source_document_id,
                    party_type, party_no, narration, created_by_user_no, created_at
                ) VALUES (
                    @ledgerNo, @PharmacyNo, @BranchNo, @journalNo, @AccountNo, @TransactionDate,
                    @TransactionType, @DebitAmount, @CreditAmount, @newBalance,
                    @SourceDocumentType, @SourceDocumentNo, @SourceDocumentId,
                    @PartyType, @PartyNo, @Narration, @CreatedByUserNo, CURRENT_TIMESTAMP
                );";

            await conn.ExecuteAsync(insertLedger, new
            {
                ledgerNo,
                entry.PharmacyNo,
                entry.BranchNo,
                journalNo,
                leg.AccountNo,
                entry.TransactionDate,
                entry.TransactionType,
                leg.DebitAmount,
                leg.CreditAmount,
                newBalance,
                entry.SourceDocumentType,
                entry.SourceDocumentNo,
                entry.SourceDocumentId,
                leg.PartyType,
                leg.PartyNo,
                leg.Narration,
                entry.CreatedByUserNo
            }, transaction: tran);
        }

        return journalNo;
    }
}

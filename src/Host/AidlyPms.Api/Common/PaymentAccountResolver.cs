using AidlyPms.Api.Common.Database;
using Dapper;
using Npgsql;

namespace AidlyPms.Api.Common;

public interface IPaymentAccountResolver
{
    Task<long> ResolveAccountNoAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction? tran,
        long pharmacyNo,
        long branchNo,
        short paymentMethod);

    Task<long> ResolveAccountNoAsync(
        long pharmacyNo,
        long branchNo,
        short paymentMethod);
}

public class PaymentAccountResolver : IPaymentAccountResolver
{
    private readonly IDbConnectionFactory _db;

    public const string NoAccountErrorMessage = "No account configured for this payment method. Add one in Accounts.";

    public PaymentAccountResolver(IDbConnectionFactory db)
    {
        _db = db;
    }

    public async Task<long> ResolveAccountNoAsync(
        long pharmacyNo,
        long branchNo,
        short paymentMethod)
    {
        await using var conn = await _db.CreateOpenConnectionAsync();
        return await ResolveAccountNoAsync(conn, null, pharmacyNo, branchNo, paymentMethod);
    }

    public async Task<long> ResolveAccountNoAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction? tran,
        long pharmacyNo,
        long branchNo,
        short paymentMethod)
    {
        long? resolvedAccountNo = null;

        // Return Credit clearing special case (method 9)
        if (paymentMethod == 9)
        {
            resolvedAccountNo = await conn.ExecuteScalarAsync<long?>(@"
                SELECT account_no
                FROM acc_transaction_accounts
                WHERE (account_code = '10901' OR account_name = 'Return Credit Clearing' OR account_name = 'Return Credit Clearing Account')
                  AND pharmacy_no = @pharmacyNo
                  AND branch_no = @branchNo
                  AND is_active = TRUE
                ORDER BY account_no ASC
                LIMIT 1;",
                new { pharmacyNo, branchNo },
                transaction: tran);

            if (resolvedAccountNo.HasValue && resolvedAccountNo.Value > 0)
            {
                return resolvedAccountNo.Value;
            }

            throw new PosSaleException("account_not_configured", NoAccountErrorMessage, 400);
        }

        // Map payment method to account_type(s)
        // Cash: 1, Bank: 2, bKash: 4 then 3, Nagad: 5, Rocket: 6, Card: 7
        short[] targetAccountTypes = paymentMethod switch
        {
            1 => [1],
            2 => [2],
            3 => [4, 3], // bKash: 4 (Merchant) first, then 3 (Personal)
            4 => [5],    // Nagad: 5
            5 => [6],    // Rocket: 6
            6 => [7],    // Card: 7
            _ => Array.Empty<short>()
        };

        foreach (var accountType in targetAccountTypes)
        {
            resolvedAccountNo = await conn.ExecuteScalarAsync<long?>(@"
                SELECT account_no
                FROM acc_transaction_accounts
                WHERE pharmacy_no = @pharmacyNo
                  AND branch_no = @branchNo
                  AND is_active = TRUE
                  AND account_type = @accountType
                ORDER BY account_no ASC
                LIMIT 1;",
                new { pharmacyNo, branchNo, accountType },
                transaction: tran);

            if (resolvedAccountNo.HasValue && resolvedAccountNo.Value > 0)
            {
                return resolvedAccountNo.Value;
            }
        }

        // If none exists, never fall back to cash. Throw 400 error.
        throw new PosSaleException("account_not_configured", NoAccountErrorMessage, 400);
    }
}

using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;

namespace AidlyPms.Api.Common.Security;

public sealed class LoginThrottle : IDisposable
{
    private readonly PartitionedRateLimiter<string> _accounts = PartitionedRateLimiter.Create<string, string>(identifier =>
        RateLimitPartition.GetFixedWindowLimiter(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identifier.Trim().ToLowerInvariant().Replace("+88", "")))),
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
    public bool Permit(string identifier)
    {
        using var lease = _accounts.AttemptAcquire(identifier);
        return lease.IsAcquired;
    }
    public void Dispose() => _accounts.Dispose();
}

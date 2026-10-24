using AidlyPms.Api.Common.Database;
using Dapper;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace AidlyPms.Api.Infrastructure;

public interface IExpiryMaintenanceService
{
    Task<int> RunExpirySweepAsync(CancellationToken ct = default);
}

public class ExpiryMaintenanceService : BackgroundService, IExpiryMaintenanceService
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<ExpiryMaintenanceService> _logger;
    private readonly IConfiguration _config;
    public const long AdvisoryLockId = 849201948271049282;

    public ExpiryMaintenanceService(
        IDbConnectionFactory db,
        ILogger<ExpiryMaintenanceService> logger,
        IConfiguration config)
    {
        _db = db;
        _logger = logger;
        _config = config;
    }

    public async Task<int> RunExpirySweepAsync(CancellationToken ct = default)
    {
        await using var conn = await _db.CreateOpenConnectionAsync();

        var acquired = await conn.ExecuteScalarAsync<bool>(
            "SELECT pg_try_advisory_lock(@LockId);",
            new { LockId = AdvisoryLockId });

        if (!acquired)
        {
            _logger.LogInformation("ExpiryMaintenanceService: Advisory lock {LockId} is held by another worker instance. Skipping sweep.", AdvisoryLockId);
            return 0;
        }

        try
        {
            var dhakaDate = DateTime.UtcNow.AddHours(6).Date;

            const string updateSql = @"
                UPDATE prod_product_batches
                SET is_expired = TRUE,
                    updated_at = NOW()
                WHERE is_expired = FALSE
                  AND is_placeholder = FALSE
                  AND expiry_date < @dhakaDate;";

            var affected = await conn.ExecuteAsync(updateSql, new { dhakaDate });
            _logger.LogInformation(
                "ExpiryMaintenanceService: Daily maintenance sweep completed for Dhaka date {DhakaDate:yyyy-MM-dd}. Marked {Rows} batch(es) as expired.",
                dhakaDate, affected);

            return affected;
        }
        finally
        {
            await conn.ExecuteAsync("SELECT pg_advisory_unlock(@LockId);", new { LockId = AdvisoryLockId });
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var runBgStr = _config["RUN_BACKGROUND_JOBS"] ?? Environment.GetEnvironmentVariable("RUN_BACKGROUND_JOBS");
        var runBg = !string.Equals(runBgStr, "false", StringComparison.OrdinalIgnoreCase);

        if (!runBg)
        {
            _logger.LogInformation("ExpiryMaintenanceService is disabled (RUN_BACKGROUND_JOBS is false).");
            return;
        }

        // Run once at startup
        try
        {
            await RunExpirySweepAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ExpiryMaintenanceService: Error executing startup expiry sweep.");
        }

        // Run daily at 02:00 Asia/Dhaka (UTC+6)
        while (!stoppingToken.IsCancellationRequested)
        {
            var dhakaNow = DateTime.UtcNow.AddHours(6);
            var next02 = dhakaNow.Date.AddHours(2);
            if (dhakaNow >= next02)
            {
                next02 = next02.AddDays(1);
            }

            var delay = next02 - dhakaNow;
            try
            {
                await Task.Delay(delay, stoppingToken);
                await RunExpirySweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ExpiryMaintenanceService: Error executing scheduled daily expiry sweep.");
            }
        }
    }
}

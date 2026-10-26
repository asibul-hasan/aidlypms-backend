using AidlyPms.Api.Common;
using AidlyPms.Api.Common.Database;
using AidlyPms.Api.Infrastructure;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AidlyPms.Api.Controllers;

[ApiController]
[Route("api/v1/sys")]
[AllowAnonymous]
public class SysController : ControllerBase
{
    private readonly IDbConnectionFactory _db;

    public SysController(IDbConnectionFactory db)
    {
        _db = db;
    }

    [HttpGet("migrations")]
    public async Task<ActionResult<ApiResponse<SysMigrationsResponse>>> GetMigrations()
    {
        try
        {
            await using var conn = await _db.CreateOpenConnectionAsync();
            var applied = (await conn.QueryAsync<SysMigrationItem>(@"
                SELECT migration_id, applied_at
                FROM __schema_migrations
                ORDER BY applied_at ASC;")).ToList();

            var response = new SysMigrationsResponse
            {
                TotalApplied = applied.Count,
                AppliedMigrations = applied,
                LastError = DatabaseMigrationRunner.LastError,
                Status = string.IsNullOrWhiteSpace(DatabaseMigrationRunner.LastError) ? "Healthy" : "Degraded"
            };

            return Ok(ApiResponse<SysMigrationsResponse>.Ok(response, "System migrations status retrieved successfully."));
        }
        catch (Exception)
        {
            // Fallback to in-memory tracking without leaking DB connection strings or raw exceptions
            var inMemoryApplied = DatabaseMigrationRunner.AppliedMigrations
                .Select(m => new SysMigrationItem { MigrationId = m.MigrationId, AppliedAt = m.AppliedAt })
                .ToList();

            var response = new SysMigrationsResponse
            {
                TotalApplied = inMemoryApplied.Count,
                AppliedMigrations = inMemoryApplied,
                LastError = DatabaseMigrationRunner.LastError ?? "Unable to query __schema_migrations table.",
                Status = "Error"
            };

            return Ok(ApiResponse<SysMigrationsResponse>.Ok(response, "System migrations status retrieved from in-memory cache."));
        }
    }
}

public class SysMigrationsResponse
{
    public int TotalApplied { get; set; }
    public List<SysMigrationItem> AppliedMigrations { get; set; } = new();
    public string? LastError { get; set; }
    public string Status { get; set; } = "Healthy";
}

public class SysMigrationItem
{
    public string MigrationId { get; set; } = string.Empty;
    public DateTime AppliedAt { get; set; }
}

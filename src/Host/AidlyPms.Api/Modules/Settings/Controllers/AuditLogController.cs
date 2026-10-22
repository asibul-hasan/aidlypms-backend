using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using AidlyPms.Api.Common;
using AidlyPms.Api.Common.Database;
using Dapper;
using Microsoft.AspNetCore.Mvc;

namespace AidlyPms.Api.Modules.Settings.Controllers;

public class AuditLogDto
{
    [JsonPropertyName("audit_no")]
    public long AuditNo { get; set; }

    [JsonPropertyName("pharmacy_no")]
    public long PharmacyNo { get; set; }

    [JsonPropertyName("branch_no")]
    public long BranchNo { get; set; }

    [JsonPropertyName("user_no")]
    public long? UserNo { get; set; }

    [JsonPropertyName("user_name")]
    public string? UserName { get; set; }

    [JsonPropertyName("table_name")]
    public string TableName { get; set; } = string.Empty;

    [JsonPropertyName("record_id")]
    public long RecordId { get; set; }

    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    [JsonPropertyName("old_data")]
    public string? OldData { get; set; }

    [JsonPropertyName("new_data")]
    public string? NewData { get; set; }

    [JsonPropertyName("ip_address")]
    public string? IpAddress { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }
}

[Route("api/v1/audit/logs")]
[Route("api/v1/sys/audit-logs")]
[RequirePermission("Pharmacy-Module")]
public class AuditLogController : BaseController
{
    private readonly IDbConnectionFactory _db;

    public AuditLogController(IDbConnectionFactory db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<AuditLogDto>>>> GetAuditLogs(
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        [FromQuery] long? userNo = null,
        [FromQuery] string? tableName = null,
        [FromQuery] string? action = null,
        [FromQuery] string? search = null)
    {
        pageIndex = Math.Max(1, pageIndex);
        pageSize = Math.Clamp(pageSize, 1, 200);

        (fromDate, toDate) = DateHelper.NormalizeDateRange(fromDate, toDate);

        using var conn = _db.CreateConnection();

        var p = new DynamicParameters();
        p.Add("CurrentPharmacyNo", CurrentPharmacyNo);
        p.Add("CurrentBranchNo", CurrentBranchNo);

        var whereClause = "WHERE a.pharmacy_no = @CurrentPharmacyNo";

        if (fromDate.HasValue)
        {
            whereClause += " AND a.created_at >= @FromDate";
            p.Add("FromDate", fromDate.Value);
        }

        if (toDate.HasValue)
        {
            whereClause += " AND a.created_at <= @ToDate";
            p.Add("ToDate", toDate.Value);
        }

        if (userNo.HasValue)
        {
            whereClause += " AND a.user_no = @UserNo";
            p.Add("UserNo", userNo.Value);
        }

        if (!string.IsNullOrWhiteSpace(tableName))
        {
            whereClause += " AND a.table_name ILIKE @TableName";
            p.Add("TableName", $"%{tableName.Trim()}%");
        }

        if (!string.IsNullOrWhiteSpace(action))
        {
            whereClause += " AND a.action ILIKE @Action";
            p.Add("Action", action.Trim());
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            whereClause += " AND (a.table_name ILIKE @Search OR a.action ILIKE @Search OR a.new_data ILIKE @Search OR a.old_data ILIKE @Search)";
            p.Add("Search", $"%{search.Trim()}%");
        }

        var countSql = $"SELECT COUNT(1) FROM sys_audit_logs a {whereClause};";
        var total = await conn.ExecuteScalarAsync<int>(countSql, p);

        var offset = (pageIndex - 1) * pageSize;
        p.Add("Offset", offset);
        p.Add("PageSize", pageSize);

        var sql = $@"
            SELECT 
                a.audit_no, a.pharmacy_no, a.branch_no, a.user_no,
                a.table_name, a.record_id, a.action, a.old_data,
                a.new_data, a.ip_address, a.created_at,
                TRIM(CONCAT(u.first_name, ' ', u.last_name)) AS user_name
            FROM sys_audit_logs a
            LEFT JOIN sys_users u ON a.user_no = u.user_no
            {whereClause}
            ORDER BY a.created_at DESC
            OFFSET @Offset LIMIT @PageSize;";

        var rows = (await conn.QueryAsync<AuditLogDto>(sql, p)).ToList();

        // Sanitize sensitive values before returning
        foreach (var r in rows)
        {
            r.OldData = SanitizeJsonData(r.OldData);
            r.NewData = SanitizeJsonData(r.NewData);
        }

        var paged = new PagedResult<AuditLogDto>(rows, total, pageIndex, pageSize);
        return OkResponse(paged);
    }

    private static string? SanitizeJsonData(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return json;
        try
        {
            var node = JsonNode.Parse(json);
            if (node != null)
            {
                AuditActionFilter.StripSensitiveProperties(node);
                return node.ToJsonString();
            }
        }
        catch
        {
            return System.Text.RegularExpressions.Regex.Replace(
                json,
                @"""(password|token|hash|secret|pin|cvv)""\s*:\s*""[^""]*""",
                @"""$1"": ""[REDACTED]""",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        return json;
    }
}

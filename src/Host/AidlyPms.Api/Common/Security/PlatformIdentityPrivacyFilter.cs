using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace AidlyPms.Api.Common.Security;

// Tenant responses hide platform identities even when operational records reference their IDs.
// Platform audit records remain complete in the database and visible to the platform owner.
public sealed class PlatformIdentityPrivacyFilter(IOptions<JsonOptions> options) : IAsyncResultFilter
{
    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        var access = RequestAccess.From(context.HttpContext);
        if (access is { IsPlatformOwner: false } && context.Result is ObjectResult { Value: not null } result && access.PlatformIdentities.Count > 0)
        {
            var node = JsonSerializer.SerializeToNode(result.Value, options.Value.JsonSerializerOptions);
            Redact(node, access.PlatformIdentities);
            result.Value = node;
        }
        await next();
    }

    public static void Redact(JsonNode? node, IReadOnlyList<PlatformIdentity> identities)
    {
        if (node is JsonArray array)
        {
            foreach (var item in array) Redact(item, identities);
            return;
        }
        if (node is not JsonObject obj) return;
        var hasProtectedActor = obj.Any(pair => IsUserId(pair.Key) && Number(pair.Value, out var value) && identities.Any(i => i.UserNo == value));
        var privateAuditTarget = obj["table_name"] is JsonValue tableValue && tableValue.TryGetValue<string>(out var tableName)
            && Number(obj["record_id"], out var recordId)
            && ((tableName == "sys_users" && identities.Any(i => i.UserNo == recordId)) || (tableName == "sys_roles" && identities.Any(i => i.RoleNo == recordId)));
        var protectedRole = obj.Any(pair => pair.Key == "role_no" && Number(pair.Value, out var value) && identities.Any(i => i.RoleNo == value));
        foreach (var pair in obj.ToList())
        {
            if (privateAuditTarget && pair.Key is "record_id" or "new_data" or "old_data")
            {
                obj[pair.Key] = null;
                continue;
            }
            if ((IsUserId(pair.Key) && Number(pair.Value, out var userNo) && identities.Any(i => i.UserNo == userNo)) ||
                (pair.Key == "role_no" && protectedRole))
            {
                obj[pair.Key] = null;
                continue;
            }
            if (pair.Value is JsonValue scalar && scalar.TryGetValue<string>(out var text))
            {
                if ((hasProtectedActor || protectedRole) && (pair.Key is "first_name" or "last_name" or "phone" or "email" or "role_name" or "alias" or "ip_address" || pair.Key.EndsWith("user_name", StringComparison.OrdinalIgnoreCase)))
                    obj[pair.Key] = "Platform support";
                else if (identities.Any(i => !string.IsNullOrWhiteSpace(i.DisplayName) && text == i.DisplayName))
                    obj[pair.Key] = "Platform support";
                else if (pair.Key is "new_data" or "old_data")
                {
                    try { var nested = JsonNode.Parse(text); Redact(nested, identities); obj[pair.Key] = nested?.ToJsonString(); }
                    catch (JsonException) { obj[pair.Key] = null; }
                }
            }
            else Redact(pair.Value, identities);
        }
    }
    private static bool IsUserId(string key) => key.Equals("user_no", StringComparison.OrdinalIgnoreCase) || key.EndsWith("_user_no", StringComparison.OrdinalIgnoreCase) || key is "started_by" or "posted_by";
    private static bool Number(JsonNode? node, out long value)
    {
        value = 0;
        return node is JsonValue scalar && scalar.TryGetValue<long>(out value);
    }
}

public sealed class PlatformIdentity
{
    public long UserNo { get; set; }
    public long RoleNo { get; set; }
    public string DisplayName { get; set; } = "";
}

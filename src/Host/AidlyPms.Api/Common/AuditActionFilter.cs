using System.Text.Json;
using System.Text.Json.Nodes;
using AidlyPms.Api.Common.Database;
using Dapper;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AidlyPms.Api.Common;

public class AuditActionFilter : IAsyncActionFilter
{
    private static readonly HashSet<string> MutatingMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "POST", "PUT", "DELETE", "PATCH"
    };

    private static readonly HashSet<string> ExcludedPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/v1/auth/login",
        "/api/v1/auth/refresh",
        "/api/v1/auth/forgot-password",
        "/health",
        "/api/health/db"
    };

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var httpContext = context.HttpContext;
        var method = httpContext.Request.Method;
        var path = httpContext.Request.Path.Value ?? string.Empty;

        // Skip non-mutating methods and excluded authentication paths
        if (!MutatingMethods.Contains(method) || ExcludedPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            await next();
            return;
        }

        // Capture incoming payload arguments before execution and strip sensitive properties
        string? payloadJson = null;
        try
        {
            if (context.ActionArguments.Count > 0)
            {
                var node = JsonSerializer.SerializeToNode(context.ActionArguments, new JsonSerializerOptions
                {
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                });
                StripSensitiveProperties(node);
                payloadJson = node?.ToJsonString();
            }
        }
        catch
        {
            // Do not block execution on serialization errors
        }

        var executedContext = await next();

        // Only log successful operations (status code < 400 and no unhandled exception)
        if (executedContext.Exception == null && httpContext.Response.StatusCode < 400)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var services = httpContext.RequestServices;
                    var dbFactory = services.GetService<IDbConnectionFactory>();
                    var tenant = services.GetService<ITenantContext>();

                    if (dbFactory == null || tenant == null) return;

                    var controllerName = context.Controller.GetType().Name.Replace("Controller", "");
                    var tableName = ResolveTableName(controllerName);
                    var actionType = method.ToUpperInvariant() switch
                    {
                        "POST" => "CREATE",
                        "PUT" => "UPDATE",
                        "PATCH" => "UPDATE",
                        "DELETE" => "DELETE",
                        _ => method.ToUpperInvariant()
                    };

                    // Extract record ID from route values if present
                    long recordId = 0;
                    foreach (var key in new[] { "id", "no", "productNo", "invoiceNo", "purchaseNo", "adjustmentNo", "transferNo" })
                    {
                        if (context.RouteData.Values.TryGetValue(key, out var val) && long.TryParse(val?.ToString(), out var parsedId))
                        {
                            recordId = parsedId;
                            break;
                        }
                    }

                    var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();

                    using var conn = dbFactory.CreateConnection();
                    const string insertSql = @"
                        INSERT INTO sys_audit_logs (
                            audit_no, pharmacy_no, branch_no, user_no, table_name, record_id, action, old_data, new_data, ip_address, created_at
                        ) VALUES (
                            nextval('seq_sys_audit_no'),
                            @PharmacyNo, @BranchNo, @UserNo, @TableName, @RecordId, @Action, NULL, @NewData, @IpAddress, CURRENT_TIMESTAMP
                        );";

                    await conn.ExecuteAsync(insertSql, new
                    {
                        PharmacyNo = tenant.PharmacyNo,
                        BranchNo = tenant.BranchNo,
                        UserNo = tenant.UserNo > 0 ? (long?)tenant.UserNo : null,
                        TableName = tableName,
                        RecordId = recordId,
                        Action = actionType,
                        NewData = payloadJson,
                        IpAddress = ipAddress
                    });
                }
                catch
                {
                    // Audit logging must be non-blocking and fail-safe
                }
            });
        }
    }

    public static void StripSensitiveProperties(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            var keysToRemove = new List<string>();
            foreach (var kvp in obj)
            {
                var key = kvp.Key;
                if (key.Contains("password", StringComparison.OrdinalIgnoreCase) ||
                    key.Contains("token", StringComparison.OrdinalIgnoreCase) ||
                    key.Contains("hash", StringComparison.OrdinalIgnoreCase) ||
                    key.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
                    key.Contains("pin", StringComparison.OrdinalIgnoreCase) ||
                    key.Contains("cvv", StringComparison.OrdinalIgnoreCase))
                {
                    keysToRemove.Add(key);
                }
                else
                {
                    StripSensitiveProperties(kvp.Value);
                }
            }

            foreach (var k in keysToRemove)
            {
                obj.Remove(k);
            }
        }
        else if (node is JsonArray arr)
        {
            foreach (var item in arr)
            {
                StripSensitiveProperties(item);
            }
        }
    }

    private static string ResolveTableName(string controllerName) => controllerName.ToLowerInvariant() switch
    {
        "product" => "prod_products",
        "possale" => "sale_invoices",
        "salereturn" => "sale_returns",
        "purchase" => "pur_purchase_invoices",
        "stockadjustment" => "inv_stock_adjustments",
        "stocktransfer" => "inv_stock_transfers",
        "company" => "prod_companies",
        "generic" => "prod_generics",
        "purchasereturn" => "pur_purchase_returns",
        "customer" => "cust_customers",
        "supplier" => "supp_suppliers",
        "expense" => "acc_expenses",
        "cashaccount" or "account" => "acc_transaction_accounts",
        "doctor" => "doc_doctors",
        "branch" => "sys_branches",
        "pharmacy" => "sys_pharmacies",
        _ => $"sys_{controllerName.ToLowerInvariant()}"
    };
}

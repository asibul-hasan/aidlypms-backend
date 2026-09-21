using System.Text.Json.Serialization;

namespace AidlyPms.Api.Common;

public class ApiResponse<T>
{
    [JsonPropertyName("success")]
    public bool Success { get; set; } = true;

    [JsonPropertyName("message")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Message { get; set; }

    [JsonPropertyName("data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public T? Data { get; set; }

    [JsonPropertyName("errors")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Errors { get; set; }

    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = DateTime.UtcNow.ToString("o");

    public static ApiResponse<T> Ok(T data, string? message = null)
    {
        return new ApiResponse<T>
        {
            Success = true,
            Data = data,
            Message = message
        };
    }

    public static ApiResponse<T> Fail(string error, string? message = null)
    {
        return new ApiResponse<T>
        {
            Success = false,
            Message = message ?? error,
            Errors = new List<string> { error }
        };
    }

    public static ApiResponse<T> Fail(List<string> errors, string? message = null)
    {
        return new ApiResponse<T>
        {
            Success = false,
            Message = message ?? (errors.Count > 0 ? errors[0] : "An error occurred"),
            Errors = errors
        };
    }
}

public class PagedResult<T>
{
    [JsonPropertyName("items")]
    public IReadOnlyList<T> Items { get; set; } = Array.Empty<T>();

    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("pageIndex")]
    public int PageIndex { get; set; } = 1;

    [JsonPropertyName("pageSize")]
    public int PageSize { get; set; } = 20;

    [JsonPropertyName("totalPages")]
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)Total / PageSize) : 0;

    public PagedResult() { }

    public PagedResult(IReadOnlyList<T> items, int total, int pageIndex, int pageSize)
    {
        Items = items;
        Total = total;
        PageIndex = pageIndex;
        PageSize = pageSize;
    }
}

public class QueryFilter
{
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? Search { get; set; }
    public string? SortBy { get; set; }
    public string? SortOrder { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public int? Status { get; set; }
    public long? CompanyNo { get; set; }
    public long? GenericNo { get; set; }
}

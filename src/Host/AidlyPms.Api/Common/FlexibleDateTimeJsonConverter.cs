using System.Text.Json;
using System.Text.Json.Serialization;

namespace AidlyPms.Api.Common;

public class FlexibleDateTimeJsonConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var str = reader.GetString();
        if (string.IsNullOrWhiteSpace(str))
            return default;

        if (DateHelper.TryParse(str, out var dt))
            return dt;

        if (DateTime.TryParse(str, out var fallback))
            return fallback;

        throw new JsonException($"Unable to parse date '{str}'. Expected format DD-MM-YYYY, DD/MM/YYYY, or ISO-8601.");
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
    }
}

public class FlexibleNullableDateTimeJsonConverter : JsonConverter<DateTime?>
{
    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        var str = reader.GetString();
        if (string.IsNullOrWhiteSpace(str))
            return null;

        if (DateHelper.TryParse(str, out var dt))
            return dt;

        if (DateTime.TryParse(str, out var fallback))
            return fallback;

        throw new JsonException($"Unable to parse date '{str}'. Expected format DD-MM-YYYY, DD/MM/YYYY, or ISO-8601.");
    }

    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
            writer.WriteStringValue(value.Value.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
        else
            writer.WriteNullValue();
    }
}

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pss.BuildingBlocks.Api;

/// <summary>
/// One JSON configuration for HTTP, Redis drafts and service-to-service calls (blueprint §8.5):
/// camelCase, enums as ints (accept int or name), lenient dates ("2023-9-1"), dictionary keys untouched, comments allowed.
/// </summary>
public static class PssJson
{
    public static JsonSerializerOptions Options { get; } = Create();

    public static JsonSerializerOptions Create()
    {
        var o = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Configure(o);
        return o;
    }

    public static void Configure(JsonSerializerOptions o)
    {
        o.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        o.DictionaryKeyPolicy = null;
        o.PropertyNameCaseInsensitive = true;
        o.NumberHandling = JsonNumberHandling.AllowReadingFromString;
        o.ReadCommentHandling = JsonCommentHandling.Skip;
        o.AllowTrailingCommas = true;
        if (!o.Converters.OfType<FlexibleEnumConverterFactory>().Any()) o.Converters.Add(new FlexibleEnumConverterFactory());
        if (!o.Converters.OfType<FlexibleDateTimeConverter>().Any()) o.Converters.Add(new FlexibleDateTimeConverter());
    }
}

/// <summary>Writes enums as integers; reads integers, numeric strings or names (case-insensitive).</summary>
public sealed class FlexibleEnumConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        => (JsonConverter)Activator.CreateInstance(typeof(FlexibleEnumConverter<>).MakeGenericType(typeToConvert))!;
}

public sealed class FlexibleEnumConverter<T> : JsonConverter<T> where T : struct, Enum
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out var n))
            return (T)Enum.ToObject(typeof(T), n);

        if (reader.TokenType == JsonTokenType.String)
        {
            var s = reader.GetString();
            if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n2))
                return (T)Enum.ToObject(typeof(T), n2);
            if (!string.IsNullOrWhiteSpace(s) && Enum.TryParse<T>(s.Trim(), ignoreCase: true, out var v))
                return v;
        }

        throw new JsonException($"The value is not valid for {typeof(T).Name}.");
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        => writer.WriteNumberValue(Convert.ToInt64(value, CultureInfo.InvariantCulture));
}

/// <summary>
/// Accepts "yyyy-M-d", "yyyy-MM-dd" and full ISO 8601. UTC values are written with 'Z';
/// station-local / date-only values without offset (Navitaire style).
/// </summary>
public sealed class FlexibleDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String) throw new JsonException("A date string was expected.");
        var s = reader.GetString();
        if (!string.IsNullOrWhiteSpace(s) &&
            DateTime.TryParse(s.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
        {
            return dt.Kind == DateTimeKind.Local ? dt.ToUniversalTime() : dt;
        }
        throw new JsonException($"'{s}' is not a valid date.");
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        var format = value.Ticks % TimeSpan.TicksPerSecond == 0
            ? "yyyy'-'MM'-'dd'T'HH':'mm':'ss"
            : "yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'fffffff";
        var text = value.ToString(format, CultureInfo.InvariantCulture);
        writer.WriteStringValue(value.Kind == DateTimeKind.Utc ? text + "Z" : text);
    }
}

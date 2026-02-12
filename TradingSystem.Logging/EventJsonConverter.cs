using System.Text.Json;
using System.Text.Json.Serialization;
using TradingSystem.Core.Events;
using TradingSystem.Core.Models;

namespace TradingSystem.Logging;

public class EventJsonConverter : JsonConverter<IEvent>
{
    public override IEvent? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        if (!root.TryGetProperty("eventType", out var eventTypeProp))
            throw new JsonException("Missing 'eventType' property");

        var eventType = eventTypeProp.GetString();
        var json = root.GetRawText();

        return eventType switch
        {
            "Quote" => JsonSerializer.Deserialize<QuoteEvent>(json, options),
            "Bar" => JsonSerializer.Deserialize<BarEvent>(json, options),
            "Signal" => JsonSerializer.Deserialize<SignalEvent>(json, options),
            "Order" => JsonSerializer.Deserialize<OrderEvent>(json, options),
            "RiskDecision" => JsonSerializer.Deserialize<RiskDecision>(json, options),
            "Fill" => JsonSerializer.Deserialize<FillEvent>(json, options),
            "PortfolioSnapshot" => JsonSerializer.Deserialize<PortfolioSnapshot>(json, options),
            _ => throw new JsonException($"Unknown event type: {eventType}")
        };
    }

    public override void Write(Utf8JsonWriter writer, IEvent value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, value.GetType(), options);
    }
}

public static class EventSerializerOptions
{
    private static JsonSerializerOptions? _instance;

    public static JsonSerializerOptions Default => _instance ??= Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new TimeSpanJsonConverter());
        // Do NOT add EventJsonConverter here — it causes infinite recursion
        // when serializing concrete types. It's only used for deserialization.
        return options;
    }

    /// <summary>
    /// Options with the IEvent converter for deserialization.
    /// </summary>
    public static JsonSerializerOptions ForDeserialization
    {
        get
        {
            var options = new JsonSerializerOptions(Default);
            options.Converters.Add(new EventJsonConverter());
            return options;
        }
    }
}

/// <summary>
/// System.Text.Json doesn't handle TimeSpan natively — serialize as ISO 8601 duration string.
/// </summary>
public class TimeSpanJsonConverter : JsonConverter<TimeSpan>
{
    public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var str = reader.GetString();
        if (str == null) return TimeSpan.Zero;

        // Try parse ISO 8601 duration or standard TimeSpan format
        if (TimeSpan.TryParse(str, out var result))
            return result;

        return TimeSpan.Zero;
    }

    public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}

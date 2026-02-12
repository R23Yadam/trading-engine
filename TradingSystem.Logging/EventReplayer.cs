using System.Runtime.CompilerServices;
using System.Text.Json;
using TradingSystem.Core.Events;

namespace TradingSystem.Logging;

public class EventReplayer
{
    private readonly JsonSerializerOptions _jsonOptions;

    public EventReplayer()
    {
        _jsonOptions = EventSerializerOptions.ForDeserialization;
    }

    public async IAsyncEnumerable<IEvent> ReplaySession(
        string filePath,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var reader = new StreamReader(filePath);
        string? line;
        while ((line = await reader.ReadLineAsync(ct)) != null)
        {
            if (ct.IsCancellationRequested) yield break;
            if (string.IsNullOrWhiteSpace(line)) continue;

            IEvent? evt = null;
            try
            {
                evt = JsonSerializer.Deserialize<IEvent>(line, _jsonOptions);
            }
            catch (JsonException)
            {
                // Skip malformed lines
            }

            if (evt != null)
                yield return evt;
        }
    }

    /// <summary>
    /// Extract only QuoteEvents for deterministic replay.
    /// </summary>
    public async IAsyncEnumerable<QuoteEvent> ReplayQuotes(
        string filePath,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var evt in ReplaySession(filePath, ct))
        {
            if (evt is QuoteEvent quote)
                yield return quote;
        }
    }
}

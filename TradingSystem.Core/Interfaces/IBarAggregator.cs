using TradingSystem.Core.Events;

namespace TradingSystem.Core.Interfaces;

/// <summary>
/// Aggregates raw quotes into time-bucketed OHLC bars.
/// </summary>
public interface IBarAggregator
{
    /// <summary>Raised when a completed bar is ready.</summary>
    event Action<BarEvent> OnBar;

    /// <summary>Processes an incoming quote, potentially triggering a bar emission.</summary>
    void ProcessQuote(QuoteEvent quote);
}

using TradingSystem.Core.Events;

namespace TradingSystem.Core.Interfaces;

/// <summary>
/// Provides a stream of real-time market quotes.
/// </summary>
public interface IMarketDataSource
{
    /// <summary>Raised when a new top-of-book quote arrives.</summary>
    event Action<QuoteEvent> OnQuote;

    /// <summary>Begins publishing quotes until the token is cancelled.</summary>
    Task StartAsync(CancellationToken ct);

    /// <summary>Gracefully shuts down the data source.</summary>
    Task StopAsync();
}

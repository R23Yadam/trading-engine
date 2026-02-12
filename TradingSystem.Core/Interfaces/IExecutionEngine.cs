using TradingSystem.Core.Events;

namespace TradingSystem.Core.Interfaces;

/// <summary>
/// Simulates order execution with slippage, fees, and latency.
/// </summary>
public interface IExecutionEngine
{
    /// <summary>Executes an order against the latest quote, returning a fill with realistic costs.</summary>
    // TODO: add limit order support — Execute should accept an optional limit price
    FillEvent Execute(OrderEvent order, QuoteEvent latestQuote);
}

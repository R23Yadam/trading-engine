using TradingSystem.Core.Events;
using TradingSystem.Core.Models;

namespace TradingSystem.Core.Interfaces;

/// <summary>
/// Manages portfolio state: positions, cash, PnL, and drawdown tracking.
/// </summary>
public interface IPortfolioManager
{
    /// <summary>Current mutable portfolio state.</summary>
    PortfolioState CurrentState { get; }

    /// <summary>Processes a fill event, updating positions, cash, and PnL.</summary>
    PortfolioSnapshot ProcessFill(FillEvent fill, QuoteEvent latestQuote);

    /// <summary>Marks all positions to current market prices.</summary>
    PortfolioSnapshot MarkToMarket(Dictionary<string, QuoteEvent> latestQuotes);
}

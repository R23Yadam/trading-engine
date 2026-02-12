using TradingSystem.Core.Events;
using TradingSystem.Core.Models;

namespace TradingSystem.Core.Interfaces;

/// <summary>
/// Converts trading signals into sized orders based on portfolio state.
/// </summary>
public interface IPositionSizer
{
    /// <summary>Sizes an order for the given signal, or returns null if below minimum threshold.</summary>
    OrderEvent? SizeOrder(SignalEvent signal, PortfolioState currentPortfolio);
}

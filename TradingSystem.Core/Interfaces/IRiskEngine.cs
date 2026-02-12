using TradingSystem.Core.Events;
using TradingSystem.Core.Models;

namespace TradingSystem.Core.Interfaces;

/// <summary>
/// Pre-trade risk gate that approves or rejects orders.
/// </summary>
public interface IRiskEngine
{
    /// <summary>Evaluates an order against all risk checks. Returns a decision with per-check results.</summary>
    RiskDecision Evaluate(OrderEvent order, PortfolioState portfolio);

    /// <summary>Notifies the risk engine of a completed fill for rate and cooldown tracking.</summary>
    void NotifyFill(FillEvent fill, decimal realizedPnL);
}

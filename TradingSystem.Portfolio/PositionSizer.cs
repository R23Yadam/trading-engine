using TradingSystem.Core.Enums;
using TradingSystem.Core.Events;
using TradingSystem.Core.Interfaces;
using TradingSystem.Core.Models;

namespace TradingSystem.Portfolio;

public class PositionSizer : IPositionSizer
{
    private readonly PositionSizerConfig _config;
    private int _orderCounter;

    public PositionSizer(PositionSizerConfig config)
    {
        _config = config;
    }

    public OrderEvent? SizeOrder(SignalEvent signal, PortfolioState portfolio)
    {
        var equity = portfolio.TotalEquity;
        if (equity <= 0) return null;

        // Get current position and price for this symbol
        var hasPosition = portfolio.Positions.TryGetValue(signal.Symbol, out var position);
        decimal currentQty = hasPosition ? position!.Quantity : 0m;
        decimal price = hasPosition ? position!.LastPrice : 0m;
        if (price <= 0 && portfolio.LastKnownPrices.TryGetValue(signal.Symbol, out var knownPrice))
            price = knownPrice;
        if (price <= 0)
            return null; // Can't size without a price

        // Calculate target position in dollar terms
        decimal targetNotional = signal.Signal switch
        {
            SignalType.LONG => equity * _config.TargetPositionPct,
            SignalType.SHORT => -equity * _config.TargetPositionPct,
            SignalType.FLAT => 0m,
            _ => 0m
        };

        // Current position in dollar terms
        decimal currentNotional = currentQty * price;

        // Delta
        decimal deltaNotional = targetNotional - currentNotional;

        // Min order threshold: skip if change is too small
        if (Math.Abs(deltaNotional) < equity * _config.MinOrderPct)
            return null;

        // Convert to shares and round
        var rawShares = deltaNotional / price;
        decimal deltaShares = Math.Round(rawShares, 0);
        if (deltaShares == 0)
            return null;

        // Determine side and make quantity positive
        var side = deltaShares > 0 ? Side.BUY : Side.SELL;
        var quantity = Math.Abs(deltaShares);

        return new OrderEvent
        {
            Timestamp = signal.Timestamp,
            OrderId = $"ORD-{++_orderCounter:D6}",
            Symbol = signal.Symbol,
            Side = side,
            Quantity = quantity,
            TargetNotional = Math.Abs(deltaNotional),
            SignalSource = signal.Reason
        };
    }
}

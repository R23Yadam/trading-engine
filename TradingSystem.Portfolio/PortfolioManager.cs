using TradingSystem.Core.Enums;
using TradingSystem.Core.Events;
using TradingSystem.Core.Interfaces;
using TradingSystem.Core.Models;

namespace TradingSystem.Portfolio;

public class PortfolioManager : IPortfolioManager
{
    private readonly PortfolioState _state;

    public PortfolioState CurrentState => _state;

    public PortfolioManager(PortfolioConfig config)
    {
        _state = new PortfolioState(config.StartingCash);
    }

    public PortfolioSnapshot ProcessFill(FillEvent fill, QuoteEvent latestQuote)
    {
        var signedQty = fill.Side == Side.BUY ? fill.Quantity : -fill.Quantity;
        decimal realizedPnL = 0m;

        if (!_state.Positions.TryGetValue(fill.Symbol, out var position))
        {
            // New position
            position = new Position
            {
                Symbol = fill.Symbol,
                Quantity = 0,
                AvgCostBasis = 0,
                LastPrice = latestQuote.MidPrice
            };
            _state.Positions[fill.Symbol] = position;
        }

        var prevQty = position.Quantity;

        if (prevQty == 0)
        {
            // Opening a new position
            position.Quantity = signedQty;
            position.AvgCostBasis = fill.FillPrice;
        }
        else if (SameDirection(prevQty, signedQty))
        {
            // Adding to existing position — update weighted average cost basis
            var totalCost = position.AvgCostBasis * Math.Abs(prevQty) + fill.FillPrice * Math.Abs(signedQty);
            position.Quantity += signedQty;
            position.AvgCostBasis = totalCost / Math.Abs(position.Quantity);
        }
        else
        {
            // Reducing, closing, or flipping position
            var closedQty = Math.Min(Math.Abs(prevQty), Math.Abs(signedQty));

            // Realized PnL on the closed portion
            if (prevQty > 0)
            {
                // Was long, selling to close
                realizedPnL = (fill.FillPrice - position.AvgCostBasis) * closedQty;
            }
            else
            {
                // Was short, buying to close
                realizedPnL = (position.AvgCostBasis - fill.FillPrice) * closedQty;
            }

            _state.RealizedPnL += realizedPnL;

            var newQty = prevQty + signedQty;
            if (newQty == 0)
            {
                // Position fully closed
                position.Quantity = 0;
                position.AvgCostBasis = 0;
            }
            else if (!SameDirection(prevQty, newQty))
            {
                // Position flipped — the remainder is a new position at fill price
                position.Quantity = newQty;
                position.AvgCostBasis = fill.FillPrice;
            }
            else
            {
                // Partially closed — cost basis stays the same
                position.Quantity = newQty;
            }
        }

        // Update last price
        position.LastPrice = latestQuote.MidPrice;

        // Adjust cash
        if (fill.Side == Side.BUY)
            _state.Cash -= (fill.Quantity * fill.FillPrice + fill.Fees);
        else
            _state.Cash += (fill.Quantity * fill.FillPrice - fill.Fees);

        // Update high water mark
        var equity = _state.TotalEquity;
        if (equity > _state.HighWaterMark)
            _state.HighWaterMark = equity;

        // Update last known price
        _state.LastKnownPrices[fill.Symbol] = latestQuote.MidPrice;

        return TakeSnapshot(fill.Timestamp);
    }

    public PortfolioSnapshot MarkToMarket(Dictionary<string, QuoteEvent> latestQuotes)
    {
        foreach (var (symbol, quote) in latestQuotes)
        {
            if (_state.Positions.TryGetValue(symbol, out var position))
                position.LastPrice = quote.MidPrice;

            _state.LastKnownPrices[symbol] = quote.MidPrice;
        }

        // Update high water mark
        var equity = _state.TotalEquity;
        if (equity > _state.HighWaterMark)
            _state.HighWaterMark = equity;

        return TakeSnapshot(DateTime.UtcNow);
    }

    private PortfolioSnapshot TakeSnapshot(DateTime timestamp)
    {
        var positionInfos = new Dictionary<string, PositionInfo>();
        foreach (var (symbol, pos) in _state.Positions)
        {
            if (pos.Quantity != 0)
                positionInfos[symbol] = pos.ToSnapshot();
        }

        return new PortfolioSnapshot
        {
            Timestamp = timestamp,
            Cash = _state.Cash,
            TotalEquity = _state.TotalEquity,
            RealizedPnL = _state.RealizedPnL,
            UnrealizedPnL = _state.UnrealizedPnL,
            DailyPnL = _state.DailyPnL,
            MaxDrawdown = _state.HighWaterMark > 0
                ? (_state.TotalEquity - _state.HighWaterMark) / _state.HighWaterMark
                : 0,
            HighWaterMark = _state.HighWaterMark,
            Positions = positionInfos
        };
    }

    private static bool SameDirection(decimal a, decimal b) =>
        (a > 0 && b > 0) || (a < 0 && b < 0);
}

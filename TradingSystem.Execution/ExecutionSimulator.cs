using TradingSystem.Core.Enums;
using TradingSystem.Core.Events;
using TradingSystem.Core.Interfaces;

namespace TradingSystem.Execution;

public class ExecutionSimulator : IExecutionEngine
{
    private readonly ExecutionConfig _config;
    private int _fillCounter;

    public ExecutionSimulator(ExecutionConfig config)
    {
        _config = config;
    }

    public FillEvent Execute(OrderEvent order, QuoteEvent latestQuote)
    {
        // Base price: BUY at ask, SELL at bid
        decimal rawPrice = order.Side == Side.BUY ? latestQuote.AskPrice : latestQuote.BidPrice;

        // Slippage: adverse direction
        decimal fillPrice;
        if (order.Side == Side.BUY)
            fillPrice = rawPrice * (1m + _config.SlippageBps / 10000m);
        else
            fillPrice = rawPrice * (1m - _config.SlippageBps / 10000m);

        // Slippage cost in dollar terms
        decimal slippageCost = order.Quantity * Math.Abs(fillPrice - rawPrice);

        // Fees
        decimal fees;
        if (_config.UseBpsFees)
            fees = order.Quantity * fillPrice * _config.FeeBps / 10000m;
        else
            fees = order.Quantity * _config.FeePerShare;

        // Fill timestamp with optional latency
        var fillTimestamp = order.Timestamp.AddMilliseconds(_config.LatencyMs);

        return new FillEvent
        {
            Timestamp = fillTimestamp,
            OrderId = order.OrderId,
            FillId = $"FILL-{++_fillCounter:D6}",
            Symbol = order.Symbol,
            Side = order.Side,
            Quantity = order.Quantity,
            FillPrice = fillPrice,
            Fees = fees,
            SlippageCost = slippageCost,
            LatencyMs = _config.LatencyMs
        };
    }
}

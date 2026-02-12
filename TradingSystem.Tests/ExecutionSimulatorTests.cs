using TradingSystem.Core.Enums;
using TradingSystem.Core.Events;
using TradingSystem.Execution;

namespace TradingSystem.Tests;

public class ExecutionSimulatorTests
{
    private readonly DateTime _now = new(2025, 1, 15, 14, 30, 0, DateTimeKind.Utc);

    private OrderEvent MakeOrder(
        string symbol = "AAPL",
        Side side = Side.BUY,
        decimal quantity = 100,
        DateTime? timestamp = null)
    {
        return new OrderEvent
        {
            Timestamp = timestamp ?? _now,
            OrderId = "order-001",
            Symbol = symbol,
            Side = side,
            Quantity = quantity,
            TargetNotional = quantity * 185m,
            SignalSource = "test"
        };
    }

    private QuoteEvent MakeQuote(
        string symbol = "AAPL",
        decimal bidPrice = 184.95m,
        decimal askPrice = 185.05m,
        DateTime? timestamp = null)
    {
        return new QuoteEvent
        {
            Timestamp = timestamp ?? _now,
            Symbol = symbol,
            BidPrice = bidPrice,
            AskPrice = askPrice,
            BidSize = 1000,
            AskSize = 1000
        };
    }

    // ─── Fill Price Logic ───────────────────────────────────────────

    [Fact]
    public void Buy_FillsAtAskPrice_PlusSlippage()
    {
        var executor = new ExecutionSimulator(new ExecutionConfig
        {
            SlippageBps = 1.0m,
            FeePerShare = 0m
        });

        var order = MakeOrder(side: Side.BUY, quantity: 100);
        var quote = MakeQuote(askPrice: 185.05m);
        var fill = executor.Execute(order, quote);

        // Expected: 185.05 * (1 + 1/10000) = 185.05 * 1.0001 = 185.068505
        var expected = 185.05m * 1.0001m;
        Assert.Equal(expected, fill.FillPrice);
        Assert.Equal(Side.BUY, fill.Side);
        Assert.Equal(100m, fill.Quantity);
    }

    [Fact]
    public void Sell_FillsAtBidPrice_MinusSlippage()
    {
        var executor = new ExecutionSimulator(new ExecutionConfig
        {
            SlippageBps = 1.0m,
            FeePerShare = 0m
        });

        var order = MakeOrder(side: Side.SELL, quantity: 100);
        var quote = MakeQuote(bidPrice: 184.95m);
        var fill = executor.Execute(order, quote);

        // Expected: 184.95 * (1 - 1/10000) = 184.95 * 0.9999 = 184.931505
        var expected = 184.95m * 0.9999m;
        Assert.Equal(expected, fill.FillPrice);
        Assert.Equal(Side.SELL, fill.Side);
    }

    [Fact]
    public void ZeroSlippage_FillsAtRawPrice()
    {
        var executor = new ExecutionSimulator(new ExecutionConfig
        {
            SlippageBps = 0m,
            FeePerShare = 0m
        });

        var buyOrder = MakeOrder(side: Side.BUY);
        var quote = MakeQuote(bidPrice: 184.95m, askPrice: 185.05m);

        var buyFill = executor.Execute(buyOrder, quote);
        Assert.Equal(185.05m, buyFill.FillPrice); // Exactly at ask

        var sellOrder = MakeOrder(side: Side.SELL);
        var sellFill = executor.Execute(sellOrder, quote);
        Assert.Equal(184.95m, sellFill.FillPrice); // Exactly at bid
    }

    // ─── Slippage Cost ──────────────────────────────────────────────

    [Fact]
    public void SlippageCost_CalculatedCorrectly_Buy()
    {
        var executor = new ExecutionSimulator(new ExecutionConfig
        {
            SlippageBps = 2.0m,
            FeePerShare = 0m
        });

        var order = MakeOrder(side: Side.BUY, quantity: 100);
        var quote = MakeQuote(askPrice: 200m);
        var fill = executor.Execute(order, quote);

        // fillPrice = 200 * (1 + 2/10000) = 200 * 1.0002 = 200.04
        // slippageCost = 100 * |200.04 - 200| = 100 * 0.04 = 4.00
        Assert.Equal(200m * 1.0002m, fill.FillPrice);
        Assert.Equal(100m * (200m * 1.0002m - 200m), fill.SlippageCost);
    }

    [Fact]
    public void SlippageCost_CalculatedCorrectly_Sell()
    {
        var executor = new ExecutionSimulator(new ExecutionConfig
        {
            SlippageBps = 2.0m,
            FeePerShare = 0m
        });

        var order = MakeOrder(side: Side.SELL, quantity: 50);
        var quote = MakeQuote(bidPrice: 200m);
        var fill = executor.Execute(order, quote);

        // fillPrice = 200 * (1 - 2/10000) = 200 * 0.9998 = 199.96
        // slippageCost = 50 * |199.96 - 200| = 50 * 0.04 = 2.00
        Assert.Equal(200m * 0.9998m, fill.FillPrice);
        Assert.Equal(50m * (200m - 200m * 0.9998m), fill.SlippageCost);
    }

    // ─── Fee Models ─────────────────────────────────────────────────

    [Fact]
    public void Fees_PerShare_CalculatedCorrectly()
    {
        var executor = new ExecutionSimulator(new ExecutionConfig
        {
            SlippageBps = 0m,
            FeePerShare = 0.005m,
            UseBpsFees = false
        });

        var order = MakeOrder(quantity: 200);
        var quote = MakeQuote();
        var fill = executor.Execute(order, quote);

        // 200 shares * $0.005 = $1.00
        Assert.Equal(1.00m, fill.Fees);
    }

    [Fact]
    public void Fees_Bps_CalculatedCorrectly()
    {
        var executor = new ExecutionSimulator(new ExecutionConfig
        {
            SlippageBps = 0m,
            FeeBps = 5.0m,  // 5 bps
            UseBpsFees = true
        });

        var order = MakeOrder(side: Side.BUY, quantity: 100);
        var quote = MakeQuote(askPrice: 200m);
        var fill = executor.Execute(order, quote);

        // 100 * 200 * 5/10000 = 100 * 200 * 0.0005 = $10.00
        Assert.Equal(100m * 200m * 5.0m / 10000m, fill.Fees);
    }

    [Fact]
    public void Fees_ZeroFees()
    {
        var executor = new ExecutionSimulator(new ExecutionConfig
        {
            SlippageBps = 0m,
            FeePerShare = 0m,
            FeeBps = 0m
        });

        var order = MakeOrder(quantity: 100);
        var quote = MakeQuote();
        var fill = executor.Execute(order, quote);

        Assert.Equal(0m, fill.Fees);
    }

    // ─── Latency ────────────────────────────────────────────────────

    [Fact]
    public void Latency_ZeroMs_TimestampMatchesOrder()
    {
        var executor = new ExecutionSimulator(new ExecutionConfig { LatencyMs = 0 });

        var order = MakeOrder();
        var quote = MakeQuote();
        var fill = executor.Execute(order, quote);

        Assert.Equal(order.Timestamp, fill.Timestamp);
        Assert.Equal(0m, fill.LatencyMs);
    }

    [Fact]
    public void Latency_50ms_TimestampOffset()
    {
        var executor = new ExecutionSimulator(new ExecutionConfig { LatencyMs = 50 });

        var order = MakeOrder();
        var quote = MakeQuote();
        var fill = executor.Execute(order, quote);

        Assert.Equal(order.Timestamp.AddMilliseconds(50), fill.Timestamp);
        Assert.Equal(50m, fill.LatencyMs);
    }

    // ─── Metadata ───────────────────────────────────────────────────

    [Fact]
    public void Fill_PreservesOrderId()
    {
        var executor = new ExecutionSimulator(new ExecutionConfig());

        var order = MakeOrder();
        var fill = executor.Execute(order, MakeQuote());

        Assert.Equal(order.OrderId, fill.OrderId);
    }

    [Fact]
    public void Fill_PreservesSymbol()
    {
        var executor = new ExecutionSimulator(new ExecutionConfig());

        var order = MakeOrder(symbol: "GOOGL");
        var quote = MakeQuote(symbol: "GOOGL");
        var fill = executor.Execute(order, quote);

        Assert.Equal("GOOGL", fill.Symbol);
    }

    [Fact]
    public void Fill_HasUniqueFillId()
    {
        var executor = new ExecutionSimulator(new ExecutionConfig());
        var quote = MakeQuote();

        var fill1 = executor.Execute(MakeOrder(), quote);
        var fill2 = executor.Execute(MakeOrder(), quote);

        Assert.NotEqual(fill1.FillId, fill2.FillId);
    }

    [Fact]
    public void Fill_EventType_IsFill()
    {
        var executor = new ExecutionSimulator(new ExecutionConfig());
        var fill = executor.Execute(MakeOrder(), MakeQuote());

        Assert.Equal("Fill", fill.EventType);
    }

    // ─── Combined realistic scenario ────────────────────────────────

    [Fact]
    public void RealisticScenario_AllFieldsPopulated()
    {
        var executor = new ExecutionSimulator(new ExecutionConfig
        {
            SlippageBps = 1.0m,
            FeePerShare = 0.005m,
            LatencyMs = 10
        });

        var order = MakeOrder(side: Side.BUY, quantity: 500);
        var quote = MakeQuote(bidPrice: 184.90m, askPrice: 185.10m);
        var fill = executor.Execute(order, quote);

        Assert.Equal(Side.BUY, fill.Side);
        Assert.Equal(500m, fill.Quantity);
        Assert.True(fill.FillPrice > 185.10m); // Ask + slippage
        Assert.True(fill.Fees > 0);
        Assert.True(fill.SlippageCost > 0);
        Assert.Equal(10m, fill.LatencyMs);
        Assert.Equal(_now.AddMilliseconds(10), fill.Timestamp);
    }
}

using TradingSystem.Core.Enums;
using TradingSystem.Core.Events;
using TradingSystem.Portfolio;

namespace TradingSystem.Tests;

public class PortfolioManagerTests
{
    private readonly DateTime _now = new(2025, 1, 15, 14, 30, 0, DateTimeKind.Utc);

    private FillEvent MakeFill(
        string symbol = "AAPL",
        Side side = Side.BUY,
        decimal quantity = 100,
        decimal fillPrice = 185m,
        decimal fees = 0.50m,
        DateTime? timestamp = null)
    {
        return new FillEvent
        {
            Timestamp = timestamp ?? _now,
            OrderId = Guid.NewGuid().ToString("N"),
            FillId = Guid.NewGuid().ToString("N"),
            Symbol = symbol,
            Side = side,
            Quantity = quantity,
            FillPrice = fillPrice,
            Fees = fees,
            SlippageCost = 0.10m,
            LatencyMs = 0
        };
    }

    private QuoteEvent MakeQuote(
        string symbol = "AAPL",
        decimal midPrice = 185m)
    {
        return new QuoteEvent
        {
            Timestamp = _now,
            Symbol = symbol,
            BidPrice = midPrice - 0.05m,
            AskPrice = midPrice + 0.05m,
            BidSize = 1000,
            AskSize = 1000
        };
    }

    // ─── Opening Positions ──────────────────────────────────────────

    [Fact]
    public void OpenLong_CashDecreases_PositionCreated()
    {
        var pm = new PortfolioManager(new PortfolioConfig { StartingCash = 100_000m });

        // Buy 100 AAPL at $185, fees $0.50
        var fill = MakeFill(side: Side.BUY, quantity: 100, fillPrice: 185m, fees: 0.50m);
        var quote = MakeQuote(midPrice: 185m);
        var snap = pm.ProcessFill(fill, quote);

        // Cash = 100,000 - (100 * 185 + 0.50) = 100,000 - 18,500.50 = 81,499.50
        Assert.Equal(81_499.50m, snap.Cash);
        Assert.Single(snap.Positions);
        Assert.Equal(100m, snap.Positions["AAPL"].Quantity);
        Assert.Equal(185m, snap.Positions["AAPL"].AvgCostBasis);
    }

    [Fact]
    public void OpenShort_CashIncreases_NegativePosition()
    {
        var pm = new PortfolioManager(new PortfolioConfig { StartingCash = 100_000m });

        // Sell 50 AAPL short at $185, fees $0.25
        var fill = MakeFill(side: Side.SELL, quantity: 50, fillPrice: 185m, fees: 0.25m);
        var quote = MakeQuote(midPrice: 185m);
        var snap = pm.ProcessFill(fill, quote);

        // Cash = 100,000 + (50 * 185 - 0.25) = 100,000 + 9,249.75 = 109,249.75
        Assert.Equal(109_249.75m, snap.Cash);
        Assert.Equal(-50m, snap.Positions["AAPL"].Quantity);
    }

    // ─── Adding to Positions ────────────────────────────────────────

    [Fact]
    public void AddToLong_WeightedAvgCostBasis()
    {
        var pm = new PortfolioManager(new PortfolioConfig { StartingCash = 100_000m });
        var quote = MakeQuote(midPrice: 190m);

        // Buy 100 at $180
        pm.ProcessFill(MakeFill(quantity: 100, fillPrice: 180m, fees: 0m), MakeQuote(midPrice: 180m));

        // Buy 100 more at $200
        var snap = pm.ProcessFill(MakeFill(quantity: 100, fillPrice: 200m, fees: 0m), quote);

        // Avg cost = (100*180 + 100*200) / 200 = 38000/200 = 190
        Assert.Equal(200m, snap.Positions["AAPL"].Quantity);
        Assert.Equal(190m, snap.Positions["AAPL"].AvgCostBasis);
    }

    [Fact]
    public void AddToShort_WeightedAvgCostBasis()
    {
        var pm = new PortfolioManager(new PortfolioConfig { StartingCash = 100_000m });
        var quote = MakeQuote(midPrice: 170m);

        // Sell short 50 at $180
        pm.ProcessFill(MakeFill(side: Side.SELL, quantity: 50, fillPrice: 180m, fees: 0m),
            MakeQuote(midPrice: 180m));

        // Sell short 50 more at $170
        var snap = pm.ProcessFill(MakeFill(side: Side.SELL, quantity: 50, fillPrice: 170m, fees: 0m), quote);

        // Avg cost = (50*180 + 50*170) / 100 = 17500/100 = 175
        Assert.Equal(-100m, snap.Positions["AAPL"].Quantity);
        Assert.Equal(175m, snap.Positions["AAPL"].AvgCostBasis);
    }

    // ─── Closing Positions — Realized PnL ───────────────────────────

    [Fact]
    public void CloseLong_Profit_RealizedPnL()
    {
        var pm = new PortfolioManager(new PortfolioConfig { StartingCash = 100_000m });

        // Buy 100 at $180
        pm.ProcessFill(MakeFill(quantity: 100, fillPrice: 180m, fees: 0m), MakeQuote(midPrice: 180m));

        // Sell 100 at $190 → profit = (190-180) * 100 = $1,000
        var snap = pm.ProcessFill(
            MakeFill(side: Side.SELL, quantity: 100, fillPrice: 190m, fees: 0m),
            MakeQuote(midPrice: 190m));

        Assert.Equal(1_000m, snap.RealizedPnL);
        Assert.Empty(snap.Positions); // Position fully closed, removed from snapshot
    }

    [Fact]
    public void CloseLong_Loss_RealizedPnL()
    {
        var pm = new PortfolioManager(new PortfolioConfig { StartingCash = 100_000m });

        // Buy 100 at $180
        pm.ProcessFill(MakeFill(quantity: 100, fillPrice: 180m, fees: 0m), MakeQuote(midPrice: 180m));

        // Sell 100 at $170 → loss = (170-180) * 100 = -$1,000
        var snap = pm.ProcessFill(
            MakeFill(side: Side.SELL, quantity: 100, fillPrice: 170m, fees: 0m),
            MakeQuote(midPrice: 170m));

        Assert.Equal(-1_000m, snap.RealizedPnL);
    }

    [Fact]
    public void CloseShort_Profit_RealizedPnL()
    {
        var pm = new PortfolioManager(new PortfolioConfig { StartingCash = 100_000m });

        // Sell short 50 at $200
        pm.ProcessFill(MakeFill(side: Side.SELL, quantity: 50, fillPrice: 200m, fees: 0m),
            MakeQuote(midPrice: 200m));

        // Buy to cover at $190 → profit = (200-190) * 50 = $500
        var snap = pm.ProcessFill(
            MakeFill(side: Side.BUY, quantity: 50, fillPrice: 190m, fees: 0m),
            MakeQuote(midPrice: 190m));

        Assert.Equal(500m, snap.RealizedPnL);
    }

    [Fact]
    public void CloseShort_Loss_RealizedPnL()
    {
        var pm = new PortfolioManager(new PortfolioConfig { StartingCash = 100_000m });

        // Sell short 50 at $200
        pm.ProcessFill(MakeFill(side: Side.SELL, quantity: 50, fillPrice: 200m, fees: 0m),
            MakeQuote(midPrice: 200m));

        // Buy to cover at $210 → loss = (200-210) * 50 = -$500
        var snap = pm.ProcessFill(
            MakeFill(side: Side.BUY, quantity: 50, fillPrice: 210m, fees: 0m),
            MakeQuote(midPrice: 210m));

        Assert.Equal(-500m, snap.RealizedPnL);
    }

    // ─── Partial Close ──────────────────────────────────────────────

    [Fact]
    public void PartialCloseLong_RealizesPnLOnClosedPortion()
    {
        var pm = new PortfolioManager(new PortfolioConfig { StartingCash = 100_000m });

        // Buy 100 at $180
        pm.ProcessFill(MakeFill(quantity: 100, fillPrice: 180m, fees: 0m), MakeQuote(midPrice: 180m));

        // Sell 60 at $200 → realized = (200-180) * 60 = $1,200
        var snap = pm.ProcessFill(
            MakeFill(side: Side.SELL, quantity: 60, fillPrice: 200m, fees: 0m),
            MakeQuote(midPrice: 200m));

        Assert.Equal(1_200m, snap.RealizedPnL);
        Assert.Equal(40m, snap.Positions["AAPL"].Quantity);
        Assert.Equal(180m, snap.Positions["AAPL"].AvgCostBasis); // Cost basis unchanged for remainder
    }

    // ─── Position Flip ──────────────────────────────────────────────

    [Fact]
    public void FlipLongToShort_RealizesAndOpensNew()
    {
        var pm = new PortfolioManager(new PortfolioConfig { StartingCash = 100_000m });

        // Buy 100 at $180
        pm.ProcessFill(MakeFill(quantity: 100, fillPrice: 180m, fees: 0m), MakeQuote(midPrice: 180m));

        // Sell 150 at $190 → close 100 (realized +$1000), open short 50 at $190
        var snap = pm.ProcessFill(
            MakeFill(side: Side.SELL, quantity: 150, fillPrice: 190m, fees: 0m),
            MakeQuote(midPrice: 190m));

        Assert.Equal(1_000m, snap.RealizedPnL); // (190-180)*100
        Assert.Equal(-50m, snap.Positions["AAPL"].Quantity);
        Assert.Equal(190m, snap.Positions["AAPL"].AvgCostBasis); // New position at fill price
    }

    [Fact]
    public void FlipShortToLong_RealizesAndOpensNew()
    {
        var pm = new PortfolioManager(new PortfolioConfig { StartingCash = 100_000m });

        // Sell short 50 at $200
        pm.ProcessFill(MakeFill(side: Side.SELL, quantity: 50, fillPrice: 200m, fees: 0m),
            MakeQuote(midPrice: 200m));

        // Buy 80 at $190 → close 50 short (realized +$500), open long 30 at $190
        var snap = pm.ProcessFill(
            MakeFill(side: Side.BUY, quantity: 80, fillPrice: 190m, fees: 0m),
            MakeQuote(midPrice: 190m));

        Assert.Equal(500m, snap.RealizedPnL); // (200-190)*50
        Assert.Equal(30m, snap.Positions["AAPL"].Quantity);
        Assert.Equal(190m, snap.Positions["AAPL"].AvgCostBasis);
    }

    // ─── Cash Accounting with Fees ──────────────────────────────────

    [Fact]
    public void Cash_DecreasesOnBuy_IncludesFees()
    {
        var pm = new PortfolioManager(new PortfolioConfig { StartingCash = 50_000m });

        var fill = MakeFill(side: Side.BUY, quantity: 10, fillPrice: 100m, fees: 5m);
        var snap = pm.ProcessFill(fill, MakeQuote(midPrice: 100m));

        // Cash = 50,000 - (10*100 + 5) = 50,000 - 1,005 = 48,995
        Assert.Equal(48_995m, snap.Cash);
    }

    [Fact]
    public void Cash_IncreasesOnSell_MinusFees()
    {
        var pm = new PortfolioManager(new PortfolioConfig { StartingCash = 50_000m });

        // Open a long first
        pm.ProcessFill(MakeFill(side: Side.BUY, quantity: 10, fillPrice: 100m, fees: 0m),
            MakeQuote(midPrice: 100m));

        // Sell: cash += qty*price - fees
        var fill = MakeFill(side: Side.SELL, quantity: 10, fillPrice: 100m, fees: 5m);
        var snap = pm.ProcessFill(fill, MakeQuote(midPrice: 100m));

        // Cash = 49,000 + (10*100 - 5) = 49,000 + 995 = 49,995
        Assert.Equal(49_995m, snap.Cash);
    }

    [Fact]
    public void Fees_SubtractedOnBothBuyAndSell()
    {
        var pm = new PortfolioManager(new PortfolioConfig { StartingCash = 100_000m });

        // Buy 100 at $100, fee $10
        pm.ProcessFill(MakeFill(quantity: 100, fillPrice: 100m, fees: 10m), MakeQuote(midPrice: 100m));
        // Cash = 100,000 - 10,010 = 89,990

        // Sell 100 at $100, fee $10
        var snap = pm.ProcessFill(
            MakeFill(side: Side.SELL, quantity: 100, fillPrice: 100m, fees: 10m),
            MakeQuote(midPrice: 100m));
        // Cash = 89,990 + (10,000 - 10) = 89,990 + 9,990 = 99,980

        Assert.Equal(99_980m, snap.Cash);
        // Total fees paid: $20 (net effect on cash from round trip at same price)
    }

    // ─── Unrealized PnL ─────────────────────────────────────────────

    [Fact]
    public void UnrealizedPnL_LongPosition_PriceUp()
    {
        var pm = new PortfolioManager(new PortfolioConfig { StartingCash = 100_000m });

        // Buy 100 at $180
        pm.ProcessFill(MakeFill(quantity: 100, fillPrice: 180m, fees: 0m), MakeQuote(midPrice: 180m));

        // Mark to market at $190
        var quotes = new Dictionary<string, QuoteEvent>
        {
            ["AAPL"] = MakeQuote(midPrice: 190m)
        };
        var snap = pm.MarkToMarket(quotes);

        // Unrealized = (190 - 180) * 100 = $1,000
        Assert.Equal(1_000m, snap.UnrealizedPnL);
    }

    [Fact]
    public void UnrealizedPnL_ShortPosition_PriceDown()
    {
        var pm = new PortfolioManager(new PortfolioConfig { StartingCash = 100_000m });

        // Sell short 50 at $200
        pm.ProcessFill(MakeFill(side: Side.SELL, quantity: 50, fillPrice: 200m, fees: 0m),
            MakeQuote(midPrice: 200m));

        // Mark to market at $190 → short is profitable
        var quotes = new Dictionary<string, QuoteEvent>
        {
            ["AAPL"] = MakeQuote(midPrice: 190m)
        };
        var snap = pm.MarkToMarket(quotes);

        // Position: qty=-50, cost=200, lastPrice=190
        // MarketValue = -50 * 190 = -9500
        // CostBasis = -50 * 200 = -10000
        // UnrealizedPnL = -9500 - (-10000) = 500
        Assert.Equal(500m, snap.UnrealizedPnL);
    }

    // ─── High Water Mark & Drawdown ─────────────────────────────────

    [Fact]
    public void HighWaterMark_UpdatesOnGains()
    {
        var pm = new PortfolioManager(new PortfolioConfig { StartingCash = 100_000m });

        // Buy at $180, mark at $190 → equity increases
        pm.ProcessFill(MakeFill(quantity: 100, fillPrice: 180m, fees: 0m), MakeQuote(midPrice: 190m));

        // Cash = 82,000, Position = 100*190 = 19,000, Equity = 101,000
        var state = pm.CurrentState;
        Assert.Equal(101_000m, state.HighWaterMark);
    }

    [Fact]
    public void Drawdown_CalculatedFromHWM()
    {
        var pm = new PortfolioManager(new PortfolioConfig { StartingCash = 100_000m });

        // Force HWM to $105,000 via a profitable trade
        pm.ProcessFill(MakeFill(quantity: 100, fillPrice: 180m, fees: 0m), MakeQuote(midPrice: 230m));
        // Cash = 82,000, Position = 100*230 = 23,000, Equity = 105,000
        Assert.Equal(105_000m, pm.CurrentState.HighWaterMark);

        // Now mark down to $180 → equity drops
        var quotes = new Dictionary<string, QuoteEvent>
        {
            ["AAPL"] = MakeQuote(midPrice: 180m)
        };
        var snap = pm.MarkToMarket(quotes);
        // Cash = 82,000, Position = 100*180 = 18,000, Equity = 100,000
        // Drawdown = (100,000 - 105,000) / 105,000 = -4.76%
        var expectedDrawdown = (100_000m - 105_000m) / 105_000m;
        Assert.Equal(expectedDrawdown, snap.MaxDrawdown);
    }

    // ─── Equity Calculation ─────────────────────────────────────────

    [Fact]
    public void TotalEquity_CashPlusPositionValues()
    {
        var pm = new PortfolioManager(new PortfolioConfig { StartingCash = 100_000m });

        // Buy 100 AAPL at $185
        pm.ProcessFill(
            MakeFill(symbol: "AAPL", quantity: 100, fillPrice: 185m, fees: 0m),
            MakeQuote(symbol: "AAPL", midPrice: 185m));

        // Buy 50 MSFT at $420
        pm.ProcessFill(
            MakeFill(symbol: "MSFT", quantity: 50, fillPrice: 420m, fees: 0m),
            MakeQuote(symbol: "MSFT", midPrice: 420m));

        var state = pm.CurrentState;
        // Cash = 100,000 - 18,500 - 21,000 = 60,500
        // AAPL = 100*185 = 18,500
        // MSFT = 50*420 = 21,000
        // Equity = 60,500 + 18,500 + 21,000 = 100,000
        Assert.Equal(100_000m, state.TotalEquity);
    }

    // ─── Daily PnL ──────────────────────────────────────────────────

    [Fact]
    public void DailyPnL_ReflectsChangeFromDayStart()
    {
        var pm = new PortfolioManager(new PortfolioConfig { StartingCash = 100_000m });

        // Buy 100 at $180, current price $185
        var snap = pm.ProcessFill(
            MakeFill(quantity: 100, fillPrice: 180m, fees: 0m),
            MakeQuote(midPrice: 185m));

        // Cash = 82,000, Position = 100*185 = 18,500, Equity = 100,500
        // DailyPnL = 100,500 - 100,000 = +500
        Assert.Equal(500m, snap.DailyPnL);
    }

    // ─── Snapshot Completeness ───────────────────────────────────────

    [Fact]
    public void Snapshot_ContainsAllFields()
    {
        var pm = new PortfolioManager(new PortfolioConfig { StartingCash = 100_000m });

        var fill = MakeFill(quantity: 50, fillPrice: 185m, fees: 1m);
        var snap = pm.ProcessFill(fill, MakeQuote(midPrice: 186m));

        Assert.Equal("PortfolioSnapshot", snap.EventType);
        Assert.Equal(fill.Timestamp, snap.Timestamp);
        Assert.True(snap.Cash > 0);
        Assert.True(snap.TotalEquity > 0);
        Assert.True(snap.HighWaterMark > 0);
        Assert.NotNull(snap.Positions);
    }

    [Fact]
    public void Snapshot_ExcludesZeroQuantityPositions()
    {
        var pm = new PortfolioManager(new PortfolioConfig { StartingCash = 100_000m });

        // Open then fully close
        pm.ProcessFill(MakeFill(quantity: 100, fillPrice: 185m, fees: 0m), MakeQuote(midPrice: 185m));
        var snap = pm.ProcessFill(
            MakeFill(side: Side.SELL, quantity: 100, fillPrice: 185m, fees: 0m),
            MakeQuote(midPrice: 185m));

        Assert.Empty(snap.Positions);
    }

    // ─── Multiple Symbols ───────────────────────────────────────────

    [Fact]
    public void MultipleSymbols_IndependentTracking()
    {
        var pm = new PortfolioManager(new PortfolioConfig { StartingCash = 100_000m });

        // Buy AAPL
        pm.ProcessFill(
            MakeFill(symbol: "AAPL", quantity: 50, fillPrice: 185m, fees: 0m),
            MakeQuote(symbol: "AAPL", midPrice: 185m));

        // Buy GOOGL
        pm.ProcessFill(
            MakeFill(symbol: "GOOGL", quantity: 30, fillPrice: 175m, fees: 0m),
            MakeQuote(symbol: "GOOGL", midPrice: 175m));

        // Sell AAPL at profit
        var snap = pm.ProcessFill(
            MakeFill(symbol: "AAPL", side: Side.SELL, quantity: 50, fillPrice: 195m, fees: 0m),
            MakeQuote(symbol: "AAPL", midPrice: 195m));

        // AAPL realized PnL = (195-185)*50 = $500
        Assert.Equal(500m, snap.RealizedPnL);
        // AAPL closed, only GOOGL remains
        Assert.Single(snap.Positions);
        Assert.True(snap.Positions.ContainsKey("GOOGL"));
        Assert.Equal(30m, snap.Positions["GOOGL"].Quantity);
    }

    // ─── Mark to Market ─────────────────────────────────────────────

    [Fact]
    public void MarkToMarket_UpdatesAllPositionPrices()
    {
        var pm = new PortfolioManager(new PortfolioConfig { StartingCash = 100_000m });

        pm.ProcessFill(MakeFill(symbol: "AAPL", quantity: 100, fillPrice: 180m, fees: 0m),
            MakeQuote(symbol: "AAPL", midPrice: 180m));
        pm.ProcessFill(MakeFill(symbol: "MSFT", quantity: 50, fillPrice: 400m, fees: 0m),
            MakeQuote(symbol: "MSFT", midPrice: 400m));

        var quotes = new Dictionary<string, QuoteEvent>
        {
            ["AAPL"] = MakeQuote(symbol: "AAPL", midPrice: 190m),
            ["MSFT"] = MakeQuote(symbol: "MSFT", midPrice: 410m)
        };
        var snap = pm.MarkToMarket(quotes);

        // AAPL unrealized = (190-180)*100 = 1000
        // MSFT unrealized = (410-400)*50 = 500
        Assert.Equal(1_500m, snap.UnrealizedPnL);
    }

    [Fact]
    public void MarkToMarket_UpdatesHighWaterMark()
    {
        var pm = new PortfolioManager(new PortfolioConfig { StartingCash = 100_000m });

        pm.ProcessFill(MakeFill(quantity: 100, fillPrice: 180m, fees: 0m),
            MakeQuote(midPrice: 180m));

        // Mark up → HWM should update
        var quotes = new Dictionary<string, QuoteEvent>
        {
            ["AAPL"] = MakeQuote(midPrice: 200m)
        };
        var snap = pm.MarkToMarket(quotes);

        // Cash = 82,000, Position = 100*200 = 20,000, Equity = 102,000
        Assert.Equal(102_000m, snap.HighWaterMark);
    }

    // ─── Cumulative Realized PnL ────────────────────────────────────

    [Fact]
    public void RealizedPnL_AccumulatesAcrossMultipleTrades()
    {
        var pm = new PortfolioManager(new PortfolioConfig { StartingCash = 100_000m });

        // Trade 1: Buy 100 at $180, sell at $190 → +$1000
        pm.ProcessFill(MakeFill(quantity: 100, fillPrice: 180m, fees: 0m), MakeQuote(midPrice: 180m));
        pm.ProcessFill(MakeFill(side: Side.SELL, quantity: 100, fillPrice: 190m, fees: 0m),
            MakeQuote(midPrice: 190m));

        // Trade 2: Buy 50 at $200, sell at $195 → -$250
        pm.ProcessFill(MakeFill(quantity: 50, fillPrice: 200m, fees: 0m), MakeQuote(midPrice: 200m));
        var snap = pm.ProcessFill(
            MakeFill(side: Side.SELL, quantity: 50, fillPrice: 195m, fees: 0m),
            MakeQuote(midPrice: 195m));

        // Cumulative: +1000 - 250 = +750
        Assert.Equal(750m, snap.RealizedPnL);
    }
}

using TradingSystem.Core.Enums;
using TradingSystem.Core.Events;
using TradingSystem.Core.Models;
using TradingSystem.Risk;

namespace TradingSystem.Tests;

public class RiskEngineTests
{
    private readonly DateTime _now = new(2025, 1, 15, 14, 30, 0, DateTimeKind.Utc);

    // ─── Helpers ────────────────────────────────────────────────────

    private static PortfolioState MakePortfolio(
        decimal cash = 100_000m,
        decimal? dailyStartEquity = null,
        decimal? highWaterMark = null,
        Dictionary<string, (decimal qty, decimal avgCost, decimal lastPrice)>? positions = null)
    {
        var portfolio = new PortfolioState(cash);
        if (dailyStartEquity.HasValue)
            portfolio.DailyStartEquity = dailyStartEquity.Value;
        if (highWaterMark.HasValue)
            portfolio.HighWaterMark = highWaterMark.Value;

        if (positions != null)
        {
            foreach (var (symbol, (qty, avgCost, lastPrice)) in positions)
            {
                portfolio.Positions[symbol] = new Position
                {
                    Symbol = symbol,
                    Quantity = qty,
                    AvgCostBasis = avgCost,
                    LastPrice = lastPrice
                };
            }
        }

        return portfolio;
    }

    private OrderEvent MakeOrder(
        string symbol = "AAPL",
        Side side = Side.BUY,
        decimal quantity = 10,
        DateTime? timestamp = null)
    {
        return new OrderEvent
        {
            Timestamp = timestamp ?? _now,
            OrderId = Guid.NewGuid().ToString("N"),
            Symbol = symbol,
            Side = side,
            Quantity = quantity,
            TargetNotional = quantity * 185m,
            SignalSource = "test"
        };
    }

    private FillEvent MakeFill(
        string symbol = "AAPL",
        Side side = Side.BUY,
        decimal quantity = 10,
        decimal fillPrice = 185m,
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
            Fees = 0.05m,
            SlippageCost = 0.01m,
            LatencyMs = 0
        };
    }

    // ─── Check 1: Max Position Size ─────────────────────────────────

    [Fact]
    public void MaxPosition_Approve_WhenPositionWithinLimit()
    {
        var engine = new RiskEngine(new RiskConfig { MaxPositionPct = 0.15m });
        var portfolio = MakePortfolio(positions: new()
        {
            ["AAPL"] = (0, 0, 185m)
        });
        portfolio.LastKnownPrices["AAPL"] = 185m;

        // Buying 50 shares at $185 = $9,250 = 9.25% of $100k → under 15%
        var order = MakeOrder(quantity: 50);
        var decision = engine.Evaluate(order, portfolio);

        Assert.Equal(RiskAction.APPROVE, decision.Action);
        Assert.True(decision.CheckResults["MaxPosition"]);
    }

    [Fact]
    public void MaxPosition_Reject_WhenPositionExceedsLimit()
    {
        var engine = new RiskEngine(new RiskConfig { MaxPositionPct = 0.15m });
        var portfolio = MakePortfolio(positions: new()
        {
            ["AAPL"] = (50, 185m, 185m) // Already hold 50 shares = $9,250
        });

        // Buying 40 more → 90 shares at $185 = $16,650 = 16.65% of $100k → over 15%
        var order = MakeOrder(quantity: 40);
        var decision = engine.Evaluate(order, portfolio);

        Assert.Equal(RiskAction.REJECT, decision.Action);
        Assert.False(decision.CheckResults["MaxPosition"]);
        Assert.Contains("Position in AAPL", decision.Reason);
        Assert.Contains("exceeding max", decision.Reason);
    }

    [Fact]
    public void MaxPosition_Approve_ExactlyAtLimit()
    {
        var engine = new RiskEngine(new RiskConfig { MaxPositionPct = 0.15m });
        var portfolio = MakePortfolio(cash: 100_000m);
        portfolio.LastKnownPrices["AAPL"] = 200m;

        // 75 shares at $200 = $15,000 = exactly 15%
        var order = MakeOrder(quantity: 75);
        var decision = engine.Evaluate(order, portfolio);

        Assert.Equal(RiskAction.APPROVE, decision.Action);
        Assert.True(decision.CheckResults["MaxPosition"]);
    }

    [Fact]
    public void MaxPosition_Approve_SellingReducesPosition()
    {
        var engine = new RiskEngine(new RiskConfig { MaxPositionPct = 0.15m });
        var portfolio = MakePortfolio(positions: new()
        {
            ["AAPL"] = (100, 185m, 185m) // Hold 100 shares = $18,500 = 18.5% (over limit)
        });

        // Selling 50 brings it down to 50 shares = $9,250 = 9.25% → approve
        var order = MakeOrder(side: Side.SELL, quantity: 50);
        var decision = engine.Evaluate(order, portfolio);

        Assert.Equal(RiskAction.APPROVE, decision.Action);
        Assert.True(decision.CheckResults["MaxPosition"]);
    }

    [Fact]
    public void MaxPosition_Reject_ShortExceedsLimit()
    {
        var engine = new RiskEngine(new RiskConfig { MaxPositionPct = 0.15m });
        var portfolio = MakePortfolio(cash: 100_000m);
        portfolio.LastKnownPrices["AAPL"] = 185m;

        // Selling 100 shares short = -$18,500 → |18.5%| exceeds 15%
        var order = MakeOrder(side: Side.SELL, quantity: 100);
        var decision = engine.Evaluate(order, portfolio);

        Assert.Equal(RiskAction.REJECT, decision.Action);
        Assert.False(decision.CheckResults["MaxPosition"]);
    }

    [Fact]
    public void MaxPosition_UsesLastKnownPrice_WhenNoPosition()
    {
        var engine = new RiskEngine(new RiskConfig { MaxPositionPct = 0.15m });
        var portfolio = MakePortfolio();
        portfolio.LastKnownPrices["AAPL"] = 185m;

        // No existing position, buying 50 at $185 = $9,250 = 9.25%
        var order = MakeOrder(quantity: 50);
        var decision = engine.Evaluate(order, portfolio);

        Assert.Equal(RiskAction.APPROVE, decision.Action);
    }

    // ─── Check 2: Max Daily Loss ────────────────────────────────────

    [Fact]
    public void MaxDailyLoss_Approve_WhenProfitable()
    {
        var engine = new RiskEngine(new RiskConfig { MaxDailyLossPct = 0.02m });
        // Portfolio is up: equity = $101,000 vs daily start of $100,000
        var portfolio = MakePortfolio(cash: 101_000m, dailyStartEquity: 100_000m);

        var order = MakeOrder(quantity: 10);
        portfolio.LastKnownPrices["AAPL"] = 185m;
        var decision = engine.Evaluate(order, portfolio);

        Assert.Equal(RiskAction.APPROVE, decision.Action);
        Assert.True(decision.CheckResults["MaxDailyLoss"]);
    }

    [Fact]
    public void MaxDailyLoss_Approve_WhenLossWithinLimit()
    {
        var engine = new RiskEngine(new RiskConfig { MaxDailyLossPct = 0.02m });
        // Portfolio down $1,500 from $100k start → 1.5% loss, under 2% limit
        var portfolio = MakePortfolio(cash: 98_500m, dailyStartEquity: 100_000m);
        portfolio.LastKnownPrices["AAPL"] = 185m;

        var order = MakeOrder(quantity: 10);
        var decision = engine.Evaluate(order, portfolio);

        Assert.Equal(RiskAction.APPROVE, decision.Action);
        Assert.True(decision.CheckResults["MaxDailyLoss"]);
    }

    [Fact]
    public void MaxDailyLoss_Reject_WhenLossExceedsLimit()
    {
        var engine = new RiskEngine(new RiskConfig { MaxDailyLossPct = 0.02m });
        // Portfolio down $2,500 from $100k start → 2.5% loss, over 2% limit
        var portfolio = MakePortfolio(cash: 97_500m, dailyStartEquity: 100_000m);
        portfolio.LastKnownPrices["AAPL"] = 185m;

        var order = MakeOrder(quantity: 10);
        var decision = engine.Evaluate(order, portfolio);

        Assert.Equal(RiskAction.REJECT, decision.Action);
        Assert.False(decision.CheckResults["MaxDailyLoss"]);
        Assert.Contains("Daily loss", decision.Reason);
    }

    [Fact]
    public void MaxDailyLoss_Reject_ExactlyAtLimit()
    {
        var engine = new RiskEngine(new RiskConfig { MaxDailyLossPct = 0.02m });
        // Exactly $2,000 loss = exactly 2%
        var portfolio = MakePortfolio(cash: 98_000m, dailyStartEquity: 100_000m);
        portfolio.LastKnownPrices["AAPL"] = 185m;

        var order = MakeOrder(quantity: 10);
        var decision = engine.Evaluate(order, portfolio);

        // Exactly at limit — should NOT reject (uses > not >=)
        Assert.True(decision.CheckResults["MaxDailyLoss"]);
    }

    // ─── Check 3: Max Drawdown ──────────────────────────────────────

    [Fact]
    public void MaxDrawdown_Approve_WhenAtHighWaterMark()
    {
        var engine = new RiskEngine(new RiskConfig { MaxDrawdownPct = 0.05m });
        var portfolio = MakePortfolio(cash: 105_000m, highWaterMark: 105_000m);
        portfolio.LastKnownPrices["AAPL"] = 185m;

        var order = MakeOrder(quantity: 10);
        var decision = engine.Evaluate(order, portfolio);

        Assert.Equal(RiskAction.APPROVE, decision.Action);
        Assert.True(decision.CheckResults["MaxDrawdown"]);
    }

    [Fact]
    public void MaxDrawdown_Approve_WhenDrawdownWithinLimit()
    {
        var engine = new RiskEngine(new RiskConfig { MaxDrawdownPct = 0.05m });
        // Equity = $97k, HWM = $100k → 3% drawdown, under 5%
        var portfolio = MakePortfolio(cash: 97_000m, highWaterMark: 100_000m);
        portfolio.LastKnownPrices["AAPL"] = 185m;

        var order = MakeOrder(quantity: 10);
        var decision = engine.Evaluate(order, portfolio);

        Assert.Equal(RiskAction.APPROVE, decision.Action);
        Assert.True(decision.CheckResults["MaxDrawdown"]);
    }

    [Fact]
    public void MaxDrawdown_Reject_WhenDrawdownExceedsLimit()
    {
        var engine = new RiskEngine(new RiskConfig { MaxDrawdownPct = 0.05m });
        // Equity = $94k, HWM = $100k → 6% drawdown, over 5%
        var portfolio = MakePortfolio(cash: 94_000m, highWaterMark: 100_000m);
        portfolio.LastKnownPrices["AAPL"] = 185m;

        var order = MakeOrder(quantity: 10);
        var decision = engine.Evaluate(order, portfolio);

        Assert.Equal(RiskAction.REJECT, decision.Action);
        Assert.False(decision.CheckResults["MaxDrawdown"]);
        Assert.Contains("Drawdown", decision.Reason);
    }

    [Fact]
    public void MaxDrawdown_Reject_LargeDrawdownAfterGains()
    {
        var engine = new RiskEngine(new RiskConfig { MaxDrawdownPct = 0.05m });
        // HWM hit $120k, now at $112k → 6.67% drawdown
        var portfolio = MakePortfolio(cash: 112_000m, highWaterMark: 120_000m);
        portfolio.LastKnownPrices["AAPL"] = 185m;

        var order = MakeOrder(quantity: 10);
        var decision = engine.Evaluate(order, portfolio);

        Assert.Equal(RiskAction.REJECT, decision.Action);
        Assert.False(decision.CheckResults["MaxDrawdown"]);
    }

    // ─── Check 4: Max Trades Per Minute ─────────────────────────────

    [Fact]
    public void MaxTradesPerMin_Approve_WhenUnderLimit()
    {
        var engine = new RiskEngine(new RiskConfig { MaxTradesPerMinute = 10 });
        var portfolio = MakePortfolio();
        portfolio.LastKnownPrices["AAPL"] = 185m;

        // Record 5 fills in the last minute
        for (int i = 0; i < 5; i++)
        {
            engine.NotifyFill(MakeFill(timestamp: _now.AddSeconds(-30 + i)), 0m);
        }

        var order = MakeOrder(quantity: 10);
        var decision = engine.Evaluate(order, portfolio);

        Assert.Equal(RiskAction.APPROVE, decision.Action);
        Assert.True(decision.CheckResults["MaxTradesPerMin"]);
    }

    [Fact]
    public void MaxTradesPerMin_Reject_WhenAtLimit()
    {
        var engine = new RiskEngine(new RiskConfig { MaxTradesPerMinute = 10 });
        var portfolio = MakePortfolio();
        portfolio.LastKnownPrices["AAPL"] = 185m;

        // Record exactly 10 fills in the last minute
        for (int i = 0; i < 10; i++)
        {
            engine.NotifyFill(MakeFill(timestamp: _now.AddSeconds(-50 + i * 5)), 0m);
        }

        var order = MakeOrder(quantity: 10);
        var decision = engine.Evaluate(order, portfolio);

        Assert.Equal(RiskAction.REJECT, decision.Action);
        Assert.False(decision.CheckResults["MaxTradesPerMin"]);
        Assert.Contains("Trade rate", decision.Reason);
    }

    [Fact]
    public void MaxTradesPerMin_Approve_OldFillsExpire()
    {
        var engine = new RiskEngine(new RiskConfig { MaxTradesPerMinute = 5 });
        var portfolio = MakePortfolio();
        portfolio.LastKnownPrices["AAPL"] = 185m;

        // Record 5 fills 2 minutes ago (should expire)
        for (int i = 0; i < 5; i++)
        {
            engine.NotifyFill(MakeFill(timestamp: _now.AddMinutes(-2).AddSeconds(i)), 0m);
        }

        var order = MakeOrder(quantity: 10);
        var decision = engine.Evaluate(order, portfolio);

        Assert.Equal(RiskAction.APPROVE, decision.Action);
        Assert.True(decision.CheckResults["MaxTradesPerMin"]);
    }

    [Fact]
    public void MaxTradesPerMin_Approve_NoFills()
    {
        var engine = new RiskEngine(new RiskConfig { MaxTradesPerMinute = 10 });
        var portfolio = MakePortfolio();
        portfolio.LastKnownPrices["AAPL"] = 185m;

        var order = MakeOrder(quantity: 10);
        var decision = engine.Evaluate(order, portfolio);

        Assert.True(decision.CheckResults["MaxTradesPerMin"]);
    }

    [Fact]
    public void MaxTradesPerMin_SlidingWindow_MixOfOldAndNew()
    {
        var engine = new RiskEngine(new RiskConfig { MaxTradesPerMinute = 5 });
        var portfolio = MakePortfolio();
        portfolio.LastKnownPrices["AAPL"] = 185m;

        // 3 old fills (expired) + 4 recent fills = only 4 count
        for (int i = 0; i < 3; i++)
            engine.NotifyFill(MakeFill(timestamp: _now.AddMinutes(-3).AddSeconds(i)), 0m);
        for (int i = 0; i < 4; i++)
            engine.NotifyFill(MakeFill(timestamp: _now.AddSeconds(-10 + i)), 0m);

        var order = MakeOrder(quantity: 10);
        var decision = engine.Evaluate(order, portfolio);

        Assert.Equal(RiskAction.APPROVE, decision.Action);
        Assert.True(decision.CheckResults["MaxTradesPerMin"]);
    }

    // ─── Check 5: Max Daily Turnover ────────────────────────────────

    [Fact]
    public void MaxDailyTurnover_Approve_WhenUnderLimit()
    {
        var engine = new RiskEngine(new RiskConfig { MaxDailyTurnoverPct = 5.0m });
        var portfolio = MakePortfolio();
        portfolio.LastKnownPrices["AAPL"] = 185m;

        // No previous turnover, buying 10 shares = $1,850 < 500% of $100k
        var order = MakeOrder(quantity: 10);
        var decision = engine.Evaluate(order, portfolio);

        Assert.True(decision.CheckResults["MaxDailyTurnover"]);
    }

    [Fact]
    public void MaxDailyTurnover_Reject_WhenExceedsLimit()
    {
        var engine = new RiskEngine(new RiskConfig
        {
            MaxDailyTurnoverPct = 5.0m,
            MaxPositionPct = 1.0m  // Relax position limit — we're testing turnover here
        });
        var portfolio = MakePortfolio();
        portfolio.LastKnownPrices["AAPL"] = 185m;

        // Simulate $490,000 of prior turnover via fills
        // 2648 shares * $185 = $489,880
        engine.NotifyFill(MakeFill(quantity: 2648, fillPrice: 185m), 0m);

        // Now try to buy 100 more shares = $18,500 → total $508,380 > $500,000 (500%)
        var order = MakeOrder(quantity: 100);
        var decision = engine.Evaluate(order, portfolio);

        Assert.Equal(RiskAction.REJECT, decision.Action);
        Assert.False(decision.CheckResults["MaxDailyTurnover"]);
        Assert.Contains("Daily turnover", decision.Reason);
    }

    [Fact]
    public void MaxDailyTurnover_AccumulatesAcrossMultipleFills()
    {
        var engine = new RiskEngine(new RiskConfig { MaxDailyTurnoverPct = 0.10m }); // 10% = $10k
        var portfolio = MakePortfolio();
        portfolio.LastKnownPrices["AAPL"] = 185m;

        // First fill: $5,550
        engine.NotifyFill(MakeFill(quantity: 30, fillPrice: 185m), 0m);
        // Second fill: $3,700
        engine.NotifyFill(MakeFill(quantity: 20, fillPrice: 185m), 0m);
        // Total: $9,250

        // New order: 10 shares = $1,850 → total $11,100 > $10,000
        var order = MakeOrder(quantity: 10);
        var decision = engine.Evaluate(order, portfolio);

        Assert.Equal(RiskAction.REJECT, decision.Action);
        Assert.False(decision.CheckResults["MaxDailyTurnover"]);
    }

    // ─── Check 6: Loss Cooldown ─────────────────────────────────────

    [Fact]
    public void LossCooldown_Approve_WhenNoRecentLoss()
    {
        var engine = new RiskEngine(new RiskConfig { LossCooldown = TimeSpan.FromSeconds(30) });
        var portfolio = MakePortfolio();
        portfolio.LastKnownPrices["AAPL"] = 185m;

        var order = MakeOrder(quantity: 10);
        var decision = engine.Evaluate(order, portfolio);

        Assert.True(decision.CheckResults["LossCooldown"]);
    }

    [Fact]
    public void LossCooldown_Reject_DuringCooldownPeriod()
    {
        var engine = new RiskEngine(new RiskConfig { LossCooldown = TimeSpan.FromSeconds(30) });
        var portfolio = MakePortfolio();
        portfolio.LastKnownPrices["AAPL"] = 185m;

        // Record a losing fill 10 seconds ago
        engine.NotifyFill(MakeFill(timestamp: _now.AddSeconds(-10)), -500m);

        var order = MakeOrder(quantity: 10);
        var decision = engine.Evaluate(order, portfolio);

        Assert.Equal(RiskAction.REJECT, decision.Action);
        Assert.False(decision.CheckResults["LossCooldown"]);
        Assert.Contains("Cooldown active for AAPL", decision.Reason);
        Assert.Contains("remaining", decision.Reason);
    }

    [Fact]
    public void LossCooldown_Approve_AfterCooldownExpires()
    {
        var engine = new RiskEngine(new RiskConfig { LossCooldown = TimeSpan.FromSeconds(30) });
        var portfolio = MakePortfolio();
        portfolio.LastKnownPrices["AAPL"] = 185m;

        // Record a losing fill 35 seconds ago → cooldown expired
        engine.NotifyFill(MakeFill(timestamp: _now.AddSeconds(-35)), -500m);

        var order = MakeOrder(quantity: 10);
        var decision = engine.Evaluate(order, portfolio);

        Assert.True(decision.CheckResults["LossCooldown"]);
    }

    [Fact]
    public void LossCooldown_PerSymbol_OnlyAffectedSymbolBlocked()
    {
        var engine = new RiskEngine(new RiskConfig { LossCooldown = TimeSpan.FromSeconds(30) });
        var portfolio = MakePortfolio();
        portfolio.LastKnownPrices["AAPL"] = 185m;
        portfolio.LastKnownPrices["MSFT"] = 420m;

        // Loss on AAPL 10 seconds ago
        engine.NotifyFill(MakeFill(symbol: "AAPL", timestamp: _now.AddSeconds(-10)), -500m);

        // AAPL should be blocked
        var aaplOrder = MakeOrder(symbol: "AAPL", quantity: 10);
        var aaplDecision = engine.Evaluate(aaplOrder, portfolio);
        Assert.False(aaplDecision.CheckResults["LossCooldown"]);

        // MSFT should be fine
        var msftOrder = MakeOrder(symbol: "MSFT", quantity: 5);
        var msftDecision = engine.Evaluate(msftOrder, portfolio);
        Assert.True(msftDecision.CheckResults["LossCooldown"]);
    }

    [Fact]
    public void LossCooldown_NotTriggered_ByWinningFill()
    {
        var engine = new RiskEngine(new RiskConfig { LossCooldown = TimeSpan.FromSeconds(30) });
        var portfolio = MakePortfolio();
        portfolio.LastKnownPrices["AAPL"] = 185m;

        // Record a WINNING fill 10 seconds ago → no cooldown
        engine.NotifyFill(MakeFill(timestamp: _now.AddSeconds(-10)), 500m);

        var order = MakeOrder(quantity: 10);
        var decision = engine.Evaluate(order, portfolio);

        Assert.True(decision.CheckResults["LossCooldown"]);
    }

    [Fact]
    public void LossCooldown_BreakEvenFill_NoCooldown()
    {
        var engine = new RiskEngine(new RiskConfig { LossCooldown = TimeSpan.FromSeconds(30) });
        var portfolio = MakePortfolio();
        portfolio.LastKnownPrices["AAPL"] = 185m;

        // Break-even fill (realizedPnL = 0) → no cooldown
        engine.NotifyFill(MakeFill(timestamp: _now.AddSeconds(-10)), 0m);

        var order = MakeOrder(quantity: 10);
        var decision = engine.Evaluate(order, portfolio);

        Assert.True(decision.CheckResults["LossCooldown"]);
    }

    // ─── CheckResults Always Complete ───────────────────────────────

    [Fact]
    public void CheckResults_AllSixKeysPresent_OnApprove()
    {
        var engine = new RiskEngine(new RiskConfig());
        var portfolio = MakePortfolio();
        portfolio.LastKnownPrices["AAPL"] = 185m;

        var order = MakeOrder(quantity: 10);
        var decision = engine.Evaluate(order, portfolio);

        Assert.Equal(6, decision.CheckResults.Count);
        Assert.True(decision.CheckResults.ContainsKey("MaxPosition"));
        Assert.True(decision.CheckResults.ContainsKey("MaxDailyLoss"));
        Assert.True(decision.CheckResults.ContainsKey("MaxDrawdown"));
        Assert.True(decision.CheckResults.ContainsKey("MaxTradesPerMin"));
        Assert.True(decision.CheckResults.ContainsKey("MaxDailyTurnover"));
        Assert.True(decision.CheckResults.ContainsKey("LossCooldown"));
    }

    [Fact]
    public void CheckResults_AllSixKeysPresent_OnReject()
    {
        var engine = new RiskEngine(new RiskConfig { MaxDailyLossPct = 0.02m });
        // Big loss to trigger rejection
        var portfolio = MakePortfolio(cash: 95_000m, dailyStartEquity: 100_000m);
        portfolio.LastKnownPrices["AAPL"] = 185m;

        var order = MakeOrder(quantity: 10);
        var decision = engine.Evaluate(order, portfolio);

        Assert.Equal(RiskAction.REJECT, decision.Action);
        Assert.Equal(6, decision.CheckResults.Count);
        // All keys present even though it was rejected on daily loss
        Assert.True(decision.CheckResults.ContainsKey("LossCooldown"));
    }

    [Fact]
    public void CheckResults_ReportsFirstFailure_WhenMultipleFail()
    {
        var engine = new RiskEngine(new RiskConfig
        {
            MaxPositionPct = 0.05m,    // 5% — very tight
            MaxDailyLossPct = 0.01m,   // 1% — very tight
        });
        // Both position and daily loss will fail
        var portfolio = MakePortfolio(cash: 98_500m, dailyStartEquity: 100_000m);
        portfolio.LastKnownPrices["AAPL"] = 185m;

        // 100 shares at $185 = $18,500 = 18.5% → position check fails first
        var order = MakeOrder(quantity: 100);
        var decision = engine.Evaluate(order, portfolio);

        Assert.Equal(RiskAction.REJECT, decision.Action);
        Assert.False(decision.CheckResults["MaxPosition"]);
        Assert.False(decision.CheckResults["MaxDailyLoss"]);
        // Reason should be from the first failing check (MaxPosition)
        Assert.Contains("Position in AAPL", decision.Reason);
    }

    // ─── Combined Scenarios ─────────────────────────────────────────

    [Fact]
    public void FullApproval_AllChecksPass()
    {
        var engine = new RiskEngine(new RiskConfig());
        var portfolio = MakePortfolio();
        portfolio.LastKnownPrices["AAPL"] = 185m;

        var order = MakeOrder(quantity: 10);
        var decision = engine.Evaluate(order, portfolio);

        Assert.Equal(RiskAction.APPROVE, decision.Action);
        Assert.Equal("Passed all checks", decision.Reason);
        Assert.All(decision.CheckResults.Values, v => Assert.True(v));
    }

    [Fact]
    public void SequentialOrders_TurnoverAccumulatesCorrectly()
    {
        var engine = new RiskEngine(new RiskConfig { MaxDailyTurnoverPct = 0.05m }); // 5% = $5k
        var portfolio = MakePortfolio();
        portfolio.LastKnownPrices["AAPL"] = 100m;

        // First order: 20 shares × $100 = $2,000 → approve
        var order1 = MakeOrder(quantity: 20);
        var d1 = engine.Evaluate(order1, portfolio);
        Assert.Equal(RiskAction.APPROVE, d1.Action);

        // Simulate fill
        engine.NotifyFill(MakeFill(quantity: 20, fillPrice: 100m), 0m);

        // Second order: 20 shares × $100 = $2,000 → total $4,000 → approve
        var order2 = MakeOrder(quantity: 20);
        var d2 = engine.Evaluate(order2, portfolio);
        Assert.Equal(RiskAction.APPROVE, d2.Action);

        // Simulate fill
        engine.NotifyFill(MakeFill(quantity: 20, fillPrice: 100m), 0m);

        // Third order: 20 shares × $100 = $2,000 → total would be $6,000 > $5,000 → reject
        var order3 = MakeOrder(quantity: 20);
        var d3 = engine.Evaluate(order3, portfolio);
        Assert.Equal(RiskAction.REJECT, d3.Action);
        Assert.False(d3.CheckResults["MaxDailyTurnover"]);
    }

    [Fact]
    public void CooldownThenRecover_ApprovalResumes()
    {
        var engine = new RiskEngine(new RiskConfig { LossCooldown = TimeSpan.FromSeconds(30) });
        var portfolio = MakePortfolio();
        portfolio.LastKnownPrices["AAPL"] = 185m;

        // Loss at T=0
        engine.NotifyFill(MakeFill(timestamp: _now), -200m);

        // T+10s → still in cooldown
        var order1 = MakeOrder(timestamp: _now.AddSeconds(10));
        Assert.Equal(RiskAction.REJECT, engine.Evaluate(order1, portfolio).Action);

        // T+25s → still in cooldown
        var order2 = MakeOrder(timestamp: _now.AddSeconds(25));
        Assert.Equal(RiskAction.REJECT, engine.Evaluate(order2, portfolio).Action);

        // T+31s → cooldown expired → approve
        var order3 = MakeOrder(timestamp: _now.AddSeconds(31));
        Assert.Equal(RiskAction.APPROVE, engine.Evaluate(order3, portfolio).Action);
    }

    [Fact]
    public void MultipleSymbols_IndependentRiskTracking()
    {
        var engine = new RiskEngine(new RiskConfig
        {
            MaxPositionPct = 0.15m,
            LossCooldown = TimeSpan.FromSeconds(30)
        });
        var portfolio = MakePortfolio();
        portfolio.LastKnownPrices["AAPL"] = 185m;
        portfolio.LastKnownPrices["MSFT"] = 420m;
        portfolio.LastKnownPrices["GOOGL"] = 175m;

        // Loss on AAPL
        engine.NotifyFill(MakeFill(symbol: "AAPL", timestamp: _now.AddSeconds(-5)), -100m);

        // AAPL blocked, MSFT and GOOGL fine
        var aaplOrder = MakeOrder(symbol: "AAPL", quantity: 10);
        var msftOrder = MakeOrder(symbol: "MSFT", quantity: 5);
        var googlOrder = MakeOrder(symbol: "GOOGL", quantity: 10);

        Assert.Equal(RiskAction.REJECT, engine.Evaluate(aaplOrder, portfolio).Action);
        Assert.Equal(RiskAction.APPROVE, engine.Evaluate(msftOrder, portfolio).Action);
        Assert.Equal(RiskAction.APPROVE, engine.Evaluate(googlOrder, portfolio).Action);
    }

    [Fact]
    public void OrderId_IsPreserved_InDecision()
    {
        var engine = new RiskEngine(new RiskConfig());
        var portfolio = MakePortfolio();
        portfolio.LastKnownPrices["AAPL"] = 185m;

        var order = MakeOrder(quantity: 10);
        var decision = engine.Evaluate(order, portfolio);

        Assert.Equal(order.OrderId, decision.OrderId);
    }

    [Fact]
    public void Timestamp_IsPreserved_InDecision()
    {
        var engine = new RiskEngine(new RiskConfig());
        var portfolio = MakePortfolio();
        portfolio.LastKnownPrices["AAPL"] = 185m;

        var specificTime = new DateTime(2025, 6, 15, 10, 30, 0, DateTimeKind.Utc);
        var order = MakeOrder(quantity: 10, timestamp: specificTime);
        var decision = engine.Evaluate(order, portfolio);

        Assert.Equal(specificTime, decision.Timestamp);
    }
}

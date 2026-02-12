using TradingSystem.Core.Enums;
using TradingSystem.Core.Events;
using TradingSystem.Core.Interfaces;
using TradingSystem.Core.Models;

namespace TradingSystem.Risk;

public class RiskEngine : IRiskEngine
{
    private readonly RiskConfig _config;

    // Tracking state for rate-based checks
    private readonly List<DateTime> _fillTimestamps = new();
    private decimal _dailyTurnover;
    private readonly Dictionary<string, DateTime> _lastLossTime = new();

    public RiskEngine(RiskConfig config)
    {
        _config = config;
    }

    public RiskDecision Evaluate(OrderEvent order, PortfolioState portfolio)
    {
        var checks = new Dictionary<string, bool>();
        string? firstFailReason = null;

        // Check 1: Max Position Size
        var posCheck = CheckMaxPosition(order, portfolio);
        checks["MaxPosition"] = posCheck.Passed;
        firstFailReason ??= posCheck.FailReason;

        // Check 2: Max Daily Loss
        var dailyLossCheck = CheckMaxDailyLoss(portfolio);
        checks["MaxDailyLoss"] = dailyLossCheck.Passed;
        firstFailReason ??= dailyLossCheck.FailReason;

        // Check 3: Max Drawdown
        var drawdownCheck = CheckMaxDrawdown(portfolio);
        checks["MaxDrawdown"] = drawdownCheck.Passed;
        firstFailReason ??= drawdownCheck.FailReason;

        // Check 4: Max Trades Per Minute
        var rateCheck = CheckMaxTradesPerMinute(order.Timestamp);
        checks["MaxTradesPerMin"] = rateCheck.Passed;
        firstFailReason ??= rateCheck.FailReason;

        // Check 5: Max Daily Turnover
        var turnoverCheck = CheckMaxDailyTurnover(order, portfolio);
        checks["MaxDailyTurnover"] = turnoverCheck.Passed;
        firstFailReason ??= turnoverCheck.FailReason;

        // Check 6: Loss Cooldown
        var cooldownCheck = CheckLossCooldown(order);
        checks["LossCooldown"] = cooldownCheck.Passed;
        firstFailReason ??= cooldownCheck.FailReason;

        var action = firstFailReason == null ? RiskAction.APPROVE : RiskAction.REJECT;
        var reason = firstFailReason ?? "Passed all checks";

        return new RiskDecision
        {
            Timestamp = order.Timestamp,
            OrderId = order.OrderId,
            Action = action,
            Reason = reason,
            CheckResults = checks
        };
    }

    public void NotifyFill(FillEvent fill, decimal realizedPnL)
    {
        // Track fill timestamp for trades-per-minute check
        _fillTimestamps.Add(fill.Timestamp);

        // Track turnover
        _dailyTurnover += fill.Quantity * fill.FillPrice;

        // Track loss cooldown per symbol
        if (realizedPnL < 0)
        {
            _lastLossTime[fill.Symbol] = fill.Timestamp;
        }
    }

    private CheckResult CheckMaxPosition(OrderEvent order, PortfolioState portfolio)
    {
        var equity = portfolio.TotalEquity;
        if (equity <= 0)
            return CheckResult.Fail($"Portfolio equity is zero or negative");

        // Current position quantity
        decimal currentQty = 0;
        decimal lastPrice = 0;
        if (portfolio.Positions.TryGetValue(order.Symbol, out var pos))
        {
            currentQty = pos.Quantity;
            lastPrice = pos.LastPrice;
        }
        if (lastPrice <= 0 && portfolio.LastKnownPrices.TryGetValue(order.Symbol, out var knownPrice))
            lastPrice = knownPrice;

        // Calculate resulting position
        var orderQty = order.Side == Side.BUY ? order.Quantity : -order.Quantity;
        var resultingQty = currentQty + orderQty;
        var resultingNotional = Math.Abs(resultingQty * lastPrice);
        var resultingPct = resultingNotional / equity;

        if (resultingPct > _config.MaxPositionPct)
        {
            return CheckResult.Fail(
                $"Position in {order.Symbol} would be {resultingPct:P1} of equity, " +
                $"exceeding max of {_config.MaxPositionPct:P1}");
        }

        return CheckResult.Pass();
    }

    private CheckResult CheckMaxDailyLoss(PortfolioState portfolio)
    {
        if (portfolio.DailyStartEquity <= 0)
            return CheckResult.Pass();

        var dailyLossPct = -portfolio.DailyPnL / portfolio.DailyStartEquity;
        if (dailyLossPct > _config.MaxDailyLossPct)
        {
            return CheckResult.Fail(
                $"Daily loss of {dailyLossPct:P1} exceeds max allowed {_config.MaxDailyLossPct:P1}");
        }

        return CheckResult.Pass();
    }

    private CheckResult CheckMaxDrawdown(PortfolioState portfolio)
    {
        // Drawdown is negative (TotalEquity - HWM) / HWM
        var drawdownPct = -portfolio.Drawdown; // Make it positive for comparison
        if (drawdownPct > _config.MaxDrawdownPct)
        {
            return CheckResult.Fail(
                $"Drawdown of {drawdownPct:P1} exceeds max allowed {_config.MaxDrawdownPct:P1}");
        }

        return CheckResult.Pass();
    }

    private CheckResult CheckMaxTradesPerMinute(DateTime orderTimestamp)
    {
        // Clean out fills older than 60 seconds
        _fillTimestamps.RemoveAll(t => (orderTimestamp - t).TotalSeconds > 60);

        if (_fillTimestamps.Count >= _config.MaxTradesPerMinute)
        {
            return CheckResult.Fail(
                $"Trade rate {_fillTimestamps.Count}/min exceeds max {_config.MaxTradesPerMinute}/min");
        }

        return CheckResult.Pass();
    }

    private CheckResult CheckMaxDailyTurnover(OrderEvent order, PortfolioState portfolio)
    {
        if (portfolio.StartingEquity <= 0)
            return CheckResult.Pass();

        var orderNotional = order.Quantity * GetPrice(order.Symbol, portfolio);
        var projectedTurnover = _dailyTurnover + orderNotional;
        var projectedPct = projectedTurnover / portfolio.StartingEquity;

        if (projectedPct > _config.MaxDailyTurnoverPct)
        {
            return CheckResult.Fail(
                $"Daily turnover would be {projectedPct:P1} of equity, " +
                $"exceeding max {_config.MaxDailyTurnoverPct:P1}");
        }

        return CheckResult.Pass();
    }

    private CheckResult CheckLossCooldown(OrderEvent order)
    {
        if (!_lastLossTime.TryGetValue(order.Symbol, out var lossTime))
            return CheckResult.Pass();

        var elapsed = order.Timestamp - lossTime;
        if (elapsed < _config.LossCooldown)
        {
            var remaining = (_config.LossCooldown - elapsed).TotalSeconds;
            return CheckResult.Fail(
                $"Cooldown active for {order.Symbol}: {remaining:F0}s remaining after realized loss");
        }

        return CheckResult.Pass();
    }

    private static decimal GetPrice(string symbol, PortfolioState portfolio)
    {
        if (portfolio.Positions.TryGetValue(symbol, out var pos) && pos.LastPrice > 0)
            return pos.LastPrice;
        if (portfolio.LastKnownPrices.TryGetValue(symbol, out var price))
            return price;
        return 0;
    }

    private readonly record struct CheckResult(bool Passed, string? FailReason)
    {
        public static CheckResult Pass() => new(true, null);
        public static CheckResult Fail(string reason) => new(false, reason);
    }
}

namespace TradingSystem.Core.Models;

public class RiskConfig
{
    public decimal MaxPositionPct { get; init; } = 0.15m;
    public decimal MaxDailyLossPct { get; init; } = 0.02m;
    public decimal MaxDrawdownPct { get; init; } = 0.05m;
    public int MaxTradesPerMinute { get; init; } = 10;
    public decimal MaxDailyTurnoverPct { get; init; } = 5.0m;
    public TimeSpan LossCooldown { get; init; } = TimeSpan.FromSeconds(30);
    // TODO: add sector/correlation-based concentration limits
}

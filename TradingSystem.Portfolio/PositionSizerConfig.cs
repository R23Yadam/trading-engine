namespace TradingSystem.Portfolio;

public class PositionSizerConfig
{
    public decimal TargetPositionPct { get; init; } = 0.10m;  // 10% of equity per position
    public decimal MinOrderPct { get; init; } = 0.02m;        // Don't trade unless change > 2% of equity
}

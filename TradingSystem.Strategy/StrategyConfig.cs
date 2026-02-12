namespace TradingSystem.Strategy;

public class StrategyConfig
{
    public int FastMAPeriod { get; init; } = 10;
    public int SlowMAPeriod { get; init; } = 30;
    // TODO: add EMA mode as an alternative to SMA
}

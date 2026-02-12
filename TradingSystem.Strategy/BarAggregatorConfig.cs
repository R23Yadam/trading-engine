namespace TradingSystem.Strategy;

public class BarAggregatorConfig
{
    public TimeSpan BarDuration { get; init; } = TimeSpan.FromSeconds(1);
    // TODO: support per-symbol bar durations (e.g. 1s for equities, 5s for crypto)
}

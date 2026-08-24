namespace TradingSystem.MarketData;

public class MarketDataConfig
{
    public List<string> Symbols { get; init; } = ["AAPL", "MSFT", "GOOGL"];
    public int TickIntervalMs { get; init; } = 250;
    public decimal Volatility { get; init; } = 0.0002m;
    public decimal SpreadBps { get; init; } = 3m;
    public Dictionary<string, decimal> StartingPrices { get; init; } = new()
    {
        ["AAPL"] = 185.50m,
        ["MSFT"] = 420.00m,
        ["GOOGL"] = 175.00m
    };

    /// <summary>
    /// When true, quote timestamps advance by <see cref="TickIntervalMs"/> each tick round
    /// from <see cref="VirtualClockStart"/> instead of using wall clock time.
    /// </summary>
    public bool UseVirtualTime { get; init; }

    /// <summary>
    /// Skip real-time delays between tick rounds (for fast deterministic backtests).
    /// </summary>
    public bool FastForward { get; init; }

    /// <summary>
    /// Anchor for virtual timestamps and for <see cref="MaxSimulatedDuration"/>.
    /// </summary>
    public DateTime VirtualClockStart { get; init; } =
        new DateTime(2025, 1, 15, 14, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// When set with <see cref="UseVirtualTime"/>, stops the feed once simulated elapsed time reaches this span.
    /// </summary>
    public TimeSpan? MaxSimulatedDuration { get; init; }

    // TODO: load config from appsettings.json instead of hardcoding
}

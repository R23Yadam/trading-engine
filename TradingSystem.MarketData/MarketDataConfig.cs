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
    // TODO: load config from appsettings.json instead of hardcoding
}

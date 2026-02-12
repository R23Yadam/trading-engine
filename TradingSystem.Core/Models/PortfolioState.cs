namespace TradingSystem.Core.Models;

public class PortfolioState
{
    public decimal Cash { get; set; }
    public decimal StartingEquity { get; }
    public Dictionary<string, Position> Positions { get; } = new();
    public Dictionary<string, decimal> LastKnownPrices { get; } = new();
    public decimal RealizedPnL { get; set; }
    public decimal HighWaterMark { get; set; }
    public decimal DailyStartEquity { get; set; }

    public decimal TotalEquity => Cash + Positions.Values.Sum(p => p.MarketValue);
    public decimal UnrealizedPnL => Positions.Values.Sum(p => p.UnrealizedPnL);
    public decimal DailyPnL => TotalEquity - DailyStartEquity;
    public decimal Drawdown => HighWaterMark > 0 ? (TotalEquity - HighWaterMark) / HighWaterMark : 0;

    public PortfolioState(decimal startingCash)
    {
        Cash = startingCash;
        StartingEquity = startingCash;
        DailyStartEquity = startingCash;
        HighWaterMark = startingCash;
    }
}

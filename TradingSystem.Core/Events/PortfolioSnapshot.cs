using TradingSystem.Core.Models;

namespace TradingSystem.Core.Events;

public record PortfolioSnapshot : IEvent
{
    public DateTime Timestamp { get; init; }
    public string EventType => "PortfolioSnapshot";
    public decimal Cash { get; init; }
    public decimal TotalEquity { get; init; }
    public decimal RealizedPnL { get; init; }
    public decimal UnrealizedPnL { get; init; }
    public decimal DailyPnL { get; init; }
    public decimal MaxDrawdown { get; init; }
    public decimal HighWaterMark { get; init; }
    public required IReadOnlyDictionary<string, PositionInfo> Positions { get; init; }
}

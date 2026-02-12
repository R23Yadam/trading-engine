using TradingSystem.Core.Enums;

namespace TradingSystem.Core.Events;

public record FillEvent : IEvent
{
    public DateTime Timestamp { get; init; }
    public string EventType => "Fill";
    public required string OrderId { get; init; }
    public required string FillId { get; init; }
    public required string Symbol { get; init; }
    public Side Side { get; init; }
    public decimal Quantity { get; init; }
    public decimal FillPrice { get; init; }
    public decimal Fees { get; init; }
    public decimal SlippageCost { get; init; }
    public decimal LatencyMs { get; init; }
}

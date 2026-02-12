using TradingSystem.Core.Enums;

namespace TradingSystem.Core.Events;

public record OrderEvent : IEvent
{
    public DateTime Timestamp { get; init; }
    public string EventType => "Order";
    public required string OrderId { get; init; }
    public required string Symbol { get; init; }
    public Side Side { get; init; }
    public decimal Quantity { get; init; }
    public decimal TargetNotional { get; init; }
    public required string SignalSource { get; init; }
}

using TradingSystem.Core.Enums;

namespace TradingSystem.Core.Events;

public record SignalEvent : IEvent
{
    public DateTime Timestamp { get; init; }
    public string EventType => "Signal";
    public required string Symbol { get; init; }
    public SignalType Signal { get; init; }
    public decimal Strength { get; init; }
    public required string Reason { get; init; }
}

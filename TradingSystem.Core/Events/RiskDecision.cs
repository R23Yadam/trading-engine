using TradingSystem.Core.Enums;

namespace TradingSystem.Core.Events;

public record RiskDecision : IEvent
{
    public DateTime Timestamp { get; init; }
    public string EventType => "RiskDecision";
    public required string OrderId { get; init; }
    public RiskAction Action { get; init; }
    public required string Reason { get; init; }
    public required IReadOnlyDictionary<string, bool> CheckResults { get; init; }
}

namespace TradingSystem.Runner;

public enum DemoEventType { Signal, Fill, Rejection }

public record DemoEvent
{
    public required DateTime Timestamp { get; init; }
    public required DemoEventType Type { get; init; }
    public required string Symbol { get; init; }
    public string? Side { get; init; }
    public decimal? Quantity { get; init; }
    public decimal? Price { get; init; }
    public decimal? RealizedPnL { get; init; }
    public string? RejectionReason { get; init; }
    public string? SignalDirection { get; init; }
}

namespace TradingSystem.Core.Events;

public record BarEvent : IEvent
{
    public DateTime Timestamp { get; init; }
    public string EventType => "Bar";
    public required string Symbol { get; init; }
    public decimal Open { get; init; }
    public decimal High { get; init; }
    public decimal Low { get; init; }
    public decimal Close { get; init; }
    public decimal MidPrice { get; init; }
    public decimal AvgSpread { get; init; }
    public int QuoteCount { get; init; }
    public TimeSpan BarDuration { get; init; }
}

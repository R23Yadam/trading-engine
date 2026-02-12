namespace TradingSystem.Core.Events;

public record QuoteEvent : IEvent
{
    public DateTime Timestamp { get; init; }
    public string EventType => "Quote";
    public required string Symbol { get; init; }
    public decimal BidPrice { get; init; }
    public decimal AskPrice { get; init; }
    public decimal BidSize { get; init; }
    public decimal AskSize { get; init; }
    public decimal MidPrice => (BidPrice + AskPrice) / 2m;
    public decimal Spread => AskPrice - BidPrice;
}

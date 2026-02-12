namespace TradingSystem.Core.Events;

public interface IEvent
{
    DateTime Timestamp { get; }
    string EventType { get; }
}

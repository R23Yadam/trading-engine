using TradingSystem.Core.Events;

namespace TradingSystem.Core.Interfaces;

/// <summary>
/// Logs typed events to persistent storage for replay and analysis.
/// </summary>
public interface IEventLogger
{
    /// <summary>Logs a single event to the session file.</summary>
    void Log(IEvent evt);

    /// <summary>Flushes any buffered events to disk.</summary>
    void Flush();
}

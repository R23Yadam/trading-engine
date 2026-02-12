using TradingSystem.Core.Events;

namespace TradingSystem.Core.Interfaces;

/// <summary>
/// Consumes bars and emits trading signals.
/// </summary>
public interface IStrategy
{
    /// <summary>Raised when the strategy produces a new signal.</summary>
    event Action<SignalEvent> OnSignal;

    /// <summary>Processes a completed bar and updates internal state.</summary>
    void ProcessBar(BarEvent bar);
}

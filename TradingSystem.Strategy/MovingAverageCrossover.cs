using TradingSystem.Core.Enums;
using TradingSystem.Core.Events;
using TradingSystem.Core.Interfaces;

namespace TradingSystem.Strategy;

public class MovingAverageCrossover : IStrategy
{
    private readonly StrategyConfig _config;
    private readonly Dictionary<string, SymbolState> _states = new();

    public event Action<SignalEvent>? OnSignal;

    public MovingAverageCrossover(StrategyConfig config)
    {
        _config = config;
    }

    public void ProcessBar(BarEvent bar)
    {
        if (!_states.TryGetValue(bar.Symbol, out var state))
        {
            state = new SymbolState(_config.SlowMAPeriod);
            _states[bar.Symbol] = state;
        }

        state.AddClose(bar.Close);

        // Don't emit signals until we have enough data for the slow MA
        if (state.Count < _config.SlowMAPeriod)
            return;

        var fastMA = state.GetSMA(_config.FastMAPeriod);
        var slowMA = state.GetSMA(_config.SlowMAPeriod);

        SignalType? newSignal = null;

        if (fastMA > slowMA && state.LastSignal != SignalType.LONG)
        {
            newSignal = SignalType.LONG;
        }
        else if (fastMA < slowMA && state.LastSignal != SignalType.SHORT)
        {
            newSignal = SignalType.SHORT;
        }

        // Only emit on state change
        if (newSignal == null)
            return;

        state.LastSignal = newSignal.Value;

        var strength = slowMA != 0
            ? Math.Abs(fastMA - slowMA) / slowMA
            : 0m;

        var direction = newSignal == SignalType.LONG ? "above" : "below";

        OnSignal?.Invoke(new SignalEvent
        {
            Timestamp = bar.Timestamp,
            Symbol = bar.Symbol,
            Signal = newSignal.Value,
            Strength = strength,
            Reason = $"FastMA({_config.FastMAPeriod})={fastMA:F4} crossed {direction} SlowMA({_config.SlowMAPeriod})={slowMA:F4}"
        });
    }

    private class SymbolState
    {
        private readonly decimal[] _closes;
        private int _head;
        public int Count { get; private set; }
        public SignalType? LastSignal { get; set; }

        public SymbolState(int capacity)
        {
            _closes = new decimal[capacity];
        }

        public void AddClose(decimal close)
        {
            _closes[_head] = close;
            _head = (_head + 1) % _closes.Length;
            if (Count < _closes.Length)
                Count++;
        }

        public decimal GetSMA(int period)
        {
            if (Count < period)
                return 0m;

            decimal sum = 0;
            for (int i = 0; i < period; i++)
            {
                // Walk backwards from the most recent value
                var idx = ((_head - 1 - i) % _closes.Length + _closes.Length) % _closes.Length;
                sum += _closes[idx];
            }
            return sum / period;
        }
    }
}

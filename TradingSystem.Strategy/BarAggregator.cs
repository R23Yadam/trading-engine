using TradingSystem.Core.Events;
using TradingSystem.Core.Interfaces;

namespace TradingSystem.Strategy;

public class BarAggregator : IBarAggregator
{
    private readonly BarAggregatorConfig _config;
    private readonly Dictionary<string, BarBuilder> _builders = new();

    public event Action<BarEvent>? OnBar;

    public BarAggregator(BarAggregatorConfig config)
    {
        _config = config;
    }

    public void ProcessQuote(QuoteEvent quote)
    {
        if (!_builders.TryGetValue(quote.Symbol, out var builder))
        {
            builder = new BarBuilder(quote.Symbol, _config.BarDuration, quote.Timestamp);
            _builders[quote.Symbol] = builder;
        }

        // Check if this quote belongs to a new bar window
        var barEnd = builder.WindowStart + _config.BarDuration;
        if (quote.Timestamp >= barEnd)
        {
            // Finalize the current bar and emit it
            var bar = builder.Build(barEnd);
            OnBar?.Invoke(bar);

            // Start a new window aligned to the bar boundary
            // Skip forward if multiple bar durations have passed
            var newStart = barEnd;
            while (newStart + _config.BarDuration <= quote.Timestamp)
                newStart += _config.BarDuration;

            builder = new BarBuilder(quote.Symbol, _config.BarDuration, newStart);
            _builders[quote.Symbol] = builder;
        }

        builder.AddQuote(quote);
    }

    private class BarBuilder
    {
        public string Symbol { get; }
        public DateTime WindowStart { get; }
        private readonly TimeSpan _duration;

        private decimal _open;
        private decimal _high;
        private decimal _low;
        private decimal _close;
        private decimal _spreadSum;
        private int _quoteCount;
        private bool _initialized;

        public BarBuilder(string symbol, TimeSpan duration, DateTime windowStart)
        {
            Symbol = symbol;
            _duration = duration;
            WindowStart = windowStart;
        }

        public void AddQuote(QuoteEvent quote)
        {
            var mid = quote.MidPrice;

            if (!_initialized)
            {
                _open = mid;
                _high = mid;
                _low = mid;
                _close = mid;
                _initialized = true;
            }
            else
            {
                if (mid > _high) _high = mid;
                if (mid < _low) _low = mid;
                _close = mid;
            }

            _spreadSum += quote.Spread;
            _quoteCount++;
        }

        public BarEvent Build(DateTime closeTime)
        {
            return new BarEvent
            {
                Timestamp = closeTime,
                Symbol = Symbol,
                Open = _open,
                High = _high,
                Low = _low,
                Close = _close,
                MidPrice = _close,
                AvgSpread = _quoteCount > 0 ? _spreadSum / _quoteCount : 0,
                QuoteCount = _quoteCount,
                BarDuration = _duration
            };
        }
    }
}

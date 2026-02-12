using TradingSystem.Core.Events;
using TradingSystem.Core.Interfaces;

namespace TradingSystem.MarketData;

public class SimulatedMarketDataSource : IMarketDataSource
{
    private readonly MarketDataConfig _config;
    private readonly Dictionary<string, decimal> _currentMids;
    private readonly Random _random;

    public event Action<QuoteEvent>? OnQuote;

    public SimulatedMarketDataSource(MarketDataConfig config, int? seed = null)
    {
        _config = config;
        _random = seed.HasValue ? new Random(seed.Value) : new Random();
        _currentMids = new Dictionary<string, decimal>(config.StartingPrices);
    }

    public async Task StartAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            foreach (var symbol in _config.Symbols)
            {
                if (ct.IsCancellationRequested) break;

                var quote = GenerateQuote(symbol);
                OnQuote?.Invoke(quote);
            }

            try
            {
                await Task.Delay(_config.TickIntervalMs, ct);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    public Task StopAsync()
    {
        return Task.CompletedTask;
    }

    private QuoteEvent GenerateQuote(string symbol)
    {
        var prevMid = _currentMids[symbol];

        // Random walk: newMid = prevMid * (1 + normalRandom * volatility)
        var normalRandom = NextGaussian();
        var newMid = prevMid * (1m + (decimal)normalRandom * _config.Volatility);

        // Ensure price stays positive
        if (newMid <= 0.01m)
            newMid = 0.01m;

        _currentMids[symbol] = newMid;

        // Randomized spread: 1-5 bps around the configured average
        var spreadBps = _config.SpreadBps + (decimal)(_random.NextDouble() * 4.0 - 2.0); // ±2 bps jitter
        if (spreadBps < 1m) spreadBps = 1m;

        var spread = newMid * spreadBps / 10000m;
        var bidPrice = Math.Round(newMid - spread / 2m, 4);
        var askPrice = Math.Round(newMid + spread / 2m, 4);

        // Random sizes in round lots (100-10,000 shares)
        var bidSize = (decimal)(_random.Next(1, 101) * 100);
        var askSize = (decimal)(_random.Next(1, 101) * 100);

        return new QuoteEvent
        {
            Timestamp = DateTime.UtcNow,
            Symbol = symbol,
            BidPrice = bidPrice,
            AskPrice = askPrice,
            BidSize = bidSize,
            AskSize = askSize
        };
    }

    /// <summary>
    /// Box-Muller transform to generate standard normal random values.
    /// </summary>
    private double NextGaussian()
    {
        var u1 = 1.0 - _random.NextDouble(); // (0, 1] to avoid log(0)
        var u2 = _random.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }
}

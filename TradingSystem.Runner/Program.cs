using TradingSystem.Core;
using TradingSystem.Core.Enums;
using TradingSystem.Core.Events;
using TradingSystem.Execution;
using TradingSystem.Logging;
using TradingSystem.MarketData;
using TradingSystem.Portfolio;
using TradingSystem.Risk;
using TradingSystem.Runner;
using TradingSystem.Strategy;
using TradingSystem.Core.Models;

// ═══════════════════════════════════════════════════════════════
//  EVENT-DRIVEN TRADING SYSTEM
// ═══════════════════════════════════════════════════════════════

// ── Parse args ─────────────────────────────────────────────────
string? replayFile = null;
var runDuration = TimeSpan.FromMinutes(2);
bool demoMode = false;
bool durationExplicitlySet = false;

for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--duration" && i + 1 < args.Length)
    {
        if (int.TryParse(args[i + 1], out var seconds))
        {
            runDuration = TimeSpan.FromSeconds(seconds);
            durationExplicitlySet = true;
        }
    }
    if (args[i] == "--replay" && i + 1 < args.Length)
        replayFile = args[i + 1];
    if (args[i] == "--demo")
        demoMode = true;
}

// Default to 60s when --demo is specified without explicit --duration
if (demoMode && !durationExplicitlySet)
    runDuration = TimeSpan.FromSeconds(60);

// ── Configuration ──────────────────────────────────────────────
var marketConfig = new MarketDataConfig
{
    Symbols = ["AAPL", "MSFT", "GOOGL"],
    TickIntervalMs = 250,
    Volatility = 0.0002m,
    SpreadBps = 3m,
    StartingPrices = new()
    {
        ["AAPL"] = 185.50m,
        ["MSFT"] = 420.00m,
        ["GOOGL"] = 175.00m
    }
};

var barConfig = new BarAggregatorConfig { BarDuration = TimeSpan.FromSeconds(1) };
var strategyConfig = new StrategyConfig { FastMAPeriod = 10, SlowMAPeriod = 30 };
var sizerConfig = new PositionSizerConfig { TargetPositionPct = 0.10m, MinOrderPct = 0.02m };
var riskConfig = new RiskConfig
{
    MaxPositionPct = 0.15m,
    MaxDailyLossPct = 0.02m,
    MaxDrawdownPct = 0.05m,
    MaxTradesPerMinute = 10,
    MaxDailyTurnoverPct = 5.0m,
    LossCooldown = TimeSpan.FromSeconds(30)
};
var execConfig = new ExecutionConfig { SlippageBps = 1.0m, FeePerShare = 0.005m, LatencyMs = 0 };
var portfolioConfig = new PortfolioConfig { StartingCash = 100_000m };

// ── Initialize modules ────────────────────────────────────────
var logDir = Path.Combine(Directory.GetCurrentDirectory(), "logs");
var logger = new JsonEventLogger(logDir);
var aggregator = new BarAggregator(barConfig);
var strategy = new MovingAverageCrossover(strategyConfig);
var sizer = new PositionSizer(sizerConfig);
var risk = new RiskEngine(riskConfig);
var executor = new ExecutionSimulator(execConfig);
var portfolio = new PortfolioManager(portfolioConfig);
var metrics = new MetricsTracker();
var latestQuotes = new Dictionary<string, QuoteEvent>();

// Track recent events for the live ticker
var recentEvents = new List<string>();
var demoEvents = new List<DemoEvent>();
var eventLock = new object();
var lastTickerUpdate = DateTime.MinValue;

// ── Wire event pipeline ───────────────────────────────────────

void ProcessQuote(QuoteEvent quote)
{
    logger.Log(quote);
    latestQuotes[quote.Symbol] = quote;
    portfolio.CurrentState.LastKnownPrices[quote.Symbol] = quote.MidPrice;
    aggregator.ProcessQuote(quote);
    metrics.TotalQuotes++;
}

aggregator.OnBar += bar =>
{
    logger.Log(bar);
    strategy.ProcessBar(bar);
    metrics.TotalBars++;
};

strategy.OnSignal += signal =>
{
    logger.Log(signal);
    metrics.TotalSignals++;

    lock (eventLock)
    {
        demoEvents.Add(new DemoEvent
        {
            Timestamp = signal.Timestamp,
            Type = DemoEventType.Signal,
            Symbol = signal.Symbol,
            SignalDirection = signal.Signal.ToString()
        });
        if (demoEvents.Count > 50) demoEvents.RemoveRange(0, demoEvents.Count - 50);
    }

    var order = sizer.SizeOrder(signal, portfolio.CurrentState);
    if (order == null) return;

    logger.Log(order);
    metrics.TotalOrders++;

    var decision = risk.Evaluate(order, portfolio.CurrentState);
    logger.Log(decision);

    if (decision.Action == RiskAction.REJECT)
    {
        var failedCheck = decision.CheckResults
            .FirstOrDefault(c => !c.Value).Key ?? "Unknown";
        metrics.RecordRejection(failedCheck);
        recentEvents.Add($"  REJECT  {order.Symbol} {order.Side} {order.Quantity}  [{failedCheck}]");
        lock (eventLock)
        {
            demoEvents.Add(new DemoEvent
            {
                Timestamp = order.Timestamp,
                Type = DemoEventType.Rejection,
                Symbol = order.Symbol,
                Side = order.Side.ToString(),
                Quantity = order.Quantity,
                RejectionReason = failedCheck
            });
            if (demoEvents.Count > 50) demoEvents.RemoveRange(0, demoEvents.Count - 50);
        }
        return;
    }

    if (!latestQuotes.TryGetValue(order.Symbol, out var latestQuote))
        return;

    var fill = executor.Execute(order, latestQuote);
    logger.Log(fill);

    decimal realizedBefore = portfolio.CurrentState.RealizedPnL;
    var snapshot = portfolio.ProcessFill(fill, latestQuote);
    logger.Log(snapshot);

    decimal realizedOnThisFill = snapshot.RealizedPnL - realizedBefore;
    risk.NotifyFill(fill, realizedOnThisFill);
    metrics.RecordFill(fill.Timestamp, fill.Fees, fill.SlippageCost,
        fill.Quantity * fill.FillPrice, portfolio.CurrentState.StartingEquity);
    metrics.UpdatePnL(snapshot.DailyPnL, snapshot.MaxDrawdown);

    var pnlStr = realizedOnThisFill != 0
        ? $"  PnL: {(realizedOnThisFill >= 0 ? "+" : "")}{realizedOnThisFill:C2}"
        : "";
    recentEvents.Add($"  FILL    {fill.Symbol} {fill.Side} {fill.Quantity} @ ${fill.FillPrice:F2}{pnlStr}");
    lock (eventLock)
    {
        demoEvents.Add(new DemoEvent
        {
            Timestamp = fill.Timestamp,
            Type = DemoEventType.Fill,
            Symbol = fill.Symbol,
            Side = fill.Side.ToString(),
            Quantity = fill.Quantity,
            Price = fill.FillPrice,
            RealizedPnL = realizedOnThisFill != 0 ? realizedOnThisFill : null
        });
        if (demoEvents.Count > 50) demoEvents.RemoveRange(0, demoEvents.Count - 50);
    }
};

// ── Startup banner ────────────────────────────────────────────
var startTime = DateTime.UtcNow;
var isReplay = replayFile != null;

System.Timers.Timer? ticker = null;

if (!demoMode)
{
Console.Clear();
Console.ForegroundColor = ConsoleColor.White;
Console.WriteLine();
Console.WriteLine("  ████████╗██████╗  █████╗ ██████╗ ██╗███╗   ██╗ ██████╗ ");
Console.WriteLine("  ╚══██╔══╝██╔══██╗██╔══██╗██╔══██╗██║████╗  ██║██╔════╝ ");
Console.WriteLine("     ██║   ██████╔╝███████║██║  ██║██║██╔██╗ ██║██║  ███╗");
Console.WriteLine("     ██║   ██╔══██╗██╔══██║██║  ██║██║██║╚██╗██║██║   ██║");
Console.WriteLine("     ██║   ██║  ██║██║  ██║██████╔╝██║██║ ╚████║╚██████╔╝");
Console.WriteLine("     ╚═╝   ╚═╝  ╚═╝╚═╝  ╚═╝╚═════╝ ╚═╝╚═╝  ╚═══╝ ╚═════╝ ");
Console.ForegroundColor = ConsoleColor.DarkGray;
Console.WriteLine("     Event-Driven Trading System                     v1.0");
Console.ResetColor();
Console.WriteLine();
Console.ForegroundColor = ConsoleColor.DarkCyan;
Console.WriteLine("  ── Configuration ───────────────────────────────────────");
Console.ResetColor();

if (isReplay)
{
    Console.ForegroundColor = ConsoleColor.Magenta;
    Console.WriteLine($"  Mode:               REPLAY");
    Console.ResetColor();
    Console.WriteLine($"  Source:              {replayFile}");
}
else
{
    Console.WriteLine($"  Mode:               LIVE (simulated)");
}

Console.WriteLine($"  Symbols:            {string.Join(", ", marketConfig.Symbols)}");
Console.WriteLine($"  Starting Equity:    ${portfolioConfig.StartingCash:N2}");
Console.WriteLine($"  Strategy:           MA Crossover ({strategyConfig.FastMAPeriod}/{strategyConfig.SlowMAPeriod})");
Console.WriteLine($"  Bar Duration:       {barConfig.BarDuration.TotalSeconds:F0}s");
Console.WriteLine($"  Risk Limits:        {riskConfig.MaxPositionPct:P0} max position | {riskConfig.MaxDailyLossPct:P0} max daily loss | {riskConfig.MaxDrawdownPct:P0} max drawdown");
Console.WriteLine($"  Execution:          {execConfig.SlippageBps} bps slippage | ${execConfig.FeePerShare}/share fee");

if (!isReplay)
    Console.WriteLine($"  Duration:           {runDuration.TotalSeconds:F0}s");

Console.WriteLine();
Console.ForegroundColor = ConsoleColor.DarkCyan;
Console.WriteLine("  ── Live Feed ───────────────────────────────────────────");
Console.ResetColor();
Console.WriteLine();

// ── Periodic status ticker (live mode only) ───────────────────
if (!isReplay)
{
    ticker = new System.Timers.Timer(2000); // Update every 2 seconds
    ticker.Elapsed += (_, _) =>
    {
        var elapsed = DateTime.UtcNow - startTime;
        var pnl = portfolio.CurrentState.DailyPnL;
        var pnlPct = portfolio.CurrentState.DailyStartEquity > 0
            ? pnl / portfolio.CurrentState.DailyStartEquity * 100 : 0;

        // Build status line
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write($"  [{elapsed.Minutes:D2}:{elapsed.Seconds:D2}]  ");
        Console.ResetColor();
        Console.Write($"Bars: {metrics.TotalBars,-6}  Signals: {metrics.TotalSignals,-4}  Fills: {metrics.TotalFills,-4}  Rejects: {metrics.TotalRejections,-4}  ");

        Console.ForegroundColor = pnl >= 0 ? ConsoleColor.Green : ConsoleColor.Red;
        Console.Write($"PnL: {(pnl >= 0 ? "+" : "")}{pnl:C2} ({(pnl >= 0 ? "+" : "")}{pnlPct:F2}%)");
        Console.ResetColor();
        Console.WriteLine();

        // Print any recent trade events since last tick
        lock (recentEvents)
        {
            foreach (var evt in recentEvents)
            {
                if (evt.Contains("FILL"))
                    Console.ForegroundColor = ConsoleColor.Cyan;
                else if (evt.Contains("REJECT"))
                    Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(evt);
                Console.ResetColor();
            }
            recentEvents.Clear();
        }
    };
    ticker.Start();
}
} // end if (!demoMode)

// ── Run ────────────────────────────────────────────────────────
if (isReplay)
{
    var replayer = new EventReplayer();
    await foreach (var quote in replayer.ReplayQuotes(replayFile!))
        ProcessQuote(quote);
}
else if (demoMode)
{
    var source = new SimulatedMarketDataSource(marketConfig, seed: 42);
    source.OnQuote += ProcessQuote;

    using var cts = new CancellationTokenSource(runDuration);
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };

    var display = new DemoDisplay(
        metrics,
        portfolio.CurrentState,
        marketConfig.Symbols,
        demoEvents,
        eventLock,
        startTime,
        runDuration);

    // Run market data in background, Spectre display in foreground
    var dataTask = Task.Run(() => source.StartAsync(cts.Token));
    await display.RunAsync(cts.Token);
    await dataTask;
}
else
{
    var source = new SimulatedMarketDataSource(marketConfig, seed: 42);
    source.OnQuote += ProcessQuote;

    using var cts = new CancellationTokenSource(runDuration);
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };

    await source.StartAsync(cts.Token);
}

// ── Shutdown ───────────────────────────────────────────────────
ticker?.Stop();
ticker?.Dispose();

var endTime = DateTime.UtcNow;

if (latestQuotes.Count > 0)
    portfolio.MarkToMarket(latestQuotes);

logger.Flush();
logger.Close();

// Print any remaining events
lock (recentEvents)
{
    foreach (var evt in recentEvents)
    {
        if (evt.Contains("FILL"))
            Console.ForegroundColor = ConsoleColor.Cyan;
        else if (evt.Contains("REJECT"))
            Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(evt);
        Console.ResetColor();
    }
    recentEvents.Clear();
}

Console.ForegroundColor = ConsoleColor.DarkGray;
Console.WriteLine();
Console.WriteLine("  Session complete. Generating report...");
Console.ResetColor();

// ── Print end-of-day report ───────────────────────────────────
SessionReport.Print(
    metrics,
    portfolio.CurrentState,
    logger.FilePath,
    logger.EventCount,
    startTime,
    endTime,
    marketConfig.Symbols);

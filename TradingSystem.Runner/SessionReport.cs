using System.Net;
using System.Text;
using TradingSystem.Core;
using TradingSystem.Core.Models;

namespace TradingSystem.Runner;

public static class SessionReport
{
    public static void Print(
        MetricsTracker metrics,
        PortfolioState portfolio,
        string sessionFile,
        int eventCount,
        DateTime startTime,
        DateTime endTime,
        List<string> symbolsTraded)
    {
        var duration = endTime - startTime;
        var dailyPnL = portfolio.DailyPnL;
        var dailyPct = portfolio.DailyStartEquity > 0 ? dailyPnL / portfolio.DailyStartEquity * 100 : 0;
        var rejectPct = metrics.TotalOrders > 0 ? (decimal)metrics.TotalRejections / metrics.TotalOrders * 100 : 0;
        var avgTradesPerMin = duration.TotalMinutes > 0 ? metrics.TotalFills / (decimal)duration.TotalMinutes : 0;

        var w = ConsoleColor.White;
        var d = ConsoleColor.DarkGray;
        var c = ConsoleColor.DarkCyan;
        var pnlColor = dailyPnL >= 0 ? ConsoleColor.Green : ConsoleColor.Red;
        var sign = dailyPnL >= 0 ? "+" : "";

        Console.WriteLine();
        W(w, "════════════════════════════════════════════════════════════");
        W(w, $"  TRADING SESSION REPORT — {startTime:yyyy-MM-dd}");
        W(w, "════════════════════════════════════════════════════════════");
        Console.WriteLine();
        Console.WriteLine($"  Duration:           {duration.Hours}h {duration.Minutes:D2}m {duration.Seconds:D2}s");
        Console.WriteLine($"  Symbols Traded:     {string.Join(", ", symbolsTraded)}");

        Console.WriteLine();
        W(c, "  ── Pipeline Stats ──────────────────────────────────────");
        Console.WriteLine($"  Quotes Received:    {metrics.TotalQuotes,10:N0}");
        Console.WriteLine($"  Bars Generated:     {metrics.TotalBars,10:N0}");
        Console.WriteLine($"  Signals Emitted:    {metrics.TotalSignals,10:N0}");
        Console.WriteLine($"  Orders Created:     {metrics.TotalOrders,10:N0}");
        Console.WriteLine($"  Orders Filled:      {metrics.TotalFills,10:N0}");
        Console.WriteLine($"  Orders Rejected:    {metrics.TotalRejections,10}  ({rejectPct:F1}% reject rate)");

        if (metrics.RejectionReasons.Count > 0)
        {
            Console.WriteLine();
            W(c, "  ── Rejection Breakdown ─────────────────────────────────");
            foreach (var (reason, count) in metrics.RejectionReasons.OrderByDescending(r => r.Value))
                Console.WriteLine($"  {reason,-24}{count,6}");
        }

        Console.WriteLine();
        W(c, "  ── Performance ─────────────────────────────────────────");
        Console.WriteLine($"  Starting Equity:    {portfolio.StartingEquity,14:C2}");
        Console.WriteLine($"  Ending Equity:      {portfolio.TotalEquity,14:C2}");
        W(pnlColor, $"  Daily P&L:          {sign}{dailyPnL,13:C2}  ({sign}{dailyPct:F2}%)");
        Console.WriteLine($"  Realized P&L:       {(portfolio.RealizedPnL >= 0 ? "+" : "")}{portfolio.RealizedPnL,13:C2}");
        Console.WriteLine($"  Unrealized P&L:     {(portfolio.UnrealizedPnL >= 0 ? "+" : "")}{portfolio.UnrealizedPnL,13:C2}");
        Console.WriteLine($"  Max Drawdown:       {metrics.MaxDrawdown * 100,13:F2}%");
        Console.WriteLine($"  High Water Mark:    {portfolio.HighWaterMark,14:C2}");

        Console.WriteLine();
        W(c, "  ── Costs ───────────────────────────────────────────────");
        Console.WriteLine($"  Total Fees:         {metrics.TotalFees,14:C2}");
        Console.WriteLine($"  Total Slippage:     {metrics.TotalSlippage,14:C2}");
        Console.WriteLine($"  Total Costs:        {metrics.TotalFees + metrics.TotalSlippage,14:C2}");

        Console.WriteLine();
        W(c, "  ── Activity ────────────────────────────────────────────");
        Console.WriteLine($"  Total Turnover:     {metrics.TotalTurnover,14:C2}  ({metrics.TurnoverPct * 100:F1}% of equity)");
        Console.WriteLine($"  Avg Trades/Min:     {avgTradesPerMin,14:F2}");
        Console.WriteLine($"  Peak Trades/Min:    {metrics.PeakTradesPerMinute,14:F0}");

        Console.WriteLine();
        W(c, "  ── Log ─────────────────────────────────────────────────");
        Console.WriteLine($"  Session File:       {sessionFile}");
        Console.WriteLine($"  Events Logged:      {eventCount,10:N0}");
        W(d, $"  Replay:             dotnet run -- --replay {sessionFile}");

        Console.WriteLine();
        W(w, "════════════════════════════════════════════════════════════");
        Console.WriteLine();
    }

    /// <summary>
    /// Writes a self-contained HTML summary (for sharing or screenshots).
    /// </summary>
    public static void ExportHtml(
        string path,
        MetricsTracker metrics,
        PortfolioState portfolio,
        string sessionFile,
        int eventCount,
        DateTime startTime,
        DateTime endTime,
        List<string> symbolsTraded,
        int? rngSeed = null,
        bool backtestMode = false,
        TimeSpan? simulatedHorizon = null)
    {
        var duration = endTime - startTime;
        var dailyPnL = portfolio.DailyPnL;
        var dailyPct = portfolio.DailyStartEquity > 0 ? dailyPnL / portfolio.DailyStartEquity * 100 : 0;
        var rejectPct = metrics.TotalOrders > 0 ? (decimal)metrics.TotalRejections / metrics.TotalOrders * 100 : 0;
        var avgTradesPerMin = duration.TotalMinutes > 0 ? metrics.TotalFills / (decimal)duration.TotalMinutes : 0;
        var pnlClass = dailyPnL >= 0 ? "pnl-pos" : "pnl-neg";
        var sign = dailyPnL >= 0 ? "+" : "";

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var rejectionRows = new StringBuilder();
        if (metrics.RejectionReasons.Count > 0)
        {
            foreach (var (reason, count) in metrics.RejectionReasons.OrderByDescending(r => r.Value))
            {
                rejectionRows.AppendLine(
                    $"<tr><td>{WebUtility.HtmlEncode(reason)}</td><td class=\"num\">{count}</td></tr>");
            }
        }

        var seedLine = rngSeed.HasValue
            ? $"<p class=\"meta\">RNG seed: <strong>{rngSeed.Value}</strong></p>"
            : "";

        var modeLine = backtestMode
            ? "<p class=\"meta\"><span class=\"badge\">Backtest</span> Virtual clock · fast-forward</p>"
            : "";

        var simLine = simulatedHorizon is { } sim
            ? $"<p class=\"meta\">Simulated horizon: <strong>{(int)sim.TotalMinutes}m {sim.Seconds}s</strong> of market time</p>"
            : "";

        var html = $"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
              <meta charset="utf-8"/>
              <meta name="viewport" content="width=device-width, initial-scale=1"/>
              <title>Session report — {WebUtility.HtmlEncode(startTime.ToString("yyyy-MM-dd"))}</title>
              <style>
                :root {{
                  --bg: #0f1419;
                  --panel: #1a2332;
                  --text: #e7ecf3;
                  --muted: #8b9cb3;
                  --accent: #3db8e8;
                  --pos: #3ecf8e;
                  --neg: #f56565;
                }}
                body {{
                  font-family: ui-sans-serif, system-ui, "Segoe UI", Roboto, Helvetica, Arial, sans-serif;
                  background: var(--bg);
                  color: var(--text);
                  margin: 0;
                  padding: 2rem;
                  line-height: 1.5;
                }}
                .wrap {{ max-width: 52rem; margin: 0 auto; }}
                h1 {{
                  font-size: 1.35rem;
                  font-weight: 600;
                  border-bottom: 1px solid #2a3545;
                  padding-bottom: 0.75rem;
                  margin-top: 0;
                }}
                .meta {{ color: var(--muted); font-size: 0.9rem; margin: 0.35rem 0; }}
                .badge {{
                  display: inline-block;
                  background: #2d3d52;
                  color: var(--accent);
                  padding: 0.15rem 0.5rem;
                  border-radius: 4px;
                  font-size: 0.75rem;
                  font-weight: 600;
                  letter-spacing: 0.03em;
                }}
                .grid {{
                  display: grid;
                  grid-template-columns: repeat(auto-fit, minmax(14rem, 1fr));
                  gap: 1rem;
                  margin: 1.25rem 0;
                }}
                .card {{
                  background: var(--panel);
                  border-radius: 8px;
                  padding: 1rem 1.1rem;
                  border: 1px solid #2a3545;
                }}
                .card h2 {{
                  margin: 0 0 0.65rem;
                  font-size: 0.75rem;
                  text-transform: uppercase;
                  letter-spacing: 0.06em;
                  color: var(--muted);
                  font-weight: 600;
                }}
                table {{ width: 100%; border-collapse: collapse; font-size: 0.92rem; }}
                th, td {{ text-align: left; padding: 0.35rem 0.5rem 0.35rem 0; border-bottom: 1px solid #2a3545; }}
                th {{ color: var(--muted); font-weight: 500; font-size: 0.8rem; }}
                .num {{ text-align: right; font-variant-numeric: tabular-nums; }}
                .pnl-pos {{ color: var(--pos); font-weight: 600; }}
                .pnl-neg {{ color: var(--neg); font-weight: 600; }}
                .footer {{ margin-top: 1.5rem; font-size: 0.85rem; color: var(--muted); word-break: break-all; }}
              </style>
            </head>
            <body>
              <div class="wrap">
                <h1>Trading session report — {WebUtility.HtmlEncode(startTime.ToString("yyyy-MM-dd"))}</h1>
                {modeLine}
                {seedLine}
                {simLine}
                <p class="meta">Wall-clock duration: {duration.Hours}h {duration.Minutes:D2}m {duration.Seconds:D2}s · Symbols: {WebUtility.HtmlEncode(string.Join(", ", symbolsTraded))}</p>

                <div class="grid">
                  <div class="card">
                    <h2>Performance</h2>
                    <table>
                      <tr><th>Starting equity</th><td class="num">{portfolio.StartingEquity:C2}</td></tr>
                      <tr><th>Ending equity</th><td class="num">{portfolio.TotalEquity:C2}</td></tr>
                      <tr><th>Daily P&amp;L</th><td class="num {pnlClass}">{sign}{dailyPnL:C2} ({sign}{dailyPct:F2}%)</td></tr>
                      <tr><th>Realized P&amp;L</th><td class="num">{(portfolio.RealizedPnL >= 0 ? "+" : "")}{portfolio.RealizedPnL:C2}</td></tr>
                      <tr><th>Unrealized P&amp;L</th><td class="num">{(portfolio.UnrealizedPnL >= 0 ? "+" : "")}{portfolio.UnrealizedPnL:C2}</td></tr>
                      <tr><th>Max drawdown</th><td class="num">{metrics.MaxDrawdown * 100:F2}%</td></tr>
                      <tr><th>High water mark</th><td class="num">{portfolio.HighWaterMark:C2}</td></tr>
                    </table>
                  </div>
                  <div class="card">
                    <h2>Pipeline</h2>
                    <table>
                      <tr><th>Quotes</th><td class="num">{metrics.TotalQuotes:N0}</td></tr>
                      <tr><th>Bars</th><td class="num">{metrics.TotalBars:N0}</td></tr>
                      <tr><th>Signals</th><td class="num">{metrics.TotalSignals:N0}</td></tr>
                      <tr><th>Orders</th><td class="num">{metrics.TotalOrders:N0}</td></tr>
                      <tr><th>Fills</th><td class="num">{metrics.TotalFills:N0}</td></tr>
                      <tr><th>Rejected</th><td class="num">{metrics.TotalRejections} ({rejectPct:F1}%)</td></tr>
                    </table>
                  </div>
                  <div class="card">
                    <h2>Activity &amp; costs</h2>
                    <table>
                      <tr><th>Turnover</th><td class="num">{metrics.TotalTurnover:C2} ({metrics.TurnoverPct * 100:F1}%)</td></tr>
                      <tr><th>Avg trades/min</th><td class="num">{avgTradesPerMin:F2}</td></tr>
                      <tr><th>Peak trades/min</th><td class="num">{metrics.PeakTradesPerMinute:F0}</td></tr>
                      <tr><th>Fees</th><td class="num">{metrics.TotalFees:C2}</td></tr>
                      <tr><th>Slippage</th><td class="num">{metrics.TotalSlippage:C2}</td></tr>
                    </table>
                  </div>
                </div>
            """;

        if (metrics.RejectionReasons.Count > 0)
        {
            html += $"""
                <div class="card" style="margin-bottom:1rem;">
                  <h2>Rejection breakdown</h2>
                  <table>
                    <thead><tr><th>Reason</th><th class="num">Count</th></tr></thead>
                    <tbody>{rejectionRows}</tbody>
                  </table>
                </div>
            """;
        }

        html += $"""
                <div class="footer">
                  <strong>Log</strong> · {WebUtility.HtmlEncode(sessionFile)} · {eventCount:N0} events<br/>
                  Replay: <code>dotnet run --project TradingSystem.Runner -- --replay {WebUtility.HtmlEncode(sessionFile)}</code>
                </div>
              </div>
            </body>
            </html>
            """;

        File.WriteAllText(path, html);
    }

    private static void W(ConsoleColor color, string text)
    {
        Console.ForegroundColor = color;
        Console.WriteLine(text);
        Console.ResetColor();
    }
}

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

    private static void W(ConsoleColor color, string text)
    {
        Console.ForegroundColor = color;
        Console.WriteLine(text);
        Console.ResetColor();
    }
}

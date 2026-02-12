using Spectre.Console;
using Spectre.Console.Rendering;
using TradingSystem.Core;
using TradingSystem.Core.Events;
using TradingSystem.Core.Models;

namespace TradingSystem.Runner;

public class DemoDisplay
{
    private readonly MetricsTracker _metrics;
    private readonly PortfolioState _portfolio;
    private readonly List<string> _symbols;
    private readonly List<DemoEvent> _demoEvents;
    private readonly object _lock;
    private readonly DateTime _startTime;
    private readonly TimeSpan _duration;

    public DemoDisplay(
        MetricsTracker metrics,
        PortfolioState portfolio,
        List<string> symbols,
        List<DemoEvent> demoEvents,
        object eventLock,
        DateTime startTime,
        TimeSpan duration)
    {
        _metrics = metrics;
        _portfolio = portfolio;
        _symbols = symbols;
        _demoEvents = demoEvents;
        _lock = eventLock;
        _startTime = startTime;
        _duration = duration;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        await AnsiConsole.Live(BuildLayout())
            .AutoClear(true)
            .Overflow(VerticalOverflow.Ellipsis)
            .Cropping(VerticalOverflowCropping.Bottom)
            .StartAsync(async ctx =>
            {
                while (!ct.IsCancellationRequested)
                {
                    ctx.UpdateTarget(BuildLayout());
                    try { await Task.Delay(300, ct); }
                    catch (TaskCanceledException) { break; }
                }
                ctx.UpdateTarget(BuildLayout());
            });
    }

    private IRenderable BuildLayout()
    {
        return new Rows(
            BuildHeader(),
            new Text(""),
            BuildPositionsTable(),
            BuildStatsBar(),
            BuildEventTicker()
        );
    }

    private IRenderable BuildHeader()
    {
        var elapsed = DateTime.UtcNow - _startTime;
        var remaining = _duration - elapsed;
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;

        var rule = new Rule(
            $"[bold white]TRADING SYSTEM[/]  " +
            $"[dim]elapsed[/] [white]{elapsed:mm\\:ss}[/]  " +
            $"[dim]remaining[/] [white]{remaining:mm\\:ss}[/]")
        {
            Justification = Justify.Center,
            Style = Style.Parse("cyan")
        };
        return rule;
    }

    private IRenderable BuildPositionsTable()
    {
        var table = new Table()
            .Border(TableBorder.Simple)
            .BorderColor(Color.Grey)
            .Expand()
            .AddColumn(new TableColumn("[bold]Symbol[/]").Centered())
            .AddColumn(new TableColumn("[bold]Side[/]").Centered())
            .AddColumn(new TableColumn("[bold]Qty[/]").RightAligned())
            .AddColumn(new TableColumn("[bold]Avg Cost[/]").RightAligned())
            .AddColumn(new TableColumn("[bold]Mkt Price[/]").RightAligned())
            .AddColumn(new TableColumn("[bold]Mkt Value[/]").RightAligned())
            .AddColumn(new TableColumn("[bold]Unrealized P&L[/]").RightAligned());

        // Snapshot positions under lock for thread safety
        List<Position> positions;
        lock (_lock)
        {
            positions = _portfolio.Positions.Values
                .Where(p => p.Quantity != 0)
                .ToList();
        }

        if (positions.Count == 0)
        {
            table.AddRow(
                new Markup("[dim]--[/]"),
                new Markup("[dim]--[/]"),
                new Markup("[dim]--[/]"),
                new Markup("[dim]--[/]"),
                new Markup("[dim]--[/]"),
                new Markup("[dim]--[/]"),
                new Markup("[dim]Waiting for signals...[/]"));
        }
        else
        {
            foreach (var pos in positions.OrderBy(p => p.Symbol))
            {
                var pnl = pos.UnrealizedPnL;
                var pnlColor = pnl >= 0 ? "green" : "red";
                var pnlSign = pnl >= 0 ? "+" : "";
                var side = pos.Quantity > 0 ? "[green]LONG[/]" : "[red]SHORT[/]";

                table.AddRow(
                    new Markup($"[bold white]{Markup.Escape(pos.Symbol)}[/]"),
                    new Markup(side),
                    new Markup($"[white]{Math.Abs(pos.Quantity):N0}[/]"),
                    new Markup($"[white]${pos.AvgCostBasis:F2}[/]"),
                    new Markup($"[white]${pos.LastPrice:F2}[/]"),
                    new Markup($"[white]${Math.Abs(pos.MarketValue):N2}[/]"),
                    new Markup($"[{pnlColor}]{pnlSign}{pnl:C2}[/]"));
            }
        }

        return new Panel(table)
            .Header("[bold cyan] Positions [/]")
            .BorderColor(Color.Grey)
            .Expand();
    }

    private IRenderable BuildStatsBar()
    {
        var equity = _portfolio.TotalEquity;
        var dailyPnL = _portfolio.DailyPnL;
        var dailyPct = _portfolio.DailyStartEquity > 0
            ? dailyPnL / _portfolio.DailyStartEquity * 100 : 0;
        var pnlColor = dailyPnL >= 0 ? "green" : "red";
        var pnlSign = dailyPnL >= 0 ? "+" : "";

        // MaxDrawdown is stored as negative in MetricsTracker
        var drawdown = Math.Abs(_metrics.MaxDrawdown) * 100;
        var ddColor = drawdown > 1m ? "red" : drawdown > 0.1m ? "yellow" : "green";

        var rejectRate = _metrics.RejectRate * 100;
        var rrColor = rejectRate > 30 ? "red" : rejectRate > 10 ? "yellow" : "green";

        var statsTable = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .Expand()
            .AddColumn(new TableColumn("").Centered())
            .AddColumn(new TableColumn("").Centered())
            .AddColumn(new TableColumn("").Centered())
            .AddColumn(new TableColumn("").Centered())
            .AddColumn(new TableColumn("").Centered())
            .AddColumn(new TableColumn("").Centered());

        statsTable.AddRow(
            new Markup($"[bold dim]Equity[/]\n[bold white]{equity:C2}[/]"),
            new Markup($"[bold dim]Daily P&L[/]\n[bold {pnlColor}]{pnlSign}{dailyPnL:C2} ({pnlSign}{dailyPct:F2}%)[/]"),
            new Markup($"[bold dim]Max Drawdown[/]\n[bold {ddColor}]{drawdown:F2}%[/]"),
            new Markup($"[bold dim]Fills[/]\n[bold white]{_metrics.TotalFills}[/]"),
            new Markup($"[bold dim]Signals[/]\n[bold white]{_metrics.TotalSignals}[/]"),
            new Markup($"[bold dim]Reject Rate[/]\n[bold {rrColor}]{rejectRate:F1}%[/]"));

        return new Panel(statsTable)
            .Header("[bold cyan] Stats [/]")
            .BorderColor(Color.Grey)
            .Expand();
    }

    private IRenderable BuildEventTicker()
    {
        var rows = new List<IRenderable>();

        List<DemoEvent> snapshot;
        lock (_lock)
        {
            snapshot = _demoEvents.TakeLast(8).ToList();
        }

        if (snapshot.Count == 0)
        {
            rows.Add(new Markup("[dim]  Waiting for trading activity...[/]"));
        }
        else
        {
            foreach (var evt in snapshot)
                rows.Add(FormatEvent(evt));
        }

        return new Panel(new Rows(rows))
            .Header("[bold cyan] Recent Events [/]")
            .BorderColor(Color.Grey)
            .Expand();
    }

    private static Markup FormatEvent(DemoEvent evt)
    {
        var ts = $"[dim]{evt.Timestamp:HH:mm:ss.fff}[/]";

        return evt.Type switch
        {
            DemoEventType.Fill =>
                new Markup(
                    $"  {ts}  [bold cyan]FILL[/]    " +
                    $"[white]{Markup.Escape(evt.Symbol)}[/] " +
                    $"{evt.Side} {evt.Quantity} @ " +
                    $"[white]${evt.Price:F2}[/]" +
                    FormatPnL(evt.RealizedPnL)),

            DemoEventType.Rejection =>
                new Markup(
                    $"  {ts}  [bold red]REJECT[/]  " +
                    $"[white]{Markup.Escape(evt.Symbol)}[/] " +
                    $"{evt.Side} {evt.Quantity}  " +
                    $"[red]\\[{Markup.Escape(evt.RejectionReason ?? "Unknown")}][/]"),

            DemoEventType.Signal =>
                new Markup(
                    $"  {ts}  [bold yellow]SIGNAL[/]  " +
                    $"[white]{Markup.Escape(evt.Symbol)}[/]  " +
                    $"[yellow]{Markup.Escape(evt.SignalDirection ?? "")}[/]"),

            _ => new Markup($"  {ts}  [dim]Unknown event[/]")
        };
    }

    private static string FormatPnL(decimal? pnl)
    {
        if (!pnl.HasValue || pnl == 0) return "";
        var color = pnl >= 0 ? "green" : "red";
        var sign = pnl >= 0 ? "+" : "";
        return $"  [{color}]PnL: {sign}{pnl:C2}[/]";
    }
}

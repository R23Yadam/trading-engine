namespace TradingSystem.Core;

public class MetricsTracker
{
    // Counters
    public int TotalQuotes { get; set; }
    public int TotalBars { get; set; }
    public int TotalSignals { get; set; }
    public int TotalOrders { get; set; }
    public int TotalFills { get; set; }
    public int TotalRejections { get; set; }

    // Rates
    public decimal RejectRate => TotalOrders > 0 ? (decimal)TotalRejections / TotalOrders : 0;
    public decimal TradesPerMinute { get; set; }

    // PnL
    public decimal CurrentPnL { get; set; }
    public decimal MaxDrawdown { get; set; }
    public decimal SharpeRatio { get; set; }

    // Costs
    public decimal TotalFees { get; set; }
    public decimal TotalSlippage { get; set; }

    // Turnover
    public decimal TotalTurnover { get; set; }
    public decimal TurnoverPct { get; set; }

    // Activity
    public decimal PeakTradesPerMinute { get; set; }

    // Rejection breakdown
    public Dictionary<string, int> RejectionReasons { get; set; } = new();

    // Timestamps for trades-per-minute tracking
    private readonly List<DateTime> _recentFillTimes = new();

    public void RecordFill(DateTime fillTime, decimal fees, decimal slippage, decimal notional, decimal startingEquity)
    {
        TotalFills++;
        TotalFees += fees;
        TotalSlippage += slippage;
        TotalTurnover += Math.Abs(notional);
        TurnoverPct = startingEquity > 0 ? TotalTurnover / startingEquity : 0;

        _recentFillTimes.Add(fillTime);
        // Remove fills older than 1 minute
        _recentFillTimes.RemoveAll(t => (fillTime - t).TotalSeconds > 60);
        TradesPerMinute = _recentFillTimes.Count;
        if (TradesPerMinute > PeakTradesPerMinute)
            PeakTradesPerMinute = TradesPerMinute;
    }

    public void RecordRejection(string reason)
    {
        TotalRejections++;
        if (!RejectionReasons.ContainsKey(reason))
            RejectionReasons[reason] = 0;
        RejectionReasons[reason]++;
    }

    public void UpdatePnL(decimal dailyPnL, decimal drawdown)
    {
        CurrentPnL = dailyPnL;
        if (drawdown < MaxDrawdown)
            MaxDrawdown = drawdown;
    }
}

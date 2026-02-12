namespace TradingSystem.Execution;

public class ExecutionConfig
{
    public decimal SlippageBps { get; init; } = 1.0m;
    public decimal FeePerShare { get; init; } = 0.005m;
    public decimal FeeBps { get; init; } = 0m;
    public bool UseBpsFees { get; init; } = false;
    public int LatencyMs { get; init; } = 0;
    // TODO: add market impact model (linear or square-root) based on order size vs. available liquidity
}

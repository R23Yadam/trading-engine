namespace TradingSystem.Core.Models;

public record PositionInfo
{
    public required string Symbol { get; init; }
    public decimal Quantity { get; init; }
    public decimal AvgCostBasis { get; init; }
    public decimal MarketValue { get; init; }
    public decimal UnrealizedPnL { get; init; }
}

namespace TradingSystem.Core.Models;

public class Position
{
    public required string Symbol { get; init; }
    public decimal Quantity { get; set; }
    public decimal AvgCostBasis { get; set; }
    public decimal LastPrice { get; set; }
    public decimal MarketValue => Quantity * LastPrice;
    public decimal CostBasis => Quantity * AvgCostBasis;
    public decimal UnrealizedPnL => MarketValue - CostBasis;

    public PositionInfo ToSnapshot() => new()
    {
        Symbol = Symbol,
        Quantity = Quantity,
        AvgCostBasis = AvgCostBasis,
        MarketValue = MarketValue,
        UnrealizedPnL = UnrealizedPnL
    };
}

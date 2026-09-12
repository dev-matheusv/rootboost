using RootBoost.Application.Traffic;

namespace RootBoost.Application.Tests;

public class GrowthPlannerTests
{
    private static readonly GrowthPlanner Sut = new();

    [Fact]
    public void Computes_margin_and_breakeven_from_price_cost_and_fees()
    {
        // 29.99 - 12 - (29.99*0.035 + 0.49) = 16.45
        var p = Sut.Plan(new GrowthInputs(UnitPrice: 29.99m, UnitCost: 12m));
        Assert.Equal(16.45m, p.GrossMarginPerUnit);
        Assert.Equal(16.45m, p.BreakevenCac);
        Assert.Equal(11.45m, p.TargetCac); // margem - lucro desejado (5)
    }

    [Fact]
    public void Organic_projection_from_shorts_assumptions()
    {
        // 2 shorts/dia * 30 * 400 views = 24000; *1% clique = 240 visitantes; *2% cvr = 4 pedidos
        var p = Sut.Plan(new GrowthInputs(29.99m, 12m));
        Assert.Equal(240, p.Organic.Visitors);
        Assert.Equal(4, p.Organic.Orders);
        Assert.Equal(0m, p.Organic.MediaCost);
        Assert.Equal(65.80m, p.Organic.Profit); // 4 * 16.45
    }

    [Fact]
    public void Zero_budget_recommends_organic_first_and_no_paid()
    {
        var p = Sut.Plan(new GrowthInputs(29.99m, 12m, MonthlyAdBudget: 0m));
        Assert.Null(p.Paid);
        Assert.Contains("Orgânico-first", p.Recommendation);
    }

    [Fact]
    public void Paid_not_viable_with_default_cvr_even_with_budget()
    {
        // CAC = 0.70 / 0.02 = 35.00 > 16.45 -> perde dinheiro
        var p = Sut.Plan(new GrowthInputs(29.99m, 12m, MonthlyAdBudget: 200m));
        Assert.NotNull(p.Paid);
        Assert.False(p.PaidViable);
        Assert.Equal(35.00m, p.EstimatedPaidCac);
        Assert.Contains("Orgânico-first", p.Recommendation);
    }

    [Fact]
    public void Paid_viable_when_conversion_rate_is_high_enough()
    {
        // CVR 5% -> CAC = 0.70 / 0.05 = 14.00 < 16.45 -> viável
        var p = Sut.Plan(new GrowthInputs(29.99m, 12m, MonthlyAdBudget: 200m, StoreConversionRate: 0.05));
        Assert.True(p.PaidViable);
        Assert.Equal(14.00m, p.EstimatedPaidCac);
        Assert.Contains("Blended", p.Recommendation);
    }
}

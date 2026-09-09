using RootBoost.Application.Traffic;

namespace RootBoost.Application.Tests;

public class CampaignOptimizerTests
{
    private static readonly OptimizationPolicy Policy = new();
    private static CampaignMetrics M(decimal spend, decimal revenue, long purchases, decimal budget = 20m)
        => new("c1", "Camp 1", spend, revenue, Impressions: 5000, Clicks: 150, Purchases: purchases, CurrentDailyBudget: budget);

    [Fact]
    public void Insufficient_spend_waits_for_data()
    {
        var d = new CampaignOptimizer(Policy).Decide(M(spend: 5m, revenue: 0m, purchases: 0));
        Assert.Equal(OptimizationAction.InsufficientData, d.Action);
    }

    [Fact]
    public void Losing_money_pauses()
    {
        // gastou 50, receita 20 -> ROAS 0.4 < 1.0
        var d = new CampaignOptimizer(Policy).Decide(M(spend: 50m, revenue: 20m, purchases: 1));
        Assert.Equal(OptimizationAction.Pause, d.Action);
        Assert.Equal(0m, d.SuggestedDailyBudget);
    }

    [Fact]
    public void Zero_purchases_after_min_spend_pauses()
    {
        var d = new CampaignOptimizer(Policy).Decide(M(spend: 30m, revenue: 0m, purchases: 0));
        Assert.Equal(OptimizationAction.Pause, d.Action);
    }

    [Fact]
    public void Strong_roas_scales_budget_by_step()
    {
        // ROAS 3.0 >= 2.0 -> escala +20% de 20 -> 24
        var d = new CampaignOptimizer(Policy).Decide(M(spend: 40m, revenue: 120m, purchases: 4, budget: 20m));
        Assert.Equal(OptimizationAction.Scale, d.Action);
        Assert.Equal(24m, d.SuggestedDailyBudget);
    }

    [Fact]
    public void Scale_is_capped_at_max_budget()
    {
        var d = new CampaignOptimizer(Policy).Decide(M(spend: 40m, revenue: 120m, purchases: 4, budget: 100m));
        Assert.Equal(OptimizationAction.Keep, d.Action); // já no teto
        Assert.Equal(100m, d.SuggestedDailyBudget);
    }

    [Fact]
    public void Lukewarm_roas_reduces_budget()
    {
        // ROAS 1.2 (entre 1.0 e 1.3) -> reduz -20% de 20 -> 16
        var d = new CampaignOptimizer(Policy).Decide(M(spend: 50m, revenue: 60m, purchases: 3, budget: 20m));
        Assert.Equal(OptimizationAction.Reduce, d.Action);
        Assert.Equal(16m, d.SuggestedDailyBudget);
    }

    [Fact]
    public void Healthy_roas_keeps()
    {
        // ROAS 1.6 (entre 1.3 e 2.0) -> manter
        var d = new CampaignOptimizer(Policy).Decide(M(spend: 50m, revenue: 80m, purchases: 3, budget: 20m));
        Assert.Equal(OptimizationAction.Keep, d.Action);
    }
}

public class CreativeSelectorTests
{
    private static readonly OptimizationPolicy Policy = new();
    private static CreativeStat C(string id, long imp, long clicks, long purchases, decimal spend)
        => new(id, id, imp, clicks, purchases, spend);

    [Fact]
    public void Keeps_testing_until_all_reach_min_impressions()
    {
        var res = new CreativeSelector(Policy).Evaluate(new[]
        {
            C("a", 1200, 40, 2, 20m),
            C("b", 300, 5, 0, 5m) // abaixo de 1000 impressões
        });
        Assert.All(res, d => Assert.Equal(CreativeAction.KeepTesting, d.Action));
    }

    [Fact]
    public void Promotes_lowest_cpa_and_pauses_bad_loser()
    {
        var res = new CreativeSelector(Policy).Evaluate(new[]
        {
            C("win", 2000, 80, 10, 50m),  // CPA 5.0
            C("lose", 2000, 30, 1, 40m)   // CPA 40 -> > 1.5x do vencedor
        });
        Assert.Equal(CreativeAction.PromoteWinner, res.First(d => d.CreativeId == "win").Action);
        Assert.Equal(CreativeAction.Pause, res.First(d => d.CreativeId == "lose").Action);
    }

    [Fact]
    public void No_purchases_yet_uses_ctr_to_pick_winner()
    {
        var res = new CreativeSelector(Policy).Evaluate(new[]
        {
            C("hi", 2000, 200, 0, 20m),  // CTR 10%
            C("lo", 2000, 20, 0, 20m)    // CTR 1% -> < metade do vencedor
        });
        Assert.Equal(CreativeAction.PromoteWinner, res.First(d => d.CreativeId == "hi").Action);
        Assert.Equal(CreativeAction.Pause, res.First(d => d.CreativeId == "lo").Action);
    }
}

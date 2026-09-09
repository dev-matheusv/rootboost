namespace RootBoost.Application.Traffic;

/// <summary>
/// Decide o que fazer com o orçamento de cada campanha a partir das métricas e da política.
/// Puro e determinístico (fácil de testar e auditar). Ordem das regras importa:
/// dados insuficientes -> perdendo dinheiro -> vencedor pra escalar -> morno pra reduzir -> manter.
/// </summary>
public sealed class CampaignOptimizer
{
    private readonly OptimizationPolicy _p;
    public CampaignOptimizer(OptimizationPolicy policy) => _p = policy;

    public OptimizationDecision Decide(CampaignMetrics m)
    {
        var budget = m.CurrentDailyBudget;

        if (m.Spend < _p.MinSpendForDecision)
            return new(m.CampaignId, OptimizationAction.InsufficientData, budget, budget,
                $"Gasto {m.Spend:0.00} abaixo do mínimo {_p.MinSpendForDecision:0.00}; aguardando dados.");

        if (m.Purchases == 0 || m.Roas < _p.CutRoas)
            return new(m.CampaignId, OptimizationAction.Pause, budget, 0m,
                $"ROAS {m.Roas:0.00} < {_p.CutRoas:0.00} (ou 0 vendas) após gastar {m.Spend:0.00}. Pausar pra estancar perda.");

        if (m.Roas >= _p.ScaleRoas)
        {
            var scaled = Math.Min(Round(budget * (1 + _p.BudgetStepPct)), _p.MaxDailyBudget);
            var action = scaled > budget ? OptimizationAction.Scale : OptimizationAction.Keep;
            var why = scaled > budget
                ? $"ROAS {m.Roas:0.00} >= {_p.ScaleRoas:0.00}. Escalar orçamento {budget:0.00} -> {scaled:0.00}."
                : $"ROAS {m.Roas:0.00} bom, mas orçamento já no teto {_p.MaxDailyBudget:0.00}. Manter.";
            return new(m.CampaignId, action, budget, scaled, why);
        }

        if (m.Roas < _p.ReduceRoas)
        {
            var reduced = Round(budget * (1 - _p.BudgetStepPct));
            return new(m.CampaignId, OptimizationAction.Reduce, budget, reduced,
                $"ROAS {m.Roas:0.00} entre {_p.CutRoas:0.00} e {_p.ReduceRoas:0.00}. Reduzir {budget:0.00} -> {reduced:0.00}.");
        }

        return new(m.CampaignId, OptimizationAction.Keep, budget, budget,
            $"ROAS {m.Roas:0.00} saudável. Manter {budget:0.00}.");
    }

    public IReadOnlyList<OptimizationDecision> DecideAll(IEnumerable<CampaignMetrics> metrics)
        => metrics.Select(Decide).ToList();

    private static decimal Round(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
}

namespace RootBoost.Application.Traffic;

/// <summary>
/// Teste de criativo: espera dados suficientes, elege o vencedor (menor CPA; se ninguém vendeu
/// ainda, maior CTR) e pausa os claramente piores. Puro e determinístico.
/// </summary>
public sealed class CreativeSelector
{
    private readonly OptimizationPolicy _p;
    public CreativeSelector(OptimizationPolicy policy) => _p = policy;

    public IReadOnlyList<CreativeDecision> Evaluate(IReadOnlyList<CreativeStat> creatives)
    {
        if (creatives.Count == 0) return Array.Empty<CreativeDecision>();

        // Ainda testando enquanto algum criativo não atingiu impressões mínimas.
        var ready = creatives.Where(c => c.Impressions >= _p.MinImpressionsPerCreative).ToList();
        if (ready.Count < creatives.Count || ready.Count < 2)
            return creatives.Select(c => new CreativeDecision(c.CreativeId, CreativeAction.KeepTesting,
                $"Coletando dados ({c.Impressions} imp; mínimo {_p.MinImpressionsPerCreative}).")).ToList();

        var anyPurchases = ready.Any(c => c.Purchases > 0);
        var winner = anyPurchases
            ? ready.Where(c => c.Purchases > 0).OrderBy(c => c.Cpa).First()
            : ready.OrderByDescending(c => c.Ctr).First();

        var decisions = new List<CreativeDecision>();
        foreach (var c in ready)
        {
            if (c.CreativeId == winner.CreativeId)
            {
                decisions.Add(new(c.CreativeId, CreativeAction.PromoteWinner,
                    anyPurchases ? $"Menor CPA ({c.Cpa:0.00})." : $"Maior CTR ({c.Ctr:P1}) enquanto não há vendas."));
                continue;
            }

            bool loser = anyPurchases
                ? (c.Purchases == 0 || c.Cpa > winner.Cpa * _p.LoserCpaMultiple)
                : (c.Ctr < winner.Ctr / _p.LoserCpaMultiple);

            decisions.Add(loser
                ? new(c.CreativeId, CreativeAction.Pause,
                    anyPurchases ? $"CPA {c.Cpa:0.00} muito acima do vencedor {winner.Cpa:0.00}." : $"CTR {c.Ctr:P1} bem abaixo do vencedor.")
                : new(c.CreativeId, CreativeAction.KeepTesting, "Competitivo; segue no teste."));
        }
        return decisions;
    }
}

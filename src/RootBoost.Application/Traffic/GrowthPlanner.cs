namespace RootBoost.Application.Traffic;

// ============================================================================
//  Planejador de crescimento por orçamento. Dado o quanto o usuário consegue
//  investir (inclusive $0), projeta cenários ORGÂNICO (YouTube Shorts) e PAGO
//  (Meta Ads) com números: pedidos, receita, lucro, CAC de equilíbrio, ROAS,
//  e devolve recomendação + sugestões. Determinístico e testável; as premissas
//  têm defaults editáveis e viram dados reais conforme a operação roda.
// ============================================================================

/// <summary>Entradas do plano. Premissas com defaults conservadores (ajustáveis por config/dados reais).</summary>
public sealed record GrowthInputs(
    decimal UnitPrice,
    decimal UnitCost,                       // custo do produto + frete (landed)
    decimal MonthlyAdBudget = 0m,           // 0 = orgânico puro
    // Funil compartilhado
    double StoreConversionRate = 0.02,      // % de visitantes que compram (2% default)
    // Orgânico (YouTube Shorts)
    int ShortsPerDay = 2,
    double AvgViewsPerShort = 400,          // média realista de um canal novo
    double ShortClickRate = 0.01,           // % de quem vê que clica no link
    // Pago (Meta)
    decimal PaidCostPerClick = 0.70m,
    // Taxas de pagamento (PayPal)
    decimal PaymentFeePct = 0.035m,
    decimal PaymentFeeFixed = 0.49m,
    // Meta de lucro por unidade (pra calcular o CAC alvo)
    decimal DesiredProfitPerUnit = 5m);

public sealed record ChannelProjection(
    string Channel,
    long Visitors,
    long Orders,
    decimal Revenue,
    decimal MediaCost,
    decimal Profit,
    decimal? Roas,
    string Note);

public sealed record GrowthPlan(
    decimal GrossMarginPerUnit,
    decimal BreakevenCac,
    decimal TargetCac,
    decimal EstimatedPaidCac,
    bool PaidViable,
    ChannelProjection Organic,
    ChannelProjection? Paid,
    string Recommendation,
    IReadOnlyList<string> Suggestions);

public sealed class GrowthPlanner
{
    public GrowthPlan Plan(GrowthInputs i)
    {
        var fees = i.UnitPrice * i.PaymentFeePct + i.PaymentFeeFixed;
        var grossMargin = R(i.UnitPrice - i.UnitCost - fees);
        var breakeven = grossMargin;                          // CAC máximo pra não ter prejuízo
        var target = R(grossMargin - i.DesiredProfitPerUnit); // CAC pra bater o lucro desejado

        // --- Orgânico (Shorts): mídia ~ $0; custo real = tempo/consistência ---
        var monthlyViews = (double)i.ShortsPerDay * 30 * i.AvgViewsPerShort;
        var organicVisitors = (long)(monthlyViews * i.ShortClickRate);
        var organicOrders = (long)(organicVisitors * i.StoreConversionRate);
        var organicRevenue = R(organicOrders * i.UnitPrice);
        var organicProfit = R(organicOrders * grossMargin);
        var organic = new ChannelProjection(
            "organic", organicVisitors, organicOrders, organicRevenue, 0m, organicProfit, null,
            $"{i.ShortsPerDay} Shorts/dia a ~{i.AvgViewsPerShort:0} views. Custo de mídia $0; custo = tempo/consistência. Um Short viral muda a escala.");

        // --- CAC estimado do pago = custo por clique / taxa de conversão ---
        var estCac = i.StoreConversionRate > 0 ? R(i.PaidCostPerClick / (decimal)i.StoreConversionRate) : 0m;
        var paidViable = estCac > 0 && estCac < breakeven;

        ChannelProjection? paid = null;
        if (i.MonthlyAdBudget > 0 && estCac > 0)
        {
            var paidOrders = (long)(i.MonthlyAdBudget / estCac);
            var paidVisitors = i.PaidCostPerClick > 0 ? (long)(i.MonthlyAdBudget / i.PaidCostPerClick) : 0;
            var paidRevenue = R(paidOrders * i.UnitPrice);
            var paidProfit = R(paidOrders * grossMargin - i.MonthlyAdBudget);
            var roas = i.MonthlyAdBudget > 0 ? R(paidRevenue / i.MonthlyAdBudget) : (decimal?)null;
            var note = paidViable
                ? $"CAC estimado ${estCac:0.00} < equilíbrio ${breakeven:0.00}: dá pra escalar com lucro."
                : $"CAC estimado ${estCac:0.00} >= equilíbrio ${breakeven:0.00}: pago perde dinheiro sem melhorar CVR/AOV.";
            paid = new ChannelProjection("paid", paidVisitors, paidOrders, paidRevenue, R(i.MonthlyAdBudget), paidProfit, roas, note);
        }

        var (rec, sugg) = Advise(i, grossMargin, breakeven, estCac, paidViable, organic, paid);
        return new GrowthPlan(grossMargin, breakeven, target, estCac, paidViable, organic, paid, rec, sugg);
    }

    private static (string, List<string>) Advise(
        GrowthInputs i, decimal grossMargin, decimal breakeven, decimal estCac, bool paidViable,
        ChannelProjection organic, ChannelProjection? paid)
    {
        var s = new List<string>();
        string rec;

        if (i.MonthlyAdBudget <= 0 || !paidViable)
        {
            rec = "Orgânico-first (YouTube Shorts). O pago " +
                  (i.MonthlyAdBudget <= 0 ? "está fora (orçamento $0)" : $"perde dinheiro agora (CAC ${estCac:0.00} >= equilíbrio ${breakeven:0.00})") +
                  ". Prove o produto de graça no orgânico antes de pagar mídia.";
        }
        else
        {
            rec = $"Blended: mantenha o orgânico (grátis) e use o pago pra escalar o que já converte (CAC ${estCac:0.00} < equilíbrio ${breakeven:0.00}).";
        }

        if (grossMargin < 12m)
            s.Add($"Margem por unidade baixa (${grossMargin:0.00}). Suba o AOV (bundle 'compre 2', upsell) ou o preço pra abrir espaço pra mídia.");
        if (!paidViable && i.MonthlyAdBudget > 0)
            s.Add($"Pra o pago fechar, precisa de CVR maior (hoje {i.StoreConversionRate:P0}) ou CPC menor. Alvo de CAC: <= ${breakeven:0.00}.");
        s.Add($"Meta de conteúdo orgânico: {i.ShortsPerDay} Shorts/dia por canal, reaproveitando 1 lote de clipes com hooks diferentes.");
        s.Add("Regra de decisão: rode até volume mínimo; corte criativo com CPA/CTR ruim e dobre no vencedor (o CampaignOptimizer/CreativeSelector já fazem isso).");
        s.Add($"CAC de equilíbrio = ${breakeven:0.00}; CAC alvo (lucro ${i.DesiredProfitPerUnit:0.00}/un) = ${(grossMargin - i.DesiredProfitPerUnit):0.00}.");

        return (rec, s);
    }

    private static decimal R(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
}

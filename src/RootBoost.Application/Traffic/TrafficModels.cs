namespace RootBoost.Application.Traffic;

// ============================================================================
//  Camada de automação de tráfego pago (o "cérebro").
//  Fluxo: puxar métricas (insights) -> decidir ação -> aplicar (ou dry-run) ->
//  reportar. Determinístico e testável; um IOptimizationAdvisor (IA) pode entrar
//  depois pra recomendações mais ricas. Nada aqui gasta anúncio por conta própria.
// ============================================================================

/// <summary>Métricas de uma campanha (ou conjunto de anúncios) numa janela de tempo.</summary>
public sealed record CampaignMetrics(
    string CampaignId,
    string Name,
    decimal Spend,
    decimal Revenue,
    long Impressions,
    long Clicks,
    long Purchases,
    decimal CurrentDailyBudget,
    string Currency = "USD")
{
    public decimal Roas => Spend > 0 ? Revenue / Spend : 0m;
    public decimal Cpa => Purchases > 0 ? Spend / Purchases : 0m;
    public decimal Ctr => Impressions > 0 ? (decimal)Clicks / Impressions : 0m;
}

public enum OptimizationAction { Keep, Scale, Reduce, Pause, InsufficientData }

public sealed record OptimizationDecision(
    string CampaignId,
    OptimizationAction Action,
    decimal CurrentDailyBudget,
    decimal SuggestedDailyBudget,
    string Rationale);

/// <summary>Estatística de um criativo dentro de um teste A/B.</summary>
public sealed record CreativeStat(
    string CreativeId,
    string Name,
    long Impressions,
    long Clicks,
    long Purchases,
    decimal Spend)
{
    public decimal Cpa => Purchases > 0 ? Spend / Purchases : 0m;
    public decimal Ctr => Impressions > 0 ? (decimal)Clicks / Impressions : 0m;
}

public enum CreativeAction { KeepTesting, PromoteWinner, Pause }

public sealed record CreativeDecision(string CreativeId, CreativeAction Action, string Rationale);

/// <summary>Limiares da política de otimização. Ajustáveis por config; defaults conservadores.</summary>
public sealed class OptimizationPolicy
{
    public const string Section = "Traffic";

    /// <summary>ROAS a partir do qual escalamos o orçamento.</summary>
    public decimal ScaleRoas { get; set; } = 2.0m;
    /// <summary>ROAS abaixo do qual reduzimos o orçamento.</summary>
    public decimal ReduceRoas { get; set; } = 1.3m;
    /// <summary>ROAS abaixo do qual pausamos (perdendo dinheiro).</summary>
    public decimal CutRoas { get; set; } = 1.0m;
    /// <summary>Gasto mínimo na janela antes de decidir qualquer coisa (evita reagir a ruído).</summary>
    public decimal MinSpendForDecision { get; set; } = 20m;
    /// <summary>Passo de ajuste de orçamento (0.20 = +/-20%).</summary>
    public decimal BudgetStepPct { get; set; } = 0.20m;
    /// <summary>Teto de orçamento diário por campanha (trava de segurança).</summary>
    public decimal MaxDailyBudget { get; set; } = 100m;

    // Criativos
    /// <summary>Impressões mínimas por criativo antes de julgar.</summary>
    public long MinImpressionsPerCreative { get; set; } = 1000;
    /// <summary>Um perdedor é pausado quando seu CPA passa deste múltiplo do CPA do vencedor.</summary>
    public decimal LoserCpaMultiple { get; set; } = 1.5m;
}

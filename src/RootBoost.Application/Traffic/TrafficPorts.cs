namespace RootBoost.Application.Traffic;

/// <summary>Fonte de métricas de campanha (Meta Ads Insights hoje). Infra implementa.</summary>
public interface ICampaignInsightsSource
{
    /// <summary>Métricas por campanha na janela recente (ex.: últimos 3 dias).</summary>
    Task<IReadOnlyList<CampaignMetrics>> GetActiveCampaignsAsync(CancellationToken ct = default);

    /// <summary>Estatística por criativo dentro de uma campanha (pro teste A/B).</summary>
    Task<IReadOnlyList<CreativeStat>> GetCreativesAsync(string campaignId, CancellationToken ct = default);
}

/// <summary>Aplica ações na plataforma de anúncios (Meta Ads). Em dry-run, só loga.</summary>
public interface ICampaignActuator
{
    bool IsDryRun { get; }
    Task SetDailyBudgetAsync(string campaignId, decimal dailyBudget, CancellationToken ct = default);
    Task PauseCampaignAsync(string campaignId, CancellationToken ct = default);
    Task PauseCreativeAsync(string creativeId, CancellationToken ct = default);
}

/// <summary>
/// Conselheiro de otimização opcional (IA). Quando presente, pode refinar/priorizar as decisões
/// determinísticas. Default é nulo (só regras). Um advisor baseado em Claude entra aqui depois.
/// </summary>
public interface IOptimizationAdvisor
{
    Task<string?> CommentAsync(CampaignMetrics metrics, OptimizationDecision baseline, CancellationToken ct = default);
}

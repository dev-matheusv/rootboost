using Microsoft.Extensions.Logging;
using RootBoost.Application.Traffic;

namespace RootBoost.Infrastructure.Tracking;

/// <summary>
/// Fonte de métricas ainda não conectada à Meta Ads Marketing API. Retorna vazio (autopilot vira
/// no-op) até implementarmos o cliente real. TODO: Meta Ads Insights (act_{id}/insights).
/// </summary>
public sealed class NotConfiguredInsightsSource : ICampaignInsightsSource
{
    private readonly ILogger<NotConfiguredInsightsSource> _log;
    public NotConfiguredInsightsSource(ILogger<NotConfiguredInsightsSource> log) => _log = log;

    public Task<IReadOnlyList<CampaignMetrics>> GetActiveCampaignsAsync(CancellationToken ct = default)
    {
        _log.LogInformation("[insights] Meta Ads não conectada; sem campanhas pra otimizar ainda.");
        return Task.FromResult<IReadOnlyList<CampaignMetrics>>(Array.Empty<CampaignMetrics>());
    }

    public Task<IReadOnlyList<CreativeStat>> GetCreativesAsync(string campaignId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<CreativeStat>>(Array.Empty<CreativeStat>());
}

/// <summary>
/// Actuator em DRY-RUN: só registra a ação que seria aplicada. É o padrão seguro — nada mexe na
/// conta de anúncios de verdade. TODO: actuator real (Meta Ads) atrás de Traffic:Live=true.
/// </summary>
public sealed class LoggingCampaignActuator : ICampaignActuator
{
    private readonly ILogger<LoggingCampaignActuator> _log;
    public LoggingCampaignActuator(ILogger<LoggingCampaignActuator> log) => _log = log;

    public bool IsDryRun => true;

    public Task SetDailyBudgetAsync(string campaignId, decimal dailyBudget, CancellationToken ct = default)
    { _log.LogInformation("[dry-run] set budget {Campaign} -> {Budget:0.00}/dia", campaignId, dailyBudget); return Task.CompletedTask; }

    public Task PauseCampaignAsync(string campaignId, CancellationToken ct = default)
    { _log.LogInformation("[dry-run] pause campaign {Campaign}", campaignId); return Task.CompletedTask; }

    public Task PauseCreativeAsync(string creativeId, CancellationToken ct = default)
    { _log.LogInformation("[dry-run] pause creative {Creative}", creativeId); return Task.CompletedTask; }
}

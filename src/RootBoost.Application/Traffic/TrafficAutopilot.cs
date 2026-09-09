using Microsoft.Extensions.Logging;

namespace RootBoost.Application.Traffic;

public sealed record TrafficRunReport(
    DateTimeOffset RanAt,
    bool DryRun,
    IReadOnlyList<OptimizationDecision> Campaigns,
    IReadOnlyList<CreativeDecision> Creatives,
    int ActionsApplied);

/// <summary>
/// Piloto automático: puxa métricas -> decide (otimizador + seletor de criativos) -> aplica via
/// actuator (ou só recomenda, em dry-run) -> devolve um relatório. Feito pra rodar num schedule.
/// Só age dentro das travas da política; nunca cria gasto novo sem uma campanha existente.
/// </summary>
public sealed class TrafficAutopilot
{
    private readonly ICampaignInsightsSource _insights;
    private readonly ICampaignActuator _actuator;
    private readonly CampaignOptimizer _optimizer;
    private readonly CreativeSelector _creatives;
    private readonly ILogger<TrafficAutopilot> _log;

    public TrafficAutopilot(
        ICampaignInsightsSource insights, ICampaignActuator actuator,
        CampaignOptimizer optimizer, CreativeSelector creatives, ILogger<TrafficAutopilot> log)
    {
        _insights = insights;
        _actuator = actuator;
        _optimizer = optimizer;
        _creatives = creatives;
        _log = log;
    }

    public async Task<TrafficRunReport> RunAsync(CancellationToken ct = default)
    {
        var campaigns = await _insights.GetActiveCampaignsAsync(ct);
        var decisions = _optimizer.DecideAll(campaigns);
        var creativeDecisions = new List<CreativeDecision>();
        var applied = 0;

        foreach (var d in decisions)
        {
            _log.LogInformation("[autopilot] {Campaign}: {Action} — {Why}", d.CampaignId, d.Action, d.Rationale);
            switch (d.Action)
            {
                case OptimizationAction.Scale:
                case OptimizationAction.Reduce:
                    await _actuator.SetDailyBudgetAsync(d.CampaignId, d.SuggestedDailyBudget, ct);
                    applied++;
                    break;
                case OptimizationAction.Pause:
                    await _actuator.PauseCampaignAsync(d.CampaignId, ct);
                    applied++;
                    break;
            }
        }

        foreach (var c in campaigns)
        {
            var stats = await _insights.GetCreativesAsync(c.CampaignId, ct);
            foreach (var cd in _creatives.Evaluate(stats))
            {
                creativeDecisions.Add(cd);
                if (cd.Action == CreativeAction.Pause)
                {
                    await _actuator.PauseCreativeAsync(cd.CreativeId, ct);
                    applied++;
                }
            }
        }

        return new TrafficRunReport(DateTimeOffset.UtcNow, _actuator.IsDryRun, decisions, creativeDecisions, applied);
    }
}

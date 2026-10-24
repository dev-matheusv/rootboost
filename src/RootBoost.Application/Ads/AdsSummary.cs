using RootBoost.Domain;

namespace RootBoost.Application.Ads;

/// <summary>Regra de parada: gastou isto sem nenhuma conversão, alguém revisa criativo e público.</summary>
public sealed class AdsStopRule
{
    public const string Section = "Ads:StopRule";

    /// <summary>Na moeda da conta. Padrão R$ 200, o valor combinado pro primeiro teste do MedlyCare.</summary>
    public decimal SpendWithoutConversion { get; set; } = 200m;
}

public sealed record AdMetrics(decimal Spend, long Impressions, long Clicks, long Reach, long Conversions)
{
    public decimal? Ctr => Impressions > 0 ? (decimal)Clicks / Impressions : null;
    public decimal? Cpc => Clicks > 0 ? Spend / Clicks : null;
    public decimal? Cpm => Impressions > 0 ? Spend / Impressions * 1000m : null;
    public decimal? Cpa => Conversions > 0 ? Spend / Conversions : null;

    public static AdMetrics Sum(IEnumerable<AdInsightDaily> rows)
    {
        decimal spend = 0; long impressions = 0, clicks = 0, reach = 0, conversions = 0;
        foreach (var r in rows)
        {
            spend += r.Spend;
            impressions += r.Impressions;
            clicks += r.Clicks;
            reach += r.Reach;
            conversions += r.Conversions;
        }
        return new(spend, impressions, clicks, reach, conversions);
    }
}

public sealed record CampaignSummary(string CampaignId, string Name, string Currency, AdMetrics Metrics);

public sealed record DailyPoint(DateOnly Date, AdMetrics Metrics);

public sealed record AdsAlert(string Kind, string ObjectId, string Name, string Message);

/// <summary>
/// Resumo do período. Alcance somado por dia superestima gente única (a mesma pessoa conta em dois dias);
/// o número é mostrado como "alcance somado", não como pessoas únicas.
/// </summary>
public sealed record AdsSummary(
    string BrandKey,
    DateOnly Since,
    DateOnly Until,
    IReadOnlyList<string> Currencies,
    AdMetrics Totals,
    IReadOnlyList<CampaignSummary> Campaigns,
    IReadOnlyList<DailyPoint> Daily,
    IReadOnlyList<AdsAlert> Alerts)
{
    /// <summary>
    /// Monta o resumo a partir das linhas de campanha. Totais só fazem sentido numa moeda: com mais de uma,
    /// a tela mostra por campanha e avisa (somar BRL com USD mente).
    /// </summary>
    public static AdsSummary Build(string brandKey, DateOnly since, DateOnly until, IReadOnlyList<AdInsightDaily> campaignRows, AdsStopRule rule)
    {
        var rows = campaignRows.Where(r => r.Level == AdLevel.Campaign && r.Date >= since && r.Date <= until).ToList();
        var currencies = rows.Select(r => r.Currency).Distinct().OrderBy(c => c).ToList();

        var campaigns = rows
            .GroupBy(r => (r.Platform, r.AccountId, r.ObjectId))
            .Select(g => new CampaignSummary(g.Key.ObjectId,
                g.OrderByDescending(r => r.Date).First().ObjectName,
                g.First().Currency,
                AdMetrics.Sum(g)))
            .OrderByDescending(c => c.Metrics.Spend)
            .ToList();

        var daily = rows.GroupBy(r => r.Date).OrderBy(g => g.Key)
            .Select(g => new DailyPoint(g.Key, AdMetrics.Sum(g))).ToList();

        var alerts = new List<AdsAlert>();
        if (currencies.Count > 1)
            alerts.Add(new("mixedCurrency", "", "", $"Contas em moedas diferentes ({string.Join(", ", currencies)}): o total soma moedas diferentes, use a visão por campanha."));
        foreach (var c in campaigns.Where(c => c.Metrics.Conversions == 0 && c.Metrics.Spend >= rule.SpendWithoutConversion))
            alerts.Add(new("stopRule", c.CampaignId, c.Name,
                $"{c.Metrics.Spend:0.00} {c.Currency} gastos sem conversão no período: revisar criativo e público antes de seguir."));

        return new(brandKey, since, until, currencies, AdMetrics.Sum(rows), campaigns, daily, alerts);
    }
}

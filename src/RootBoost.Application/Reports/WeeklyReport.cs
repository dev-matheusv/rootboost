using RootBoost.Application.Ads;
using RootBoost.Domain;

namespace RootBoost.Application.Reports;

public sealed record ReportLine(string Text, string Kind = "info");

/// <summary>
/// Resumo da semana da marca (RB-34): o que saiu, o que vem, como foram os vídeos e os anúncios, e o que fazer agora.
/// Montado só com dado do banco, sem IA (custo zero e número que bate com as telas). Cada linha é uma frase pronta.
/// </summary>
public sealed record WeeklyReport(
    string BrandKey,
    DateOnly Since,
    DateOnly Until,
    int Published,
    int Failed,
    int Scheduled,
    int AwaitingApproval,
    int VideosDelivered,
    int VideosFailed,
    AdMetrics Ads,
    IReadOnlyList<ReportLine> Highlights,
    IReadOnlyList<ReportLine> NextSteps)
{
    public static WeeklyReport Build(string brandKey, DateTimeOffset now, int days,
        IReadOnlyList<Publication> publications, IReadOnlyList<CreativeJob> jobs, IReadOnlyList<AdInsightDaily> adRows,
        IReadOnlyList<(AdCampaignDraft Campaign, CampaignAdvice Advice)> campaigns)
    {
        var from = now.AddDays(-days);
        var next = now.AddDays(7);
        var past = publications.Where(p => p.ScheduledFor >= from && p.ScheduledFor <= now).ToList();
        var published = past.Count(p => p.Status == PublicationStatus.Published);
        var failed = past.Count(p => p.Status == PublicationStatus.Failed);
        var upcoming = publications.Where(p => p.ScheduledFor > now && p.ScheduledFor <= next).ToList();
        var awaiting = upcoming.Count(p => p.Status == PublicationStatus.Draft);
        var recentJobs = jobs.Where(j => j.UpdatedAt >= from).ToList();
        var delivered = recentJobs.Count(j => j.Status is CreativeJobStatus.Review or CreativeJobStatus.Done);
        var videoFails = recentJobs.Count(j => j.Status == CreativeJobStatus.Failed);
        var since = DateOnly.FromDateTime(from.UtcDateTime);
        var until = DateOnly.FromDateTime(now.UtcDateTime);
        var ads = AdMetrics.Sum(adRows.Where(r => r.Level == AdLevel.Campaign && r.Date >= since && r.Date <= until));

        var highlights = new List<ReportLine>
        {
            new(published == 0 ? "Nenhum post saiu nesta semana." : $"{published} post(s) publicado(s) nesta semana.", published == 0 ? "warning" : "good"),
        };
        if (failed > 0) highlights.Add(new($"{failed} post(s) falharam ao publicar.", "bad"));
        if (delivered > 0) highlights.Add(new($"{delivered} vídeo(s) prontos no Estúdio.", "good"));
        if (videoFails > 0) highlights.Add(new($"{videoFails} vídeo(s) falharam no Estúdio.", "bad"));
        if (ads.Impressions > 0)
            highlights.Add(new($"Anúncios: {ads.Spend:0.00} gastos, {ads.Impressions} impressões, {ads.Clicks} cliques" +
                (ads.Conversions > 0 ? $", {ads.Conversions} resultado(s), {ads.Cpa:0.00} por resultado." : "."), "info"));

        var steps = new List<ReportLine>();
        if (failed > 0) steps.Add(new("Abrir Publicações e tentar de novo os posts que falharam.", "bad"));
        if (awaiting > 0) steps.Add(new($"Aprovar {awaiting} post(s) agendado(s) para os próximos 7 dias.", "warning"));
        if (upcoming.Count == 0) steps.Add(new("Nada agendado para os próximos 7 dias: peça um vídeo no Estúdio.", "warning"));
        var review = jobs.Count(j => j.Status is CreativeJobStatus.Review or CreativeJobStatus.VoiceReview);
        if (review > 0) steps.Add(new($"Revisar {review} vídeo(s) esperando você no Estúdio.", "warning"));
        foreach (var (c, a) in campaigns.Where(x => x.Advice.Kind is AdviceKind.Pause or AdviceKind.ReviewCreative or AdviceKind.Scale))
            steps.Add(new($"Campanha \"{c.Name}\": {a.Message}", a.Kind == AdviceKind.Scale ? "good" : "bad"));
        if (steps.Count == 0) steps.Add(new("Tudo em dia. Nada pendente.", "good"));

        return new(brandKey, since, until, published, failed, upcoming.Count, awaiting, delivered, videoFails, ads, highlights, steps);
    }
}

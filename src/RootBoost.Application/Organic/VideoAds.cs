using RootBoost.Application.Ads;
using RootBoost.Domain;

namespace RootBoost.Application.Organic;

/// <summary>
/// Resultado de anúncio de cada vídeo do ranking (13/11, passo 2 da Onda 4b): o post que saiu pela plataforma e virou
/// campanha (mesmo arquivo de vídeo) soma os números diários dos anúncios dele. Assim o ranking mostra o orgânico e o
/// pago lado a lado, e a IA vê os dois.
/// </summary>
public static class VideoAds
{
    public static IReadOnlyDictionary<string, AdMetrics> ForPosts(IEnumerable<OrganicPost> posts, IEnumerable<Publication> publications,
        IEnumerable<AdCampaignDraft> campaigns, IEnumerable<AdInsightDaily> adRows)
    {
        var mediaByPub = publications.ToDictionary(p => p.Id, p => p.MediaUrl);
        var adsByVideo = campaigns.Where(c => c.ExternalAdId is not null)
            .GroupBy(c => c.VideoUrl).ToDictionary(g => g.Key, g => g.Select(c => c.ExternalAdId!).ToHashSet());
        var rows = adRows.Where(r => r.Level == AdLevel.Ad).ToList();
        var result = new Dictionary<string, AdMetrics>();
        foreach (var post in posts)
        {
            if (post.PublicationId is not { } pid || !mediaByPub.TryGetValue(pid, out var url) || !adsByVideo.TryGetValue(url, out var ads)) continue;
            var m = AdMetrics.Sum(rows.Where(r => ads.Contains(r.ObjectId)));
            if (m.Impressions > 0) result[post.ExternalId] = m;
        }
        return result;
    }

    /// <summary>Frase curta pro ranking e pra IA: "Anúncio: R$ 12,30, 1.840 impressões, 31 cliques (CTR 1,7%)".</summary>
    public static string Describe(AdMetrics m) =>
        $"Anúncio: R$ {m.Spend:0.00}, {m.Impressions} impressões, {m.Clicks} cliques (CTR {(m.Ctr ?? 0):P1})" +
        (m.Conversions > 0 ? $", {m.Conversions} resultado(s)" : "") + ".";
}

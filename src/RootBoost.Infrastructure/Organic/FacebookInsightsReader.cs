using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RootBoost.Application.Organic;
using RootBoost.Application.Publishing;
using RootBoost.Domain;
using RootBoost.Infrastructure.Publishing;
using RootBoost.Infrastructure.Tracking;

namespace RootBoost.Infrastructure.Organic;

/// <summary>
/// Números dos Reels da Página do Facebook publicados pela plataforma (13/11, RB-25). A doc diz que
/// <c>GET /{page-id}/video_reels</c> não lê, então a lista vem das nossas publicações (o id gravado é o do vídeo).
/// Conferido na referência "Video Insights" v27.0 em 12/11: <c>GET /{video-id}/video_insights?metric=...</c>, todas
/// lifetime: <c>blue_reels_play_count</c> (reproduções sem repetição), <c>post_impressions_unique</c> (alcance estimado),
/// <c>post_video_avg_time_watched</c> (ms), <c>post_video_social_actions</c> (comentários e compartilhamentos) e
/// <c>post_video_likes_by_reaction_type</c>. Permissões: <c>read_insights</c> e <c>pages_manage_engagement</c>.
/// O formato do valor de social_actions e reações (objeto por tipo) não está na doc: soma o que vier, sem inventar chave.
/// </summary>
public sealed class FacebookInsightsReader : IOrganicInsightsReader
{
    private const string Metrics = "blue_reels_play_count,post_impressions_unique,post_video_avg_time_watched,post_video_social_actions,post_video_likes_by_reaction_type";
    private readonly IHttpClientFactory _factory;
    private readonly SocialAccounts _accounts;
    private readonly IPublicationStore _publications;
    private readonly string _version;
    private readonly ILogger<FacebookInsightsReader> _log;

    public FacebookInsightsReader(IHttpClientFactory factory, SocialAccounts accounts, IPublicationStore publications, IOptions<MetaOptions> meta,
        ILogger<FacebookInsightsReader> log)
    {
        _factory = factory;
        _accounts = accounts;
        _publications = publications;
        _version = meta.Value.ApiVersion;
        _log = log;
    }

    public SocialChannel Channel => SocialChannel.Facebook;

    public IReadOnlyList<string> Brands => _accounts.All.Where(a => a.Channel == Channel).Select(a => a.BrandKey).Distinct().ToList();

    public async Task<IReadOnlyList<OrganicPostRow>> ReadRecentAsync(string brandKey, DateTimeOffset since, CancellationToken ct = default)
    {
        var account = _accounts.For(brandKey, Channel);
        var http = _factory.CreateClient("meta");
        var videos = (await _publications.ListByBrandAsync(brandKey, since, DateTimeOffset.UtcNow, ct))
            .Where(p => p.Channel == Channel && p.Status == PublicationStatus.Published && p.ExternalId is not null && InstagramPublisher.IsVideo(p.MediaUrl))
            .ToList();
        var rows = new List<OrganicPostRow>();
        string? lastError = null;
        foreach (var p in videos)
        {
            try
            {
                using var doc = await GraphApi.SendAsync(http, HttpMethod.Get, $"{_version}/{p.ExternalId}/video_insights?metric={Metrics}", account.AccessToken, null, ct);
                var m = Parse(doc.RootElement);
                long Get(string k) => m.TryGetValue(k, out var v) ? v : 0;
                var social = Get("post_video_social_actions");
                var reactions = Get("post_video_likes_by_reaction_type");
                rows.Add(new OrganicPostRow(p.ExternalId!, "REELS", "VIDEO", p.Caption, p.PublishedUrl, p.CoverUrl, p.ScheduledFor,
                    Get("blue_reels_play_count"), Get("post_impressions_unique"), reactions, 0, social, 0, reactions + social,
                    m.TryGetValue("post_video_avg_time_watched", out var w) ? w : null));
            }
            catch (InvalidOperationException ex)
            {
                lastError = ex.Message;
                _log.LogWarning("Facebook {Brand}: sem métricas do vídeo {Id}: {Error}", brandKey, p.ExternalId, ex.Message);
            }
        }
        if (rows.Count == 0 && lastError is not null) throw new InvalidOperationException($"nenhum vídeo do Facebook com métricas: {lastError}");
        return rows;
    }

    /// <summary>Valor numérico direto ou objeto por tipo (soma os números dele). Ignora o resto.</summary>
    public static Dictionary<string, long> Parse(JsonElement root)
    {
        var result = new Dictionary<string, long>(StringComparer.Ordinal);
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return result;
        foreach (var item in data.EnumerateArray())
        {
            if (!item.TryGetProperty("name", out var n) || n.GetString() is not { Length: > 0 } name) continue;
            if (!item.TryGetProperty("values", out var values) || values.ValueKind != JsonValueKind.Array || values.GetArrayLength() == 0) continue;
            if (!values[0].TryGetProperty("value", out var v)) continue;
            if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) && d >= 0) result[name] = (long)Math.Round(d);
            else if (v.ValueKind == JsonValueKind.Object)
                result[name] = v.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetDouble(out var x) && x >= 0)
                    .Sum(p => (long)Math.Round(p.Value.GetDouble()));
        }
        return result;
    }
}

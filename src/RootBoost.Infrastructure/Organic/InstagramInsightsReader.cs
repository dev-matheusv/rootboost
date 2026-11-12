using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RootBoost.Application.Organic;
using RootBoost.Domain;
using RootBoost.Infrastructure.Publishing;
using RootBoost.Infrastructure.Tracking;

namespace RootBoost.Infrastructure.Organic;

/// <summary>
/// Números orgânicos do Instagram (RB-25). Conferido na doc oficial v27.0 em 12/11/2026:
/// <list type="number">
/// <item><c>GET /{ig-user-id}/media?fields=...&amp;since=</c> (paginação por tempo; até 10 mil mídias). Campos do nó IG Media:
///   <c>id, caption, media_type</c> (IMAGE, VIDEO, CAROUSEL_ALBUM), <c>media_product_type</c> (AD, FEED, STORY, REELS),
///   <c>permalink, timestamp, thumbnail_url</c> (só VIDEO).</item>
/// <item><c>GET /{ig-media-id}/insights?metric=...</c>. Reels: <c>reach, views, likes, comments, shares, saved,
///   total_interactions, ig_reels_avg_watch_time</c>. Feed: os mesmos sem o tempo assistido. Métrica que não vale pro
///   tipo derruba a chamada inteira, por isso a lista depende do tipo. Dado pode atrasar até 48 h; sem dado vem vazio.</item>
/// </list>
/// Story some em 24 h e AD é anúncio: ficam de fora. Permissões: <c>instagram_basic</c> e <c>instagram_manage_insights</c>.
/// </summary>
public sealed class InstagramInsightsReader : IOrganicInsightsReader
{
    public const int MaxPages = 10;
    private const string Fields = "id,caption,media_type,media_product_type,permalink,timestamp,thumbnail_url";
    private const string FeedMetrics = "reach,views,likes,comments,shares,saved,total_interactions";
    private const string ReelsMetrics = FeedMetrics + ",ig_reels_avg_watch_time";

    private readonly IHttpClientFactory _factory;
    private readonly SocialAccounts _accounts;
    private readonly string _version;
    private readonly ILogger<InstagramInsightsReader> _log;

    public InstagramInsightsReader(IHttpClientFactory factory, SocialAccounts accounts, IOptions<MetaOptions> meta,
        ILogger<InstagramInsightsReader> log)
    {
        _factory = factory;
        _accounts = accounts;
        _version = meta.Value.ApiVersion;
        _log = log;
    }

    public SocialChannel Channel => SocialChannel.Instagram;

    public IReadOnlyList<string> Brands => _accounts.All.Where(a => a.Channel == Channel).Select(a => a.BrandKey).Distinct().ToList();

    public async Task<IReadOnlyList<OrganicPostRow>> ReadRecentAsync(string brandKey, DateTimeOffset since, CancellationToken ct = default)
    {
        var account = _accounts.For(brandKey, Channel);
        var http = _factory.CreateClient("meta");
        var media = new List<JsonElement>();
        string? next = $"{_version}/{account.AccountId}/media?fields={Fields}&since={since.ToUnixTimeSeconds()}";
        for (var page = 0; next is not null && page < MaxPages; page++)
        {
            using var doc = await GraphApi.SendAsync(http, HttpMethod.Get, next, account.AccessToken, null, ct);
            if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                media.AddRange(data.EnumerateArray().Select(e => e.Clone()));
            next = doc.RootElement.TryGetProperty("paging", out var paging) && paging.TryGetProperty("next", out var n)
                && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
        }

        var rows = new List<OrganicPostRow>();
        var failed = 0;
        string? lastError = null;
        foreach (var m in media)
        {
            var id = Str(m, "id");
            var product = Str(m, "media_product_type").ToUpperInvariant();
            if (id.Length == 0 || (product != "REELS" && product != "FEED")) continue;
            if (!DateTimeOffset.TryParse(Str(m, "timestamp"), out var postedAt) || postedAt < since) continue;
            Dictionary<string, long> metrics;
            try
            {
                using var ins = await GraphApi.SendAsync(http, HttpMethod.Get,
                    $"{_version}/{id}/insights?metric={(product == "REELS" ? ReelsMetrics : FeedMetrics)}", account.AccessToken, null, ct);
                metrics = ParseInsights(ins.RootElement);
            }
            catch (InvalidOperationException ex)
            {
                // Um post sem insights (ex.: publicado antes da conta virar profissional) não derruba os outros.
                failed++;
                lastError = ex.Message;
                _log.LogWarning("Instagram {Brand}: sem métricas do post {Id}: {Error}", brandKey, id, ex.Message);
                continue;
            }
            long Get(string k) => metrics.TryGetValue(k, out var v) ? v : 0;
            rows.Add(new OrganicPostRow(id, product, Str(m, "media_type"), NullIfEmpty(Str(m, "caption")), NullIfEmpty(Str(m, "permalink")),
                NullIfEmpty(Str(m, "thumbnail_url")), postedAt, Get("views"), Get("reach"), Get("likes"), Get("comments"), Get("shares"),
                Get("saved"), Get("total_interactions"), metrics.TryGetValue("ig_reels_avg_watch_time", out var w) ? w : null));
        }
        if (rows.Count == 0 && failed > 0)
            throw new InvalidOperationException($"nenhum post com métricas ({failed} falharam): {lastError}");
        return rows;
    }

    /// <summary>
    /// Resposta de insights: <c>{"data":[{"name":"reach","values":[{"value":123}]}]}</c>; algumas métricas vêm em
    /// <c>total_value.value</c>. Aceita as duas formas e ignora o que não for número.
    /// </summary>
    public static Dictionary<string, long> ParseInsights(JsonElement root)
    {
        var result = new Dictionary<string, long>(StringComparer.Ordinal);
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return result;
        foreach (var item in data.EnumerateArray())
        {
            var name = Str(item, "name");
            if (name.Length == 0) continue;
            JsonElement value = default;
            if (item.TryGetProperty("values", out var values) && values.ValueKind == JsonValueKind.Array && values.GetArrayLength() > 0)
                values[0].TryGetProperty("value", out value);
            else if (item.TryGetProperty("total_value", out var total))
                total.TryGetProperty("value", out value);
            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var d) && d >= 0)
                result[name] = (long)Math.Round(d);
        }
        return result;
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static string? NullIfEmpty(string s) => s.Length == 0 ? null : s;
}

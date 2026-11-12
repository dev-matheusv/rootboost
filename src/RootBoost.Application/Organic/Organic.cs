using Microsoft.Extensions.Logging;
using RootBoost.Application.Publishing;
using RootBoost.Domain;

namespace RootBoost.Application.Organic;

/// <summary>Um post como o canal devolve, com os números acumulados desde a publicação.</summary>
public sealed record OrganicPostRow(
    string ExternalId,
    string ProductType,
    string MediaType,
    string? Caption,
    string? Permalink,
    string? ThumbnailUrl,
    DateTimeOffset PostedAt,
    long Views,
    long Reach,
    long Likes,
    long Comments,
    long Shares,
    long Saves,
    long Interactions,
    long? AvgWatchMs);

/// <summary>Lê os posts recentes de uma marca num canal e os números de cada um. Só leitura.</summary>
public interface IOrganicInsightsReader
{
    SocialChannel Channel { get; }

    /// <summary>Marcas com conta configurada nesse canal.</summary>
    IReadOnlyList<string> Brands { get; }

    Task<IReadOnlyList<OrganicPostRow>> ReadRecentAsync(string brandKey, DateTimeOffset since, CancellationToken ct = default);
}

public interface IOrganicPostStore
{
    /// <summary>Grava ou atualiza por (canal, id no canal). Idempotente.</summary>
    Task<(int Inserted, int Updated)> UpsertAsync(string brandKey, SocialChannel channel, IReadOnlyList<OrganicPostRow> rows,
        IReadOnlyDictionary<string, Guid> publicationsByExternalId, DateTimeOffset fetchedAt, CancellationToken ct = default);

    Task<IReadOnlyList<OrganicPost>> ListByBrandAsync(string brandKey, DateTimeOffset since, CancellationToken ct = default);

    Task<OrganicPost?> FindAsync(SocialChannel channel, string externalId, CancellationToken ct = default);
}

public sealed record OrganicSyncResult(string BrandKey, SocialChannel Channel, bool Ok, int Inserted, int Updated, string? Error);

/// <summary>
/// Relê os posts dos últimos dias de cada marca e grava os números (RB-25). O canal ajusta os números por até 48 h e
/// eles continuam crescendo depois, então reler e sobrescrever é o certo. Falha numa marca (token vencido, limite da
/// API) vira resultado com erro e não impede as outras.
/// </summary>
public sealed class SyncOrganicInsights
{
    public const int DefaultDays = 90;

    private readonly IReadOnlyList<IOrganicInsightsReader> _readers;
    private readonly IOrganicPostStore _store;
    private readonly IPublicationStore _publications;
    private readonly TimeProvider _time;
    private readonly ILogger<SyncOrganicInsights> _log;

    public SyncOrganicInsights(IEnumerable<IOrganicInsightsReader> readers, IOrganicPostStore store, IPublicationStore publications,
        TimeProvider time, ILogger<SyncOrganicInsights> log)
    {
        _readers = readers.ToList();
        _store = store;
        _publications = publications;
        _time = time;
        _log = log;
    }

    public async Task<IReadOnlyList<OrganicSyncResult>> RunAsync(string? brandKey = null, int days = DefaultDays, CancellationToken ct = default)
    {
        days = Math.Clamp(days, 1, 365);
        var now = _time.GetUtcNow();
        var since = now.AddDays(-days);
        var results = new List<OrganicSyncResult>();
        foreach (var reader in _readers)
        {
            foreach (var brand in reader.Brands.Where(b => brandKey is null || b == brandKey))
            {
                try
                {
                    var rows = await reader.ReadRecentAsync(brand, since, ct);
                    // Post que saiu da plataforma: liga pelo id gravado na publicação.
                    var pubs = (await _publications.ListByBrandAsync(brand, since.AddDays(-7), now, ct))
                        .Where(p => p.Channel == reader.Channel && p.Status == PublicationStatus.Published && p.ExternalId is not null)
                        .GroupBy(p => p.ExternalId!)
                        .ToDictionary(g => g.Key, g => g.First().Id);
                    var (ins, upd) = await _store.UpsertAsync(brand, reader.Channel, rows, pubs, now, ct);
                    results.Add(new(brand, reader.Channel, true, ins, upd, null));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _log.LogWarning(ex, "Métricas orgânicas {Brand} {Channel} falharam", brand, reader.Channel);
                    results.Add(new(brand, reader.Channel, false, 0, 0, ex.Message));
                }
            }
        }
        return results;
    }
}

/// <summary>Um post no ranking, com a nota (0 a 100) e o motivo em linguagem simples.</summary>
public sealed record RankedPost(OrganicPost Post, int Score, bool EnoughData, decimal EngagementRate, string Reason);

/// <summary>
/// Ranking dos vídeos de uma marca pelo que o público fez (RB-25, Onda 4b do plano). Regras explicáveis: cada post
/// é comparado com os outros da mesma marca em três sinais e ganha a posição relativa (percentil) em cada um.
/// <list type="bullet">
/// <item>Interesse (50%): salvamentos e compartilhamentos valem 3, comentários 2, curtidas 1, por pessoa alcançada.
///   Salvar e compartilhar é o que mais prevê que o vídeo funciona como anúncio.</item>
/// <item>Retenção (30%): tempo médio assistido. Sem esse número, o peso vai pro interesse.</item>
/// <item>Alcance (20%): quantas contas viram.</item>
/// </list>
/// Post com menos de <see cref="MinReach"/> pessoas alcançadas aparece, mas marcado como pouco dado.
/// </summary>
public static class CreativeRanking
{
    public const long MinReach = 100;

    public static decimal Engagement(OrganicPost p) =>
        p.Reach == 0 ? 0 : (p.Saves * 3m + p.Shares * 3m + p.Comments * 2m + p.Likes) / p.Reach;

    public static IReadOnlyList<RankedPost> Rank(IEnumerable<OrganicPost> posts, bool videosOnly = true)
    {
        var list = posts.Where(p => !videosOnly || p.IsVideo).ToList();
        var eligible = list.Where(p => p.Reach >= MinReach).ToList();
        var eng = eligible.Select(Engagement).ToList();
        var watch = eligible.Where(p => p.AvgWatchMs is not null).Select(p => (decimal)p.AvgWatchMs!.Value).ToList();
        var reach = eligible.Select(p => (decimal)p.Reach).ToList();
        decimal avgEng = eng.Count == 0 ? 0 : eng.Average();

        var ranked = list.Select(p =>
        {
            var e = Engagement(p);
            if (p.Reach < MinReach)
                return new RankedPost(p, 0, false, e, $"Pouco dado ainda: {p.Reach} pessoas alcançadas (mínimo {MinReach}).");
            var pe = Percentile(eng, e);
            var pr = Percentile(reach, p.Reach);
            int score;
            if (p.AvgWatchMs is { } w && watch.Count > 0)
                score = (int)Math.Round((0.5m * pe + 0.3m * Percentile(watch, w) + 0.2m * pr) * 100);
            else
                score = (int)Math.Round((0.8m * pe + 0.2m * pr) * 100);
            return new RankedPost(p, score, true, e, Explain(p, e, avgEng));
        })
        .OrderByDescending(r => r.EnoughData)
        .ThenByDescending(r => r.Score)
        .ThenByDescending(r => r.Post.Reach)
        .ToList();
        return ranked;
    }

    /// <summary>Fração dos outros valores que ficam abaixo (empate conta metade). Um só post = 0,5.</summary>
    private static decimal Percentile(IReadOnlyList<decimal> all, decimal v)
    {
        if (all.Count <= 1) return 0.5m;
        var below = all.Count(x => x < v);
        var equal = all.Count(x => x == v) - 1;
        return (below + equal / 2m) / (all.Count - 1);
    }

    private static string Explain(OrganicPost p, decimal e, decimal avg)
    {
        var parts = new List<string> { $"{p.Reach} pessoas alcançadas" };
        if (p.Saves + p.Shares > 0) parts.Add($"{p.Saves} salvamentos e {p.Shares} compartilhamentos");
        if (p.AvgWatchMs is { } w) parts.Add($"{w / 1000m:0.0} s assistidos em média");
        var vs = avg == 0 ? "" : e >= avg * 1.2m ? ", interesse acima da média da marca" : e <= avg * 0.8m ? ", interesse abaixo da média da marca" : ", interesse na média da marca";
        return string.Join(", ", parts) + vs + ".";
    }

    /// <summary>Resumo dos números de um post pra IA usar ao montar a campanha (é DADO, não instrução).</summary>
    public static string Describe(RankedPost r, int position, int total) =>
        $"Posição {position} de {total} no ranking orgânico da marca (nota {r.Score}/100). {r.Reason} " +
        $"Visualizações {r.Post.Views}, curtidas {r.Post.Likes}, comentários {r.Post.Comments}.";
}

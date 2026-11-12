using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RootBoost.Application.Organic;
using RootBoost.Domain;
using RootBoost.Infrastructure.Persistence;

namespace RootBoost.Infrastructure.Organic;

/// <summary>
/// Leitor de mentira (Organic:Reader=Fake): quatro Reels com números fixos, sem rede, pra tela e testes terem ranking.
/// </summary>
public sealed class FakeOrganicInsightsReader : IOrganicInsightsReader
{
    private readonly IReadOnlyList<string> _brands;

    public FakeOrganicInsightsReader(IEnumerable<string> brands) => _brands = brands.ToList();

    public SocialChannel Channel => SocialChannel.Instagram;
    public IReadOnlyList<string> Brands => _brands;

    public Task<IReadOnlyList<OrganicPostRow>> ReadRecentAsync(string brandKey, DateTimeOffset since, CancellationToken ct = default)
    {
        var day = since.AddDays(1);
        IReadOnlyList<OrganicPostRow> rows = new[]
        {
            Row("fake-ig-1", "Passo a passo da ficha de atendimento", day, 4200, 3100, 120, 9, 41, 66, 18_400),
            Row("fake-ig-2", "Agenda cheia sem falta", day.AddDays(3), 2600, 2000, 80, 4, 6, 9, 9_100),
            Row("fake-ig-3", "Relatório do mês em 1 minuto", day.AddDays(6), 1500, 1200, 30, 2, 12, 25, 14_700),
            Row("fake-ig-4", "Bastidores", day.AddDays(9), 90, 70, 3, 0, 0, 0, 3_000),
        };
        return Task.FromResult(rows);
    }

    private static OrganicPostRow Row(string id, string caption, DateTimeOffset at, long views, long reach, long likes, long comments,
        long shares, long saves, long watchMs) =>
        new(id, "REELS", "VIDEO", caption, $"https://www.instagram.com/reel/{id}/", null, at, views, reach, likes, comments, shares, saves,
            likes + comments + shares + saves, watchMs);
}

public sealed class EfOrganicPostStore : IOrganicPostStore
{
    private readonly RootBoostDbContext _db;

    public EfOrganicPostStore(RootBoostDbContext db) => _db = db;

    public async Task<(int Inserted, int Updated)> UpsertAsync(string brandKey, SocialChannel channel, IReadOnlyList<OrganicPostRow> rows,
        IReadOnlyDictionary<string, Guid> publicationsByExternalId, DateTimeOffset fetchedAt, CancellationToken ct = default)
    {
        if (rows.Count == 0) return (0, 0);
        var ids = rows.Select(r => r.ExternalId).Distinct().ToList();
        var existing = await _db.OrganicPosts.Where(x => x.Channel == channel && ids.Contains(x.ExternalId)).ToDictionaryAsync(x => x.ExternalId, ct);
        int inserted = 0, updated = 0;
        foreach (var r in rows)
        {
            if (!existing.TryGetValue(r.ExternalId, out var post))
            {
                post = new OrganicPost(brandKey, channel, r.ExternalId, r.PostedAt);
                _db.OrganicPosts.Add(post);
                existing[r.ExternalId] = post;
                inserted++;
            }
            else updated++;
            post.Describe(r.ProductType, r.MediaType, r.Caption, r.Permalink, r.ThumbnailUrl);
            if (publicationsByExternalId.TryGetValue(r.ExternalId, out var pubId)) post.LinkPublication(pubId);
            post.SetMetrics(r.Views, r.Reach, r.Likes, r.Comments, r.Shares, r.Saves, r.Interactions, r.AvgWatchMs, fetchedAt);
        }
        await _db.SaveChangesAsync(ct);
        return (inserted, updated);
    }

    public async Task<IReadOnlyList<OrganicPost>> ListByBrandAsync(string brandKey, DateTimeOffset since, CancellationToken ct = default) =>
        await _db.OrganicPosts.AsNoTracking().Where(x => x.BrandKey == brandKey && x.PostedAt >= since).ToListAsync(ct);

    public Task<OrganicPost?> FindAsync(SocialChannel channel, string externalId, CancellationToken ct = default) =>
        _db.OrganicPosts.AsNoTracking().FirstOrDefaultAsync(x => x.Channel == channel && x.ExternalId == externalId, ct);
}

/// <summary>Relê as métricas orgânicas a cada 12 horas (posts dos últimos 90 dias). Erro só vai pro log.</summary>
public sealed class OrganicInsightsScheduler : BackgroundService
{
    private static readonly TimeSpan FirstRun = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(12);
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<OrganicInsightsScheduler> _log;

    public OrganicInsightsScheduler(IServiceScopeFactory scopes, ILogger<OrganicInsightsScheduler> log)
    {
        _scopes = scopes;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(FirstRun, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopes.CreateScope();
                    foreach (var r in await scope.ServiceProvider.GetRequiredService<SyncOrganicInsights>().RunAsync(ct: stoppingToken))
                    {
                        if (r.Ok) _log.LogInformation("Orgânico {Brand} {Channel}: {Inserted} novos, {Updated} atualizados.", r.BrandKey, r.Channel, r.Inserted, r.Updated);
                        else _log.LogWarning("Orgânico {Brand} {Channel} falhou: {Error}", r.BrandKey, r.Channel, r.Error);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _log.LogError(ex, "Rodada de métricas orgânicas falhou.");
                }
                await Task.Delay(Interval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // desligando
        }
    }
}

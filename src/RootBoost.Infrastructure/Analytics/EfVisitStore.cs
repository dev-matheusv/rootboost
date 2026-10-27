using Microsoft.EntityFrameworkCore;
using RootBoost.Application.Abstractions;
using RootBoost.Infrastructure.Persistence;

namespace RootBoost.Infrastructure.Analytics;

/// <summary>
/// Implementa <see cref="IVisitStore"/> com um contador diário no banco. O upsert é UMA instrução
/// atômica (INSERT ... ON CONFLICT DO UPDATE): duas visitas no mesmo instante não perdem contagem nem
/// batem na chave primária (o "lê, soma, grava" antigo perdia no Postgres).
/// </summary>
public sealed class EfVisitStore : IVisitStore
{
    private readonly RootBoostDbContext _db;
    public EfVisitStore(RootBoostDbContext db) => _db = db;

    public async Task RecordViewAsync(string productKey, string? lang, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(productKey)) return;
        productKey = productKey.Trim();
        var normLang = (lang ?? "").Trim().ToLowerInvariant();
        if (normLang.Length > 8) normLang = normLang[..8];
        if (productKey.Length > 64) productKey = productKey[..64];
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        await _db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "LandingViews" ("ProductKey", "Lang", "DateUtc", "Count")
            VALUES ({productKey}, {normLang}, {today}, 1)
            ON CONFLICT ("ProductKey", "Lang", "DateUtc")
            DO UPDATE SET "Count" = "LandingViews"."Count" + 1
            """, ct);
    }

    public async Task<IReadOnlyDictionary<string, long>> GetViewCountsAsync(DateTimeOffset? since = null, CancellationToken ct = default)
    {
        IQueryable<LandingViewDaily> q = _db.LandingViews.AsNoTracking();
        if (since is { } s)
        {
            var sinceDate = s.UtcDateTime.Date;
            q = q.Where(x => x.DateUtc >= sinceDate);
        }

        var grouped = await q
            .GroupBy(x => x.ProductKey)
            .Select(g => new { Key = g.Key, Total = g.Sum(x => x.Count) })
            .ToListAsync(ct);

        return grouped.ToDictionary(g => g.Key, g => g.Total, StringComparer.OrdinalIgnoreCase);
    }
}

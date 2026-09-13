using Microsoft.EntityFrameworkCore;
using RootBoost.Application.Abstractions;
using RootBoost.Infrastructure.Persistence;

namespace RootBoost.Infrastructure.Analytics;

/// <summary>
/// Implementa <see cref="IVisitStore"/> com um contador diário no SQLite. Upsert simples:
/// acha a linha (produto, idioma, hoje) e incrementa, ou cria. SQLite tem escritor único,
/// então a corrida entre duas visitas simultâneas é serializada pelo banco.
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
        var today = DateTime.UtcNow.Date;

        var row = await _db.LandingViews
            .FirstOrDefaultAsync(x => x.ProductKey == productKey && x.Lang == normLang && x.DateUtc == today, ct);

        if (row is null)
            _db.LandingViews.Add(new LandingViewDaily { ProductKey = productKey, Lang = normLang, DateUtc = today, Count = 1 });
        else
            row.Count += 1;

        await _db.SaveChangesAsync(ct);
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

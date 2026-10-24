using Microsoft.EntityFrameworkCore;
using RootBoost.Application.Ads;
using RootBoost.Domain;

namespace RootBoost.Infrastructure.Persistence;

public sealed class EfAdInsightStore : IAdInsightStore
{
    private readonly RootBoostDbContext _db;

    public EfAdInsightStore(RootBoostDbContext db) => _db = db;

    public async Task<UpsertResult> UpsertAsync(AdAccount account, IReadOnlyList<AdInsightRow> rows, DateTimeOffset fetchedAt, CancellationToken ct = default)
    {
        if (rows.Count == 0) return new(0, 0);
        var since = rows.Min(r => r.Date);
        var until = rows.Max(r => r.Date);

        // Carrega a janela inteira de uma vez e casa em memória: poucas centenas de linhas por conta.
        var existing = await _db.AdInsights
            .Where(x => x.Platform == account.Platform && x.AccountId == account.AccountId && x.Date >= since && x.Date <= until)
            .ToListAsync(ct);
        var byKey = existing.ToDictionary(x => (x.Level, x.ObjectId, x.Date));

        int inserted = 0, updated = 0;
        foreach (var row in rows)
        {
            if (!byKey.TryGetValue((row.Level, row.ObjectId, row.Date), out var entity))
            {
                entity = new AdInsightDaily(account.BrandKey, account.Platform, account.AccountId, row.Level, row.ObjectId,
                    row.ObjectName, row.CampaignId, row.Date, row.Currency, fetchedAt);
                _db.AdInsights.Add(entity);
                byKey[(row.Level, row.ObjectId, row.Date)] = entity;
                inserted++;
            }
            else
            {
                updated++;
            }
            entity.SetMetrics(row.ObjectName, row.Spend, row.Impressions, row.Clicks, row.Reach, row.Conversions, row.Currency, fetchedAt);
        }

        await _db.SaveChangesAsync(ct);
        return new(inserted, updated);
    }

    public async Task<IReadOnlyList<AdInsightDaily>> ListAsync(string brandKey, DateOnly since, DateOnly until, AdLevel level, CancellationToken ct = default) =>
        await _db.AdInsights.AsNoTracking()
            .Where(x => x.BrandKey == brandKey && x.Level == level && x.Date >= since && x.Date <= until)
            .OrderBy(x => x.Date)
            .ToListAsync(ct);
}

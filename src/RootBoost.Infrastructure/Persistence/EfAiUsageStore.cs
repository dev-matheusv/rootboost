using Microsoft.EntityFrameworkCore;
using RootBoost.Application.Ai;

namespace RootBoost.Infrastructure.Persistence;

public sealed class EfAiUsageStore : IAiUsageStore
{
    private readonly RootBoostDbContext _db;

    public EfAiUsageStore(RootBoostDbContext db) => _db = db;

    public async Task AddAsync(AiUsageEntry entry, CancellationToken ct = default)
    {
        _db.AiUsage.Add(entry);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<AiUsageEntry>> ListSinceAsync(DateTimeOffset since, CancellationToken ct = default) =>
        await _db.AiUsage.AsNoTracking().Where(u => u.CreatedAt >= since).ToListAsync(ct);
}

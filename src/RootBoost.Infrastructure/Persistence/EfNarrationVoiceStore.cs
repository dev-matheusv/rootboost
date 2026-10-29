using Microsoft.EntityFrameworkCore;
using RootBoost.Application.Studio;
using RootBoost.Domain;

namespace RootBoost.Infrastructure.Persistence;

public sealed class EfNarrationVoiceStore : INarrationVoiceStore
{
    private readonly RootBoostDbContext _db;

    public EfNarrationVoiceStore(RootBoostDbContext db) => _db = db;

    public async Task AddAsync(NarrationVoice voice, CancellationToken ct = default)
    {
        _db.NarrationVoices.Add(voice);
        await _db.SaveChangesAsync(ct);
    }

    public Task<NarrationVoice?> GetAsync(Guid id, CancellationToken ct = default) => _db.NarrationVoices.FirstOrDefaultAsync(v => v.Id == id, ct);

    public Task<NarrationVoice?> FindByKeyAsync(string key, CancellationToken ct = default) =>
        _db.NarrationVoices.AsNoTracking().FirstOrDefaultAsync(v => v.Key == key, ct);

    public async Task<IReadOnlyList<NarrationVoice>> ListByBrandAsync(string brandKey, CancellationToken ct = default) =>
        await _db.NarrationVoices.AsNoTracking().Where(v => v.BrandKey == brandKey).OrderByDescending(v => v.CreatedAt).Take(50).ToListAsync(ct);

    /// <summary>Mesmo molde do estúdio: FOR UPDATE SKIP LOCKED, dois workers nunca levam a mesma voz.</summary>
    public async Task<NarrationVoice?> ClaimNextAsync(string workerId, DateTimeOffset now, TimeSpan lease, CancellationToken ct = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var voice = (await _db.NarrationVoices
            .FromSqlInterpolated($"""
                SELECT * FROM "NarrationVoices"
                WHERE "Status" = 'Queued' OR ("Status" = 'Processing' AND "LeaseUntil" < {now})
                ORDER BY "CreatedAt"
                LIMIT 1
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(ct)).FirstOrDefault();
        if (voice is null) return null;
        voice.Claim(workerId, now, lease);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return voice;
    }

    public async Task RemoveAsync(NarrationVoice voice, CancellationToken ct = default)
    {
        _db.NarrationVoices.Remove(voice);
        await _db.SaveChangesAsync(ct);
    }

    public Task SaveAsync(NarrationVoice voice, CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}

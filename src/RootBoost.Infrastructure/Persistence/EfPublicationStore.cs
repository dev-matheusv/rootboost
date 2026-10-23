using Microsoft.EntityFrameworkCore;
using RootBoost.Application.Publishing;
using RootBoost.Domain;

namespace RootBoost.Infrastructure.Persistence;

public sealed class EfPublicationStore : IPublicationStore
{
    private readonly RootBoostDbContext _db;

    public EfPublicationStore(RootBoostDbContext db) => _db = db;

    public async Task AddAsync(Publication publication, CancellationToken ct = default)
    {
        _db.Publications.Add(publication);
        await _db.SaveChangesAsync(ct);
    }

    public Task<Publication?> GetAsync(Guid id, CancellationToken ct = default) =>
        _db.Publications.FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<IReadOnlyList<Publication>> ListByBrandAsync(string brandKey, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct = default)
    {
        var q = _db.Publications.AsNoTracking().Where(p => p.BrandKey == brandKey);
        if (from is { } f) q = q.Where(p => p.ScheduledFor >= f);
        if (to is { } t) q = q.Where(p => p.ScheduledFor <= t);
        return await q.OrderBy(p => p.ScheduledFor).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Publication>> ListDueAsync(DateTimeOffset now, TimeSpan stuckAfter, CancellationToken ct = default)
    {
        var stuckBefore = now - stuckAfter;
        return await _db.Publications
            .Where(p => (p.Status == PublicationStatus.Approved && p.ScheduledFor <= now) ||
                        (p.Status == PublicationStatus.Publishing && p.PublishingStartedAt <= stuckBefore))
            .OrderBy(p => p.ScheduledFor)
            .ToListAsync(ct);
    }

    public async Task SaveAsync(Publication publication, CancellationToken ct = default)
    {
        if (_db.Entry(publication).State == EntityState.Detached) _db.Publications.Update(publication);
        await _db.SaveChangesAsync(ct);
    }
}

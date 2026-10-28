using Microsoft.EntityFrameworkCore;
using RootBoost.Application.Alerts;
using RootBoost.Domain;

namespace RootBoost.Infrastructure.Persistence;

public sealed class EfAlertStore : IAlertStore
{
    private readonly RootBoostDbContext _db;

    public EfAlertStore(RootBoostDbContext db) => _db = db;

    public Task<OpsAlert?> GetOpenAsync(string key, CancellationToken ct = default) =>
        _db.OpsAlerts.FirstOrDefaultAsync(a => a.Key == key && a.ResolvedAt == null, ct);

    public Task<OpsAlert?> GetAsync(Guid id, CancellationToken ct = default) =>
        _db.OpsAlerts.FirstOrDefaultAsync(a => a.Id == id, ct);

    public async Task<IReadOnlyList<OpsAlert>> ListOpenAsync(CancellationToken ct = default) =>
        await _db.OpsAlerts.AsNoTracking().Where(a => a.ResolvedAt == null).OrderByDescending(a => a.LastSeenAt).ToListAsync(ct);

    public async Task AddAsync(OpsAlert alert, CancellationToken ct = default)
    {
        _db.OpsAlerts.Add(alert);
        await _db.SaveChangesAsync(ct);
    }

    public Task SaveAsync(OpsAlert alert, CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}

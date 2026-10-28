using Microsoft.EntityFrameworkCore;
using RootBoost.Application.Push;
using RootBoost.Domain;

namespace RootBoost.Infrastructure.Persistence;

public sealed class EfPushDeviceStore : IPushDeviceStore
{
    private readonly RootBoostDbContext _db;

    public EfPushDeviceStore(RootBoostDbContext db) => _db = db;

    public Task<PushDevice?> GetByEndpointAsync(string endpoint, CancellationToken ct = default) =>
        _db.PushDevices.FirstOrDefaultAsync(d => d.Endpoint == endpoint, ct);

    public async Task<IReadOnlyList<PushDevice>> ListAsync(CancellationToken ct = default) =>
        await _db.PushDevices.OrderBy(d => d.CreatedAt).Take(50).ToListAsync(ct);

    public async Task AddAsync(PushDevice device, CancellationToken ct = default)
    {
        _db.PushDevices.Add(device);
        await _db.SaveChangesAsync(ct);
    }

    public Task SaveAsync(PushDevice device, CancellationToken ct = default) => _db.SaveChangesAsync(ct);

    public async Task RemoveAsync(PushDevice device, CancellationToken ct = default)
    {
        _db.PushDevices.Remove(device);
        await _db.SaveChangesAsync(ct);
    }
}

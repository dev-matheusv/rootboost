using Microsoft.EntityFrameworkCore;
using RootBoost.Application.Media;
using RootBoost.Domain;

namespace RootBoost.Infrastructure.Persistence;

public sealed class EfMediaAssetStore : IMediaAssetStore
{
    private readonly RootBoostDbContext _db;

    public EfMediaAssetStore(RootBoostDbContext db) => _db = db;

    public async Task AddAsync(MediaAsset asset, CancellationToken ct = default)
    {
        _db.MediaAssets.Add(asset);
        await _db.SaveChangesAsync(ct);
    }

    public Task<MediaAsset?> GetAsync(Guid id, CancellationToken ct = default) =>
        _db.MediaAssets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<MediaAsset>> ListByBrandAsync(string brandKey, int limit, CancellationToken ct = default) =>
        await _db.MediaAssets.AsNoTracking()
            .Where(x => x.BrandKey == brandKey)
            .OrderByDescending(x => x.CreatedAt)
            .Take(Math.Clamp(limit, 1, 500))
            .ToListAsync(ct);

    public async Task RemoveAsync(MediaAsset asset, CancellationToken ct = default)
    {
        await _db.MediaAssets.Where(x => x.Id == asset.Id).ExecuteDeleteAsync(ct);
    }
}

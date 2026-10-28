using Microsoft.EntityFrameworkCore;
using RootBoost.Application.Ai;
using RootBoost.Domain;

namespace RootBoost.Infrastructure.Persistence;

public sealed class EfBrandProductStore : IBrandProductStore
{
    private readonly RootBoostDbContext _db;

    public EfBrandProductStore(RootBoostDbContext db) => _db = db;

    public Task<BrandProduct?> GetAsync(string brandKey, CancellationToken ct = default) =>
        _db.BrandProducts.FirstOrDefaultAsync(p => p.BrandKey == brandKey, ct);

    public async Task AddAsync(BrandProduct product, CancellationToken ct = default)
    {
        _db.BrandProducts.Add(product);
        await _db.SaveChangesAsync(ct);
    }

    public Task SaveAsync(BrandProduct product, CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}

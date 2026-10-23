using Microsoft.EntityFrameworkCore;
using RootBoost.Application.Abstractions;
using RootBoost.Domain;

namespace RootBoost.Infrastructure.Persistence;

public sealed class EfBrandStore : IBrandStore
{
    private readonly RootBoostDbContext _db;

    public EfBrandStore(RootBoostDbContext db) => _db = db;

    public async Task<IReadOnlyList<Brand>> ListAsync(CancellationToken ct = default)
        => await _db.Brands.AsNoTracking().OrderBy(b => b.Name).ToListAsync(ct);
}

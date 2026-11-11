using Microsoft.EntityFrameworkCore;
using RootBoost.Application.Ads;
using RootBoost.Domain;

namespace RootBoost.Infrastructure.Persistence;

public sealed class EfAdCampaignStore : IAdCampaignStore
{
    private readonly RootBoostDbContext _db;

    public EfAdCampaignStore(RootBoostDbContext db) => _db = db;

    public async Task AddAsync(AdCampaignDraft draft, CancellationToken ct = default)
    {
        _db.AdCampaignDrafts.Add(draft);
        await _db.SaveChangesAsync(ct);
    }

    public Task<AdCampaignDraft?> GetAsync(Guid id, CancellationToken ct = default) =>
        _db.AdCampaignDrafts.FirstOrDefaultAsync(d => d.Id == id, ct);

    public Task SaveAsync(AdCampaignDraft draft, CancellationToken ct = default) => _db.SaveChangesAsync(ct);

    public async Task<IReadOnlyList<AdCampaignDraft>> ListByBrandAsync(string brandKey, CancellationToken ct = default) =>
        await _db.AdCampaignDrafts.AsNoTracking().Where(d => d.BrandKey == brandKey).OrderByDescending(d => d.CreatedAt).Take(50).ToListAsync(ct);
}

using Microsoft.EntityFrameworkCore;
using RootBoost.Application.Abstractions;
using RootBoost.Domain;

namespace RootBoost.Infrastructure.Persistence;

public sealed class EfBrandProfileStore : IBrandProfileStore
{
    private readonly RootBoostDbContext _db;

    public EfBrandProfileStore(RootBoostDbContext db) => _db = db;

    public async Task<BrandProfile?> GetAsync(string brandKey, CancellationToken ct = default)
    {
        var brand = await _db.Brands.AsNoTracking().FirstOrDefaultAsync(b => b.Key == brandKey, ct);
        if (brand is null) return null;

        var proof = await _db.ProofItems.AsNoTracking().Where(p => p.BrandKey == brandKey)
            .OrderByDescending(p => p.Active).ThenBy(p => p.Kind).ThenBy(p => p.CreatedAt).ToListAsync(ct);
        var phrases = await _db.CustomerPhrases.AsNoTracking().Where(p => p.BrandKey == brandKey)
            .OrderByDescending(p => p.CollectedAt).ToListAsync(ct);

        return new BrandProfile(brand, brand.Voice, proof, phrases);
    }

    public async Task AddProofAsync(ProofItem item, CancellationToken ct = default)
    {
        _db.ProofItems.Add(item);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<bool> RetireProofAsync(string brandKey, Guid proofId, CancellationToken ct = default)
    {
        var item = await _db.ProofItems.FirstOrDefaultAsync(p => p.Id == proofId && p.BrandKey == brandKey, ct);
        if (item is null) return false;
        item.Retire();
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task AddPhraseAsync(CustomerPhrase phrase, CancellationToken ct = default)
    {
        _db.CustomerPhrases.Add(phrase);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<bool> SetVoiceAsync(string brandKey, BrandVoice voice, CancellationToken ct = default)
    {
        var brand = await _db.Brands.FirstOrDefaultAsync(b => b.Key == brandKey, ct);
        if (brand is null) return false;
        brand.SetVoice(voice);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> ImportIfEmptyAsync(string brandKey, BrandVoice voice, IReadOnlyList<ProofItem> proof,
        IReadOnlyList<CustomerPhrase> phrases, CancellationToken ct = default)
    {
        var brand = await _db.Brands.FirstOrDefaultAsync(b => b.Key == brandKey, ct);
        if (brand is null) return false;
        if (await _db.ProofItems.AnyAsync(p => p.BrandKey == brandKey, ct) ||
            await _db.CustomerPhrases.AnyAsync(p => p.BrandKey == brandKey, ct))
            return false;

        brand.SetVoice(voice);
        _db.ProofItems.AddRange(proof);
        _db.CustomerPhrases.AddRange(phrases);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}

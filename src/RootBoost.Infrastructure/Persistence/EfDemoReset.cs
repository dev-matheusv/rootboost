using Microsoft.EntityFrameworkCore;
using RootBoost.Application.Studio;
using RootBoost.Domain;

namespace RootBoost.Infrastructure.Persistence;

public sealed class EfDemoReset : IDemoReset
{
    private readonly RootBoostDbContext _db;

    public EfDemoReset(RootBoostDbContext db) => _db = db;

    public async Task<IReadOnlyList<string>> ResetAsync(string brandKey, CancellationToken ct = default)
    {
        // Trava dupla: mesmo que alguém chame por engano, marca real nunca é apagada aqui.
        if (!BrandKeys.IsDemo(brandKey)) throw new InvalidOperationException("só marca de demonstração pode ser restaurada");
        var media = await _db.MediaAssets.AsNoTracking().Where(m => m.BrandKey == brandKey).ToListAsync(ct);
        var voices = await _db.NarrationVoices.AsNoTracking().Where(v => v.BrandKey == brandKey).ToListAsync(ct);
        var keys = media.SelectMany(m => new[] { m.StorageKey, m.RedactedStorageKey })
            .Concat(voices.SelectMany(v => v.Samples.Select(s => s.StorageKey).Append(v.ReferenceStorageKey)))
            .OfType<string>().ToList();
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        await _db.Publications.Where(p => p.BrandKey == brandKey).ExecuteDeleteAsync(ct);
        await _db.CreativeJobs.Where(j => j.BrandKey == brandKey).ExecuteDeleteAsync(ct);
        await _db.MediaAssets.Where(m => m.BrandKey == brandKey).ExecuteDeleteAsync(ct);
        await _db.NarrationVoices.Where(v => v.BrandKey == brandKey).ExecuteDeleteAsync(ct);
        await tx.CommitAsync(ct);
        return keys;
    }
}

using Microsoft.EntityFrameworkCore;
using RootBoost.Application.Studio;
using RootBoost.Domain;

namespace RootBoost.Infrastructure.Persistence;

public sealed class EfCreativeJobStore : ICreativeJobStore
{
    private readonly RootBoostDbContext _db;

    public EfCreativeJobStore(RootBoostDbContext db) => _db = db;

    public async Task AddAsync(CreativeJob job, CancellationToken ct = default)
    {
        _db.CreativeJobs.Add(job);
        await _db.SaveChangesAsync(ct);
    }

    public Task<CreativeJob?> GetAsync(Guid id, CancellationToken ct = default) =>
        _db.CreativeJobs.FirstOrDefaultAsync(j => j.Id == id, ct);

    public async Task<IReadOnlyList<CreativeJob>> ListByBrandAsync(string brandKey, CancellationToken ct = default) =>
        await _db.CreativeJobs.AsNoTracking().Where(j => j.BrandKey == brandKey).OrderByDescending(j => j.CreatedAt).Take(200).ToListAsync(ct);

    /// <summary>
    /// FOR UPDATE SKIP LOCKED: a linha escolhida fica travada até o commit e outro worker pula pra próxima, então
    /// ninguém leva o mesmo job. A regra de quem pode ser pego continua no domínio (<see cref="CreativeJob.Claim"/>).
    /// </summary>
    public async Task<CreativeJob?> ClaimNextAsync(string workerId, DateTimeOffset now, TimeSpan lease, CancellationToken ct = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var job = (await _db.CreativeJobs
            .FromSqlInterpolated($"""
                SELECT * FROM "CreativeJobs"
                WHERE "Status" = 'Queued' OR ("Status" = 'Rendering' AND "LeaseUntil" < {now})
                ORDER BY "CreatedAt"
                LIMIT 1
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(ct)).FirstOrDefault();
        if (job is null) return null;
        job.Claim(workerId, now, lease);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return job;
    }

    public Task SaveAsync(CreativeJob job, CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}

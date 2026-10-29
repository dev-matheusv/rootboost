using Microsoft.EntityFrameworkCore;
using RootBoost.Application.Studio;
using RootBoost.Domain;

namespace RootBoost.Infrastructure.Persistence;

public sealed class EfStudioStatusStore : IStudioStatusStore
{
    private readonly RootBoostDbContext _db;

    public EfStudioStatusStore(RootBoostDbContext db) => _db = db;

    /// <summary>Upsert atômico (ON CONFLICT): duas batidas ao mesmo tempo do mesmo worker não brigam pela linha.</summary>
    public async Task BeatAsync(string workerId, string? activity, DateTimeOffset now, CancellationToken ct = default)
    {
        var beat = new WorkerHeartbeat(workerId, now, activity); // valida e corta
        await _db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "WorkerHeartbeats" ("WorkerId", "LastSeenAt", "Activity") VALUES ({beat.WorkerId}, {now.ToUniversalTime()}, {beat.Activity})
            ON CONFLICT ("WorkerId") DO UPDATE SET "LastSeenAt" = EXCLUDED."LastSeenAt", "Activity" = EXCLUDED."Activity"
            """, ct);
    }

    public async Task<IReadOnlyList<WorkerHeartbeat>> ListWorkersAsync(CancellationToken ct = default) =>
        await _db.WorkerHeartbeats.AsNoTracking().OrderByDescending(w => w.LastSeenAt).Take(20).ToListAsync(ct);

    public async Task<IReadOnlyList<Guid>> QueueAsync(CancellationToken ct = default) =>
        await _db.CreativeJobs.AsNoTracking().Where(j => j.Status == CreativeJobStatus.Queued)
            .OrderBy(j => j.CreatedAt).Select(j => j.Id).Take(500).ToListAsync(ct);

    public async Task<int?> AverageRenderSecondsAsync(CancellationToken ct = default)
    {
        var last = await _db.CreativeJobs.AsNoTracking().Where(j => j.RenderSeconds != null)
            .OrderByDescending(j => j.UpdatedAt).Take(10).Select(j => j.RenderSeconds!.Value).ToListAsync(ct);
        return last.Count == 0 ? null : (int)last.Average();
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RootBoost.Application.Studio;
using RootBoost.Domain;

namespace RootBoost.Infrastructure.Persistence;

public sealed class EfContentIdeaStore : IContentIdeaStore
{
    private readonly RootBoostDbContext _db;

    public EfContentIdeaStore(RootBoostDbContext db) => _db = db;

    public async Task<IReadOnlyList<ContentIdea>> ListAsync(string brandKey, CancellationToken ct = default) =>
        await _db.ContentIdeas.Where(x => x.BrandKey == brandKey).OrderBy(x => x.Order).ThenBy(x => x.CreatedAt).ToListAsync(ct);

    public Task<ContentIdea?> GetAsync(Guid id, CancellationToken ct = default) => _db.ContentIdeas.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task AddAsync(ContentIdea idea, CancellationToken ct = default)
    {
        _db.ContentIdeas.Add(idea);
        await _db.SaveChangesAsync(ct);
    }

    public Task SaveAsync(ContentIdea idea, CancellationToken ct = default) => _db.SaveChangesAsync(ct);

    public async Task RemoveAsync(ContentIdea idea, CancellationToken ct = default)
    {
        _db.ContentIdeas.Remove(idea);
        await _db.SaveChangesAsync(ct);
    }

    public Task<ContentIdea?> NextQueuedAsync(CancellationToken ct = default) =>
        _db.ContentIdeas.Where(x => x.Status == ContentIdeaStatus.Queued).OrderBy(x => x.Order).ThenBy(x => x.UpdatedAt).FirstOrDefaultAsync(ct);
}

/// <summary>Anda a fila da pauta: um tema por vez, conferindo a cada 30 s. Erro só vai pro log.</summary>
public sealed class PautaScheduler : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<PautaScheduler> _log;

    public PautaScheduler(IServiceScopeFactory scopes, ILogger<PautaScheduler> log)
    {
        _scopes = scopes;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    bool more;
                    do
                    {
                        using var scope = _scopes.CreateScope();
                        more = await scope.ServiceProvider.GetRequiredService<PautaRunner>().RunOneAsync(stoppingToken);
                    } while (more && !stoppingToken.IsCancellationRequested);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _log.LogError(ex, "Rodada da pauta falhou.");
                }
                await Task.Delay(Interval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // desligando
        }
    }
}

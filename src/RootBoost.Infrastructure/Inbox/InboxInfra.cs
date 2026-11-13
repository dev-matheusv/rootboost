using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RootBoost.Application.Inbox;
using RootBoost.Domain;
using RootBoost.Infrastructure.Persistence;

namespace RootBoost.Infrastructure.Inbox;

/// <summary>Leitor de mentira (Inbox:Reader=Fake): duas conversas fictícias, sem rede, pra tela e testes.</summary>
public sealed class FakeInboxReader : IInboxReader
{
    private readonly IReadOnlyList<string> _brands;
    private readonly TimeProvider _time;

    public FakeInboxReader(IEnumerable<string> brands, TimeProvider time)
    {
        _brands = brands.ToList();
        _time = time;
    }

    public SocialChannel Channel => SocialChannel.Instagram;
    public IReadOnlyList<string> Brands => _brands;

    public Task<IReadOnlyList<InboxMessageRow>> ReadRecentAsync(string brandKey, DateTimeOffset since, IReadOnlySet<string> known,
        CancellationToken ct = default)
    {
        var now = _time.GetUtcNow();
        IReadOnlyList<InboxMessageRow> rows = new[]
        {
            new InboxMessageRow("fake-conv-1", "fake-msg-1", "fake-contato-1", "clinica.exemplo", false, "Oi! O sistema tem agenda online?", false, now.AddHours(-3)),
            new InboxMessageRow("fake-conv-1", "fake-msg-2", "fake-contato-1", "clinica.exemplo", true, "Tem sim! Quer que eu te mostre?", false, now.AddHours(-2)),
            new InboxMessageRow("fake-conv-1", "fake-msg-3", "fake-contato-1", "clinica.exemplo", false, "Quero, quanto custa?", false, now.AddHours(-1)),
            new InboxMessageRow("fake-conv-2", "fake-msg-4", "fake-contato-2", "estetica.teste", false, null, true, now.AddDays(-3)),
        };
        return Task.FromResult<IReadOnlyList<InboxMessageRow>>(rows.Where(r => !known.Contains(r.ExternalId)).ToList());
    }
}

public sealed class EfInboxStore : IInboxStore
{
    private readonly RootBoostDbContext _db;

    public EfInboxStore(RootBoostDbContext db) => _db = db;

    public async Task<IReadOnlySet<string>> KnownIdsAsync(string brandKey, SocialChannel channel, CancellationToken ct = default) =>
        (await _db.InboxMessages.AsNoTracking().Where(x => x.BrandKey == brandKey && x.Channel == channel).Select(x => x.ExternalId)
            .ToListAsync(ct)).ToHashSet();

    public async Task<int> AddAsync(string brandKey, SocialChannel channel, IReadOnlyList<InboxMessageRow> rows, DateTimeOffset fetchedAt,
        CancellationToken ct = default)
    {
        if (rows.Count == 0) return 0;
        var ids = rows.Select(r => r.ExternalId).Distinct().ToList();
        var existing = (await _db.InboxMessages.Where(x => x.Channel == channel && ids.Contains(x.ExternalId)).Select(x => x.ExternalId)
            .ToListAsync(ct)).ToHashSet();
        var inserted = 0;
        foreach (var r in rows)
        {
            if (!existing.Add(r.ExternalId)) continue;
            _db.InboxMessages.Add(new InboxMessage(brandKey, channel, r.ConversationId, r.ExternalId, r.ContactId, r.ContactName,
                r.FromBusiness, r.Text, r.HasAttachment, r.SentAt, fetchedAt));
            inserted++;
        }
        await _db.SaveChangesAsync(ct);
        return inserted;
    }

    public async Task<IReadOnlyList<InboxMessage>> ListAsync(string brandKey, DateTimeOffset since, CancellationToken ct = default) =>
        await _db.InboxMessages.AsNoTracking().Where(x => x.BrandKey == brandKey && x.SentAt >= since).ToListAsync(ct);

    public async Task<IReadOnlyList<InboxMessage>> ConversationAsync(string brandKey, string conversationId, CancellationToken ct = default) =>
        await _db.InboxMessages.AsNoTracking().Where(x => x.BrandKey == brandKey && x.ConversationId == conversationId)
            .OrderBy(x => x.SentAt).ToListAsync(ct);
}

/// <summary>Lê as mensagens novas a cada 10 minutos (Inbox:IntervalMinutes muda). Erro só vai pro log.</summary>
public sealed class InboxScheduler : BackgroundService
{
    private static readonly TimeSpan FirstRun = TimeSpan.FromMinutes(2);
    private readonly TimeSpan _interval;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<InboxScheduler> _log;

    public InboxScheduler(IServiceScopeFactory scopes, Microsoft.Extensions.Configuration.IConfiguration config, ILogger<InboxScheduler> log)
    {
        _scopes = scopes;
        _log = log;
        _interval = TimeSpan.FromMinutes(Math.Clamp(int.TryParse(config["Inbox:IntervalMinutes"], out var m) ? m : 10, 2, 240));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(FirstRun, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopes.CreateScope();
                    foreach (var r in await scope.ServiceProvider.GetRequiredService<SyncInbox>().RunAsync(ct: stoppingToken))
                    {
                        if (!r.Ok) _log.LogWarning("Caixa de entrada {Brand} {Channel} falhou: {Error}", r.BrandKey, r.Channel, r.Error);
                        else if (r.Inserted > 0) _log.LogInformation("Caixa de entrada {Brand} {Channel}: {Inserted} mensagens novas.", r.BrandKey, r.Channel, r.Inserted);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _log.LogError(ex, "Rodada da caixa de entrada falhou.");
                }
                await Task.Delay(_interval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // desligando
        }
    }
}

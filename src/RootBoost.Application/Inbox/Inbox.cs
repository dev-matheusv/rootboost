using Microsoft.Extensions.Logging;
using RootBoost.Domain;

namespace RootBoost.Application.Inbox;

/// <summary>Uma mensagem como o canal devolve.</summary>
public sealed record InboxMessageRow(
    string ConversationId,
    string ExternalId,
    string ContactId,
    string? ContactName,
    bool FromBusiness,
    string? Text,
    bool HasAttachment,
    DateTimeOffset SentAt);

/// <summary>Lê as conversas recentes de uma marca num canal. Só leitura: nunca responde nem marca como lida.</summary>
public interface IInboxReader
{
    SocialChannel Channel { get; }

    /// <summary>Marcas com conta configurada nesse canal.</summary>
    IReadOnlyList<string> Brands { get; }

    /// <summary>
    /// Mensagens de conversas mexidas desde <paramref name="since"/>. <paramref name="known"/> são ids já gravados: o
    /// leitor não busca o detalhe deles de novo (cada detalhe é uma chamada na API).
    /// </summary>
    Task<IReadOnlyList<InboxMessageRow>> ReadRecentAsync(string brandKey, DateTimeOffset since, IReadOnlySet<string> known,
        CancellationToken ct = default);
}

/// <summary>Uma conversa resumida pra lista do Console.</summary>
public sealed record InboxThread(
    SocialChannel Channel,
    string ConversationId,
    string ContactId,
    string? ContactName,
    string LastText,
    bool LastHasAttachment,
    bool LastFromBusiness,
    DateTimeOffset LastAt,
    int Count,
    /// <summary>Até quando dá pra responder (24 h depois da última mensagem da pessoa); nulo se ela nunca escreveu.</summary>
    DateTimeOffset? ReplyUntil);

public interface IInboxStore
{
    Task<IReadOnlySet<string>> KnownIdsAsync(string brandKey, SocialChannel channel, CancellationToken ct = default);

    /// <summary>Grava as mensagens novas por (canal, id). Idempotente: id já gravado é ignorado.</summary>
    Task<int> AddAsync(string brandKey, SocialChannel channel, IReadOnlyList<InboxMessageRow> rows, DateTimeOffset fetchedAt,
        CancellationToken ct = default);

    Task<IReadOnlyList<InboxMessage>> ListAsync(string brandKey, DateTimeOffset since, CancellationToken ct = default);

    Task<IReadOnlyList<InboxMessage>> ConversationAsync(string brandKey, string conversationId, CancellationToken ct = default);
}

public static class InboxThreads
{
    /// <summary>Janela padrão pra responder no Instagram e no Messenger: 24 h depois da última mensagem da pessoa.</summary>
    public static readonly TimeSpan ReplyWindow = TimeSpan.FromHours(24);

    public static IReadOnlyList<InboxThread> From(IEnumerable<InboxMessage> messages) =>
        messages.GroupBy(m => (m.Channel, m.ConversationId))
            .Select(g =>
            {
                var ordered = g.OrderBy(m => m.SentAt).ToList();
                var last = ordered[^1];
                var contact = ordered.LastOrDefault(m => m.ContactName is not null) ?? last;
                var lastFromContact = ordered.LastOrDefault(m => !m.FromBusiness);
                return new InboxThread(g.Key.Channel, g.Key.ConversationId, last.ContactId, contact.ContactName, last.Text,
                    last.HasAttachment, last.FromBusiness, last.SentAt, ordered.Count, lastFromContact?.SentAt.Add(ReplyWindow));
            })
            .OrderByDescending(t => t.LastAt)
            .ToList();
}

public sealed record InboxSyncResult(string BrandKey, SocialChannel Channel, bool Ok, int Inserted, string? Error);

/// <summary>
/// Lê as conversas recentes de cada marca e grava as mensagens novas (Onda 5c). Sem webhook ainda (exige o app
/// verificado), então é consulta periódica. Falha numa marca vira resultado com erro e não impede as outras.
/// </summary>
public sealed class SyncInbox
{
    public const int DefaultDays = 30;

    private readonly IReadOnlyList<IInboxReader> _readers;
    private readonly IInboxStore _store;
    private readonly TimeProvider _time;
    private readonly ILogger<SyncInbox> _log;

    public SyncInbox(IEnumerable<IInboxReader> readers, IInboxStore store, TimeProvider time, ILogger<SyncInbox> log)
    {
        _readers = readers.ToList();
        _store = store;
        _time = time;
        _log = log;
    }

    public bool Enabled => _readers.Count > 0;

    public async Task<IReadOnlyList<InboxSyncResult>> RunAsync(string? brandKey = null, int days = DefaultDays, CancellationToken ct = default)
    {
        var now = _time.GetUtcNow();
        var since = now.AddDays(-Math.Clamp(days, 1, 90));
        var results = new List<InboxSyncResult>();
        foreach (var reader in _readers)
        foreach (var brand in reader.Brands.Where(b => brandKey is null || b == brandKey))
        {
            try
            {
                var known = await _store.KnownIdsAsync(brand, reader.Channel, ct);
                var rows = await reader.ReadRecentAsync(brand, since, known, ct);
                var inserted = await _store.AddAsync(brand, reader.Channel, rows, now, ct);
                results.Add(new InboxSyncResult(brand, reader.Channel, true, inserted, null));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogWarning(ex, "Caixa de entrada {Brand} {Channel} falhou.", brand, reader.Channel);
                results.Add(new InboxSyncResult(brand, reader.Channel, false, 0, ex.Message));
            }
        }
        return results;
    }
}

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RootBoost.Application.Inbox;
using RootBoost.Domain;
using RootBoost.Infrastructure.Publishing;
using RootBoost.Infrastructure.Tracking;

namespace RootBoost.Infrastructure.Inbox;

/// <summary>
/// Mensagens do Instagram Direct pelo login do Facebook (token da Página ligada à conta). Conferido na doc oficial em
/// 13/11/2026 (Messenger Platform, Instagram Messaging, e referência do nó Conversation v27.0):
/// <list type="number">
/// <item><c>GET /{page-id}/conversations?platform=instagram</c>. Campos do nó: <c>id, participants, updated_time</c>
///   ("hora em que a última mensagem foi adicionada"); paginação por cursor.</item>
/// <item><c>GET /{conversa}?fields=messages</c>: ids e <c>created_time</c> de todas as mensagens, a mais recente primeiro.</item>
/// <item><c>GET /{mensagem}?fields=id,created_time,from,to,message</c>: só funciona nas 20 mais recentes da conversa
///   (mais antiga dá erro de "mensagem apagada").</item>
/// </list>
/// Permissões: <c>instagram_basic</c>, <c>instagram_manage_messages</c>, <c>pages_manage_metadata</c>, <c>pages_messaging</c>.
/// Só leitura: não responde, não marca como lida.
/// </summary>
public sealed class InstagramInboxReader : IInboxReader
{
    public const int MaxConversationPages = 5;
    public const int MaxDetailPerConversation = 20;

    private readonly IHttpClientFactory _factory;
    private readonly SocialAccounts _accounts;
    private readonly string _version;
    private readonly ILogger<InstagramInboxReader> _log;

    public InstagramInboxReader(IHttpClientFactory factory, SocialAccounts accounts, IOptions<MetaOptions> meta, ILogger<InstagramInboxReader> log)
    {
        _factory = factory;
        _accounts = accounts;
        _version = meta.Value.ApiVersion;
        _log = log;
    }

    public SocialChannel Channel => SocialChannel.Instagram;

    /// <summary>Precisa das duas contas: a Página (id e token pra ler) e o Instagram (id pra saber o que a marca mandou).</summary>
    public IReadOnlyList<string> Brands => _accounts.All.Where(a => a.Channel == SocialChannel.Instagram).Select(a => a.BrandKey)
        .Intersect(_accounts.All.Where(a => a.Channel == SocialChannel.Facebook).Select(a => a.BrandKey)).Distinct().ToList();

    public async Task<IReadOnlyList<InboxMessageRow>> ReadRecentAsync(string brandKey, DateTimeOffset since, IReadOnlySet<string> known,
        CancellationToken ct = default)
    {
        var page = _accounts.For(brandKey, SocialChannel.Facebook);
        var igId = _accounts.For(brandKey, SocialChannel.Instagram).AccountId;
        var http = _factory.CreateClient("meta");
        var token = await PageTokenAsync(http, page.AccountId, page.AccessToken, ct);

        var conversations = new List<string>();
        string? next = $"{_version}/{page.AccountId}/conversations?platform=instagram&fields=id,updated_time";
        for (var p = 0; next is not null && p < MaxConversationPages; p++)
        {
            using var doc = await GraphApi.SendAsync(http, HttpMethod.Get, next, token, null, ct);
            var old = false;
            if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                foreach (var c in data.EnumerateArray())
                {
                    if (DateTimeOffset.TryParse(Str(c, "updated_time"), out var updated) && updated < since) { old = true; continue; }
                    if (Str(c, "id") is { Length: > 0 } id) conversations.Add(id);
                }
            next = old ? null : NextPage(doc.RootElement);
        }

        var rows = new List<InboxMessageRow>();
        foreach (var conversation in conversations)
        {
            using var doc = await GraphApi.SendAsync(http, HttpMethod.Get, $"{_version}/{conversation}?fields=messages", token, null, ct);
            if (!doc.RootElement.TryGetProperty("messages", out var msgs) || !msgs.TryGetProperty("data", out var list)) continue;
            foreach (var m in list.EnumerateArray().Take(MaxDetailPerConversation))
            {
                var id = Str(m, "id");
                if (id.Length == 0 || known.Contains(id)) continue;
                if (DateTimeOffset.TryParse(Str(m, "created_time"), out var created) && created < since) break; // vem da mais nova pra mais velha
                try
                {
                    using var detail = await GraphApi.SendAsync(http, HttpMethod.Get, $"{_version}/{id}?fields=id,created_time,from,to,message",
                        token, null, ct);
                    if (Parse(conversation, igId, detail.RootElement) is { } row) rows.Add(row);
                }
                catch (InvalidOperationException ex)
                {
                    // Uma mensagem que não abre (apagada, fora das 20) não derruba a conversa; o motivo vai pro log.
                    _log.LogWarning("Mensagem {Id} da conversa {Conversation} não abriu: {Error}", id, conversation, ex.Message);
                }
            }
        }
        return rows;
    }

    private readonly Dictionary<string, (string Token, DateTimeOffset At)> _pageTokens = new();

    /// <summary>
    /// Conversa só sai com token da Página (13/11: "(#190) This method must be called with a Page Access Token"). O token
    /// configurado é de usuário; a Página devolve o dela em <c>GET /{page-id}?fields=access_token</c>. Guardado 1 hora.
    /// Sem permissão pra isso, segue com o token configurado e o erro da Meta aparece no Console.
    /// </summary>
    private async Task<string> PageTokenAsync(HttpClient http, string pageId, string configured, CancellationToken ct)
    {
        lock (_pageTokens)
            if (_pageTokens.TryGetValue(pageId, out var c) && DateTimeOffset.UtcNow - c.At < TimeSpan.FromHours(1)) return c.Token;
        try
        {
            using var doc = await GraphApi.SendAsync(http, HttpMethod.Get, $"{_version}/{pageId}?fields=access_token", configured, null, ct);
            if (Str(doc.RootElement, "access_token") is { Length: > 0 } t)
            {
                lock (_pageTokens) _pageTokens[pageId] = (t, DateTimeOffset.UtcNow);
                return t;
            }
        }
        catch (InvalidOperationException ex)
        {
            _log.LogWarning("Token da Página {Page} não veio, sigo com o configurado: {Error}", pageId, ex.Message);
        }
        return configured;
    }

    /// <summary>Lê o detalhe de uma mensagem. A pessoa é quem mandou, ou o destinatário quando foi a marca que mandou.</summary>
    public static InboxMessageRow? Parse(string conversationId, string businessIgId, JsonElement m)
    {
        var id = Str(m, "id");
        if (id.Length == 0 || !DateTimeOffset.TryParse(Str(m, "created_time"), out var at)) return null;
        var from = m.TryGetProperty("from", out var f) ? f : default;
        var fromId = from.ValueKind == JsonValueKind.Object ? Str(from, "id") : string.Empty;
        var fromBusiness = fromId == businessIgId;
        JsonElement contact = from;
        if (fromBusiness && m.TryGetProperty("to", out var to) && to.TryGetProperty("data", out var toList) && toList.ValueKind == JsonValueKind.Array)
            contact = toList.EnumerateArray().FirstOrDefault(x => Str(x, "id") != businessIgId);
        if (contact.ValueKind != JsonValueKind.Object || Str(contact, "id") is not { Length: > 0 } contactId) return null;
        var text = Str(m, "message");
        var username = Str(contact, "username");
        return new InboxMessageRow(conversationId, id, contactId, username.Length > 0 ? username : null, fromBusiness,
            text, text.Length == 0, at);
    }

    private static string? NextPage(JsonElement root) =>
        root.TryGetProperty("paging", out var paging) && paging.TryGetProperty("next", out var n) && n.ValueKind == JsonValueKind.String
            ? n.GetString() : null;

    private static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : string.Empty;
}

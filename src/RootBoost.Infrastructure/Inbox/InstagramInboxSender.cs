using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RootBoost.Application.Inbox;
using RootBoost.Domain;
using RootBoost.Infrastructure.Publishing;
using RootBoost.Infrastructure.Tracking;

namespace RootBoost.Infrastructure.Inbox;

/// <summary>
/// Resposta no Instagram Direct (18/11), pela doc oficial (Messenger Platform, Instagram Messaging, Send API):
/// <c>POST /{page-id}/messages</c> com o token da Página, <c>recipient = {"id": IGSID}</c> e <c>message = {"text": ...}</c>;
/// devolve <c>{"recipient_id", "message_id"}</c>. Permissão <c>instagram_manage_messages</c>. Texto até 1000 bytes.
/// </summary>
public sealed class InstagramInboxSender : IInboxSender
{
    private readonly IHttpClientFactory _factory;
    private readonly SocialAccounts _accounts;
    private readonly string _version;
    private readonly ILogger<InstagramInboxSender> _log;

    public InstagramInboxSender(IHttpClientFactory factory, SocialAccounts accounts, IOptions<MetaOptions> meta, ILogger<InstagramInboxSender> log)
    {
        _factory = factory;
        _accounts = accounts;
        _version = meta.Value.ApiVersion;
        _log = log;
    }

    public SocialChannel Channel => SocialChannel.Instagram;

    public async Task<string> SendAsync(string brandKey, string contactId, string text, CancellationToken ct = default)
    {
        var page = _accounts.For(brandKey, SocialChannel.Facebook);
        var http = _factory.CreateClient("meta-inbox");
        var token = await InstagramInboxReader.PageTokenAsync(http, _version, page.AccountId, page.AccessToken, _log, ct);
        using var doc = await GraphApi.SendAsync(http, HttpMethod.Post, $"{_version}/{page.AccountId}/messages", token, new Dictionary<string, string>
        {
            ["recipient"] = JsonSerializer.Serialize(new { id = contactId }),
            ["message"] = JsonSerializer.Serialize(new { text }),
        }, ct);
        return doc.RootElement.TryGetProperty("message_id", out var id) && id.ValueKind == JsonValueKind.String
            ? id.GetString()!
            : throw new InvalidOperationException("a Meta não devolveu o id da mensagem enviada");
    }
}

/// <summary>Envio de mentira (Inbox:Reader=Fake): não chama rede, devolve um id novo.</summary>
public sealed class FakeInboxSender : IInboxSender
{
    public SocialChannel Channel => SocialChannel.Instagram;
    public Task<string> SendAsync(string brandKey, string contactId, string text, CancellationToken ct = default) =>
        Task.FromResult("fake-enviada-" + Guid.NewGuid().ToString("N")[..8]);
}

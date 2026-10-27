using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using RootBoost.Application.Publishing;
using RootBoost.Domain;
using RootBoost.Infrastructure.Tracking;

namespace RootBoost.Infrastructure.Publishing;

/// <summary>
/// Publica na Página do Facebook. Token de Página com <c>pages_manage_posts</c>.
/// <para>Foto, conferido na referência "Page Photos" em 24/10/2026: <c>POST /{page-id}/photos</c> com <c>url</c> e
/// <c>caption</c> (o <c>message</c> está depreciado) → <c>id</c> e <c>post_id</c>; link = https://www.facebook.com/{post_id}.</para>
/// <para>Vídeo vira Reels, conferido no guia "Reels Publishing" em 27/10/2026: 1) <c>POST /{page-id}/video_reels</c> com
/// <c>upload_phase=start</c> → <c>video_id</c>; 2) <c>POST rupload.facebook.com/video-upload/{versão}/{video-id}</c> com os
/// cabeçalhos <c>Authorization: OAuth {token}</c> e <c>file_url</c> (link público do bucket, sem mandar os bytes);
/// 3) <c>GET /{video-id}?fields=status</c> até <c>video_status=ready</c>; 4) <c>POST /{page-id}/video_reels</c> com
/// <c>video_id</c>, <c>upload_phase=finish</c>, <c>video_state=PUBLISHED</c> e <c>description</c> → <c>success</c>.
/// O guia não documenta link do post: fica sem link. Limite: 30 Reels por API em 24 h; vídeo de 3 a 90 s, 9:16.</para>
/// </summary>
public sealed class FacebookPagePublisher : ISocialPublisher
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);
    public const int MaxPolls = 5;
    private const string PhotoPrefix = "fb-pending-";

    private readonly IHttpClientFactory _factory;
    private readonly SocialAccounts _accounts;
    private readonly string _version;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public FacebookPagePublisher(IHttpClientFactory factory, SocialAccounts accounts, IOptions<MetaOptions> meta)
        : this(factory, accounts, meta, Task.Delay) { }

    public FacebookPagePublisher(IHttpClientFactory factory, SocialAccounts accounts, IOptions<MetaOptions> meta,
        Func<TimeSpan, CancellationToken, Task> delay)
    {
        _factory = factory;
        _accounts = accounts;
        _version = meta.Value.ApiVersion;
        _delay = delay;
    }

    public SocialChannel Channel => SocialChannel.Facebook;

    /// <summary>
    /// Foto é uma chamada só: aqui só valida e devolve uma referência local. Vídeo abre a sessão de Reels e manda o
    /// Facebook buscar o arquivo pelo link; o <c>video_id</c> é gravado antes de publicar (é o que evita publicar duas vezes).
    /// </summary>
    public async Task<string> PrepareAsync(Publication publication, CancellationToken ct = default)
    {
        var account = _accounts.For(publication.BrandKey, Channel);
        if (!InstagramPublisher.IsVideo(publication.MediaUrl)) return $"{PhotoPrefix}{publication.Id:N}";

        var http = _factory.CreateClient("meta");
        using var start = await GraphApi.SendAsync(http, HttpMethod.Post, $"{_version}/{account.AccountId}/video_reels",
            account.AccessToken, new Dictionary<string, string> { ["upload_phase"] = "start" }, ct);
        var videoId = start.Str("video_id");
        if (videoId.Length == 0) throw new InvalidOperationException("Facebook não devolveu o video_id do Reels");

        using var req = new HttpRequestMessage(HttpMethod.Post, $"https://rupload.facebook.com/video-upload/{_version}/{videoId}");
        req.Headers.Authorization = new AuthenticationHeaderValue("OAuth", account.AccessToken);
        req.Headers.TryAddWithoutValidation("file_url", publication.MediaUrl);
        using var res = await http.SendAsync(req, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode) throw new InvalidOperationException(GraphApi.DescribeError((int)res.StatusCode, body));
        using var uploaded = JsonDocument.Parse(body);
        if (!Success(uploaded)) throw new InvalidOperationException("Facebook não confirmou o envio do vídeo do Reels");
        return videoId;
    }

    /// <summary>
    /// Reels: o status diz se uma tentativa anterior já publicou. Foto não tem como saber (limite conhecido: se o
    /// processo cair entre o post e a gravação, o job pode repetir; por isso o publish e o MarkPublished ficam colados).
    /// </summary>
    public async Task<PublishedPost?> FindPublishedAsync(Publication publication, string externalId, CancellationToken ct = default)
    {
        if (externalId.StartsWith(PhotoPrefix, StringComparison.Ordinal)) return null;
        var account = _accounts.For(publication.BrandKey, Channel);
        var status = await StatusAsync(account, externalId, ct);
        return status.PublishStatus == "published" ? new PublishedPost(externalId, null) : null;
    }

    public async Task<PublishedPost> PublishAsync(Publication publication, string externalId, CancellationToken ct = default)
    {
        var account = _accounts.For(publication.BrandKey, Channel);
        return externalId.StartsWith(PhotoPrefix, StringComparison.Ordinal)
            ? await PublishPhotoAsync(publication, account, ct)
            : await PublishReelAsync(publication, account, externalId, ct);
    }

    private async Task<PublishedPost> PublishPhotoAsync(Publication publication, SocialAccount account, CancellationToken ct)
    {
        using var doc = await GraphApi.SendAsync(_factory.CreateClient("meta"), HttpMethod.Post,
            $"{_version}/{account.AccountId}/photos", account.AccessToken,
            new Dictionary<string, string> { ["url"] = publication.MediaUrl, ["caption"] = publication.Caption }, ct);

        var postId = doc.Str("post_id");
        var id = postId.Length > 0 ? postId : doc.Str("id");
        if (id.Length == 0) throw new InvalidOperationException("Facebook não devolveu o id do post");
        return new PublishedPost(id, postId.Length > 0 ? $"https://www.facebook.com/{postId}" : null);
    }

    private async Task<PublishedPost> PublishReelAsync(Publication publication, SocialAccount account, string videoId, CancellationToken ct)
    {
        for (var poll = 1; ; poll++)
        {
            var status = await StatusAsync(account, videoId, ct);
            if (status.PublishStatus == "published") return new PublishedPost(videoId, null);
            if (status.VideoStatus == "ready") break;
            if (status.VideoStatus is "error" or "expired" or "upload_failed")
                throw new InvalidOperationException($"vídeo do Reels no Facebook em {status.VideoStatus}" +
                    (status.Error is { Length: > 0 } e ? $": {e}" : ": confira o formato (9:16, 3 a 90 s) e crie de novo"));
            if (poll >= MaxPolls)
                throw new InvalidOperationException("o Facebook ainda está processando o vídeo; tenta de novo na próxima rodada");
            await _delay(PollInterval, ct);
        }

        using var finish = await GraphApi.SendAsync(_factory.CreateClient("meta"), HttpMethod.Post,
            $"{_version}/{account.AccountId}/video_reels", account.AccessToken, new Dictionary<string, string>
            {
                ["video_id"] = videoId,
                ["upload_phase"] = "finish",
                ["video_state"] = "PUBLISHED",
                ["description"] = publication.Caption,
            }, ct);
        if (!Success(finish)) throw new InvalidOperationException("Facebook não confirmou a publicação do Reels");
        return new PublishedPost(videoId, null);
    }

    private sealed record ReelStatus(string VideoStatus, string PublishStatus, string? Error);

    /// <summary><c>status.video_status</c>, <c>status.publishing_phase.publish_status</c> e a mensagem de erro da fase que falhou.</summary>
    private async Task<ReelStatus> StatusAsync(SocialAccount account, string videoId, CancellationToken ct)
    {
        using var doc = await GraphApi.SendAsync(_factory.CreateClient("meta"), HttpMethod.Get,
            $"{_version}/{videoId}?fields=status", account.AccessToken, null, ct);
        if (!doc.RootElement.TryGetProperty("status", out var s) || s.ValueKind != JsonValueKind.Object)
            return new ReelStatus("", "", null);

        var video = Text(s, "video_status");
        var publish = s.TryGetProperty("publishing_phase", out var pp) && pp.ValueKind == JsonValueKind.Object ? Text(pp, "publish_status") : "";
        string? error = null;
        foreach (var phase in new[] { "uploading_phase", "processing_phase", "publishing_phase" })
            if (s.TryGetProperty(phase, out var p) && p.ValueKind == JsonValueKind.Object &&
                p.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.Object && Text(err, "message") is { Length: > 0 } m)
            {
                error = m;
                break;
            }
        return new ReelStatus(video, publish, error);
    }

    private static string Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static bool Success(JsonDocument doc) =>
        doc.RootElement.TryGetProperty("success", out var v) && v.ValueKind == JsonValueKind.True;
}

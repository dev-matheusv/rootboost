using Microsoft.Extensions.Options;
using RootBoost.Application.Publishing;
using RootBoost.Domain;
using RootBoost.Infrastructure.Tracking;

namespace RootBoost.Infrastructure.Publishing;

/// <summary>
/// Publica no Instagram pela API com login do Facebook. Conferido na doc "Content Publishing" em 24/10/2026:
/// 1) <c>POST /{ig-id}/media</c> com <c>image_url</c> ou <c>video_url</c> + <c>media_type=REELS</c> e <c>caption</c> → id do container;
/// 2) <c>GET /{container}?fields=status_code</c> (EXPIRED, ERROR, FINISHED, IN_PROGRESS, PUBLISHED), no máximo
///    uma vez por minuto por até 5 minutos; 3) <c>POST /{ig-id}/media_publish</c> com <c>creation_id</c> → id da mídia;
/// 4) <c>GET /{media}?fields=permalink</c>. Limite da conta: 100 posts por API em 24 h.
/// </summary>
public sealed class InstagramPublisher : ISocialPublisher
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);
    public const int MaxPolls = 5;

    private static readonly string[] VideoExtensions = { ".mp4", ".mov" };

    private readonly IHttpClientFactory _factory;
    private readonly SocialAccounts _accounts;
    private readonly string _version;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public InstagramPublisher(IHttpClientFactory factory, SocialAccounts accounts, IOptions<MetaOptions> meta)
        : this(factory, accounts, meta, Task.Delay) { }

    public InstagramPublisher(IHttpClientFactory factory, SocialAccounts accounts, IOptions<MetaOptions> meta,
        Func<TimeSpan, CancellationToken, Task> delay)
    {
        _factory = factory;
        _accounts = accounts;
        _version = meta.Value.ApiVersion;
        _delay = delay;
    }

    public SocialChannel Channel => SocialChannel.Instagram;

    public static bool IsVideo(string mediaUrl) =>
        VideoExtensions.Any(ext => new Uri(mediaUrl).AbsolutePath.EndsWith(ext, StringComparison.OrdinalIgnoreCase));

    public async Task<string> PrepareAsync(Publication publication, CancellationToken ct = default)
    {
        var account = _accounts.For(publication.BrandKey, Channel);
        var form = new Dictionary<string, string> { ["caption"] = publication.Caption };
        if (IsVideo(publication.MediaUrl))
        {
            form["media_type"] = "REELS";
            form["video_url"] = publication.MediaUrl;
            // Conferido na referência IG User Media em 27/10/2026: cover_url (JPEG público) vence thumb_offset (ms).
            form["share_to_feed"] = publication.ShareToFeed ? "true" : "false";
            if (publication.CoverUrl is { } cover) form["cover_url"] = cover;
            else if (publication.ThumbOffsetMs is { } offset) form["thumb_offset"] = offset.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        else
        {
            form["image_url"] = publication.MediaUrl;
        }

        using var doc = await GraphApi.SendAsync(_factory.CreateClient("meta"), HttpMethod.Post,
            $"{_version}/{account.AccountId}/media", account.AccessToken, form, ct);
        var id = doc.Str("id");
        return id.Length > 0 ? id : throw new InvalidOperationException("Instagram não devolveu o id do container");
    }

    /// <summary>
    /// Container já PUBLISHED = o processo caiu depois de publicar. Devolve o próprio container como referência
    /// (a doc não liga container a mídia), o que basta pra não publicar de novo.
    /// </summary>
    public async Task<PublishedPost?> FindPublishedAsync(Publication publication, string externalId, CancellationToken ct = default)
    {
        var account = _accounts.For(publication.BrandKey, Channel);
        return await StatusAsync(account, externalId, ct) == "PUBLISHED" ? new PublishedPost(externalId, null) : null;
    }

    public async Task<PublishedPost> PublishAsync(Publication publication, string externalId, CancellationToken ct = default)
    {
        var account = _accounts.For(publication.BrandKey, Channel);
        var http = _factory.CreateClient("meta");

        for (var poll = 1; ; poll++)
        {
            var status = await StatusAsync(account, externalId, ct);
            if (status == "FINISHED") break;
            if (status is "ERROR" or "EXPIRED")
                throw new InvalidOperationException($"container do Instagram em {status}: mídia recusada ou vencida, crie de novo");
            if (status == "PUBLISHED") return new PublishedPost(externalId, null);
            if (poll >= MaxPolls)
                throw new InvalidOperationException("o Instagram ainda está processando a mídia; tenta de novo na próxima rodada");
            await _delay(PollInterval, ct);
        }

        using var published = await GraphApi.SendAsync(http, HttpMethod.Post, $"{_version}/{account.AccountId}/media_publish",
            account.AccessToken, new Dictionary<string, string> { ["creation_id"] = externalId }, ct);
        var mediaId = published.Str("id");
        if (mediaId.Length == 0) throw new InvalidOperationException("Instagram não devolveu o id da mídia publicada");

        // Link é bônus: se falhar, o post já está no ar e não pode virar erro (senão o job tentaria de novo).
        string? permalink = null;
        try
        {
            using var media = await GraphApi.SendAsync(http, HttpMethod.Get, $"{_version}/{mediaId}?fields=permalink",
                account.AccessToken, null, ct);
            permalink = media.Str("permalink") is { Length: > 0 } p ? p : null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException) { }

        return new PublishedPost(mediaId, permalink);
    }

    private async Task<string> StatusAsync(SocialAccount account, string containerId, CancellationToken ct)
    {
        using var doc = await GraphApi.SendAsync(_factory.CreateClient("meta"), HttpMethod.Get,
            $"{_version}/{containerId}?fields=status_code", account.AccessToken, null, ct);
        return doc.Str("status_code");
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using RootBoost.Domain;
using RootBoost.Infrastructure.Publishing;
using RootBoost.Infrastructure.Tracking;

namespace RootBoost.Api.IntegrationTests;

/// <summary>Publicadores reais (Instagram e Página do Facebook) contra respostas no formato da doc oficial.</summary>
public class MetaPublishersTests
{
    private sealed class Graph : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path, string Query, string Body, string? Auth)> Calls { get; } = new();
        public List<string?> FileUrls { get; } = new();
        public Queue<string> ReelStatuses { get; } = new();

        private string ReelStatus()
        {
            var s = ReelStatuses.Count > 0 ? ReelStatuses.Dequeue() : "ready";
            var publish = s == "published" ? "published" : "draft";
            var video = s is "published" or "in_progress" ? "ready" : s;
            var phase = s == "published" ? "complete" : s == "in_progress" ? "in_progress" : "not_started";
            var error = s == "error" ? ""","processing_phase":{"status":"error","error":{"message":"Resolution too low"}}""" : "";
            return $$"""{"status":{"video_status":"{{video}}","publishing_phase":{"status":"{{phase}}","publish_status":"{{publish}}"}{{error}}},"id":"vid-7"}""";
        }
        public Queue<string> Statuses { get; } = new();
        public string? PhotoResponse { get; set; } = """{"id":"55","post_id":"200_55"}""";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            var body = req.Content is null ? "" : await req.Content.ReadAsStringAsync(ct);
            var path = req.RequestUri!.AbsolutePath;
            Calls.Add((req.Method, path, req.RequestUri.Query, body, req.Headers.Authorization?.ToString()));
            if (req.Headers.TryGetValues("file_url", out var fileUrl)) FileUrls.Add(fileUrl.Single());

            string json =
                path.EndsWith("/media") ? """{"id":"cont-1"}""" :
                path.EndsWith("/media_publish") ? """{"id":"media-9"}""" :
                path.EndsWith("/photos") ? PhotoResponse! :
                path.EndsWith("/video_reels") && body.Contains("upload_phase=start") ? """{"video_id":"vid-7","upload_url":"https://rupload.facebook.com/video-upload/vid-7"}""" :
                path.EndsWith("/video_reels") && body.Contains("upload_phase=finish") ? """{"success":true}""" :
                req.RequestUri.Host == "rupload.facebook.com" ? """{"success":true}""" :
                req.RequestUri.Query.Contains("fields=status") && !req.RequestUri.Query.Contains("status_code") ? ReelStatus() :
                req.RequestUri.Query.Contains("status_code") ? $$"""{"status_code":"{{(Statuses.Count > 0 ? Statuses.Dequeue() : "FINISHED")}}","id":"cont-1"}""" :
                req.RequestUri.Query.Contains("permalink") ? """{"permalink":"https://www.instagram.com/reel/abc/","id":"media-9"}""" :
                """{"error":{"message":"rota inesperada","code":100}}""";
            var status = json.StartsWith("{\"error\"") ? HttpStatusCode.BadRequest : HttpStatusCode.OK;
            return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class Factory(HttpMessageHandler h) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(h, disposeHandler: false) { BaseAddress = new Uri("https://graph.facebook.com/") };
    }

    private static SocialAccounts Accounts() => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Social:Accounts:0:Brand"] = "medlycare", ["Social:Accounts:0:Channel"] = "Instagram",
        ["Social:Accounts:0:AccountId"] = "1784", ["Social:Accounts:0:AccessToken"] = "tok-ig",
        ["Social:Accounts:1:Brand"] = "medlycare", ["Social:Accounts:1:Channel"] = "Facebook",
        ["Social:Accounts:1:AccountId"] = "200", ["Social:Accounts:1:AccessToken"] = "tok-fb",
    }).Build());

    private static readonly IOptions<MetaOptions> Meta = Options.Create(new MetaOptions { ApiVersion = "v26.0" });
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static Publication Post(SocialChannel channel, string media, string brand = "medlycare") =>
        new(brand, channel, media, "Agenda para fisioterapia", Now, Now);

    private static (InstagramPublisher Pub, List<TimeSpan> Waits) Instagram(Graph g)
    {
        var waits = new List<TimeSpan>();
        return (new InstagramPublisher(new Factory(g), Accounts(), Meta, (d, _) => { waits.Add(d); return Task.CompletedTask; }), waits);
    }

    [Fact]
    public async Task Instagram_reel_creates_container_waits_for_processing_and_publishes()
    {
        var g = new Graph();
        g.Statuses.Enqueue("IN_PROGRESS");
        g.Statuses.Enqueue("FINISHED");
        var (ig, waits) = Instagram(g);
        var post = Post(SocialChannel.Instagram, "https://cdn.exemplo.com/reel.mp4");

        var container = await ig.PrepareAsync(post);
        var published = await ig.PublishAsync(post, container);

        Assert.Equal("cont-1", container);
        var create = g.Calls[0];
        Assert.Equal("/v26.0/1784/media", create.Path);
        Assert.Contains("media_type=REELS", create.Body);
        Assert.Contains("video_url=https%3A%2F%2Fcdn.exemplo.com%2Freel.mp4", create.Body);
        Assert.Contains("caption=Agenda", create.Body);
        Assert.Equal("Bearer tok-ig", create.Auth);
        Assert.DoesNotContain(g.Calls, c => c.Query.Contains("tok-"));

        Assert.Single(waits); // um IN_PROGRESS, uma espera de 1 minuto
        Assert.Equal(InstagramPublisher.PollInterval, waits[0]);
        Assert.Contains(g.Calls, c => c.Path == "/v26.0/1784/media_publish" && c.Body == "creation_id=cont-1");
        Assert.Equal("media-9", published.ExternalId);
        Assert.Equal("https://www.instagram.com/reel/abc/", published.Url);
    }

    [Fact]
    public async Task Instagram_reel_sends_cover_and_share_to_feed()
    {
        var g = new Graph();
        var (ig, _) = Instagram(g);
        var post = Post(SocialChannel.Instagram, "https://cdn.exemplo.com/reel.mp4");
        post.SetReelOptions("https://cdn.exemplo.com/capa.jpg", 1500, false, DateTimeOffset.UtcNow);

        await ig.PrepareAsync(post);

        Assert.Contains("share_to_feed=false", g.Calls[0].Body);
        Assert.Contains("cover_url=https%3A%2F%2Fcdn.exemplo.com%2Fcapa.jpg", g.Calls[0].Body);
        Assert.DoesNotContain("thumb_offset", g.Calls[0].Body); // capa vence o quadro
    }

    [Fact]
    public async Task Instagram_reel_without_cover_uses_frame_and_feed_by_default()
    {
        var g = new Graph();
        var (ig, _) = Instagram(g);
        var post = Post(SocialChannel.Instagram, "https://cdn.exemplo.com/reel.mp4");
        post.SetReelOptions(null, 2000, true, DateTimeOffset.UtcNow);

        await ig.PrepareAsync(post);

        Assert.Contains("share_to_feed=true", g.Calls[0].Body);
        Assert.Contains("thumb_offset=2000", g.Calls[0].Body);
        Assert.DoesNotContain("cover_url", g.Calls[0].Body);
    }

    [Fact]
    public async Task Instagram_image_uses_image_url()
    {
        var g = new Graph();
        var (ig, _) = Instagram(g);
        await ig.PrepareAsync(Post(SocialChannel.Instagram, "https://cdn.exemplo.com/post.jpg"));
        Assert.Contains("image_url=", g.Calls[0].Body);
        Assert.DoesNotContain("media_type", g.Calls[0].Body);
        Assert.DoesNotContain("share_to_feed", g.Calls[0].Body);
    }

    [Fact]
    public async Task Instagram_container_error_fails_and_long_processing_gives_up_after_five_polls()
    {
        var g = new Graph();
        g.Statuses.Enqueue("ERROR");
        var (ig, _) = Instagram(g);
        var post = Post(SocialChannel.Instagram, "https://cdn.exemplo.com/reel.mp4");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => ig.PublishAsync(post, "cont-1"));
        Assert.Contains("ERROR", ex.Message);

        var slow = new Graph();
        for (var i = 0; i < 10; i++) slow.Statuses.Enqueue("IN_PROGRESS");
        var (ig2, waits) = Instagram(slow);
        await Assert.ThrowsAsync<InvalidOperationException>(() => ig2.PublishAsync(post, "cont-1"));
        Assert.Equal(InstagramPublisher.MaxPolls - 1, waits.Count);
        Assert.DoesNotContain(slow.Calls, c => c.Path.EndsWith("/media_publish"));
    }

    [Fact]
    public async Task Instagram_already_published_container_is_recognized()
    {
        var g = new Graph();
        g.Statuses.Enqueue("PUBLISHED");
        var (ig, _) = Instagram(g);
        var found = await ig.FindPublishedAsync(Post(SocialChannel.Instagram, "https://cdn.exemplo.com/a.jpg"), "cont-1");
        Assert.NotNull(found);
        Assert.DoesNotContain(g.Calls, c => c.Path.EndsWith("/media_publish"));
    }

    [Fact]
    public async Task Brand_without_account_fails_clearly()
    {
        var (ig, _) = Instagram(new Graph());
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ig.PrepareAsync(Post(SocialChannel.Instagram, "https://cdn.exemplo.com/a.jpg", brand: "rootboost-loja")));
        Assert.Contains("rootboost-loja", ex.Message);
    }

    [Fact]
    public async Task Facebook_photo_posts_url_and_caption_and_links_the_post()
    {
        var g = new Graph();
        var fb = new FacebookPagePublisher(new Factory(g), Accounts(), Meta);
        var post = Post(SocialChannel.Facebook, "https://cdn.exemplo.com/post.jpg");

        var reference = await fb.PrepareAsync(post);
        var published = await fb.PublishAsync(post, reference);

        var call = Assert.Single(g.Calls);
        Assert.Equal("/v26.0/200/photos", call.Path);
        Assert.Contains("url=https%3A%2F%2Fcdn.exemplo.com%2Fpost.jpg", call.Body);
        Assert.Contains("caption=Agenda", call.Body);
        Assert.Equal("Bearer tok-fb", call.Auth);
        Assert.Equal("200_55", published.ExternalId);
        Assert.Equal("https://www.facebook.com/200_55", published.Url);
    }

    private static (FacebookPagePublisher Pub, List<TimeSpan> Waits) Facebook(Graph g)
    {
        var waits = new List<TimeSpan>();
        return (new FacebookPagePublisher(new Factory(g), Accounts(), Meta, (d, _) => { waits.Add(d); return Task.CompletedTask; }), waits);
    }

    [Fact]
    public async Task Facebook_reel_starts_uploads_by_link_finishes_and_follows_until_published()
    {
        var g = new Graph();
        g.ReelStatuses.Enqueue("upload_complete"); // processamento só começa depois do finish (28/10)
        g.ReelStatuses.Enqueue("published");
        var (fb, waits) = Facebook(g);
        var post = Post(SocialChannel.Facebook, "https://cdn.exemplo.com/reel.mp4");

        var videoId = await fb.PrepareAsync(post);
        var published = await fb.PublishAsync(post, videoId);

        Assert.Equal("vid-7", videoId);
        Assert.Equal("/v26.0/200/video_reels", g.Calls[0].Path);
        Assert.Equal("upload_phase=start", g.Calls[0].Body);
        Assert.Equal("Bearer tok-fb", g.Calls[0].Auth);

        Assert.Equal("/video-upload/v26.0/vid-7", g.Calls[1].Path);
        Assert.Equal("OAuth tok-fb", g.Calls[1].Auth);
        Assert.Equal("https://cdn.exemplo.com/reel.mp4", Assert.Single(g.FileUrls)); // o Facebook busca pelo link, sem bytes
        Assert.Equal("", g.Calls[1].Body);

        Assert.Single(waits);
        Assert.Equal(FacebookPagePublisher.PollInterval, waits[0]);
        var finish = g.Calls.Single(c => c.Body.Contains("upload_phase=finish"));
        Assert.True(g.Calls.IndexOf(finish) < g.Calls.Count - 1); // finish antes de acompanhar
        Assert.Equal("/v26.0/200/video_reels", finish.Path);
        Assert.Contains("video_id=vid-7", finish.Body);
        Assert.Contains("upload_phase=finish", finish.Body);
        Assert.Contains("video_state=PUBLISHED", finish.Body);
        Assert.Contains("description=Agenda", finish.Body);
        Assert.DoesNotContain(g.Calls, c => c.Query.Contains("tok-"));
        Assert.Equal("vid-7", published.ExternalId);
    }

    [Fact]
    public async Task Facebook_reel_retry_while_meta_is_publishing_does_not_finish_twice()
    {
        var g = new Graph();
        for (var i = 0; i < 6; i++) g.ReelStatuses.Enqueue("in_progress");
        var (fb, _) = Facebook(g);
        var post = Post(SocialChannel.Facebook, "https://cdn.exemplo.com/reel.mp4");

        var published = await fb.PublishAsync(post, "vid-7"); // ainda processando: aceito, a Meta conclui sozinha
        Assert.Equal("vid-7", published.ExternalId);
        Assert.DoesNotContain(g.Calls, c => c.Body.Contains("upload_phase=finish"));
    }

    [Fact]
    public async Task Facebook_reel_already_published_is_found_and_not_finished_again()
    {
        var g = new Graph();
        g.ReelStatuses.Enqueue("published");
        var (fb, _) = Facebook(g);
        var post = Post(SocialChannel.Facebook, "https://cdn.exemplo.com/reel.mp4");

        Assert.NotNull(await fb.FindPublishedAsync(post, "vid-7"));
        Assert.DoesNotContain(g.Calls, c => c.Body.Contains("upload_phase=finish"));
        Assert.Null(await fb.FindPublishedAsync(post, $"fb-pending-{post.Id:N}")); // foto: sem como saber
    }

    [Fact]
    public async Task Facebook_reel_processing_error_fails_with_the_reason()
    {
        var g = new Graph();
        g.ReelStatuses.Enqueue("error");
        var (fb, _) = Facebook(g);
        var post = Post(SocialChannel.Facebook, "https://cdn.exemplo.com/reel.mp4");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => fb.PublishAsync(post, "vid-7"));
        Assert.Contains("Resolution too low", ex.Message);
        Assert.DoesNotContain(g.Calls, c => c.Body.Contains("upload_phase=finish"));
    }
}

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
        public Queue<string> Statuses { get; } = new();
        public string? PhotoResponse { get; set; } = """{"id":"55","post_id":"200_55"}""";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            var body = req.Content is null ? "" : await req.Content.ReadAsStringAsync(ct);
            var path = req.RequestUri!.AbsolutePath;
            Calls.Add((req.Method, path, req.RequestUri.Query, body, req.Headers.Authorization?.ToString()));

            string json =
                path.EndsWith("/media") ? """{"id":"cont-1"}""" :
                path.EndsWith("/media_publish") ? """{"id":"media-9"}""" :
                path.EndsWith("/photos") ? PhotoResponse! :
                req.RequestUri.Query.Contains("status_code") ? $$"""{"status_code":"{{(Statuses.Count > 0 ? Statuses.Dequeue() : "FINISHED")}}","id":"cont-1"}""" :
                req.RequestUri.Query.Contains("permalink") ? """{"permalink":"https://www.instagram.com/reel/abc/","id":"media-9"}""" :
                """{"error":{"message":"rota inesperada","code":100}}""";
            var status = json.Contains("\"error\"") ? HttpStatusCode.BadRequest : HttpStatusCode.OK;
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
    public async Task Instagram_image_uses_image_url()
    {
        var g = new Graph();
        var (ig, _) = Instagram(g);
        await ig.PrepareAsync(Post(SocialChannel.Instagram, "https://cdn.exemplo.com/post.jpg"));
        Assert.Contains("image_url=", g.Calls[0].Body);
        Assert.DoesNotContain("media_type", g.Calls[0].Body);
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

    [Fact]
    public async Task Facebook_video_is_refused_before_any_call()
    {
        var g = new Graph();
        var fb = new FacebookPagePublisher(new Factory(g), Accounts(), Meta);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fb.PrepareAsync(Post(SocialChannel.Facebook, "https://cdn.exemplo.com/reel.mp4")));
        Assert.Empty(g.Calls);
    }
}

public class FacebookVideoApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public FacebookVideoApiTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Creating_a_facebook_video_post_is_rejected_up_front()
    {
        var res = await _factory.CreateClient().PostAsJsonAsync("/admin/brands/medlycare/publications",
            new { channel = "facebook", mediaUrl = "https://cdn.exemplo.com/reel.mp4", caption = "x", scheduledFor = DateTimeOffset.UtcNow });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }
}

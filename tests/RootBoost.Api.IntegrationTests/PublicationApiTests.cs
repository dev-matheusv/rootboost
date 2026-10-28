using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace RootBoost.Api.IntegrationTests;

/// <summary>Publicação própria pela API, com o publicador de mentira (Social:Live=false). SQLite e Postgres.</summary>
public abstract class PublicationApiTestsBase
{
    protected abstract HttpClient Client();

    private const string Base = "/admin/brands/medlycare/publications";

    private static async Task<JsonElement> Create(HttpClient client, string caption, DateTimeOffset when)
    {
        var res = await client.PostAsJsonAsync(Base, new { channel = "instagram", mediaUrl = "https://cdn.exemplo.com/post.jpg", caption, scheduledFor = when });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<JsonElement> Get(HttpClient client, string id) =>
        (await client.GetFromJsonAsync<JsonElement>(Base)).EnumerateArray().Single(p => p.GetProperty("id").GetString() == id);

    [Fact]
    public async Task Approved_post_is_published_by_the_job_and_then_locked()
    {
        var client = Client();
        var id = (await Create(client, $"Agenda para fisioterapia {Guid.NewGuid():N}", DateTimeOffset.UtcNow.AddMinutes(-1))).GetProperty("id").GetString()!;

        var approve = await client.PostAsync($"{Base}/{id}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        Assert.Equal("Approved", (await approve.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("publication").GetProperty("status").GetString());

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/admin/publications/run", null)).StatusCode);

        var published = await Get(client, id);
        Assert.Equal("Published", published.GetProperty("status").GetString());
        Assert.StartsWith("fake-post-", published.GetProperty("externalId").GetString());

        var edit = await client.PutAsJsonAsync($"{Base}/{id}/caption", new { caption = "outra legenda" });
        Assert.Equal(HttpStatusCode.BadRequest, edit.StatusCode);
    }

    [Fact]
    public async Task Approval_can_be_undone_before_it_goes_live()
    {
        var client = Client();
        var id = (await Create(client, $"Agenda para fisioterapia {Guid.NewGuid():N}", DateTimeOffset.UtcNow.AddHours(1))).GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"{Base}/{id}/approve", null)).StatusCode);

        var undo = await client.PostAsync($"{Base}/{id}/unapprove", null);
        Assert.Equal(HttpStatusCode.OK, undo.StatusCode);
        Assert.Equal("Draft", (await Get(client, id)).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync($"{Base}/{id}/unapprove", null)).StatusCode);
    }

    [Fact]
    public async Task Reel_cover_and_feed_option_are_saved_and_only_valid_for_instagram_video()
    {
        var client = Client();
        var res = await client.PostAsJsonAsync(Base, new { channel = "instagram", mediaUrl = "https://cdn.exemplo.com/reel.mp4",
            caption = $"Reel {Guid.NewGuid():N}", scheduledFor = DateTimeOffset.UtcNow.AddHours(1) });
        var id = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        Assert.True((await Get(client, id)).GetProperty("shareToFeed").GetBoolean());

        var set = await client.PutAsJsonAsync($"{Base}/{id}/reel", new { coverUrl = "https://cdn.exemplo.com/capa.jpg", shareToFeed = false });
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        var saved = await Get(client, id); // relido do banco: false precisa persistir de verdade
        Assert.False(saved.GetProperty("shareToFeed").GetBoolean());
        Assert.Equal("https://cdn.exemplo.com/capa.jpg", saved.GetProperty("coverUrl").GetString());

        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PutAsJsonAsync($"{Base}/{id}/reel", new { coverUrl = "https://cdn.exemplo.com/capa.png" })).StatusCode);
        var image = (await Create(client, $"Foto {Guid.NewGuid():N}", DateTimeOffset.UtcNow.AddHours(1))).GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PutAsJsonAsync($"{Base}/{image}/reel", new { thumbOffsetMs = 1000 })).StatusCode);
    }

    [Fact]
    public async Task Blocked_caption_cannot_be_approved_and_stays_draft()
    {
        var client = Client();
        var id = (await Create(client, "Sua clínica — sem falta", DateTimeOffset.UtcNow.AddMinutes(-1))).GetProperty("id").GetString()!;

        var approve = await client.PostAsync($"{Base}/{id}/approve", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, approve.StatusCode);
        var body = await approve.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(body.GetProperty("checklist").EnumerateArray(), f => f.GetProperty("blocking").GetBoolean());

        await client.PostAsync("/admin/publications/run", null);
        Assert.Equal("Draft", (await Get(client, id)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Editing_after_approval_returns_to_draft_and_is_not_published()
    {
        var client = Client();
        var id = (await Create(client, $"Agenda para fisioterapia {Guid.NewGuid():N}", DateTimeOffset.UtcNow.AddMinutes(-1))).GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"{Base}/{id}/approve", null)).StatusCode);

        var edit = await client.PutAsJsonAsync($"{Base}/{id}/caption", new { caption = "Agenda revisada para fisioterapia" });
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        var edited = await edit.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Draft", edited.GetProperty("status").GetString());
        Assert.Equal(2, edited.GetProperty("captionVersion").GetInt32());

        await client.PostAsync("/admin/publications/run", null);
        Assert.Equal("Draft", (await Get(client, id)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Mode_reports_dry_run_by_default()
    {
        var mode = await Client().GetFromJsonAsync<JsonElement>("/admin/publications/mode");
        Assert.False(mode.GetProperty("live").GetBoolean());
    }

    [Fact]
    public async Task Invalid_input_is_rejected()
    {
        var client = Client();
        var badChannel = await client.PostAsJsonAsync(Base, new { channel = "orkut", mediaUrl = "https://cdn.exemplo.com/a.jpg", caption = "x", scheduledFor = DateTimeOffset.UtcNow });
        Assert.Equal(HttpStatusCode.BadRequest, badChannel.StatusCode);

        var httpMedia = await client.PostAsJsonAsync(Base, new { channel = "instagram", mediaUrl = "http://cdn.exemplo.com/a.jpg", caption = "x", scheduledFor = DateTimeOffset.UtcNow });
        Assert.Equal(HttpStatusCode.BadRequest, httpMedia.StatusCode);

        var unknownBrand = await client.PostAsJsonAsync("/admin/brands/nao-existe/publications", new { channel = "instagram", mediaUrl = "https://cdn.exemplo.com/a.jpg", caption = "x", scheduledFor = DateTimeOffset.UtcNow });
        Assert.Equal(HttpStatusCode.NotFound, unknownBrand.StatusCode);
    }
}

public class PublicationApiPostgresTests : PublicationApiTestsBase, IClassFixture<PostgresWebApplicationFactory>
{
    private readonly PostgresWebApplicationFactory _factory;
    public PublicationApiPostgresTests(PostgresWebApplicationFactory factory) => _factory = factory;
    protected override HttpClient Client() => _factory.CreateClient();

}

public class PublicationEditApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string Base = "/admin/brands/medlycare/publications";
    private readonly CustomWebApplicationFactory _factory;
    public PublicationEditApiTests(CustomWebApplicationFactory factory) => _factory = factory;

    private async Task<string> Create(HttpClient c, string channel, string media, DateTimeOffset when)
    {
        var res = await c.PostAsJsonAsync(Base, new { channel, mediaUrl = media, caption = $"Agenda para fisioterapia {Guid.NewGuid():N}", scheduledFor = when });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task Media_of_an_approved_post_can_be_swapped_and_it_returns_to_draft()
    {
        var c = _factory.CreateClient();
        var id = await Create(c, "instagram", "https://cdn.exemplo.com/velho.mp4", DateTimeOffset.UtcNow.AddDays(2));
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsync($"{Base}/{id}/approve", null)).StatusCode);

        var res = await c.PutAsJsonAsync($"{Base}/{id}/media", new { mediaUrl = "https://cdn.exemplo.com/novo.mp4" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Draft", body.GetProperty("status").GetString());
        Assert.Equal("https://cdn.exemplo.com/novo.mp4", body.GetProperty("mediaUrl").GetString());

        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync($"{Base}/{id}/media", new { mediaUrl = "http://inseguro/x.mp4" })).StatusCode);
    }

    [Fact]
    public async Task Draft_can_be_deleted_but_a_published_post_cannot()
    {
        var c = _factory.CreateClient();
        var draft = await Create(c, "instagram", "https://cdn.exemplo.com/a.jpg", DateTimeOffset.UtcNow.AddDays(1));
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"{Base}/{draft}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.DeleteAsync($"{Base}/{draft}")).StatusCode);

        var live = await Create(c, "instagram", "https://cdn.exemplo.com/b.jpg", DateTimeOffset.UtcNow.AddMinutes(-1));
        await c.PostAsync($"{Base}/{live}/approve", null);
        await c.PostAsync("/admin/publications/run", null);
        Assert.Equal(HttpStatusCode.Conflict, (await c.DeleteAsync($"{Base}/{live}")).StatusCode);
    }

    [Fact]
    public async Task Facebook_post_can_be_a_reel()
    {
        var c = _factory.CreateClient();
        var id = await Create(c, "facebook", "https://cdn.exemplo.com/a.jpg", DateTimeOffset.UtcNow.AddDays(1));
        Assert.Equal(HttpStatusCode.OK, (await c.PutAsJsonAsync($"{Base}/{id}/media", new { mediaUrl = "https://cdn.exemplo.com/v.mp4" })).StatusCode);
        Assert.NotNull(await Create(c, "facebook", "https://cdn.exemplo.com/reel.mp4", DateTimeOffset.UtcNow.AddDays(1)));
    }
}

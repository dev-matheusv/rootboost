using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;

namespace RootBoost.Api.IntegrationTests;

/// <summary>Upload de mídia (RB-33): sobe, serve no link público com os mesmos bytes, valida tipo e tamanho.</summary>
public class MediaApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string Base = "/admin/brands/medlycare/media";
    private readonly CustomWebApplicationFactory _factory;
    public MediaApiTests(CustomWebApplicationFactory factory) => _factory = factory;

    private static MultipartFormDataContent File(string name, byte[] bytes)
    {
        var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(content, "file", name);
        return form;
    }

    [Fact]
    public async Task Uploaded_video_is_served_on_a_stable_public_link_with_the_same_bytes()
    {
        var client = _factory.CreateClient();
        var bytes = Enumerable.Range(0, 5000).Select(i => (byte)(i % 251)).ToArray();

        var res = await client.PostAsync(Base, File("Reel Estética #6.mp4", bytes));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var asset = await res.Content.ReadFromJsonAsync<JsonElement>();
        var url = asset.GetProperty("url").GetString()!;
        Assert.True(asset.GetProperty("isVideo").GetBoolean());
        Assert.EndsWith(".mp4", url);                       // o publicador do Instagram decide REELS pela extensão
        Assert.DoesNotContain(" ", url);

        var file = await client.GetAsync(new Uri(url).PathAndQuery);
        Assert.Equal(HttpStatusCode.OK, file.StatusCode);
        Assert.Equal("video/mp4", file.Content.Headers.ContentType?.MediaType);
        Assert.Equal(bytes.Length, file.Content.Headers.ContentLength);
        Assert.Equal(bytes, await file.Content.ReadAsByteArrayAsync());

        var list = await client.GetFromJsonAsync<JsonElement>(Base);
        Assert.Contains(list.EnumerateArray(), m => m.GetProperty("url").GetString() == url);
    }

    [Fact]
    public async Task Unsupported_type_and_unknown_brand_are_rejected()
    {
        var client = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync(Base, File("virus.exe", new byte[] { 1, 2, 3 }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync(Base, File("vazio.jpg", Array.Empty<byte>()))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.PostAsync("/admin/brands/nao-existe/media", File("a.jpg", new byte[] { 1 }))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/media/{Guid.NewGuid():N}/a.jpg")).StatusCode);
    }

    [Fact]
    public async Task File_above_the_limit_is_rejected()
    {
        var client = _factory.WithWebHostBuilder(b => b.UseSetting("Media:MaxBytes", "100")).CreateClient();
        var res = await client.PostAsync(Base, File("grande.jpg", new byte[500]));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task File_used_by_an_unpublished_post_cannot_be_deleted()
    {
        var client = _factory.CreateClient();
        var asset = await (await client.PostAsync(Base, File("post.jpg", new byte[] { 9, 9, 9 }))).Content.ReadFromJsonAsync<JsonElement>();
        var id = asset.GetProperty("id").GetString();
        var url = asset.GetProperty("url").GetString()!;

        var post = await client.PostAsJsonAsync("/admin/brands/medlycare/publications",
            new { channel = "instagram", mediaUrl = url, caption = "Agenda para fisioterapia", scheduledFor = DateTimeOffset.UtcNow.AddDays(3) });
        Assert.Equal(HttpStatusCode.Created, post.StatusCode);

        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"{Base}/{id}")).StatusCode);

        var loose = await (await client.PostAsync(Base, File("solto.png", new byte[] { 1 }))).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"{Base}/{loose.GetProperty("id").GetString()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(new Uri(loose.GetProperty("url").GetString()!).PathAndQuery)).StatusCode);
    }
}

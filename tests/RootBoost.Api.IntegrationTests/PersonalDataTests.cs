using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RootBoost.Domain;

namespace RootBoost.Api.IntegrationTests;

/// <summary>Proteção de dado pessoal no acervo (pedido do Matheus, 29/10): marcar, borrar, travar roteiro e render.</summary>
public class PersonalDataTests : IClassFixture<StudioAiWebApplicationFactory>
{
    private readonly StudioAiWebApplicationFactory _factory;
    public PersonalDataTests(StudioAiWebApplicationFactory factory) => _factory = factory;

    private static async Task<JsonElement> Upload(HttpClient c, string brand, string name)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(TestImages.Png(200, 120)), "file", name);
        var res = await c.PostAsync($"/admin/brands/{brand}/media", form);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task Real_personal_data_is_blurred_and_the_public_link_never_serves_the_original()
    {
        var c = _factory.CreateClient();
        var up = await Upload(c, BrandKeys.Store, "atestado-cpf.png");
        var id = up.GetProperty("id").GetString();
        var described = await (await c.PostAsync($"/admin/brands/{BrandKeys.Store}/media/{id}/describe", null)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Real", described.GetProperty("personalData").GetString());
        Assert.True(described.GetProperty("redacted").GetBoolean());

        var publicRes = await _factory.CreateClient().GetAsync(new Uri(described.GetProperty("url").GetString()!).PathAndQuery);
        Assert.Equal(HttpStatusCode.OK, publicRes.StatusCode);
        Assert.Equal("image/jpeg", publicRes.Content.Headers.ContentType?.MediaType); // a borrada, não o PNG original
        Assert.Equal("image/png", (await c.GetAsync($"/admin/brands/{BrandKeys.Store}/media/{id}/original")).Content.Headers.ContentType?.MediaType);

        // Sem área marcada, item real fica sem versão borrada: o link público some.
        var cleared = await (await c.PutAsJsonAsync($"/admin/brands/{BrandKeys.Store}/media/{id}/redaction", new { boxes = Array.Empty<object>() }))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(cleared.GetProperty("redacted").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, (await _factory.CreateClient().GetAsync(new Uri(described.GetProperty("url").GetString()!).PathAndQuery)).StatusCode);

        // A pessoa marca de novo a área: volta a ter versão borrada.
        var redone = await (await c.PutAsJsonAsync($"/admin/brands/{BrandKeys.Store}/media/{id}/redaction",
            new { boxes = new[] { new { x = 0.2, y = 0.2, w = 0.3, h = 0.3 } } })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(redone.GetProperty("redacted").GetBoolean());
    }

    [Fact]
    public async Task Brand_that_only_uses_fictional_data_blocks_real_data_in_the_script_and_the_render()
    {
        var c = _factory.CreateClient();
        Assert.Equal("FictionalOnly", (await c.GetFromJsonAsync<JsonElement>("/admin/brands/medlycare/privacy")).GetProperty("policy").GetString());

        var up = await Upload(c, "medlycare", "receituario.png");
        var id = up.GetProperty("id").GetString()!;
        await c.PostAsync($"/admin/brands/medlycare/media/{id}/describe", null);
        var job = await (await c.PostAsJsonAsync("/admin/brands/medlycare/creative-jobs", new { kind = "organic", theme = "Receituário" }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var jobId = job.GetProperty("id").GetString();
        var written = await (await c.PostAsJsonAsync($"/admin/brands/medlycare/creative-jobs/{jobId}/write-script", new { })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("acervo-", written.GetRawText());

        // Descobriu-se que o receituário é de paciente real: mesmo borrado, não entra nesta marca.
        var marked = await (await c.PutAsJsonAsync($"/admin/brands/medlycare/media/{id}/redaction",
            new { status = "Real", boxes = new[] { new { x = 0.1, y = 0.1, w = 0.5, h = 0.2 } } })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(marked.GetProperty("redacted").GetBoolean());
        var render = await c.PostAsync($"/admin/brands/medlycare/creative-jobs/{jobId}/render", null);
        Assert.Equal(HttpStatusCode.BadRequest, render.StatusCode);
        Assert.Contains("só usa dado fictício", await render.Content.ReadAsStringAsync());

        // Job que já estava na fila quando a foto foi barrada: falha ao ser pego, com o motivo (não roda sem a foto).
        await c.PutAsJsonAsync($"/admin/brands/medlycare/media/{id}/redaction", new { status = "None", boxes = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsync($"/admin/brands/medlycare/creative-jobs/{jobId}/render", null)).StatusCode);
        await c.PutAsJsonAsync($"/admin/brands/medlycare/media/{id}/redaction", new { status = "Real", boxes = Array.Empty<object>() });
        var w = _factory.CreateClient();
        w.DefaultRequestHeaders.Add("X-Worker-Key", "chave-gravacao");
        for (var i = 0; i < 20; i++)
        {
            var claim = await w.PostAsJsonAsync("/worker/jobs/claim", new { workerId = "pc" });
            if (claim.StatusCode == HttpStatusCode.NoContent) break;
        }
        var failed = await c.GetFromJsonAsync<JsonElement>($"/admin/brands/medlycare/creative-jobs/{jobId}");
        Assert.Equal("Failed", failed.GetProperty("status").GetString());
        Assert.Contains("proteção de dado pessoal", failed.GetProperty("error").GetString());

        // E a IA nem vê o item no próximo roteiro.
        var again = await (await c.PostAsJsonAsync($"/admin/brands/medlycare/creative-jobs/{jobId}/write-script", new { })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.DoesNotContain($"acervo-{id.Replace("-", "")}", again.GetRawText());
    }
}

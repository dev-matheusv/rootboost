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

public class FinishedVideoTests : IClassFixture<StudioAiWebApplicationFactory>
{
    private readonly StudioAiWebApplicationFactory _factory;
    public FinishedVideoTests(StudioAiWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Studio_delivery_is_a_finished_video_and_never_a_clip()
    {
        var w = _factory.CreateClient();
        w.DefaultRequestHeaders.Add("X-Worker-Key", "chave-gravacao");
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent([0, 0, 0, 24, 0x66, 0x74, 0x79, 0x70]), "file", "reel-receitas.mp4");
        var up = await (await w.PostAsync("/worker/media/medlycare", form)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(up.GetProperty("finished").GetBoolean());

        var c = _factory.CreateClient();
        var id = up.GetProperty("id").GetString();
        var job = await (await c.PostAsJsonAsync("/admin/brands/medlycare/creative-jobs", new { kind = "organic", theme = "Receitas" })).Content.ReadFromJsonAsync<JsonElement>();
        var written = await (await c.PostAsJsonAsync($"/admin/brands/medlycare/creative-jobs/{job.GetProperty("id").GetString()}/write-script", new { }))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.DoesNotContain($"acervo-{id!.Replace("-", "")}", written.GetRawText());

        var raw = await (await c.PutAsJsonAsync($"/admin/brands/medlycare/media/{id}/finished", new { finished = false })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(raw.GetProperty("finished").GetBoolean());
    }
}

public class DemoBrandTests : IClassFixture<StudioAiWebApplicationFactory>
{
    private readonly StudioAiWebApplicationFactory _factory;
    public DemoBrandTests(StudioAiWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Demo_brand_can_be_reset_and_a_real_brand_never()
    {
        var c = _factory.CreateClient();
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(TestImages.Png(40, 30)), "file", "demo.png");
        Assert.Equal(HttpStatusCode.Created, (await c.PostAsync($"/admin/brands/{BrandKeys.Demo}/media", form)).StatusCode);
        await c.PostAsJsonAsync($"/admin/brands/{BrandKeys.Demo}/creative-jobs", new { kind = "organic", theme = "Demo" });

        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsync("/admin/brands/medlycare/demo/reset", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsync($"/admin/brands/{BrandKeys.Demo}/demo/reset", null)).StatusCode);
        Assert.Empty((await c.GetFromJsonAsync<JsonElement>($"/admin/brands/{BrandKeys.Demo}/media")).EnumerateArray());
        Assert.Empty((await c.GetFromJsonAsync<JsonElement>($"/admin/brands/{BrandKeys.Demo}/creative-jobs")).EnumerateArray());
    }
}

public class RawUploadTests : IClassFixture<StudioAiWebApplicationFactory>
{
    private readonly StudioAiWebApplicationFactory _factory;
    public RawUploadTests(StudioAiWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Batch_raw_recording_stays_a_clip_with_its_description()
    {
        var w = _factory.CreateClient();
        w.DefaultRequestHeaders.Add("X-Worker-Key", "chave-gravacao");
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent([0, 0, 0, 24, 0x66, 0x74, 0x79, 0x70]), "file", "arrastar.mp4");
        form.Add(new StringContent("true"), "raw");
        form.Add(new StringContent("Arrasta uma consulta para outro horario."), "description");
        var up = await (await w.PostAsync("/worker/media/medlycare", form)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(up.GetProperty("finished").GetBoolean());
        Assert.Equal("Arrasta uma consulta para outro horario.", up.GetProperty("description").GetString());
    }
}

public class MomentsTests : IClassFixture<StudioAiWebApplicationFactory>
{
    private readonly StudioAiWebApplicationFactory _factory;
    public MomentsTests(StudioAiWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Script_uses_the_chosen_moment_of_the_video_not_second_zero()
    {
        var w = _factory.CreateClient();
        w.DefaultRequestHeaders.Add("X-Worker-Key", "chave-gravacao");
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent([0, 0, 0, 24, 0x66, 0x74, 0x79, 0x70]), "file", "momentos-demo.mp4");
        form.Add(new StringContent("true"), "raw");
        form.Add(new StringContent("Agenda"), "description");
        Assert.Equal(HttpStatusCode.Created, (await w.PostAsync($"/worker/media/{BrandKeys.Demo}", form)).StatusCode);
        var updated = await w.PutAsJsonAsync($"/worker/media/{BrandKeys.Demo}/details", new
        {
            fileName = "momentos-demo.mp4",
            description = "Abre a agenda. Marca sessão semanal.",
            moments = new[] { new { start = 1.0, end = 4.3, description = "Abre a agenda" }, new { start = 12.0, end = 30.0, description = "Marca sessão semanal" } },
        });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        var c = _factory.CreateClient();
        var job = await (await c.PostAsJsonAsync($"/admin/brands/{BrandKeys.Demo}/creative-jobs", new { kind = "organic", theme = "Agenda" })).Content.ReadFromJsonAsync<JsonElement>();
        var written = await (await c.PostAsJsonAsync($"/admin/brands/{BrandKeys.Demo}/creative-jobs/{job.GetProperty("id").GetString()}/write-script", new { }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var trecho = written.GetProperty("job").GetProperty("roteiro").GetProperty("blocos")[0].GetProperty("trechos")[0];
        Assert.StartsWith("acervo-", trecho[0].GetString());
        Assert.Equal(12.0, trecho[1].GetDouble()); // momento 1 escolhido pela IA (fake), não o segundo zero
        Assert.True(trecho[2].GetDouble() < 30.0); // cortado pra caber na fala
    }
}

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;

namespace RootBoost.Api.IntegrationTests;

/// <summary>Mesma API, com a chave do worker configurada (sem ela as rotas /worker nem existem pra fora).</summary>
public sealed class StudioWebApplicationFactory : CustomWebApplicationFactory
{
    public const string WorkerKey = "chave-do-worker-de-teste";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Studio:WorkerKey", WorkerKey);
    }
}

public class StudioApiTests : IClassFixture<StudioWebApplicationFactory>
{
    private const string Base = "/admin/brands/medlycare/creative-jobs";
    private readonly StudioWebApplicationFactory _factory;

    public StudioApiTests(StudioWebApplicationFactory factory) => _factory = factory;

    private HttpClient Worker()
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Add("X-Worker-Key", StudioWebApplicationFactory.WorkerKey);
        return c;
    }

    private static readonly object Roteiro = new
    {
        nome = "combo-8-psicologia",
        abertura = new[] { "Psicologia sem planilha", "Agenda e evolução juntas" },
        blocos = new object[]
        {
            new { titulo = "Sessão toda semana", sub = "Marca uma vez", trechos = new object[] { new object[] { "psico1", 1, 4.3, 2 } } },
            new { titulo = "Evolução em minutos", sub = "Texto pronto", trechos = new object[] { new object[] { "psico2", 0.8, 14 } } },
        },
    };

    private static async Task<string> NewJob(HttpClient c)
    {
        var res = await c.PostAsJsonAsync(Base, new { kind = "organic", theme = $"Psicologia {Guid.NewGuid():N}", voiceKey = "matheus2b" });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
    }

    /// <summary>Pega jobs até achar o deste teste (outros testes da classe podem ter deixado job na fila).</summary>
    private static async Task<JsonElement> ClaimUntil(HttpClient worker, string id)
    {
        for (var i = 0; i < 20; i++)
        {
            var res = await worker.PostAsJsonAsync("/worker/jobs/claim", new { workerId = "pc-teste" });
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            var job = await res.Content.ReadFromJsonAsync<JsonElement>();
            if (job.GetProperty("id").GetString() == id) return job;
        }
        throw new InvalidOperationException("job não apareceu na fila");
    }

    [Fact]
    public async Task Script_out_of_contract_is_refused_with_the_reasons()
    {
        var c = _factory.CreateClient();
        var id = await NewJob(c);
        var res = await c.PutAsJsonAsync($"{Base}/{id}/script", new { roteiro = Roteiro, falas = new[] { "Uma.", "Duas." } });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(body.GetProperty("errors").EnumerateArray(), e => e.GetString()!.Contains("esperado 4"));
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsync($"{Base}/{id}/render", null)).StatusCode); // sem roteiro, não renderiza
    }

    [Fact]
    public async Task Job_goes_from_script_to_worker_to_draft_publications()
    {
        var c = _factory.CreateClient();
        var worker = Worker();
        var id = await NewJob(c);

        var falas = new[] { "Psicólogo com agenda no papel perde sessão.", "Você marca uma vez.", "A evolução vai pronta.", "Três meses grátis." };
        Assert.Equal(HttpStatusCode.OK, (await c.PutAsJsonAsync($"{Base}/{id}/script", new { roteiro = Roteiro, falas })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsync($"{Base}/{id}/render", null)).StatusCode);

        var job = await ClaimUntil(worker, id);
        Assert.Equal("matheus2b", job.GetProperty("voiceKey").GetString());
        Assert.Equal("combo-8-psicologia", job.GetProperty("roteiro").GetProperty("nome").GetString());
        Assert.Equal(4, job.GetProperty("falas").GetArrayLength());

        Assert.Equal(HttpStatusCode.NoContent, (await worker.PostAsJsonAsync($"/worker/jobs/{id}/progress", new { workerId = "pc-teste", percent = 60, note = "alinhado, 0 sem par" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await worker.PostAsJsonAsync($"/worker/jobs/{id}/progress", new { workerId = "outro", percent = 10 })).StatusCode);

        var deliver = await worker.PostAsJsonAsync($"/worker/jobs/{id}/deliver", new
        {
            workerId = "pc-teste",
            seed = 42,
            warnings = new[] { "fator 1,9 no bloco 2" },
            outputs = new[]
            {
                new { role = "video-instagram", url = "https://rootboost.test/media/a/psico-narrado-instagram.mp4" },
                new { role = "video", url = "https://rootboost.test/media/b/psico-narrado.mp4" },
                new { role = "cover-instagram", url = "https://rootboost.test/media/c/psico-narrado-instagram.jpg" },
            },
        });
        Assert.Equal(HttpStatusCode.OK, deliver.StatusCode);
        Assert.Equal("Review", (await deliver.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());

        var when = DateTimeOffset.UtcNow.AddDays(3);
        var pubs = await c.PostAsJsonAsync($"{Base}/{id}/publications", new { scheduledFor = when, instagram = "Legenda do Instagram", linkedIn = "Legenda do LinkedIn" });
        Assert.Equal(HttpStatusCode.OK, pubs.StatusCode);
        var body = await pubs.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Done", body.GetProperty("job").GetProperty("status").GetString());
        var created = body.GetProperty("publications").EnumerateArray().ToList();
        Assert.Equal(2, created.Count);
        var ig = created.Single(p => p.GetProperty("channel").GetString() == "Instagram");
        Assert.Equal("Draft", ig.GetProperty("status").GetString());
        Assert.EndsWith("psico-narrado-instagram.mp4", ig.GetProperty("mediaUrl").GetString());
        Assert.EndsWith("psico-narrado-instagram.jpg", ig.GetProperty("coverUrl").GetString());
        var li = created.Single(p => p.GetProperty("channel").GetString() == "LinkedIn");
        Assert.EndsWith("psico-narrado.mp4", li.GetProperty("mediaUrl").GetString());

        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync($"{Base}/{id}/publications", new { scheduledFor = when, instagram = "de novo" })).StatusCode);
    }

    [Fact]
    public async Task Worker_routes_need_the_key()
    {
        var anon = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsJsonAsync("/worker/jobs/claim", new { workerId = "x" })).StatusCode);
        anon.DefaultRequestHeaders.Add("X-Worker-Key", "errada");
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsJsonAsync("/worker/jobs/claim", new { workerId = "x" })).StatusCode);
    }

    [Fact]
    public async Task Worker_uploads_files_to_the_bucket()
    {
        var worker = Worker();
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(new byte[] { 1, 2, 3 });
        file.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
        form.Add(file, "file", "psico-narrado.mp4");
        var res = await worker.PostAsync("/worker/media/medlycare", form);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        Assert.StartsWith("https://rootboost.test/media/", (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("url").GetString());
    }
}

public class StudioWithoutWorkerKeyTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public StudioWithoutWorkerKeyTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Without_key_configured_the_worker_routes_do_not_exist()
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Add("X-Worker-Key", "qualquer");
        Assert.Equal(HttpStatusCode.NotFound, (await c.PostAsJsonAsync("/worker/jobs/claim", new { workerId = "x" })).StatusCode);
    }
}

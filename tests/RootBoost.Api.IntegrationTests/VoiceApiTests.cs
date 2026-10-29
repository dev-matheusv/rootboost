using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RootBoost.Domain;

namespace RootBoost.Api.IntegrationTests;

public class VoiceApiTests : IClassFixture<StudioAiWebApplicationFactory>
{
    private readonly StudioAiWebApplicationFactory _factory;
    public VoiceApiTests(StudioAiWebApplicationFactory factory) => _factory = factory;

    private HttpClient Worker()
    {
        var w = _factory.CreateClient();
        w.DefaultRequestHeaders.Add("X-Worker-Key", "chave-gravacao");
        return w;
    }

    [Fact]
    public async Task Recorded_voice_needs_consent_goes_through_the_worker_and_the_person_picks_a_sample()
    {
        var c = _factory.CreateClient();
        var script = await c.GetFromJsonAsync<JsonElement>("/admin/brands/medlycare/voices/script?person=Laura");
        Assert.StartsWith("Eu, Laura, autorizo a MedlyCare", script.GetProperty("consent").GetString());

        MultipartFormDataContent Form(bool consent)
        {
            var f = new MultipartFormDataContent();
            f.Add(new ByteArrayContent([1, 2, 3, 4]), "file", "gravacao.webm");
            f.Add(new StringContent("Laura"), "name");
            f.Add(new StringContent("Laura Souza"), "person");
            f.Add(new StringContent(consent ? "true" : "false"), "consent");
            return f;
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsync("/admin/brands/medlycare/voices/clone", Form(false))).StatusCode);
        var created = await c.PostAsync("/admin/brands/medlycare/voices/clone", Form(true));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var voice = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = voice.GetProperty("id").GetString()!;
        Assert.Equal("Laura Souza", voice.GetProperty("consentBy").GetString());

        var w = Worker();
        JsonElement claimed = default;
        for (var i = 0; i < 10; i++)
        {
            claimed = await (await w.PostAsJsonAsync("/worker/voices/claim", new { workerId = "pc" })).Content.ReadFromJsonAsync<JsonElement>();
            if (claimed.GetProperty("id").GetString() == id) break;
        }
        Assert.True(claimed.GetProperty("hasReference").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, (await w.GetAsync($"/worker/voices/{id}/reference?workerId=outro")).StatusCode);
        Assert.Equal([1, 2, 3, 4], await w.GetByteArrayAsync($"/worker/voices/{id}/reference?workerId=pc"));
        Assert.NotEqual(HttpStatusCode.OK, (await _factory.CreateClient().GetAsync($"/worker/voices/{id}/reference?workerId=pc")).StatusCode); // sem a chave do worker não sai

        using var samples = new MultipartFormDataContent();
        samples.Add(new StringContent("pc"), "workerId");
        samples.Add(new ByteArrayContent([7, 7]), "sample-1", "amostra-1.m4a");
        samples.Add(new ByteArrayContent([8, 8]), "sample-2", "amostra-2.m4a");
        Assert.Equal(HttpStatusCode.OK, (await w.PostAsync($"/worker/voices/{id}/complete", samples)).StatusCode);

        Assert.Equal([8, 8], await c.GetByteArrayAsync($"/admin/brands/medlycare/voices/{id}/samples/2"));
        var chosen = await (await c.PostAsJsonAsync($"/admin/brands/medlycare/voices/{id}/choose", new { seed = 2 })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, chosen.GetProperty("chosenSeed").GetInt32());
        Assert.Equal("Ready", chosen.GetProperty("status").GetString());

        // O job com essa voz sai pro worker com a semente escolhida.
        var key = chosen.GetProperty("key").GetString();
        var job = await (await c.PostAsJsonAsync("/admin/brands/medlycare/creative-jobs", new { kind = "organic", theme = "Teste de voz", voiceKey = key }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var jobId = job.GetProperty("id").GetString();
        await c.PutAsJsonAsync($"/admin/brands/medlycare/creative-jobs/{jobId}/script", new
        {
            roteiro = new
            {
                nome = "teste-voz",
                abertura = new[] { "Agenda sem papel", "Tudo num lugar" },
                blocos = new object[]
                {
                    new { titulo = "Sessão toda semana", sub = "Marca uma vez", trechos = new object[] { new object[] { "psico1", 1, 4.3, 2 } } },
                    new { titulo = "Evolução em minutos", sub = "Texto pronto", trechos = new object[] { new object[] { "psico2", 0.8, 14 } } },
                },
            },
            falas = new[] { "Psicólogo com agenda no papel perde sessão.", "Você marca uma vez.", "A evolução vai pronta.", "Três meses grátis." },
        });
        await c.PostAsync($"/admin/brands/medlycare/creative-jobs/{jobId}/render", null);
        JsonElement got = default;
        for (var i = 0; i < 20; i++)
        {
            got = await (await w.PostAsJsonAsync("/worker/jobs/claim", new { workerId = "pc" })).Content.ReadFromJsonAsync<JsonElement>();
            if (got.GetProperty("id").GetString() == jobId) break;
        }
        Assert.Equal(jobId, got.GetProperty("id").GetString());
        Assert.Equal(2, got.GetProperty("seed").GetInt32());
    }

    [Fact]
    public async Task Voice_of_one_brand_never_narrates_another()
    {
        var c = _factory.CreateClient();
        var voice = await (await c.PostAsJsonAsync($"/admin/brands/{BrandKeys.Store}/voices/design",
            new { name = "Ana", gender = "female", age = "middle-aged", pitch = "low pitch" })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Design", voice.GetProperty("kind").GetString());
        var res = await c.PostAsJsonAsync("/admin/brands/medlycare/creative-jobs",
            new { kind = "organic", theme = "Voz emprestada", voiceKey = voice.GetProperty("key").GetString() });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);

        var bad = await c.PostAsJsonAsync("/admin/brands/medlycare/voices/design", new { name = "X", gender = "robot", age = "middle-aged", pitch = "low pitch" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }
}

public class StudioStatusTests : IClassFixture<StudioAiWebApplicationFactory>
{
    private readonly StudioAiWebApplicationFactory _factory;
    public StudioStatusTests(StudioAiWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Worker_heartbeat_shows_up_and_queued_jobs_get_a_position()
    {
        var c = _factory.CreateClient();
        var w = _factory.CreateClient();
        w.DefaultRequestHeaders.Add("X-Worker-Key", "chave-gravacao");

        var job = await (await c.PostAsJsonAsync("/admin/brands/medlycare/creative-jobs", new { kind = "organic", theme = "Fila" })).Content.ReadFromJsonAsync<JsonElement>();
        var id = job.GetProperty("id").GetString()!;
        await c.PutAsJsonAsync($"/admin/brands/medlycare/creative-jobs/{id}/script", new
        {
            roteiro = new
            {
                nome = "teste-fila",
                abertura = new[] { "Agenda sem papel", "Tudo num lugar" },
                blocos = new object[]
                {
                    new { titulo = "Sessão toda semana", sub = "Marca uma vez", trechos = new object[] { new object[] { "psico1", 1, 4.3, 2 } } },
                    new { titulo = "Evolução em minutos", sub = "Texto pronto", trechos = new object[] { new object[] { "psico2", 0.8, 14 } } },
                },
            },
            falas = new[] { "Psicólogo com agenda no papel perde sessão.", "Você marca uma vez.", "A evolução vai pronta.", "Três meses grátis." },
        });
        await c.PostAsync($"/admin/brands/medlycare/creative-jobs/{id}/render", null);

        var status = await c.GetFromJsonAsync<JsonElement>("/admin/brands/medlycare/studio/status");
        Assert.True(status.GetProperty("positions").GetProperty(id).GetInt32() >= 1);

        Assert.Equal(HttpStatusCode.NoContent, (await w.PostAsJsonAsync("/worker/heartbeat", new { workerId = "pc-teste", note = "livre" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await w.PostAsJsonAsync("/worker/heartbeat", new { workerId = "pc-teste", note = "livre de novo" })).StatusCode);
        status = await c.GetFromJsonAsync<JsonElement>("/admin/brands/medlycare/studio/status");
        Assert.True(status.GetProperty("workersOnline").GetInt32() >= 1);
    }
}

public class VoiceReviewTests : IClassFixture<StudioAiWebApplicationFactory>
{
    private readonly StudioAiWebApplicationFactory _factory;
    public VoiceReviewTests(StudioAiWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Worker_delivers_two_voices_the_person_picks_one_and_the_job_goes_back_approved()
    {
        var c = _factory.CreateClient();
        var w = _factory.CreateClient();
        w.DefaultRequestHeaders.Add("X-Worker-Key", "chave-gravacao");
        var job = await (await c.PostAsJsonAsync("/admin/brands/medlycare/creative-jobs", new { kind = "organic", theme = "Revisão da voz" })).Content.ReadFromJsonAsync<JsonElement>();
        var id = job.GetProperty("id").GetString()!;
        await c.PutAsJsonAsync($"/admin/brands/medlycare/creative-jobs/{id}/script", new
        {
            roteiro = new
            {
                nome = "teste-revisao-voz",
                abertura = new[] { "Agenda sem papel", "Tudo num lugar" },
                blocos = new object[]
                {
                    new { titulo = "Sessão toda semana", sub = "Marca uma vez", trechos = new object[] { new object[] { "psico1", 1, 4.3, 2 } } },
                    new { titulo = "Evolução em minutos", sub = "Texto pronto", trechos = new object[] { new object[] { "psico2", 0.8, 14 } } },
                },
            },
            falas = new[] { "Psicólogo com agenda no papel perde sessão.", "Você marca uma vez.", "A evolução vai pronta.", "Três meses grátis." },
        });
        await c.PostAsync($"/admin/brands/medlycare/creative-jobs/{id}/render", null);

        async Task<JsonElement> ClaimMine()
        {
            for (var i = 0; i < 20; i++)
            {
                var r = await w.PostAsJsonAsync("/worker/jobs/claim", new { workerId = "pc" });
                if (r.StatusCode == HttpStatusCode.NoContent) continue;
                var j = await r.Content.ReadFromJsonAsync<JsonElement>();
                if (j.GetProperty("id").GetString() == id) return j;
            }
            throw new Xunit.Sdk.XunitException("job não veio pro worker");
        }

        Assert.False((await ClaimMine()).GetProperty("voiceApproved").GetBoolean());
        using var takes = new MultipartFormDataContent();
        takes.Add(new StringContent("pc"), "workerId");
        takes.Add(new ByteArrayContent([1, 1]), "take-1", "voz-1.m4a");
        takes.Add(new StringContent("0"), "unmatched-1");
        takes.Add(new ByteArrayContent([2, 2]), "take-3", "voz-3.m4a");
        takes.Add(new StringContent("0"), "unmatched-3");
        var review = await (await w.PostAsync($"/worker/jobs/{id}/voice", takes)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("VoiceReview", review.GetProperty("status").GetString());
        Assert.Equal(2, review.GetProperty("voiceTakes").GetArrayLength());

        Assert.Equal([2, 2], await c.GetByteArrayAsync($"/admin/brands/medlycare/creative-jobs/{id}/voice/3"));
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync($"/admin/brands/medlycare/creative-jobs/{id}/voice/choose", new { seed = 2 })).StatusCode);
        var chosen = await (await c.PostAsJsonAsync($"/admin/brands/medlycare/creative-jobs/{id}/voice/choose", new { seed = 3 })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Queued", chosen.GetProperty("status").GetString());

        var again = await ClaimMine();
        Assert.True(again.GetProperty("voiceApproved").GetBoolean());
        Assert.Equal(3, again.GetProperty("seed").GetInt32());
    }
}

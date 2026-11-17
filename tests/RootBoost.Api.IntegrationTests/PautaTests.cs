using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RootBoost.Application.Studio;
using RootBoost.Domain;

namespace RootBoost.Api.IntegrationTests;

/// <summary>Pauta de conteúdos (17/11): importar, mandar pra fila e a fila virar vídeo com roteiro e render.</summary>
public class PautaTests : IClassFixture<StudioAiWebApplicationFactory>
{
    private readonly StudioAiWebApplicationFactory _factory;
    public PautaTests(StudioAiWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public void Auto_voice_job_picks_the_best_take_and_goes_back_to_the_queue()
    {
        var now = DateTimeOffset.UtcNow;
        var job = new CreativeJob(BrandKeys.MedlyCare, CreativeKind.Organic, "Relatório do mês", null, now, VideoFormat.Explainer);
        job.EnableAutoVoice(now);
        job.SetScript("{}", "[]", now);
        job.QueueRender(now);
        job.Claim("w1", now, TimeSpan.FromMinutes(5));
        job.DeliverVoice("w1", [new VoiceTake(3, "k3", 4), new VoiceTake(5, "k5", 1)], now);
        Assert.Equal(CreativeJobStatus.Queued, job.Status);
        Assert.True(job.VoiceApproved);
        Assert.Equal(5, job.Seed);
    }

    [Fact]
    public async Task Import_queue_and_runner_make_a_video()
    {
        var client = _factory.CreateClient();
        var imported = await client.PostAsJsonAsync("/admin/brands/medlycare/pauta/import", new
        {
            items = new[]
            {
                new { title = "Fila de espera", request = "Paciente chegou sem hora marcada: como atender pela Fila de Espera.", group = "Onda 1" },
                new { title = "Fila de espera", request = "repetido", group = "Onda 1" },
            },
        });
        Assert.Equal(1, (await imported.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("added").GetInt32());

        var list = await client.GetFromJsonAsync<JsonElement>("/admin/brands/medlycare/pauta");
        var id = list.EnumerateArray().First(x => x.GetProperty("title").GetString() == "Fila de espera").GetProperty("id").GetGuid();
        var q = await client.PostAsJsonAsync("/admin/brands/medlycare/pauta/queue", new { ids = new[] { id }, autoVoice = true });
        Assert.Equal(1, (await q.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("queued").GetInt32());

        using (var scope = _factory.Services.CreateScope())
            Assert.True(await scope.ServiceProvider.GetRequiredService<PautaRunner>().RunOneAsync());

        var after = (await client.GetFromJsonAsync<JsonElement>("/admin/brands/medlycare/pauta")).EnumerateArray()
            .First(x => x.GetProperty("id").GetGuid() == id);
        Assert.Equal("InProduction", after.GetProperty("status").GetString());
        Assert.Equal("Queued", after.GetProperty("jobStatus").GetString());
        Assert.True(after.GetProperty("autoVoice").GetBoolean());

        var del = await client.DeleteAsync($"/admin/brands/medlycare/pauta/{id}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);
    }
}

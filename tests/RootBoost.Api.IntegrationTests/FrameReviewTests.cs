using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RootBoost.Application.Ai;

namespace RootBoost.Api.IntegrationTests;

/// <summary>Conferência pela IA (roadmap A5, 19/11): problemas da folha de quadros viram aviso legível; lixo da IA fica de fora.</summary>
public class FrameReviewTests : IClassFixture<StudioAiWebApplicationFactory>
{
    private readonly StudioAiWebApplicationFactory _factory;
    public FrameReviewTests(StudioAiWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public void Read_keeps_known_kinds_and_valid_frames_only()
    {
        var problems = FrameReviewer.Read("""
            {"problemas":[
              {"quadro":8,"tipo":"imagem-pixelada","descricao":"Logo ampliado e pixelado; use a imagem inteira sobre o fundo."},
              {"quadro":40,"tipo":"texto-duplo","descricao":"Título e legenda ao mesmo tempo."},
              {"quadro":2,"tipo":"inventado","descricao":"Não devia aparecer."},
              {"quadro":3,"tipo":"outro","descricao":"  "}
            ]}
            """);
        Assert.Equal(2, problems.Count);
        Assert.Equal("Conferência da IA, quadro 8: Logo ampliado e pixelado; use a imagem inteira sobre o fundo.", problems[0]);
        Assert.Equal("Conferência da IA: Título e legenda ao mesmo tempo.", problems[1]); // quadro fora de 1 a 12 some
    }

    [Fact]
    public async Task Endpoint_needs_the_sheet_and_returns_the_problems()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Worker-Key", "chave-gravacao");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/worker/ai/review-frames", new { brandKey = "medlycare" })).StatusCode);
        var res = await client.PostAsJsonAsync("/worker/ai/review-frames", new
        {
            brandKey = "medlycare",
            theme = "Relatório do mês",
            lines = new[] { "Toca em Mais.", "Escolhe o período." },
            image = Convert.ToBase64String(new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 }),
        });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(0, (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("problems").GetArrayLength());
    }
}

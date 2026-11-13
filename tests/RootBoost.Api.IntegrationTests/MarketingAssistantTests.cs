using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace RootBoost.Api.IntegrationTests;

/// <summary>Assistente de conversa e sugestões da semana (13/11), no modo Fake da IA, pela API.</summary>
public class MarketingAssistantTests : IClassFixture<StudioAiWebApplicationFactory>
{
    private readonly StudioAiWebApplicationFactory _factory;
    public MarketingAssistantTests(StudioAiWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Assistant_answers_and_refuses_a_conversation_without_question()
    {
        var client = _factory.CreateClient();
        var ok = await client.PostAsJsonAsync("/admin/brands/medlycare/assistant/chat",
            new { messages = new[] { new { role = "user", text = "Qual vídeo eu devo anunciar?" } } });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Contains("Campanhas", (await ok.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("answer").GetString());

        var empty = await client.PostAsJsonAsync("/admin/brands/medlycare/assistant/chat", new { messages = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
    }

    [Fact]
    public async Task Weekly_advice_returns_suggestions()
    {
        var res = await _factory.CreateClient().PostAsync("/admin/brands/medlycare/report/advice", null);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("suggestions").GetArrayLength() > 0);
    }
}

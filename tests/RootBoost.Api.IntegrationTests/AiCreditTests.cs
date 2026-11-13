using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace RootBoost.Api.IntegrationTests;

/// <summary>Crédito da IA (13/11): teto do mês, e a IA recusando depois do teto com mensagem clara.</summary>
public class AiCreditTests : IClassFixture<StudioAiWebApplicationFactory>
{
    private readonly StudioAiWebApplicationFactory _factory;
    public AiCreditTests(StudioAiWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Limit_is_saved_and_zero_limit_blocks_the_ai()
    {
        var client = _factory.CreateClient();
        var set = await client.PutAsJsonAsync("/admin/ai/credit", new { monthlyLimitUsd = 25m, alertPercent = 70 });
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        var status = await set.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(25m, status.GetProperty("limitUsd").GetDecimal());
        Assert.False(status.GetProperty("blocked").GetBoolean());

        var ok = await client.PostAsJsonAsync("/admin/brands/medlycare/assistant/chat",
            new { messages = new[] { new { role = "user", text = "Qual vídeo eu devo anunciar?" } } });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        // Teto zero: gasto 0 já é "atingido"; a IA para e diz o motivo.
        await client.PutAsJsonAsync("/admin/ai/credit", new { monthlyLimitUsd = 0m, alertPercent = 80 });
        var blocked = await client.PostAsJsonAsync("/admin/brands/medlycare/assistant/chat",
            new { messages = new[] { new { role = "user", text = "E agora?" } } });
        Assert.NotEqual(HttpStatusCode.OK, blocked.StatusCode);
        Assert.Contains("teto mensal da IA", await blocked.Content.ReadAsStringAsync());

        await client.PutAsJsonAsync("/admin/ai/credit", new { monthlyLimitUsd = (decimal?)null, alertPercent = 80 });
        var bad = await client.PutAsJsonAsync("/admin/ai/credit", new { monthlyLimitUsd = -1m });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }
}

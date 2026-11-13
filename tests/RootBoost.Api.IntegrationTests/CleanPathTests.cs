using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RootBoost.Application.Ai;

namespace RootBoost.Api.IntegrationTests;

/// <summary>Caminho limpo (13/11): só índices que existem, uma vez, em ordem; a API devolve uma lista por cena.</summary>
public class CleanPathTests : IClassFixture<StudioAiWebApplicationFactory>
{
    private readonly StudioAiWebApplicationFactory _factory;
    public CleanPathTests(StudioAiWebApplicationFactory factory) => _factory = factory;

    private static readonly RehearsalAction[] Actions =
    [
        new(0, 0, "clicar", "Mais", null, true, null),
        new(1, 0, "clicar", "Relatórios", null, true, null),
        new(2, 1, "clicar", "Limpar", null, false, null),
        new(3, 1, "clicar", "Data inicial", null, true, null),
    ];

    [Fact]
    public void Validate_drops_unknown_repeated_and_out_of_order_indices()
    {
        var plan = PathCleaner.Validate("""{"cenas":[{"acoes":[0,1,1,9]},{"acoes":[0,3]}]}""", 2, Actions);
        Assert.Equal([0, 1], plan[0]);
        Assert.Equal([3], plan[1]);
        Assert.Throws<InvalidOperationException>(() => PathCleaner.Validate("""{"cenas":[{"acoes":[]}]}""", 2, Actions));
    }

    [Fact]
    public async Task Endpoint_returns_one_list_per_scene()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Worker-Key", "chave-gravacao");
        var res = await client.PostAsJsonAsync("/worker/ai/clean-path", new
        {
            brandKey = "medlycare",
            scenes = new[] { "Abrir relatórios", "Escolher a data" },
            actions = Actions.Select(a => new { a.Index, a.Scene, a.Action, a.Text, a.Typed, a.Changed, a.Reason }),
        });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, body.GetProperty("scenes").GetArrayLength());
        Assert.Equal(3, body.GetProperty("scenes")[1][0].GetInt32());
    }
}

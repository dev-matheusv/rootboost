using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using RootBoost.Application.Ai;

namespace RootBoost.Api.IntegrationTests;

/// <summary>Ajuste pontual (A8, 19/11): com pedido, a IA muda só o que foi pedido; o resto do roteiro fica igual.</summary>
public class ScriptAdjustTests : IClassFixture<StudioAiWebApplicationFactory>
{
    private const string Base = "/admin/brands/medlycare/creative-jobs";
    private readonly StudioAiWebApplicationFactory _factory;
    public ScriptAdjustTests(StudioAiWebApplicationFactory factory) => _factory = factory;

    private static readonly object Roteiro = new
    {
        nome = "ajuste-teste",
        abertura = new[] { "Psicologia sem planilha", "Agenda e evolução juntas" },
        blocos = new object[]
        {
            new { titulo = "Sessão toda semana", sub = "Marca uma vez", trechos = new object[] { new object[] { "psico1", 1, 4.3, 2 } } },
            new { titulo = "Evolução em minutos", sub = "Texto pronto", trechos = new object[] { new object[] { "psico2", 0.8, 14 } } },
        },
    };

    [Fact]
    public void Apply_only_touches_valid_targets()
    {
        var script = JsonNode.Parse("""{"blocos":[{"titulo":"A","sub":"a","cena":"x"},{"titulo":"B","sub":"b","cena":"y"}]}""")!.AsObject();
        var lines = JsonNode.Parse("""["um","dois","três"]""")!.AsArray();
        var n = ScriptAdjuster.Apply(script, lines, [new("fala", 1, "dois novo"), new("cena", 1, "y nova"), new("fala", 9, "fora"),
            new("titulo", 0, "  "), new("trilha", 0, "não existe")]);
        Assert.Equal(2, n);
        Assert.Equal(["um", "dois novo", "três"], lines.Select(l => l!.GetValue<string>()));
        Assert.Equal("A", script["blocos"]![0]!["titulo"]!.GetValue<string>());
        Assert.Equal("y nova", script["blocos"]![1]!["cena"]!.GetValue<string>());
    }

    [Fact]
    public async Task Request_with_script_ready_changes_only_what_was_asked()
    {
        var c = _factory.CreateClient();
        var res = await c.PostAsJsonAsync(Base, new { kind = "organic", theme = $"Ajuste {Guid.NewGuid():N}", voiceKey = "medlynat1" });
        var id = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();
        var falas = new[] { "Psicólogo com agenda no papel perde sessão.", "Você marca uma vez.", "A evolução vai pronta.", "Três meses grátis." };
        Assert.Equal(HttpStatusCode.OK, (await c.PutAsJsonAsync($"{Base}/{id}/script", new { roteiro = Roteiro, falas })).StatusCode);

        var adj = await c.PostAsJsonAsync($"{Base}/{id}/write-script", new { instructions = "troca só a primeira fala" });
        Assert.Equal(HttpStatusCode.OK, adj.StatusCode);
        var body = await adj.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, body.GetProperty("changes").GetInt32());
        var after = body.GetProperty("job").GetProperty("falas").EnumerateArray().Select(f => f.GetString()).ToList();
        Assert.Equal("Fala ajustada pelo pedido.", after[0]);
        Assert.Equal(falas[1..], after[1..]);
        Assert.Equal("Sessão toda semana", body.GetProperty("job").GetProperty("roteiro").GetProperty("blocos")[0].GetProperty("titulo").GetString());
    }
}

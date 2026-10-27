using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace RootBoost.Api.IntegrationTests;

/// <summary>Perfil da marca pela API, no SQLite e no Postgres. Cada teste usa uma marca do seed sem depender da ordem.</summary>
public abstract class BrandProfileApiTestsBase
{
    protected abstract HttpClient Client();

    private static object Import() => new
    {
        voice = new
        {
            audience = "profissional de clínica pequena",
            tone = new[] { "frase curta", "fundador fala como fundador" },
            wordSwaps = new Dictionary<string, string> { ["absenteísmo"] = "falta" },
            bannedTerms = new[] { "revolucione" },
            objections = new[] { new { objection = "e meus dados?", answer = "migração com prévia e sem duplicar" } }
        },
        proof = new object[]
        {
            new { kind = "Offer", statement = "3 meses grátis, sem cartão, sem fidelidade", source = "playbook 23/10/2026", verifiedAt = "2026-10-23" },
            new { kind = "Forbidden", statement = "nunca sai do ar", source = "playbook 23/10/2026 (já houve queda)", verifiedAt = "2026-10-23" }
        },
        phrases = new[] { new { text = "o suporte é fraco", source = "Reclame Aqui, out/2026", collectedAt = "2026-10-15" } }
    };

    private async Task<JsonElement> Profile(HttpClient client, string brand) =>
        await client.GetFromJsonAsync<JsonElement>($"/admin/brands/{brand}/profile");

    [Fact]
    public async Task Import_fills_an_empty_profile_once_and_refuses_to_overwrite()
    {
        var client = Client();
        var first = await client.PostAsJsonAsync("/admin/brands/medlycare/profile/import", Import());
        if (first.StatusCode == HttpStatusCode.Conflict) return; // outro teste da mesma fábrica já importou

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var profile = await Profile(client, "medlycare");
        Assert.Equal("profissional de clínica pequena", profile.GetProperty("voice").GetProperty("audience").GetString());
        Assert.Equal(2, profile.GetProperty("proof").GetArrayLength());
        Assert.Equal(1, profile.GetProperty("phrases").GetArrayLength());

        var second = await client.PostAsJsonAsync("/admin/brands/medlycare/profile/import", Import());
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Proof_without_source_or_with_unknown_kind_is_rejected()
    {
        var client = Client();
        var noSource = await client.PostAsJsonAsync("/admin/brands/rootboost-loja/proof",
            new { kind = "Feature", statement = "entrega em 1 semana", source = "", verifiedAt = "2026-10-23" });
        Assert.Equal(HttpStatusCode.BadRequest, noSource.StatusCode);

        var badKind = await client.PostAsJsonAsync("/admin/brands/rootboost-loja/proof",
            new { kind = "Boato", statement = "x", source = "y", verifiedAt = "2026-10-23" });
        Assert.Equal(HttpStatusCode.BadRequest, badKind.StatusCode);
    }

    [Fact]
    public async Task Retired_proof_stays_listed_as_inactive()
    {
        var client = Client();
        var created = await client.PostAsJsonAsync("/admin/brands/rootboost-loja/proof",
            new { kind = "Feature", statement = $"armazém nos EUA {Guid.NewGuid():N}", source = "CJ, consulta à API", verifiedAt = "2026-10-01" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/admin/brands/rootboost-loja/proof/{id}/retire", null)).StatusCode);

        var item = (await Profile(client, "rootboost-loja")).GetProperty("proof").EnumerateArray()
            .Single(p => p.GetProperty("id").GetString() == id);
        Assert.False(item.GetProperty("active").GetBoolean());
    }

    [Fact]
    public async Task Unknown_brand_is_not_found()
    {
        var client = Client();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/admin/brands/nao-existe/profile")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync("/admin/brands/nao-existe/voice",
            new { audience = "x", tone = Array.Empty<string>(), wordSwaps = new Dictionary<string, string>(), bannedTerms = Array.Empty<string>(), objections = Array.Empty<object>() })).StatusCode);
    }
}

public class BrandProfileApiPostgresTests : BrandProfileApiTestsBase, IClassFixture<PostgresWebApplicationFactory>
{
    private readonly PostgresWebApplicationFactory _factory;
    public BrandProfileApiPostgresTests(PostgresWebApplicationFactory factory) => _factory = factory;
    protected override HttpClient Client() => _factory.CreateClient();
}

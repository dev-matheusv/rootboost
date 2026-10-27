using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace RootBoost.Api.IntegrationTests;

/// <summary>Conector de anúncios no modo Fake (Ads:Reader=Fake), pela API, no SQLite e no Postgres.</summary>
public abstract class AdsApiTestsBase
{
    protected abstract WebApplicationFactory<Program> Factory();

    private HttpClient FakeAdsClient() =>
        Factory().WithWebHostBuilder(b => b.UseSetting("Ads:Reader", "Fake")).CreateClient();

    [Fact]
    public async Task Sync_is_idempotent_and_the_summary_shows_the_stop_rule()
    {
        var client = FakeAdsClient();

        var first = await (await client.PostAsync("/admin/ads/sync?days=7", null)).Content.ReadFromJsonAsync<JsonElement>();
        var second = await (await client.PostAsync("/admin/ads/sync?days=7", null)).Content.ReadFromJsonAsync<JsonElement>();

        var r2 = second.EnumerateArray().Single();
        Assert.True(r2.GetProperty("ok").GetBoolean());
        Assert.Equal(0, r2.GetProperty("inserted").GetInt32()); // a segunda passada só atualiza
        Assert.True(r2.GetProperty("updated").GetInt32() > 0);
        Assert.True(first.EnumerateArray().Single().GetProperty("ok").GetBoolean());

        var summary = await client.GetFromJsonAsync<JsonElement>("/admin/brands/medlycare/ads/summary?days=7");
        Assert.Equal(2, summary.GetProperty("campaigns").GetArrayLength());
        Assert.Equal(7, summary.GetProperty("daily").GetArrayLength());
        Assert.True(summary.GetProperty("totals").GetProperty("spend").GetDecimal() > 0);
        Assert.Contains(summary.GetProperty("alerts").EnumerateArray(),
            a => a.GetProperty("kind").GetString() == "stopRule" && a.GetProperty("objectId").GetString() == "fake-cmp-remarketing");
    }

    [Fact]
    public async Task Without_reader_nothing_is_called_and_the_summary_is_empty()
    {
        var client = Factory().CreateClient(); // Ads:Reader padrão = None

        var accounts = await client.GetFromJsonAsync<JsonElement>("/admin/ads/accounts");
        Assert.Equal("None", accounts.GetProperty("reader").GetString());
        Assert.Equal(0, accounts.GetProperty("accounts").GetArrayLength());

        var sync = await client.PostAsync("/admin/ads/sync", null);
        Assert.Equal(HttpStatusCode.OK, sync.StatusCode);
        Assert.Equal(0, (await sync.Content.ReadFromJsonAsync<JsonElement>()).GetArrayLength());

        var summary = await client.GetFromJsonAsync<JsonElement>("/admin/brands/rootboost-loja/ads/summary");
        Assert.Equal(0, summary.GetProperty("campaigns").GetArrayLength());
    }
}

public class AdsApiPostgresTests : AdsApiTestsBase, IClassFixture<PostgresWebApplicationFactory>
{
    private readonly PostgresWebApplicationFactory _factory;
    public AdsApiPostgresTests(PostgresWebApplicationFactory factory) => _factory = factory;
    protected override WebApplicationFactory<Program> Factory() => _factory;
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace RootBoost.Api.IntegrationTests;

/// <summary>Marcas pela API, no SQLite (padrão) e no Postgres (produção).</summary>
public abstract class BrandApiTestsBase
{
    protected abstract HttpClient Client();

    private static object Payment(string paymentId) => new
    {
        paymentId,
        productKey = "rack",
        quantity = 1,
        email = "jane@example.com",
        name = "Jane Buyer",
        line1 = "123 Main St",
        city = "Austin",
        state = "TX",
        zip = "78701",
        countryCode = "US",
        amount = 34.99,
        currency = "USD"
    };

    [Fact]
    public async Task Both_initial_brands_are_listed()
    {
        var brands = await Client().GetFromJsonAsync<List<JsonElement>>("/admin/brands");
        var keys = brands!.Select(b => b.GetProperty("key").GetString()).ToList();
        Assert.Contains("rootboost-loja", keys);
        Assert.Contains("medlycare", keys);
    }

    [Fact]
    public async Task Orders_can_be_filtered_by_brand()
    {
        var client = Client();
        var paymentId = $"BRAND-{Guid.NewGuid():N}";
        (await client.PostAsJsonAsync("/webhook/payment", Payment(paymentId))).EnsureSuccessStatusCode();

        var store = await client.GetFromJsonAsync<List<JsonElement>>("/orders?brand=rootboost-loja");
        var order = store!.Single(o => o.GetProperty("paymentId").GetString() == paymentId);
        Assert.Equal("rootboost-loja", order.GetProperty("brandKey").GetString());

        var medly = await client.GetFromJsonAsync<List<JsonElement>>("/orders?brand=medlycare");
        Assert.DoesNotContain(medly!, o => o.GetProperty("paymentId").GetString() == paymentId);
    }

    [Fact]
    public async Task Invalid_brand_is_a_bad_request()
    {
        var client = Client();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/orders?brand=com espaco")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/admin/products/performance?brand=***")).StatusCode);
    }
}

public class BrandApiSqliteTests : BrandApiTestsBase, IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public BrandApiSqliteTests(CustomWebApplicationFactory factory) => _factory = factory;
    protected override HttpClient Client() => _factory.CreateClient();
}

public class BrandApiPostgresTests : BrandApiTestsBase, IClassFixture<PostgresWebApplicationFactory>
{
    private readonly PostgresWebApplicationFactory _factory;
    public BrandApiPostgresTests(PostgresWebApplicationFactory factory) => _factory = factory;
    protected override HttpClient Client() => _factory.CreateClient();
}

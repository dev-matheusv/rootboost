using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace RootBoost.Api.IntegrationTests;

/// <summary>
/// End-to-end sobre o pipeline HTTP real: bate visitas em /track/view e um pedido pago, depois
/// confere que /admin/products/performance cruza os dois em conversão por produto.
/// </summary>
public class ProductPerformanceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public ProductPerformanceTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Views_and_orders_combine_into_conversion_per_product()
    {
        var client = _factory.CreateClient();

        // 3 visitas na landing do "rack" (produto do catálogo de teste).
        for (int i = 0; i < 3; i++)
        {
            var v = await client.PostAsJsonAsync("/track/view", new { productKey = "rack", lang = "en" });
            Assert.Equal(HttpStatusCode.NoContent, v.StatusCode);
        }

        // 1 pedido pago do "rack".
        await client.PostAsJsonAsync("/webhook/payment", new
        {
            paymentId = "PAY-PERF-1", productKey = "rack", quantity = 1, email = "b@example.com",
            name = "B Buyer", line1 = "1 St", city = "Austin", state = "TX", zip = "78701",
            countryCode = "US", phone = "+15551234567", amount = 34.99, currency = "USD"
        });

        var report = await client.GetFromJsonAsync<JsonElement>("/admin/products/performance");
        var products = report.GetProperty("products").EnumerateArray().ToList();
        var rack = products.First(p => p.GetProperty("productKey").GetString() == "rack");

        Assert.Equal(3, rack.GetProperty("views").GetInt64());
        Assert.Equal(1, rack.GetProperty("orders").GetInt32());
        Assert.True(rack.GetProperty("conversionRate").GetDouble() > 0);
        Assert.True(rack.GetProperty("revenue").GetDecimal() >= 34.99m);
    }

    [Fact]
    public async Task Unknown_product_view_is_ignored()
    {
        var client = _factory.CreateClient();
        var res = await client.PostAsJsonAsync("/track/view", new { productKey = "does-not-exist", lang = "en" });
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);

        var report = await client.GetFromJsonAsync<JsonElement>("/admin/products/performance");
        var products = report.GetProperty("products").EnumerateArray();
        Assert.DoesNotContain(products, p => p.GetProperty("productKey").GetString() == "does-not-exist");
    }
}

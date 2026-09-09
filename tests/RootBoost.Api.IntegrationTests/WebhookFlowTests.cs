using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace RootBoost.Api.IntegrationTests;

/// <summary>
/// End-to-end over the real HTTP pipeline: payment webhook -> order -> mock supplier -> tracking.
/// </summary>
public class WebhookFlowTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public WebhookFlowTests(CustomWebApplicationFactory factory) => _factory = factory;

    private static object PaymentPayload(string paymentId) => new
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
        phone = "+15551234567",
        amount = 34.99,
        currency = "USD"
    };

    [Fact]
    public async Task Health_returns_ok()
    {
        var client = _factory.CreateClient();
        var res = await client.GetAsync("/health");
        res.EnsureSuccessStatusCode();
        Assert.Contains("ok", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Payment_webhook_fulfills_and_appears_in_orders()
    {
        var client = _factory.CreateClient();

        var res = await client.PostAsJsonAsync("/webhook/payment", PaymentPayload("PAY-IT-1"));
        res.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        Assert.Equal("Fulfilled", body.RootElement.GetProperty("outcome").GetString());

        var orders = await client.GetFromJsonAsync<List<JsonElement>>("/orders");
        var order = orders!.First(o => o.GetProperty("paymentId").GetString() == "PAY-IT-1");
        Assert.Equal("PlacedAtSupplier", order.GetProperty("status").GetString());
        Assert.Equal("MOCK-PAY-IT-1", order.GetProperty("supplierOrderId").GetString());
    }

    [Fact]
    public async Task Duplicate_payment_is_idempotent()
    {
        var client = _factory.CreateClient();

        await client.PostAsJsonAsync("/webhook/payment", PaymentPayload("PAY-IT-DUP"));
        var second = await client.PostAsJsonAsync("/webhook/payment", PaymentPayload("PAY-IT-DUP"));
        using var body = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        Assert.Equal("AlreadyProcessed", body.RootElement.GetProperty("outcome").GetString());

        var orders = await client.GetFromJsonAsync<List<JsonElement>>("/orders");
        Assert.Single(orders!, o => o.GetProperty("paymentId").GetString() == "PAY-IT-DUP");
    }

    [Fact]
    public async Task Cj_webhook_attaches_tracking_and_marks_shipped()
    {
        var client = _factory.CreateClient();

        await client.PostAsJsonAsync("/webhook/payment", PaymentPayload("PAY-IT-SHIP"));
        // MockSupplier returns "MOCK-{orderReference}" where the reference is the paymentId.
        var cj = await client.PostAsJsonAsync("/webhook/cj", new { orderId = "MOCK-PAY-IT-SHIP", trackNumber = "LP123456789CN" });
        cj.EnsureSuccessStatusCode();

        var orders = await client.GetFromJsonAsync<List<JsonElement>>("/orders");
        var order = orders!.First(o => o.GetProperty("paymentId").GetString() == "PAY-IT-SHIP");
        Assert.Equal("Shipped", order.GetProperty("status").GetString());
        Assert.Equal("LP123456789CN", order.GetProperty("tracking").GetString());
    }

    [Fact]
    public async Task Orders_dashboard_is_reachable()
    {
        var client = _factory.CreateClient();
        var res = await client.GetAsync("/orders");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }
}

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace RootBoost.Api.IntegrationTests;

/// <summary>
/// Trava de fulfillment no checkout: em modo LIVE (Payments:Verifier != Test), o create-order
/// recusa produto nao faturavel (VID da CJ ainda TODO), pra nunca capturar dinheiro do que nao
/// da pra enviar. Este factory sobe em modo "PayPal" (live) mas o guard responde ANTES de tocar
/// no PayPal, entao nenhuma chamada externa acontece.
/// </summary>
public sealed class LiveModeFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rootboost-live-{Guid.NewGuid():N}.db");
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Supplier", "Mock");
        builder.UseSetting("Notifier", "Logging");
        builder.UseSetting("Payments:Verifier", "PayPal"); // modo LIVE (nao-teste)
        builder.UseSetting("ConnectionStrings:Sqlite", $"Data Source={_dbPath}");
        builder.UseSetting("Catalog:Path", Path.Combine(AppContext.BaseDirectory, "test-catalog.json"));
    }
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
    }
}

public class CreateOrderGuardTests : IClassFixture<LiveModeFactory>
{
    private readonly LiveModeFactory _factory;
    public CreateOrderGuardTests(LiveModeFactory factory) => _factory = factory;

    [Fact]
    public async Task Live_mode_refuses_checkout_for_unfulfillable_product()
    {
        var client = _factory.CreateClient();
        var res = await client.PostAsJsonAsync("/paypal/create-order", new { productKey = "softlaunch", quantity = 1 });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("not available", await res.Content.ReadAsStringAsync());
    }
}

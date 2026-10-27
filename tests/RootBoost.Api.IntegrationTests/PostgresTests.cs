using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RootBoost.Domain;
using RootBoost.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace RootBoost.Api.IntegrationTests;

/// <summary>
/// Pipeline real contra um Postgres de verdade (contêiner descartável). Prova que as migrações
/// sobem, que pedido e visita persistem e que visitas simultâneas não perdem contagem.
/// </summary>
public class PostgresTests : IClassFixture<PostgresWebApplicationFactory>
{
    private readonly PostgresWebApplicationFactory _factory;
    public PostgresTests(PostgresWebApplicationFactory factory) => _factory = factory;

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
    public async Task Migrations_apply_and_a_paid_order_persists_and_lists()
    {
        var client = _factory.CreateClient();

        var res = await client.PostAsJsonAsync("/webhook/payment", Payment("PG-1"));
        res.EnsureSuccessStatusCode();

        var orders = await client.GetFromJsonAsync<List<JsonElement>>("/orders");
        var order = orders!.Single(o => o.GetProperty("paymentId").GetString() == "PG-1");
        Assert.Equal("PlacedAtSupplier", order.GetProperty("status").GetString());
        Assert.Equal(34.99m, order.GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task Duplicate_payment_stays_a_single_order_on_postgres()
    {
        var client = _factory.CreateClient();
        await client.PostAsJsonAsync("/webhook/payment", Payment("PG-DUP"));
        await client.PostAsJsonAsync("/webhook/payment", Payment("PG-DUP"));

        var orders = await client.GetFromJsonAsync<List<JsonElement>>("/orders");
        Assert.Single(orders!, o => o.GetProperty("paymentId").GetString() == "PG-DUP");
    }

    [Fact]
    public async Task Landing_views_are_counted_on_postgres()
    {
        var client = _factory.CreateClient();
        (await client.PostAsJsonAsync("/track/view", new { productKey = "rack", lang = "en" })).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync("/track/view", new { productKey = "rack", lang = "en" })).EnsureSuccessStatusCode();

        await using var db = NewPostgresContext();
        var today = DateTime.UtcNow.Date;
        var row = await db.LandingViews.SingleAsync(v => v.ProductKey == "rack" && v.Lang == "en" && v.DateUtc == today);
        Assert.True(row.Count >= 2);
    }

    [Fact]
    public async Task Concurrent_views_never_lose_a_count()
    {
        var client = _factory.CreateClient();
        await using (var before = NewPostgresContext())
            _ = await before.LandingViews.CountAsync(); // garante as migrações antes da rajada

        await Task.WhenAll(Enumerable.Range(0, 25).Select(_ =>
            client.PostAsJsonAsync("/track/view", new { productKey = "rack", lang = "pt" })));

        await using var db = NewPostgresContext();
        var today = DateTime.UtcNow.Date;
        var row = await db.LandingViews.SingleAsync(v => v.ProductKey == "rack" && v.Lang == "pt" && v.DateUtc == today);
        Assert.Equal(25, row.Count);
    }

    [Fact]
    public void Railway_database_url_becomes_an_npgsql_connection_string()
    {
        var cs = DatabaseSetup.FromUrl("postgresql://user%40x:p%3Ass@db.railway.internal:6543/railway?sslmode=require");
        var b = new Npgsql.NpgsqlConnectionStringBuilder(cs);
        Assert.Equal("db.railway.internal", b.Host);
        Assert.Equal(6543, b.Port);
        Assert.Equal("railway", b.Database);
        Assert.Equal("user@x", b.Username);
        Assert.Equal("p:ss", b.Password);
        Assert.Equal(Npgsql.SslMode.Require, b.SslMode);
    }

    private RootBoostDbContext NewPostgresContext(string databaseSuffix = "")
    {
        var builder = new Npgsql.NpgsqlConnectionStringBuilder(_factory.ConnectionString);
        builder.Database += databaseSuffix;
        var options = new DbContextOptionsBuilder<RootBoostDbContext>().UseNpgsql(builder.ConnectionString).Options;
        return new RootBoostDbContext(options);
    }
}

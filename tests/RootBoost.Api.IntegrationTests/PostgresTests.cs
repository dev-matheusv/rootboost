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
/// sobem, que pedido e visita persistem, e que a importação do SQLite não perde nem duplica nada.
/// </summary>
public sealed class PostgresWebApplicationFactory : CustomWebApplicationFactory, IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();

    public string ConnectionString => _pg.GetConnectionString();

    public Task InitializeAsync() => _pg.StartAsync();

    async Task IAsyncLifetime.DisposeAsync() => await _pg.DisposeAsync();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Database:Provider", "Postgres");
        builder.UseSetting("ConnectionStrings:Postgres", ConnectionString);
    }
}

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
    public async Task Sqlite_import_copies_everything_once_and_never_duplicates()
    {
        _ = _factory.CreateClient(); // garante que as migrações rodaram
        var sqlitePath = Path.Combine(Path.GetTempPath(), $"rootboost-import-{Guid.NewGuid():N}.db");
        try
        {
            await SeedSqliteAsync(sqlitePath);

            // Banco do importador isolado: o mesmo Postgres, mas um schema de dados vazio.
            await using var target = NewPostgresContext(databaseSuffix: "_import");
            await target.Database.MigrateAsync();

            var first = await SqliteImporter.ImportIfEmptyAsync(target, sqlitePath, NullLogger.Instance);
            Assert.True(first.Imported);
            Assert.Equal(2, first.Orders);
            Assert.Equal(1, first.Views);

            var imported = await target.Orders.AsNoTracking().SingleAsync(o => o.PaymentId == "OLD-1");
            Assert.Equal("Old Buyer", imported.ShipTo.Name);
            Assert.Equal(OrderStatus.PlacedAtSupplier, imported.Status);

            var second = await SqliteImporter.ImportIfEmptyAsync(target, sqlitePath, NullLogger.Instance);
            Assert.False(second.Imported);
            Assert.Equal(2, await target.Orders.CountAsync());
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(sqlitePath)) File.Delete(sqlitePath);
        }
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

    private static async Task SeedSqliteAsync(string path)
    {
        var options = new DbContextOptionsBuilder<RootBoostDbContext>().UseSqlite($"Data Source={path}").Options;
        await using var db = new RootBoostDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var address = new ShippingAddress("Old Buyer", "1 Old St", "Austin", "TX", "78701", "US");
        var placed = new Order("OLD-1", "rack", 1, "old@example.com", address, 34.99m, "USD",
            new DateTimeOffset(2026, 9, 12, 19, 17, 0, TimeSpan.FromHours(-3)));
        placed.MarkPlacedAtSupplier("CJ-OLD-1");
        db.Orders.Add(placed);
        // Cada pedido com o próprio endereço: o EF não compartilha a mesma instância de tipo owned.
        db.Orders.Add(new Order("OLD-2", "rack", 2, "old2@example.com", address with { }, 69.98m, "USD"));
        db.LandingViews.Add(new LandingViewDaily { ProductKey = "rack", Lang = "en", DateUtc = new DateTime(2026, 9, 12), Count = 7 });
        await db.SaveChangesAsync();
    }
}

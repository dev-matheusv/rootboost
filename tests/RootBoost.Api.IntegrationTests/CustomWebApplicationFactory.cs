using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Testcontainers.PostgreSql;

namespace RootBoost.Api.IntegrationTests;

/// <summary>
/// Sobe o pipeline real da API com os adaptadores determinísticos (fornecedor Mock, notificador Logging,
/// verificador Test) contra um Postgres de verdade, o mesmo banco da produção (RB-29: sem SQLite).
/// Um contêiner só pra rodada inteira de testes; cada fábrica ganha o próprio banco (as migrações criam).
/// </summary>
public class CustomWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private static readonly Lazy<Task<PostgreSqlContainer>> Shared = new(async () =>
    {
        var container = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
        await container.StartAsync();
        return container; // o Testcontainers (Ryuk) derruba no fim da rodada
    });

    private readonly string _database = $"it_{Guid.NewGuid():N}";

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        var container = await Shared.Value;
        ConnectionString = new NpgsqlConnectionStringBuilder(container.GetConnectionString()) { Database = _database }.ConnectionString;
    }

    Task IAsyncLifetime.DisposeAsync() => Task.CompletedTask;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Postgres", ConnectionString);
        builder.UseSetting("Supplier", "Mock");
        builder.UseSetting("Notifier", "Logging");
        builder.UseSetting("Payments:Verifier", "Test");
        builder.UseSetting("Catalog:Path", Path.Combine(AppContext.BaseDirectory, "test-catalog.json"));
        // Agendadores desligados: os testes disparam os jobs pelos endpoints, sem corrida com o timer.
        builder.UseSetting("Social:SchedulerEnabled", "false");
        builder.UseSetting("Ads:SyncEnabled", "false");
        // no Orders:ApiKey -> /orders is open in tests
    }
}

/// <summary>Mantido pelo nome: os testes que antes rodavam "também no Postgres" agora são o padrão.</summary>
public sealed class PostgresWebApplicationFactory : CustomWebApplicationFactory;
